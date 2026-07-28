using System.Globalization;
using System.Net;
using System.Net.Http.Json;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using FortuneForge.Server.Payments.Configuration;
using FortuneForge.Server.Payments.Models;
using FortuneForge.Server.Payments.Storage;
using Microsoft.Extensions.Options;

namespace FortuneForge.Server.Payments.Providers;

internal sealed partial class MerchantGatewayPaymentProvider
{
    private async Task<InvoiceReconciliationResult> ApplyRemoteStatusAsync(
        StoredPaymentCheckout checkout,
        MerchantGatewayInvoiceResponse remote,
        string? expectedStatus,
        CancellationToken cancellationToken)
    {
        if (!MatchesLocalInvoice(checkout, remote))
        {
            logger.LogError(
                "MerchantGateway invoice {RemoteInvoiceId} did not match local checkout {CheckoutId}; refusing to apply its status.",
                remote.Id,
                checkout.CheckoutId);
            return new InvoiceReconciliationResult(
                checkout,
                PaymentReconciliationStatus.Retryable);
        }

        var status = MapStatus(remote.Status, checkout.Status);
        if (expectedStatus is not null &&
            !SatisfiesExpectedStatus(status, expectedStatus))
        {
            logger.LogInformation(
                "MerchantGateway invoice {RemoteInvoiceId} status {RemoteStatus} has not reached expected callback status {ExpectedStatus}; leaving the event retryable.",
                remote.Id,
                remote.Status,
                expectedStatus);
            return new InvoiceReconciliationResult(
                checkout,
                PaymentReconciliationStatus.Retryable);
        }

        if (string.Equals(status, checkout.Status, StringComparison.Ordinal))
        {
            if (expectedStatus is "completed")
            {
                return new InvoiceReconciliationResult(
                    checkout,
                    checkout.CreditedBalance is not null
                        ? PaymentReconciliationStatus.TerminalNoOp
                        : PaymentReconciliationStatus.Retryable);
            }

            return new InvoiceReconciliationResult(
                checkout,
                PaymentReconciliationStatus.Applied);
        }

        var updatedAtUtc = remote.CompletedAtUtc?.UtcDateTime ?? DateTime.UtcNow;
        var result = await paymentStore.UpdateStatusAsync(
            checkout.CheckoutId,
            checkout.UserId,
            status,
            updatedAtUtc,
            cancellationToken);
        if (result.Value is not null)
        {
            return new InvoiceReconciliationResult(
                result.Value,
                PaymentReconciliationStatus.Applied);
        }

        logger.LogWarning(
            "Could not apply MerchantGateway status {Status} to checkout {CheckoutId}; local status remains {LocalStatus}.",
            remote.Status,
            checkout.CheckoutId,
            checkout.Status);
        return new InvoiceReconciliationResult(
            checkout,
            PaymentReconciliationStatus.Retryable);
    }

    private async Task<MerchantGatewayPathwayResponse?> TryGetPreferredPathwayAsync(
        HttpClient client,
        string market,
        CancellationToken cancellationToken)
    {
        try
        {
            using var request = CreateRequest(HttpMethod.Get, "api/v1/pathway-configs");
            using var response = await client.SendAsync(request, cancellationToken);
            if (!response.IsSuccessStatusCode)
            {
                logger.LogWarning(
                    "Could not read MerchantGateway pathway configs for market {Market}; the invoice will let the gateway choose a route.",
                    market);
                return null;
            }

            var pathways = await response.Content.ReadFromJsonAsync<MerchantGatewayPathwayResponse[]>(
                cancellationToken) ?? [];
            if (pathways.Length == 0)
            {
                logger.LogWarning(
                    "MerchantGateway returned no active pathway configs for market {Market}; the invoice will be submitted without a route key.",
                    market);
                return null;
            }

            if (_options.PathwayKeys.TryGetValue(market, out var configuredKey) &&
                IsUsablePathwayKey(configuredKey))
            {
                var configured = pathways.FirstOrDefault(candidate =>
                    candidate.Key.Equals(configuredKey.Trim(), StringComparison.OrdinalIgnoreCase));
                if (configured is not null)
                {
                    return configured;
                }

                logger.LogWarning(
                    "Configured MerchantGateway pathway key for market {Market} is not active; falling back to the gateway default route.",
                    market);
            }

            return pathways[0];
        }
        catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
        {
            logger.LogWarning(
                "MerchantGateway timed out while reading pathway configs for market {Market}; the invoice will let the gateway choose a route.",
                market);
            return null;
        }
        catch (HttpRequestException exception)
        {
            logger.LogWarning(
                exception,
                "MerchantGateway was unavailable while reading pathway configs for market {Market}; the invoice will let the gateway choose a route.",
                market);
            return null;
        }
        catch (JsonException exception)
        {
            logger.LogWarning(
                exception,
                "MerchantGateway returned invalid pathway JSON for market {Market}; the invoice will let the gateway choose a route.",
                market);
            return null;
        }
    }
}
