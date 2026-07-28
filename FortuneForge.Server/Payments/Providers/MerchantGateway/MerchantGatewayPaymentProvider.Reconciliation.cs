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
    public async Task<PaymentReconciliationStatus> ReconcileInvoiceAsync(
        string checkoutId,
        string expectedStatus,
        CancellationToken cancellationToken)
    {
        if (!Guid.TryParseExact(checkoutId, "N", out _))
        {
            return PaymentReconciliationStatus.Retryable;
        }

        var checkout = await paymentStore.FindByProviderCheckoutIdForAdminAsync(
            Id,
            checkoutId,
            cancellationToken);
        checkout ??= await paymentStore.FindByCheckoutIdForAdminAsync(
            checkoutId,
            cancellationToken);
        if (checkout is null)
        {
            return PaymentReconciliationStatus.Retryable;
        }

        return (await RefreshAsync(checkout, expectedStatus, cancellationToken)).Status;
    }

    public async Task<int> ReconcilePendingAsync(CancellationToken cancellationToken)
    {
        var pending = await paymentStore.ListPendingAsync(
            Id,
            _options.ReconciliationBatchSize,
            cancellationToken);
        var reconciled = 0;
        var client = httpClientFactory.CreateClient(HttpClientName);
        foreach (var local in pending)
        {
            if (string.IsNullOrWhiteSpace(local.ProviderCheckoutId))
            {
                await SubmitCheckoutAsync(client, local, cancellationToken);
            }
            else
            {
                await RefreshAsync(local, expectedStatus: null, cancellationToken);
            }

            reconciled++;
        }

        return reconciled;
    }

    private async Task<StoredPaymentCheckout> RefreshAsync(
        StoredPaymentCheckout checkout,
        CancellationToken cancellationToken) =>
        (await RefreshAsync(checkout, expectedStatus: null, cancellationToken)).Checkout;

    private async Task<InvoiceReconciliationResult> RefreshAsync(
        StoredPaymentCheckout checkout,
        string? expectedStatus,
        CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(checkout.ProviderCheckoutId))
        {
            var clientForSubmission = httpClientFactory.CreateClient(HttpClientName);
            var submitted = await SubmitCheckoutAsync(
                clientForSubmission,
                checkout,
                cancellationToken);
            return new InvoiceReconciliationResult(
                submitted.Value ?? checkout,
                submitted.Value is not null && !string.IsNullOrWhiteSpace(submitted.Value.ProviderCheckoutId)
                    ? PaymentReconciliationStatus.Applied
                    : PaymentReconciliationStatus.Retryable);
        }

        try
        {
            var client = httpClientFactory.CreateClient(HttpClientName);
            using var request = CreateRequest(
                HttpMethod.Get,
                $"api/v1/invoices/{checkout.ProviderCheckoutId}");
            using var response = await client.SendAsync(request, cancellationToken);
            if (response.StatusCode == HttpStatusCode.NotFound)
            {
                logger.LogWarning(
                    "MerchantGateway invoice {CheckoutId} no longer exists; retaining the local checkout.",
                    checkout.ProviderCheckoutId);
                return new InvoiceReconciliationResult(
                    checkout,
                    PaymentReconciliationStatus.Retryable);
            }

            if (!response.IsSuccessStatusCode)
            {
                LogRefreshFailure(response.StatusCode);
                return new InvoiceReconciliationResult(
                    checkout,
                    PaymentReconciliationStatus.Retryable);
            }

            var remote = await response.Content.ReadFromJsonAsync<MerchantGatewayInvoiceResponse>(
                cancellationToken);
            return remote is null
                ? new InvoiceReconciliationResult(
                    checkout,
                    PaymentReconciliationStatus.Retryable)
                : await ApplyRemoteStatusAsync(
                    checkout,
                    remote,
                    expectedStatus,
                    cancellationToken);
        }
        catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
        {
            logger.LogWarning("MerchantGateway timed out while refreshing invoice {CheckoutId}.", checkout.CheckoutId);
            return new InvoiceReconciliationResult(
                checkout,
                PaymentReconciliationStatus.Retryable);
        }
        catch (HttpRequestException exception)
        {
            logger.LogWarning(
                exception,
                "MerchantGateway was unavailable while refreshing invoice {CheckoutId}.",
                checkout.ProviderCheckoutId);
            return new InvoiceReconciliationResult(
                checkout,
                PaymentReconciliationStatus.Retryable);
        }
        catch (JsonException exception)
        {
            logger.LogError(
                exception,
                "MerchantGateway returned invalid JSON while refreshing invoice {CheckoutId}.",
                checkout.ProviderCheckoutId);
            return new InvoiceReconciliationResult(
                checkout,
                PaymentReconciliationStatus.Retryable);
        }
    }
}
