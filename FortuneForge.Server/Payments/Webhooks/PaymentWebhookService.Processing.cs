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

public sealed partial class PaymentWebhookService
{
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
}
