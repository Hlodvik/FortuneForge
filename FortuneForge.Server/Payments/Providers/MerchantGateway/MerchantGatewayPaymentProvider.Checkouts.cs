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

internal sealed partial class MerchantGatewayPaymentProvider(
    IPaymentStore paymentStore,
    IHttpClientFactory httpClientFactory,
    IOptions<PaymentsOptions> options,
    ILogger<MerchantGatewayPaymentProvider> logger) : IPaymentProvider, IPaymentReconciler
{
    public const string HttpClientName = "MerchantGateway";
    private static readonly TimeSpan CheckoutSubmissionLeaseDuration = TimeSpan.FromSeconds(45);
    private const int CheckoutSubmissionInitialBackoffSeconds = 15;
    private const int CheckoutSubmissionMaximumBackoffSeconds = 300;
    private const int CheckoutSubmissionJitterMaximumMilliseconds = 5_000;

    private readonly MerchantGatewayOptions _options = options.Value.MerchantGateway;

    public string Id => "merchantgateway-api";

    public bool IsMock => false;

    public async Task<PaymentResult<StoredPaymentCheckout>> CreateCheckoutAsync(
        PaymentCheckoutDraft draft,
        CancellationToken cancellationToken)
    {
        var client = httpClientFactory.CreateClient(HttpClientName);
        var pathway = await TryGetPreferredPathwayAsync(client, draft.Market.Code, cancellationToken);
        var localCheckout = CreateLocalCheckoutAttempt(draft, pathway);
        var localResult = await paymentStore.CreateAsync(localCheckout, cancellationToken);
        if (localResult.Value is null)
        {
            return localResult;
        }

        var checkout = localResult.Value;
        if (!string.IsNullOrWhiteSpace(checkout.ProviderCheckoutId))
        {
            return PaymentResult<StoredPaymentCheckout>.Success(
                await RefreshAsync(checkout, cancellationToken));
        }

        if (checkout.Status is "completed" or "failed" or "expired")
        {
            return PaymentResult<StoredPaymentCheckout>.Success(checkout);
        }

        return await SubmitCheckoutAsync(client, checkout, cancellationToken);
    }

    private async Task<PaymentResult<StoredPaymentCheckout>> SubmitCheckoutAsync(
        HttpClient client,
        StoredPaymentCheckout checkout,
        CancellationToken cancellationToken)
    {
        var lease = await paymentStore.TryBeginCheckoutProviderSubmissionAsync(
            checkout.CheckoutId,
            checkout.UserId,
            DateTime.UtcNow,
            CheckoutSubmissionLeaseDuration,
            cancellationToken);
        if (!lease.Acquired || lease.Checkout is null || string.IsNullOrWhiteSpace(lease.LeaseId))
        {
            return lease.State == PaymentCheckoutProviderSubmissionLeaseState.NotFound
                ? PaymentResult<StoredPaymentCheckout>.Failure(PaymentError.CheckoutNotFound)
                : PaymentResult<StoredPaymentCheckout>.Success(lease.Checkout ?? checkout);
        }

        var leasedCheckout = lease.Checkout;
        try
        {
            using var request = CreateRequest(HttpMethod.Post, "api/v1/invoices");
            request.Headers.TryAddWithoutValidation("Idempotency-Key", leasedCheckout.IdempotencyKey);
            request.Content = JsonContent.Create(new MerchantGatewayInvoiceCreateRequest(
                leasedCheckout.InvoiceId,
                leasedCheckout.Amount,
                leasedCheckout.Market.Currency,
                NormalizePathwayKey(leasedCheckout.ProviderPathwayKey),
                leasedCheckout.Customer.CustomerReference,
                leasedCheckout.Customer.BeneficiaryReference));

            using var response = await client.SendAsync(request, cancellationToken);
            if (!response.IsSuccessStatusCode)
            {
                await LogProviderRejectionAsync(
                    response,
                    "invoice",
                    leasedCheckout.InvoiceId,
                    cancellationToken);
                return await MarkCheckoutSubmissionUncertainAsync(
                    leasedCheckout,
                    lease.LeaseId,
                    response.StatusCode,
                    cancellationToken);
            }

            var created = await response.Content.ReadFromJsonAsync<MerchantGatewayCreatedResponse>(
                cancellationToken);
            if (created is null || created.Id == Guid.Empty || created.OurNumber <= 0)
            {
                logger.LogError("MerchantGateway returned an invalid invoice creation response.");
                return await MarkCheckoutSubmissionUncertainAsync(
                    leasedCheckout,
                    lease.LeaseId,
                    providerStatusCode: null,
                    cancellationToken);
            }

            return await paymentStore.UpdateCheckoutProviderAsync(
                leasedCheckout.CheckoutId,
                leasedCheckout.UserId,
                created.Id.ToString("N"),
                MapStatus(created.Status, "received"),
                leasedCheckout.BankTransfer,
                DateTime.UtcNow,
                cancellationToken);
        }
        catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
        {
            logger.LogWarning("MerchantGateway timed out while creating an invoice.");
            return await MarkCheckoutSubmissionUncertainAsync(
                leasedCheckout,
                lease.LeaseId,
                providerStatusCode: null,
                CancellationToken.None);
        }
        catch (HttpRequestException exception)
        {
            logger.LogWarning(exception, "MerchantGateway was unavailable while creating an invoice.");
            return await MarkCheckoutSubmissionUncertainAsync(
                leasedCheckout,
                lease.LeaseId,
                providerStatusCode: null,
                CancellationToken.None);
        }
        catch (JsonException exception)
        {
            logger.LogError(exception, "MerchantGateway returned invalid JSON while creating an invoice.");
            return await MarkCheckoutSubmissionUncertainAsync(
                leasedCheckout,
                lease.LeaseId,
                providerStatusCode: null,
                CancellationToken.None);
        }
    }

}
