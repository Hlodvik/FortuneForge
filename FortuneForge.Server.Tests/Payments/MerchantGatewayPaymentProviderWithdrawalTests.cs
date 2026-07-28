using System.Net;
using System.Net.Http.Json;
using FortuneForge.Server.Payments;
using FortuneForge.Server.Payments.Configuration;
using FortuneForge.Server.Payments.Models;
using FortuneForge.Server.Payments.Providers;
using FortuneForge.Server.Payments.Storage;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Xunit;

namespace FortuneForge.Server.Tests.Payments;

public sealed class MerchantGatewayPaymentProviderWithdrawalTests
{
    private static readonly Guid RemoteWithdrawalId = Guid.Parse("22222222-2222-2222-2222-222222222222");

    [Fact]
    public async Task TimeoutAfterProviderCreateThenReplayRecoversWithoutDoubleDebitOrRefund()
    {
        var store = new InMemoryPaymentStore(availableCredits: 1_000);
        var handler = new QueueHttpMessageHandler(
            JsonResponse(HttpStatusCode.OK, Pathways("active-pathway-key")),
            Throw(new TaskCanceledException("Synthetic provider timeout after upstream create.")),
            JsonResponse(HttpStatusCode.OK, Pathways("active-pathway-key")),
            JsonResponse(HttpStatusCode.OK, CreatedWithdrawal(RemoteWithdrawalId, "Pending")));
        var provider = CreateProvider(store, handler);
        var firstDraft = CreateDraft("FF-WD-20260727010101000-AAAA");
        var replayDraft = CreateDraft("FF-WD-20260727010101999-BBBB");

        var first = await provider.CreateWithdrawalAsync(firstDraft, CancellationToken.None);
        var replay = await provider.CreateWithdrawalAsync(replayDraft, CancellationToken.None);

        Assert.NotNull(first.Value);
        Assert.Equal("pending", first.Value.Status);
        Assert.Equal(string.Empty, first.Value.ProviderWithdrawalId);
        Assert.NotNull(replay.Value);
        Assert.Equal(RemoteWithdrawalId.ToString("N"), replay.Value.ProviderWithdrawalId);
        Assert.Equal("pending", replay.Value.Status);
        Assert.Equal(900, store.AvailableCredits);
        Assert.Equal(1, store.ReservationDebitCount);
        Assert.Equal(0, store.RefundCount);
        Assert.Equal(1, store.UncertainMarkCount);
        Assert.Equal(1, store.ProviderUpdateCount);

        var posts = handler.CapturedRequests
            .Where(request => request.Method == HttpMethod.Post)
            .ToArray();
        Assert.Equal(2, posts.Length);
        Assert.Equal(posts[0].Body, posts[1].Body);
        Assert.All(posts, request =>
        {
            Assert.Equal(firstDraft.IdempotencyKey, request.IdempotencyKey);
            Assert.Contains(firstDraft.WithdrawalId, request.Body, StringComparison.Ordinal);
            Assert.DoesNotContain(replayDraft.WithdrawalId, request.Body, StringComparison.Ordinal);
        });
    }

    [Fact]
    public async Task NullOriginalPathwayRemainsNullOnUncertainReplayAndRecoversPublicId()
    {
        var store = new InMemoryPaymentStore(availableCredits: 1_000);
        var handler = new QueueHttpMessageHandler(
            JsonResponse(HttpStatusCode.OK, Array.Empty<object>()),
            Throw(new TaskCanceledException("Synthetic provider timeout after upstream create.")),
            JsonResponse(HttpStatusCode.OK, Pathways("newly-available-pathway-key")),
            JsonResponse(HttpStatusCode.OK, CreatedWithdrawal(RemoteWithdrawalId, "Pending")));
        var provider = CreateProvider(store, handler);
        var firstDraft = CreateDraft("FF-WD-20260727010606000-ZZZZ");
        var replayDraft = CreateDraft("FF-WD-20260727010606999-YYYY");

        var first = await provider.CreateWithdrawalAsync(firstDraft, CancellationToken.None);
        var replay = await provider.CreateWithdrawalAsync(replayDraft, CancellationToken.None);

        Assert.NotNull(first.Value);
        Assert.Equal("pending", first.Value.Status);
        Assert.Null(first.Value.ProviderPathwayKey);
        Assert.Equal(string.Empty, first.Value.ProviderWithdrawalId);
        Assert.NotNull(replay.Value);
        Assert.Equal(RemoteWithdrawalId.ToString("N"), replay.Value.ProviderWithdrawalId);
        Assert.Equal("pending", replay.Value.Status);
        Assert.Null(replay.Value.ProviderPathwayKey);
        Assert.Equal(900, store.AvailableCredits);
        Assert.Equal(1, store.ReservationDebitCount);
        Assert.Equal(0, store.RefundCount);
        Assert.Equal(1, store.UncertainMarkCount);
        Assert.Equal(1, store.ProviderUpdateCount);

        var posts = handler.CapturedRequests
            .Where(request => request.Method == HttpMethod.Post)
            .ToArray();
        Assert.Equal(2, posts.Length);
        Assert.Equal(posts[0].Body, posts[1].Body);
        Assert.Contains("\"pathwayKey\":null", posts[0].Body, StringComparison.Ordinal);
        Assert.DoesNotContain("newly-available-pathway-key", posts[1].Body, StringComparison.Ordinal);
        Assert.All(posts, request =>
        {
            Assert.Equal(firstDraft.IdempotencyKey, request.IdempotencyKey);
            Assert.Contains(firstDraft.WithdrawalId, request.Body, StringComparison.Ordinal);
            Assert.DoesNotContain(replayDraft.WithdrawalId, request.Body, StringComparison.Ordinal);
        });
    }

    [Fact]
    public async Task UncertainReplayWithPathwayDriftAndPayRelayConflictKeepsReservationHeld()
    {
        var store = new InMemoryPaymentStore(availableCredits: 1_000);
        var handler = new QueueHttpMessageHandler(
            JsonResponse(HttpStatusCode.OK, Pathways("active-pathway-key")),
            Throw(new TaskCanceledException("Synthetic provider timeout after upstream create.")),
            JsonResponse(HttpStatusCode.OK, Pathways("drifted-pathway-key")),
            RawResponse(
                HttpStatusCode.Conflict,
                "Idempotency-Key was already used with a different request"));
        var provider = CreateProvider(store, handler);
        var firstDraft = CreateDraft("FF-WD-20260727011111000-AAAA");
        var replayDraft = CreateDraft("FF-WD-20260727011111999-BBBB");

        var first = await provider.CreateWithdrawalAsync(firstDraft, CancellationToken.None);
        var replay = await provider.CreateWithdrawalAsync(replayDraft, CancellationToken.None);

        Assert.NotNull(first.Value);
        Assert.NotNull(replay.Value);
        Assert.Equal("pending", replay.Value.Status);
        Assert.Equal(string.Empty, replay.Value.ProviderWithdrawalId);
        Assert.Equal("active-pathway-key", replay.Value.ProviderPathwayKey);
        Assert.Equal(900, store.AvailableCredits);
        Assert.Equal(1, store.ReservationDebitCount);
        Assert.Equal(0, store.RefundCount);
        Assert.Equal(2, store.UncertainMarkCount);

        var posts = handler.CapturedRequests
            .Where(request => request.Method == HttpMethod.Post)
            .ToArray();
        Assert.Equal(2, posts.Length);
        Assert.Equal(posts[0].Body, posts[1].Body);
        Assert.Contains("\"pathwayKey\":\"active-pathway-key\"", posts[0].Body, StringComparison.Ordinal);
        Assert.DoesNotContain("drifted-pathway-key", posts[1].Body, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData(HttpStatusCode.BadRequest)]
    [InlineData(HttpStatusCode.Unauthorized)]
    [InlineData(HttpStatusCode.Forbidden)]
    [InlineData(HttpStatusCode.Conflict)]
    [InlineData(HttpStatusCode.UnprocessableEntity)]
    public async Task UncertainReplayNonSuccessKeepsReservationHeld(HttpStatusCode replayStatusCode)
    {
        var store = new InMemoryPaymentStore(availableCredits: 1_000);
        var handler = new QueueHttpMessageHandler(
            JsonResponse(HttpStatusCode.OK, Pathways("active-pathway-key")),
            Throw(new TaskCanceledException("Synthetic provider timeout after upstream create.")),
            JsonResponse(HttpStatusCode.OK, Pathways("replacement-pathway-key")),
            RawResponse(replayStatusCode, "Replay could not recover the provider id yet."));
        var provider = CreateProvider(store, handler);

        var first = await provider.CreateWithdrawalAsync(
            CreateDraft("FF-WD-20260727012121000-CCCC"),
            CancellationToken.None);
        var replay = await provider.CreateWithdrawalAsync(
            CreateDraft("FF-WD-20260727012121999-DDDD"),
            CancellationToken.None);

        Assert.NotNull(first.Value);
        Assert.NotNull(replay.Value);
        Assert.Equal("pending", replay.Value.Status);
        Assert.Equal(string.Empty, replay.Value.ProviderWithdrawalId);
        Assert.Equal("active-pathway-key", replay.Value.ProviderPathwayKey);
        Assert.Equal(900, store.AvailableCredits);
        Assert.Equal(1, store.ReservationDebitCount);
        Assert.Equal(0, store.RefundCount);
        Assert.Equal(2, store.UncertainMarkCount);

        var posts = handler.CapturedRequests
            .Where(request => request.Method == HttpMethod.Post)
            .ToArray();
        Assert.Equal(2, posts.Length);
        Assert.Equal(posts[0].Body, posts[1].Body);
    }

    [Fact]
    public async Task FirstAttemptConflictIsUncertainAndDoesNotRefundWithoutNoCreateClassifier()
    {
        var store = new InMemoryPaymentStore(availableCredits: 1_000);
        var handler = new QueueHttpMessageHandler(
            JsonResponse(HttpStatusCode.OK, Pathways("active-pathway-key")),
            RawResponse(
                HttpStatusCode.Conflict,
                "Idempotency-Key was already used with a different request"));
        var provider = CreateProvider(store, handler);

        var result = await provider.CreateWithdrawalAsync(
            CreateDraft("FF-WD-20260727013131000-EEEE"),
            CancellationToken.None);

        Assert.NotNull(result.Value);
        Assert.Equal("pending", result.Value.Status);
        Assert.Equal(900, store.AvailableCredits);
        Assert.Equal(1, store.ReservationDebitCount);
        Assert.Equal(0, store.RefundCount);
        Assert.Equal(1, store.UncertainMarkCount);
    }

    [Fact]
    public async Task InvalidSuccessResponsePreservesPendingReservationWithoutRefund()
    {
        var store = new InMemoryPaymentStore(availableCredits: 1_000);
        var handler = new QueueHttpMessageHandler(
            JsonResponse(HttpStatusCode.OK, Pathways("active-pathway-key")),
            RawResponse(HttpStatusCode.OK, "{not valid json"));
        var provider = CreateProvider(store, handler);

        var result = await provider.CreateWithdrawalAsync(
            CreateDraft("FF-WD-20260727020202000-BBBB"),
            CancellationToken.None);

        Assert.NotNull(result.Value);
        Assert.Equal("pending", result.Value.Status);
        Assert.Equal(string.Empty, result.Value.ProviderWithdrawalId);
        Assert.Equal(900, store.AvailableCredits);
        Assert.Equal(1, store.ReservationDebitCount);
        Assert.Equal(0, store.RefundCount);
        Assert.Equal(1, store.UncertainMarkCount);
        Assert.Equal(0, store.ProviderUpdateCount);
    }

    [Fact]
    public async Task TransientHttpResponsePreservesPendingReservationWithoutRefund()
    {
        var store = new InMemoryPaymentStore(availableCredits: 1_000);
        var handler = new QueueHttpMessageHandler(
            JsonResponse(HttpStatusCode.OK, Pathways("active-pathway-key")),
            RawResponse(HttpStatusCode.ServiceUnavailable, "gateway unavailable"));
        var provider = CreateProvider(store, handler);

        var result = await provider.CreateWithdrawalAsync(
            CreateDraft("FF-WD-20260727030303000-CCCC"),
            CancellationToken.None);

        Assert.NotNull(result.Value);
        Assert.Equal("pending", result.Value.Status);
        Assert.Equal(900, store.AvailableCredits);
        Assert.Equal(1, store.ReservationDebitCount);
        Assert.Equal(0, store.RefundCount);
        Assert.Equal(1, store.UncertainMarkCount);
    }

    [Fact]
    public async Task AuthoritativeRejectionRefundsOnceAndDoesNotReplayTerminalWithdrawal()
    {
        var store = new InMemoryPaymentStore(availableCredits: 1_000);
        var handler = new QueueHttpMessageHandler(
            JsonResponse(HttpStatusCode.OK, Pathways("active-pathway-key")),
            RawResponse(HttpStatusCode.UnprocessableEntity, "withdrawal rejected"),
            JsonResponse(HttpStatusCode.OK, Pathways("active-pathway-key")));
        var provider = CreateProvider(store, handler);
        var firstDraft = CreateDraft("FF-WD-20260727040404000-DDDD");
        var replayDraft = CreateDraft("FF-WD-20260727040404999-EEEE");

        var first = await provider.CreateWithdrawalAsync(firstDraft, CancellationToken.None);
        var replay = await provider.CreateWithdrawalAsync(replayDraft, CancellationToken.None);

        Assert.Null(first.Value);
        Assert.Equal(PaymentError.ProviderRejected, first.Error);
        Assert.Null(replay.Value);
        Assert.Equal(PaymentError.ProviderRejected, replay.Error);
        Assert.Equal("failed", store.Withdrawal!.Status);
        Assert.Equal(1_000, store.AvailableCredits);
        Assert.Equal(1, store.ReservationDebitCount);
        Assert.Equal(1, store.RefundCount);
        Assert.Equal(0, store.UncertainMarkCount);

        Assert.Single(handler.CapturedRequests, request => request.Method == HttpMethod.Post);
    }

    private static MerchantGatewayPaymentProvider CreateProvider(
        IPaymentStore store,
        QueueHttpMessageHandler handler) =>
        new(
            store,
            new TestHttpClientFactory(handler),
            Options.Create(new PaymentsOptions
            {
                Provider = "merchantgateway",
                MerchantGateway = new MerchantGatewayOptions
                {
                    BaseUrl = "https://gateway.test/",
                    ApiKey = "merchant-api-key-123456",
                    PathwayKeys = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
                    {
                        ["ZA"] = "active-pathway-key"
                    }
                }
            }),
            NullLogger<MerchantGatewayPaymentProvider>.Instance);

    private static PaymentWithdrawalDraft CreateDraft(string withdrawalId)
    {
        var market = PaymentCatalog.Markets.First(candidate => candidate.Code == "ZA");
        var createdAtUtc = DateTime.UtcNow;
        return new PaymentWithdrawalDraft(
            "fortune-forge-user-123",
            withdrawalId,
            "withdrawal-idempotency-key-123",
            market,
            10,
            1_000,
            100,
            new PaymentCustomerDetails(
                "Test",
                "Customer",
                "test@example.com",
                "ABCDEFGH",
                "ABCDEFGH"),
            new WithdrawalBankDetails(
                "Test Customer",
                "Test Bank",
                "1234567890",
                "250655",
                "Cheque"),
            createdAtUtc);
    }

    private static object[] Pathways(string key) =>
    [
        new
        {
            key,
            name = "Active ZA pathway",
            bank = "Test Bank",
            accountHolder = "Test Account",
            accountNumber = "1234567890",
            branchCode = "250655",
            accountType = "Cheque",
            invoiceRate = 0,
            withdrawalRate = 0
        }
    ];

    private static object CreatedWithdrawal(Guid id, string status) => new
    {
        id,
        ourNumber = 1000002,
        status,
        amount = 10,
        currency = "ZAR",
        feeAmount = 0,
        netAmount = 10,
        rowVersion = "row-version",
        idempotentReplay = true
    };

    private static Func<HttpRequestMessage, CancellationToken, Task<HttpResponseMessage>> JsonResponse(
        HttpStatusCode statusCode,
        object body) =>
        (_, _) => Task.FromResult(new HttpResponseMessage(statusCode)
        {
            Content = JsonContent.Create(body)
        });

    private static Func<HttpRequestMessage, CancellationToken, Task<HttpResponseMessage>> RawResponse(
        HttpStatusCode statusCode,
        string body) =>
        (_, _) => Task.FromResult(new HttpResponseMessage(statusCode)
        {
            Content = new StringContent(body)
        });

    private static Func<HttpRequestMessage, CancellationToken, Task<HttpResponseMessage>> Throw(
        Exception exception) =>
        (_, _) => Task.FromException<HttpResponseMessage>(exception);

    private sealed class InMemoryPaymentStore(long availableCredits) : IPaymentStore
    {
        private bool _refundRecorded;

        public StoredPaymentWithdrawal? Withdrawal { get; private set; }

        public long AvailableCredits { get; private set; } = availableCredits;

        public int ReservationDebitCount { get; private set; }

        public int RefundCount { get; private set; }

        public int UncertainMarkCount { get; private set; }

        public int ProviderUpdateCount { get; private set; }

        public Task<PaymentResult<StoredPaymentWithdrawal>> CreateWithdrawalReservationAsync(
            StoredPaymentWithdrawal withdrawal,
            CancellationToken cancellationToken)
        {
            if (Withdrawal is not null)
            {
                return Task.FromResult(Matches(Withdrawal, withdrawal)
                    ? PaymentResult<StoredPaymentWithdrawal>.Success(Withdrawal)
                    : PaymentResult<StoredPaymentWithdrawal>.Failure(PaymentError.IdempotencyConflict));
            }

            if (AvailableCredits < withdrawal.CreditsDebited)
            {
                return Task.FromResult(
                    PaymentResult<StoredPaymentWithdrawal>.Failure(PaymentError.InsufficientCredits));
            }

            AvailableCredits -= withdrawal.CreditsDebited;
            ReservationDebitCount++;
            Withdrawal = withdrawal;
            return Task.FromResult(PaymentResult<StoredPaymentWithdrawal>.Success(withdrawal));
        }

        public Task<PaymentResult<StoredPaymentWithdrawal>> UpdateWithdrawalProviderAsync(
            string withdrawalId,
            string userId,
            string providerWithdrawalId,
            string status,
            DateTime updatedAtUtc,
            CancellationToken cancellationToken)
        {
            if (Withdrawal is null ||
                Withdrawal.WithdrawalId != withdrawalId ||
                Withdrawal.UserId != userId)
            {
                return Task.FromResult(
                    PaymentResult<StoredPaymentWithdrawal>.Failure(PaymentError.CheckoutNotFound));
            }

            var normalized = WithdrawalStatusProjection.NormalizeProviderStatus(status);
            if (string.IsNullOrWhiteSpace(providerWithdrawalId) ||
                normalized is null ||
                !WithdrawalStatusProjection.CanApply(Withdrawal.Status, normalized))
            {
                return Task.FromResult(
                    PaymentResult<StoredPaymentWithdrawal>.Failure(
                        PaymentError.InvalidStatusTransition));
            }

            ProviderUpdateCount++;
            Withdrawal = Withdrawal with
            {
                ProviderWithdrawalId = providerWithdrawalId,
                Status = normalized,
                StatusUpdatedAtUtc = updatedAtUtc,
                Notice = WithdrawalStatusProjection.NoticeFor(normalized)
            };
            return Task.FromResult(PaymentResult<StoredPaymentWithdrawal>.Success(Withdrawal));
        }

        public Task<PaymentResult<StoredPaymentWithdrawal>> FailWithdrawalReservationAsync(
            string withdrawalId,
            string userId,
            DateTime updatedAtUtc,
            CancellationToken cancellationToken)
        {
            if (Withdrawal is null ||
                Withdrawal.WithdrawalId != withdrawalId ||
                Withdrawal.UserId != userId)
            {
                return Task.FromResult(
                    PaymentResult<StoredPaymentWithdrawal>.Failure(PaymentError.CheckoutNotFound));
            }

            if (!_refundRecorded)
            {
                _refundRecorded = true;
                AvailableCredits += Withdrawal.CreditsDebited;
                RefundCount++;
            }

            Withdrawal = Withdrawal with
            {
                Status = "failed",
                StatusUpdatedAtUtc = updatedAtUtc,
                Notice = "Withdrawal request failed before the payment provider accepted it. Reserved credits were returned."
            };
            return Task.FromResult(PaymentResult<StoredPaymentWithdrawal>.Success(Withdrawal));
        }

        public Task<PaymentResult<StoredPaymentWithdrawal>> MarkWithdrawalProviderSubmissionUncertainAsync(
            string withdrawalId,
            string userId,
            DateTime updatedAtUtc,
            CancellationToken cancellationToken)
        {
            if (Withdrawal is null ||
                Withdrawal.WithdrawalId != withdrawalId ||
                Withdrawal.UserId != userId)
            {
                return Task.FromResult(
                    PaymentResult<StoredPaymentWithdrawal>.Failure(PaymentError.CheckoutNotFound));
            }

            UncertainMarkCount++;
            Withdrawal = Withdrawal with
            {
                Status = "pending",
                StatusUpdatedAtUtc = updatedAtUtc,
                Notice = "Withdrawal request was submitted to the payment provider, but confirmation is pending. Reserved credits remain held."
            };
            return Task.FromResult(PaymentResult<StoredPaymentWithdrawal>.Success(Withdrawal));
        }

        public Task<PaymentResult<StoredPaymentCheckout>> CreateAsync(
            StoredPaymentCheckout checkout,
            CancellationToken cancellationToken) =>
            throw new NotImplementedException();

        public Task<PaymentResult<StoredPaymentCheckout>> UpdateCheckoutProviderAsync(
            string checkoutId,
            string userId,
            string providerCheckoutId,
            string status,
            BankTransferInstructions? bankTransfer,
            DateTime updatedAtUtc,
            CancellationToken cancellationToken) =>
            throw new NotImplementedException();

        public Task<PaymentCheckoutProviderSubmissionLease> TryBeginCheckoutProviderSubmissionAsync(
            string checkoutId,
            string userId,
            DateTime nowUtc,
            TimeSpan leaseDuration,
            CancellationToken cancellationToken) =>
            throw new NotImplementedException();

        public Task<PaymentResult<StoredPaymentCheckout>> MarkCheckoutProviderSubmissionUncertainAsync(
            string checkoutId,
            string userId,
            string leaseId,
            DateTime updatedAtUtc,
            DateTime nextRetryAtUtc,
            int? providerStatusCode,
            CancellationToken cancellationToken) =>
            throw new NotImplementedException();

        public Task<PaymentResult<StoredPaymentWithdrawal>> ProjectWithdrawalProviderStatusAsync(
            string providerId,
            string providerWithdrawalId,
            string status,
            DateTime updatedAtUtc,
            CancellationToken cancellationToken) =>
            throw new NotImplementedException();

        public Task<StoredPaymentCheckout?> FindByCheckoutIdAsync(
            string checkoutId,
            string userId,
            CancellationToken cancellationToken) =>
            throw new NotImplementedException();

        public Task<StoredPaymentCheckout?> FindByCheckoutIdForAdminAsync(
            string checkoutId,
            CancellationToken cancellationToken) =>
            throw new NotImplementedException();

        public Task<StoredPaymentCheckout?> FindByProviderCheckoutIdForAdminAsync(
            string providerId,
            string providerCheckoutId,
            CancellationToken cancellationToken) =>
            throw new NotImplementedException();

        public Task<StoredPaymentCheckout?> FindByInvoiceIdAsync(
            string invoiceId,
            string userId,
            CancellationToken cancellationToken) =>
            throw new NotImplementedException();

        public Task<StoredPaymentCheckout?> FindByInvoiceIdForAdminAsync(
            string invoiceId,
            CancellationToken cancellationToken) =>
            throw new NotImplementedException();

        public Task<IReadOnlyList<StoredPaymentCheckout>> ListAsync(
            string userId,
            int limit,
            CancellationToken cancellationToken) =>
            throw new NotImplementedException();

        public Task<IReadOnlyList<StoredPaymentCheckout>> ListPendingAsync(
            string providerId,
            int limit,
            CancellationToken cancellationToken) =>
            throw new NotImplementedException();

        public Task<PaymentResult<StoredPaymentCheckout>> UpdateStatusAsync(
            string checkoutId,
            string userId,
            string status,
            DateTime updatedAtUtc,
            CancellationToken cancellationToken) =>
            throw new NotImplementedException();

        public Task<PaymentProviderEventProcessingLease> BeginProviderEventProcessingAsync(
            string providerId,
            string eventId,
            string eventType,
            DateTime occurredAtUtc,
            DateTime receivedAtUtc,
            CancellationToken cancellationToken) =>
            throw new NotImplementedException();

        public Task MarkProviderEventAppliedAsync(
            string providerId,
            string eventId,
            DateTime appliedAtUtc,
            CancellationToken cancellationToken) =>
            throw new NotImplementedException();

        private static bool Matches(
            StoredPaymentWithdrawal existing,
            StoredPaymentWithdrawal proposed) =>
            string.Equals(existing.UserId, proposed.UserId, StringComparison.Ordinal) &&
            string.Equals(existing.IdempotencyKey, proposed.IdempotencyKey, StringComparison.Ordinal) &&
            string.Equals(existing.Market.Code, proposed.Market.Code, StringComparison.Ordinal) &&
            existing.Amount == proposed.Amount &&
            existing.CreditsDebited == proposed.CreditsDebited &&
            string.Equals(existing.Customer.Email, proposed.Customer.Email, StringComparison.OrdinalIgnoreCase) &&
            string.Equals(existing.Bank.AccountNumber, proposed.Bank.AccountNumber, StringComparison.Ordinal);
    }

    private sealed class QueueHttpMessageHandler(
        params Func<HttpRequestMessage, CancellationToken, Task<HttpResponseMessage>>[] responses)
        : HttpMessageHandler
    {
        private readonly Queue<Func<HttpRequestMessage, CancellationToken, Task<HttpResponseMessage>>> _responses =
            new(responses);

        public List<CapturedRequest> CapturedRequests { get; } = [];

        protected override async Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request,
            CancellationToken cancellationToken)
        {
            var body = request.Content is null
                ? string.Empty
                : await request.Content.ReadAsStringAsync(cancellationToken);
            var idempotencyKey = request.Headers.TryGetValues("Idempotency-Key", out var values)
                ? values.Single()
                : string.Empty;
            CapturedRequests.Add(new CapturedRequest(
                request.Method,
                request.RequestUri?.ToString() ?? string.Empty,
                idempotencyKey,
                body));

            if (_responses.Count == 0)
            {
                throw new InvalidOperationException("No queued HTTP response was available.");
            }

            return await _responses.Dequeue()(request, cancellationToken);
        }
    }

    private sealed record CapturedRequest(
        HttpMethod Method,
        string Uri,
        string IdempotencyKey,
        string Body);

    private sealed class TestHttpClientFactory(QueueHttpMessageHandler handler) : IHttpClientFactory
    {
        public HttpClient CreateClient(string name) => new(handler, disposeHandler: false)
        {
            BaseAddress = new Uri("https://gateway.test/")
        };
    }
}
