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

internal sealed class MerchantGatewayPaymentProvider(
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

    public async Task<PaymentResult<StoredPaymentWithdrawal>> CreateWithdrawalAsync(
        PaymentWithdrawalDraft draft,
        CancellationToken cancellationToken)
    {
        StoredPaymentWithdrawal? reservation = null;
        try
        {
            var client = httpClientFactory.CreateClient(HttpClientName);
            var pathway = await TryGetPreferredPathwayAsync(client, draft.Market.Code, cancellationToken);

            var createdAtUtc = DateTime.UtcNow;
            var localWithdrawal = new StoredPaymentWithdrawal(
                draft.WithdrawalId,
                string.Empty,
                NormalizePathwayKey(pathway?.Key),
                draft.UserId,
                draft.IdempotencyKey,
                Id,
                false,
                draft.Market,
                draft.Amount,
                draft.AmountMinor,
                draft.CreditsDebited,
                "received",
                createdAtUtc,
                draft.CreatedAtUtc,
                null,
                draft.Customer,
                draft.Bank,
                "Withdrawal request reserved locally. Payout status is pending.");
            var reservationResult = await paymentStore.CreateWithdrawalReservationAsync(
                localWithdrawal,
                cancellationToken);
            if (reservationResult.Value is null)
            {
                return reservationResult;
            }

            reservation = reservationResult.Value;
            var withdrawal = reservation;
            if (!string.IsNullOrWhiteSpace(withdrawal.ProviderWithdrawalId) ||
                withdrawal.Status == "completed")
            {
                return PaymentResult<StoredPaymentWithdrawal>.Success(withdrawal);
            }

            if (WithdrawalStatusProjection.IsNegativeTerminal(withdrawal.Status))
            {
                return PaymentResult<StoredPaymentWithdrawal>.Failure(PaymentError.ProviderRejected);
            }

            var isUncertainReplay =
                withdrawal.Status == "pending" &&
                string.IsNullOrWhiteSpace(withdrawal.ProviderWithdrawalId);
            var submittedPathwayKey = isUncertainReplay
                ? NormalizePathwayKey(withdrawal.ProviderPathwayKey)
                : NormalizePathwayKey(withdrawal.ProviderPathwayKey) ??
                    NormalizePathwayKey(pathway?.Key);
            using var request = CreateRequest(HttpMethod.Post, "api/v1/withdrawals");
            request.Headers.TryAddWithoutValidation("Idempotency-Key", withdrawal.IdempotencyKey);
            request.Content = JsonContent.Create(new MerchantGatewayWithdrawalCreateRequest(
                withdrawal.WithdrawalId,
                withdrawal.Amount,
                withdrawal.Market.Currency,
                submittedPathwayKey,
                withdrawal.Bank.AccountHolder,
                withdrawal.Bank.BankName,
                withdrawal.Bank.AccountNumber,
                withdrawal.Bank.BranchCode,
                withdrawal.Bank.AccountType));

            using var response = await client.SendAsync(request, cancellationToken);
            if (!response.IsSuccessStatusCode)
            {
                var responseBody = await LogProviderRejectionAsync(
                    response,
                    "withdrawal",
                    withdrawal.WithdrawalId,
                    cancellationToken);
                if (!IsAuthoritativeWithdrawalCreateRejection(
                    response.StatusCode,
                    responseBody,
                    isUncertainReplay))
                {
                    return await MarkWithdrawalSubmissionUncertainAsync(
                        withdrawal,
                        cancellationToken);
                }

                var failed = await paymentStore.FailWithdrawalReservationAsync(
                    withdrawal.WithdrawalId,
                    withdrawal.UserId,
                    DateTime.UtcNow,
                    cancellationToken);
                if (failed.Value is null)
                {
                    return failed;
                }

                return PaymentResult<StoredPaymentWithdrawal>.Failure(
                    MapProviderError(response.StatusCode, responseBody));
            }

            var created = await response.Content.ReadFromJsonAsync<MerchantGatewayCreatedResponse>(
                cancellationToken);
            if (created is null || created.Id == Guid.Empty)
            {
                logger.LogError("MerchantGateway returned an invalid withdrawal creation response.");
                return await MarkWithdrawalSubmissionUncertainAsync(
                    withdrawal,
                    cancellationToken);
            }

            return await paymentStore.UpdateWithdrawalProviderAsync(
                withdrawal.WithdrawalId,
                withdrawal.UserId,
                created.Id.ToString("N"),
                MapWithdrawalStatus(created.Status, "pending"),
                DateTime.UtcNow,
                cancellationToken);
        }
        catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
        {
            logger.LogWarning("MerchantGateway timed out while creating a withdrawal.");
            return await MarkWithdrawalSubmissionUncertainAsync(
                reservation,
                CancellationToken.None);
        }
        catch (HttpRequestException exception)
        {
            logger.LogWarning(exception, "MerchantGateway was unavailable while creating a withdrawal.");
            return await MarkWithdrawalSubmissionUncertainAsync(
                reservation,
                CancellationToken.None);
        }
        catch (JsonException exception)
        {
            logger.LogError(exception, "MerchantGateway returned invalid JSON while creating a withdrawal.");
            return await MarkWithdrawalSubmissionUncertainAsync(
                reservation,
                CancellationToken.None);
        }
    }

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

    private HttpRequestMessage CreateRequest(HttpMethod method, string path)
    {
        var request = new HttpRequestMessage(method, path);
        request.Headers.TryAddWithoutValidation("x-merchant-api-key", _options.ApiKey.Trim());
        request.Headers.TryAddWithoutValidation("Accept", "application/json");
        return request;
    }

    private async Task<string> LogProviderRejectionAsync(
        HttpResponseMessage response,
        string transactionType,
        string localNumber,
        CancellationToken cancellationToken)
    {
        var body = await response.Content.ReadAsStringAsync(cancellationToken);
        logger.LogWarning(
            "MerchantGateway rejected {TransactionType} {LocalNumber} with HTTP {StatusCode}: {ResponseBody}",
            transactionType,
            localNumber,
            (int)response.StatusCode,
            Truncate(body, 512));
        return body;
    }

    private void LogRefreshFailure(HttpStatusCode statusCode)
    {
        if (statusCode is HttpStatusCode.Unauthorized or HttpStatusCode.Forbidden)
        {
            logger.LogError("MerchantGateway rejected the configured API credential while refreshing invoices.");
            return;
        }

        logger.LogWarning("MerchantGateway returned HTTP {StatusCode} while refreshing invoices.", (int)statusCode);
    }

    private async Task<PaymentResult<StoredPaymentWithdrawal>> MarkWithdrawalSubmissionUncertainAsync(
        StoredPaymentWithdrawal? withdrawal,
        CancellationToken cancellationToken)
    {
        if (withdrawal is null)
        {
            return PaymentResult<StoredPaymentWithdrawal>.Failure(PaymentError.ProviderUnavailable);
        }

        var result = await paymentStore.MarkWithdrawalProviderSubmissionUncertainAsync(
            withdrawal.WithdrawalId,
            withdrawal.UserId,
            DateTime.UtcNow,
            cancellationToken);
        return result.Value is null
            ? result
            : PaymentResult<StoredPaymentWithdrawal>.Success(result.Value);
    }

    private async Task<PaymentResult<StoredPaymentCheckout>> MarkCheckoutSubmissionUncertainAsync(
        StoredPaymentCheckout checkout,
        string leaseId,
        HttpStatusCode? providerStatusCode,
        CancellationToken cancellationToken)
    {
        var nowUtc = DateTime.UtcNow;
        var nextRetryAtUtc = nowUtc.Add(ComputeCheckoutSubmissionBackoff(checkout));
        var result = await paymentStore.MarkCheckoutProviderSubmissionUncertainAsync(
            checkout.CheckoutId,
            checkout.UserId,
            leaseId,
            nowUtc,
            nextRetryAtUtc,
            providerStatusCode is null ? null : (int)providerStatusCode.Value,
            cancellationToken);
        return result.Value is null
            ? result
            : PaymentResult<StoredPaymentCheckout>.Success(result.Value);
    }

    private static TimeSpan ComputeCheckoutSubmissionBackoff(StoredPaymentCheckout checkout)
    {
        var attempt = Math.Max(1, checkout.ProviderSubmissionAttempt);
        var exponent = Math.Min(attempt - 1, 8);
        var baseSeconds = Math.Min(
            CheckoutSubmissionMaximumBackoffSeconds,
            CheckoutSubmissionInitialBackoffSeconds * (1 << exponent));
        var jitterMilliseconds = RandomNumberGenerator.GetInt32(
            0,
            CheckoutSubmissionJitterMaximumMilliseconds + 1);
        return TimeSpan.FromSeconds(baseSeconds) + TimeSpan.FromMilliseconds(jitterMilliseconds);
    }

    private StoredPaymentCheckout CreateLocalCheckoutAttempt(
        PaymentCheckoutDraft draft,
        MerchantGatewayPathwayResponse? pathway)
    {
        var providerPathwayKey = NormalizePathwayKey(pathway?.Key);
        return new StoredPaymentCheckout(
            CreateLocalCheckoutId(draft.InvoiceId),
            string.Empty,
            providerPathwayKey,
            draft.InvoiceId,
            draft.UserId,
            draft.IdempotencyKey,
            Id,
            false,
            draft.Market,
            draft.PaymentMethod,
            draft.Amount,
            draft.AmountMinor,
            draft.Credits,
            "received",
            draft.CreatedAtUtc,
            draft.CreatedAtUtc,
            draft.ExpiresAtUtc,
            null,
            null,
            null,
            draft.Customer,
            draft.PayerBank,
            CreateBankTransfer(pathway, draft),
            "Payment invoice was prepared locally. Slot credits are added only after the invoice is marked completed.");
    }

    private static string CreateLocalCheckoutId(string invoiceId) =>
        Convert.ToHexStringLower(
            SHA256.HashData(Encoding.UTF8.GetBytes(invoiceId)))[..32];

    private static BankTransferInstructions? CreateBankTransfer(
        MerchantGatewayPathwayResponse? pathway,
        PaymentCheckoutDraft draft)
    {
        if (pathway is null ||
            string.IsNullOrWhiteSpace(pathway.Bank) ||
            string.IsNullOrWhiteSpace(pathway.AccountHolder) ||
            string.IsNullOrWhiteSpace(pathway.AccountNumber))
        {
            return null;
        }

        var reference = !string.IsNullOrWhiteSpace(draft.Customer.CustomerReference)
            ? draft.Customer.CustomerReference
            : !string.IsNullOrWhiteSpace(draft.Customer.BeneficiaryReference)
                ? draft.Customer.BeneficiaryReference
                : draft.InvoiceId;
        return new BankTransferInstructions(
            pathway.Bank,
            pathway.AccountHolder,
            pathway.AccountNumber,
            string.IsNullOrWhiteSpace(pathway.BranchCode) ? "Not supplied" : pathway.BranchCode,
            reference,
            $"Transfer exactly {draft.Amount.ToString(CultureInfo.InvariantCulture)} {draft.Market.Currency} and use {reference} as the payment reference.");
    }

    private static PaymentError MapProviderError(HttpStatusCode statusCode, string? responseBody = null)
    {
        if (statusCode is HttpStatusCode.Unauthorized or HttpStatusCode.Forbidden)
        {
            return PaymentError.ProviderAuthenticationFailed;
        }

        if (statusCode is HttpStatusCode.BadRequest or HttpStatusCode.Conflict or HttpStatusCode.UnprocessableEntity)
        {
            return responseBody?.Contains("pathwayKey", StringComparison.OrdinalIgnoreCase) == true
                ? PaymentError.PaymentPathwayUnavailable
                : PaymentError.ProviderRejected;
        }

        return PaymentError.ProviderUnavailable;
    }

    private static bool IsAuthoritativeWithdrawalCreateRejection(
        HttpStatusCode statusCode,
        string? responseBody,
        bool isUncertainReplay)
    {
        if (isUncertainReplay)
        {
            return false;
        }

        if (statusCode == HttpStatusCode.Conflict)
        {
            return IsKnownNoCreateConflict(responseBody);
        }

        return statusCode is HttpStatusCode.BadRequest or
            HttpStatusCode.Unauthorized or
            HttpStatusCode.Forbidden or
            HttpStatusCode.UnprocessableEntity;
    }

    private static bool IsKnownNoCreateConflict(string? responseBody) =>
        responseBody?.Contains("no-create", StringComparison.OrdinalIgnoreCase) == true;

    private static string? NormalizePathwayKey(string? value) =>
        string.IsNullOrWhiteSpace(value) ? null : value.Trim();

    private static bool IsUsablePathwayKey(string? value) =>
        !string.IsNullOrWhiteSpace(value) &&
        !value.Trim().StartsWith("unconfigured-", StringComparison.OrdinalIgnoreCase);

    private static string Truncate(string value, int maxLength) =>
        value.Length <= maxLength ? value : value[..maxLength] + "…";

    private static bool MatchesLocalInvoice(
        StoredPaymentCheckout checkout,
        MerchantGatewayInvoiceResponse remote) =>
        remote.Id.ToString("N").Equals(checkout.ProviderCheckoutId, StringComparison.OrdinalIgnoreCase) &&
        string.Equals(remote.TheirNumber, checkout.InvoiceId, StringComparison.Ordinal) &&
        (string.IsNullOrWhiteSpace(remote.CustomerReference) ||
            string.Equals(remote.CustomerReference, checkout.Customer.CustomerReference, StringComparison.OrdinalIgnoreCase) ||
            string.Equals(remote.CustomerReference, checkout.UserId, StringComparison.Ordinal)) &&
        remote.Amount == checkout.Amount &&
        string.Equals(remote.Currency, checkout.Market.Currency, StringComparison.OrdinalIgnoreCase);

    private static string MapStatus(string? status, string fallback) => status?.Trim().ToLowerInvariant() switch
    {
        "pending" => "received",
        "processing" => "processing",
        "completed" => "completed",
        "cancelled" => "failed",
        _ => fallback
    };

    private static bool SatisfiesExpectedStatus(string actualStatus, string expectedStatus) =>
        expectedStatus switch
        {
            "received" => actualStatus is "received" or "processing" or "completed" or "failed" or "expired",
            "processing" => actualStatus is "processing" or "completed",
            "completed" => actualStatus is "completed",
            "failed" => actualStatus is "failed",
            "expired" => actualStatus is "expired",
            _ => false
        };

    private static string MapWithdrawalStatus(string? status, string fallback) =>
        WithdrawalStatusProjection.NormalizeProviderStatus(status) ?? fallback;
}

internal sealed record InvoiceReconciliationResult(
    StoredPaymentCheckout Checkout,
    PaymentReconciliationStatus Status);

internal sealed record MerchantGatewayInvoiceCreateRequest(
    string TheirNumber,
    decimal Amount,
    string Currency,
    string? PathwayKey,
    string? CustomerReference,
    string? BeneficiaryReference);

internal sealed record MerchantGatewayWithdrawalCreateRequest(
    string TheirNumber,
    decimal Amount,
    string Currency,
    string? PathwayKey,
    string AccountHolder,
    string BankName,
    string AccountNumber,
    string? BranchCode,
    string? AccountType);

internal sealed record MerchantGatewayCreatedResponse(
    Guid Id,
    long OurNumber,
    string Status,
    decimal? Amount,
    string? Currency,
    decimal? FeeAmount,
    decimal? NetAmount,
    string? RowVersion,
    bool? IdempotentReplay);

internal sealed record MerchantGatewayInvoiceResponse(
    Guid Id,
    long OurNumber,
    string TheirNumber,
    string? CustomerReference,
    string? BeneficiaryReference,
    decimal Amount,
    string Currency,
    decimal? FeeRate,
    decimal FeeAmount,
    decimal NetAmount,
    string Status,
    DateTimeOffset CreatedAtUtc,
    DateTimeOffset? CompletedAtUtc,
    string RowVersion);

internal sealed record MerchantGatewayPathwayResponse(
    string Key,
    string Name,
    string Bank,
    string? AccountHolder,
    string? AccountNumber,
    string? BranchCode,
    string? AccountType,
    decimal InvoiceRate,
    decimal WithdrawalRate);
