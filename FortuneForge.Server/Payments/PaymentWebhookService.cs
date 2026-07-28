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

public sealed class PaymentWebhookService
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

    private async Task<PaymentWebhookStatus> ProcessMerchantGatewayEventAsync(
        IPaymentReconciler reconciler,
        Guid eventId,
        MerchantGatewayWebhookEnvelope envelope,
        Guid remotePublicId,
        CancellationToken cancellationToken)
    {
        if (envelope.Type.StartsWith("withdrawal.", StringComparison.Ordinal))
        {
            var withdrawalStatus = WithdrawalStatusProjection.FromMerchantGatewayEvent(envelope.Type);
            if (withdrawalStatus is null)
            {
                return PaymentWebhookStatus.Invalid;
            }

            var result = await _paymentStore.ProjectWithdrawalProviderStatusAsync(
                _provider.Id,
                remotePublicId.ToString("N"),
                withdrawalStatus,
                envelope.OccurredAtUtc.UtcDateTime,
                cancellationToken);
            if (result.Value is not null)
            {
                await MarkProviderEventAppliedAsync(eventId, cancellationToken);
                return PaymentWebhookStatus.Accepted;
            }

            if (result.Error == PaymentError.CheckoutNotFound)
            {
                _logger.LogInformation(
                    "MerchantGateway event {EventId} is waiting for local withdrawal provider key {RemoteWithdrawalId}.",
                    eventId,
                    remotePublicId);
                return PaymentWebhookStatus.Retryable;
            }

            if (result.Error == PaymentError.InvalidStatusTransition)
            {
                _logger.LogWarning(
                    "Ignored MerchantGateway withdrawal event {EventId} ({EventType}) for {RemoteWithdrawalId} because it would regress or mismatch local withdrawal state.",
                    eventId,
                    envelope.Type,
                    remotePublicId);
                await MarkProviderEventAppliedAsync(eventId, cancellationToken);
                return PaymentWebhookStatus.Accepted;
            }

            return PaymentWebhookStatus.Retryable;
        }

        if (!envelope.Type.StartsWith("invoice.", StringComparison.Ordinal))
        {
            return PaymentWebhookStatus.Invalid;
        }

        var invoiceStatus = InvoiceStatusFromMerchantGatewayEvent(envelope.Type);
        if (invoiceStatus is null)
        {
            return PaymentWebhookStatus.Invalid;
        }

        var reconciliation = await reconciler.ReconcileInvoiceAsync(
            remotePublicId.ToString("N"),
            invoiceStatus,
            cancellationToken);
        if (reconciliation == PaymentReconciliationStatus.Retryable)
        {
            _logger.LogInformation(
                "MerchantGateway event {EventId} is waiting for local invoice provider key {RemoteInvoiceId}.",
                eventId,
                remotePublicId);
            return PaymentWebhookStatus.Retryable;
        }

        await MarkProviderEventAppliedAsync(eventId, cancellationToken);
        return PaymentWebhookStatus.Accepted;
    }

    private static string? InvoiceStatusFromMerchantGatewayEvent(string eventType) =>
        eventType switch
        {
            "invoice.created" => "received",
            "invoice.processing" => "processing",
            "invoice.completed" => "completed",
            "invoice.cancelled" => "failed",
            _ => null
        };

    private Task MarkProviderEventAppliedAsync(
        Guid eventId,
        CancellationToken cancellationToken) =>
        _paymentStore.MarkProviderEventAppliedAsync(
            MerchantGatewayEventProviderId,
            eventId.ToString("D"),
            DateTime.UtcNow,
            cancellationToken);

    private bool HasValidSignature(
        long timestamp,
        Guid eventId,
        ReadOnlySpan<byte> body,
        ReadOnlySpan<byte> providedDigest)
    {
        var valid = false;
        foreach (var secret in _options.WebhookSigningSecrets)
        {
            var expectedDigest = CreateDigest(secret, timestamp, eventId, body);
            valid |= CryptographicOperations.FixedTimeEquals(expectedDigest, providedDigest);
        }

        return valid;
    }

    private static byte[] CreateDigest(
        string signingSecret,
        long timestamp,
        Guid eventId,
        ReadOnlySpan<byte> body)
    {
        var prefix = Encoding.UTF8.GetBytes(string.Create(
            CultureInfo.InvariantCulture,
            $"{timestamp}.{eventId:D}."));
        var input = new byte[prefix.Length + body.Length];
        prefix.CopyTo(input, 0);
        body.CopyTo(input.AsSpan(prefix.Length));
        return HMACSHA256.HashData(Encoding.UTF8.GetBytes(signingSecret), input);
    }

    private static bool TryDecodeSignature(string value, out byte[] digest)
    {
        digest = [];
        if (!value.StartsWith("v1=", StringComparison.Ordinal) || value.Length != 67)
        {
            return false;
        }

        try
        {
            digest = Convert.FromHexString(value.AsSpan(3));
            return digest.Length == 32;
        }
        catch (FormatException)
        {
            return false;
        }
    }

    private static bool TryReadGuid(JsonElement element, string name, out Guid value)
    {
        value = Guid.Empty;
        if (element.ValueKind != JsonValueKind.Object)
        {
            return false;
        }

        foreach (var property in element.EnumerateObject())
        {
            if (property.Name.Equals(name, StringComparison.OrdinalIgnoreCase) &&
                property.Value.ValueKind == JsonValueKind.String &&
                Guid.TryParse(property.Value.GetString(), out value) &&
                value != Guid.Empty)
            {
                return true;
            }
        }

        value = Guid.Empty;
        return false;
    }

    private static bool HasMultipleValues(string value) =>
        string.IsNullOrWhiteSpace(value) || value.Contains(',', StringComparison.Ordinal);

    private sealed record MerchantGatewayWebhookEnvelope(
        Guid EventId,
        string Type,
        DateTimeOffset OccurredAtUtc,
        JsonElement Data);
}
