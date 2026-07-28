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
    public async Task<StoredPaymentCheckout?> GetCheckoutAsync(
        string checkoutId,
        string userId,
        CancellationToken cancellationToken)
    {
        var checkout = await paymentStore.FindByCheckoutIdAsync(
            checkoutId,
            userId,
            cancellationToken);
        return checkout is null ? null : await RefreshAsync(checkout, cancellationToken);
    }

    public async Task<StoredPaymentCheckout?> GetInvoiceAsync(
        string invoiceId,
        string userId,
        CancellationToken cancellationToken)
    {
        var checkout = await paymentStore.FindByInvoiceIdAsync(invoiceId, userId, cancellationToken);
        return checkout is null ? null : await RefreshAsync(checkout, cancellationToken);
    }

    public async Task<StoredPaymentCheckout?> GetInvoiceForAdminAsync(
        string invoiceId,
        CancellationToken cancellationToken)
    {
        var checkout = await paymentStore.FindByInvoiceIdForAdminAsync(invoiceId, cancellationToken);
        return checkout is null ? null : await RefreshAsync(checkout, cancellationToken);
    }

    public async Task<IReadOnlyList<StoredPaymentCheckout>> ListInvoicesAsync(
        string userId,
        int limit,
        CancellationToken cancellationToken)
    {
        var localInvoices = await paymentStore.ListAsync(userId, limit, cancellationToken);
        if (localInvoices.Count == 0)
        {
            return localInvoices;
        }

        try
        {
            var client = httpClientFactory.CreateClient(HttpClientName);
            using var request = CreateRequest(HttpMethod.Get, "api/v1/invoices?limit=500");
            using var response = await client.SendAsync(request, cancellationToken);
            if (!response.IsSuccessStatusCode)
            {
                LogRefreshFailure(response.StatusCode);
                return localInvoices;
            }

            var remoteInvoices = await response.Content.ReadFromJsonAsync<MerchantGatewayInvoiceResponse[]>(
                cancellationToken) ?? [];
            var remoteById = remoteInvoices.ToDictionary(
                invoice => invoice.Id.ToString("N"),
                StringComparer.OrdinalIgnoreCase);
            var refreshed = new List<StoredPaymentCheckout>(localInvoices.Count);
            foreach (var local in localInvoices)
            {
                if (string.IsNullOrWhiteSpace(local.ProviderCheckoutId))
                {
                    refreshed.Add((await SubmitCheckoutAsync(client, local, cancellationToken)).Value ?? local);
                    continue;
                }

                refreshed.Add(remoteById.TryGetValue(local.ProviderCheckoutId, out var remote)
                    ? (await ApplyRemoteStatusAsync(
                        local,
                        remote,
                        expectedStatus: null,
                        cancellationToken)).Checkout
                    : local);
            }

            return refreshed;
        }
        catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
        {
            logger.LogWarning("MerchantGateway timed out while refreshing invoices.");
            return localInvoices;
        }
        catch (HttpRequestException exception)
        {
            logger.LogWarning(exception, "MerchantGateway was unavailable while refreshing invoices.");
            return localInvoices;
        }
        catch (JsonException exception)
        {
            logger.LogError(exception, "MerchantGateway returned invalid JSON while refreshing invoices.");
            return localInvoices;
        }
    }
}
