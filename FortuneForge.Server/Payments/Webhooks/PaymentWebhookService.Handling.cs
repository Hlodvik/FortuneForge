using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using FortuneForge.Server.Payments.Configuration;
using FortuneForge.Server.Payments.Models;
using FortuneForge.Server.Payments.Providers;
using FortuneForge.Server.Payments.Storage;
using Microsoft.Extensions.Options;

namespace FortuneForge.Server.Payments;

public enum PaymentWebhookStatus
{
    Accepted,
    Duplicate,
    Invalid,
    Unauthorized,
    Disabled,
    Retryable
}

public sealed partial class PaymentWebhookService
{
    private const string MerchantGatewayEventProviderId = "merchantgateway";

    public const string EventIdHeader = "X-MerchantGateway-Event-Id";
    public const string EventTypeHeader = "X-MerchantGateway-Event-Type";
    public const string TimestampHeader = "X-MerchantGateway-Timestamp";
    public const string SignatureHeader = "X-MerchantGateway-Signature";

    private static readonly JsonSerializerOptions WebhookJsonOptions = new()
    {
        PropertyNameCaseInsensitive = true
    };

    private static readonly HashSet<string> SupportedEventTypes = new(StringComparer.Ordinal)
    {
        "invoice.created",
        "invoice.processing",
        "invoice.completed",
        "invoice.cancelled",
        "withdrawal.created",
        "withdrawal.pending",
        "withdrawal.processing",
        "withdrawal.completed",
        "withdrawal.rejected",
        "withdrawal.failed",
        "withdrawal.cancelled",
        "withdrawal.canceled",
        "withdrawal.reversed"
    };

    private readonly IPaymentStore _paymentStore;
    private readonly IPaymentProvider _provider;
    private readonly MerchantGatewayOptions _options;
    private readonly ILogger<PaymentWebhookService> _logger;

    internal PaymentWebhookService(
        IPaymentStore paymentStore,
        IPaymentProvider provider,
        IOptions<PaymentsOptions> options,
        ILogger<PaymentWebhookService> logger)
    {
        _paymentStore = paymentStore;
        _provider = provider;
        _options = options.Value.MerchantGateway;
        _logger = logger;
    }

    public async Task<PaymentWebhookStatus> HandleMerchantGatewayAsync(
        string eventIdHeader,
        string eventTypeHeader,
        string timestampHeader,
        string signatureHeader,
        ReadOnlyMemory<byte> body,
        CancellationToken cancellationToken)
    {
        if (_provider is not IPaymentReconciler reconciler ||
            _options.WebhookSigningSecrets.Count == 0)
        {
            return PaymentWebhookStatus.Disabled;
        }

        if (HasMultipleValues(eventIdHeader) ||
            HasMultipleValues(eventTypeHeader) ||
            HasMultipleValues(timestampHeader) ||
            HasMultipleValues(signatureHeader) ||
            !Guid.TryParse(eventIdHeader, out var eventId) ||
            eventId == Guid.Empty ||
            !SupportedEventTypes.Contains(eventTypeHeader) ||
            !long.TryParse(
                timestampHeader,
                NumberStyles.None,
                CultureInfo.InvariantCulture,
                out var timestamp))
        {
            return PaymentWebhookStatus.Invalid;
        }

        DateTimeOffset signedAtUtc;
        try
        {
            signedAtUtc = DateTimeOffset.FromUnixTimeSeconds(timestamp);
        }
        catch (ArgumentOutOfRangeException)
        {
            return PaymentWebhookStatus.Invalid;
        }

        if (Math.Abs((DateTimeOffset.UtcNow - signedAtUtc).TotalSeconds) >
            _options.WebhookToleranceSeconds)
        {
            return PaymentWebhookStatus.Unauthorized;
        }

        if (!TryDecodeSignature(signatureHeader, out var providedDigest) ||
            !HasValidSignature(timestamp, eventId, body.Span, providedDigest))
        {
            return PaymentWebhookStatus.Unauthorized;
        }

        MerchantGatewayWebhookEnvelope? envelope;
        try
        {
            envelope = JsonSerializer.Deserialize<MerchantGatewayWebhookEnvelope>(
                body.Span,
                WebhookJsonOptions);
        }
        catch (JsonException)
        {
            return PaymentWebhookStatus.Invalid;
        }

        if (envelope is null ||
            envelope.EventId != eventId ||
            !string.Equals(envelope.Type, eventTypeHeader, StringComparison.Ordinal) ||
            envelope.OccurredAtUtc == default ||
            !TryReadGuid(envelope.Data, "publicId", out var remotePublicId))
        {
            return PaymentWebhookStatus.Invalid;
        }

        var receivedAtUtc = DateTime.UtcNow;
        var processing = await _paymentStore.BeginProviderEventProcessingAsync(
            MerchantGatewayEventProviderId,
            eventId.ToString("D"),
            envelope.Type,
            envelope.OccurredAtUtc.UtcDateTime,
            receivedAtUtc,
            cancellationToken);

        if (processing.State == PaymentProviderEventProcessingState.Applied)
        {
            _logger.LogWarning(
                "Ignored already applied MerchantGateway event {EventId} ({EventType}).",
                eventId,
                envelope.Type);
            return PaymentWebhookStatus.Duplicate;
        }

        if (processing.State == PaymentProviderEventProcessingState.Conflict)
        {
            _logger.LogWarning(
                "Rejected MerchantGateway event {EventId} because its metadata conflicts with a previously delivered event.",
                eventId);
            return PaymentWebhookStatus.Invalid;
        }

        try
        {
            return await ProcessMerchantGatewayEventAsync(
                reconciler,
                eventId,
                envelope,
                remotePublicId,
                cancellationToken);
        }
        catch (Exception exception) when (exception is not OperationCanceledException ||
            !cancellationToken.IsCancellationRequested)
        {
            _logger.LogWarning(
                exception,
                "MerchantGateway event {EventId} ({EventType}) was left retryable because projection did not complete.",
                eventId,
                envelope.Type);
            return PaymentWebhookStatus.Retryable;
        }
    }

}
