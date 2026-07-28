using System.Net;
using System.Net.Http.Json;
using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using FortuneForge.Server.Payments;
using FortuneForge.Server.Payments.Configuration;
using FortuneForge.Server.Payments.Models;
using FortuneForge.Server.Payments.Providers;
using FortuneForge.Server.Payments.Storage;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Xunit;

namespace FortuneForge.Server.Tests.Payments;

public sealed class MerchantGatewayPaymentProviderCheckoutTests
{
    private static readonly Guid RemoteInvoiceId = Guid.Parse("11111111-1111-1111-1111-111111111111");
    private const string SigningSecret = "fortune-forge-webhook-signing-secret-12345";

    [Fact]
    public async Task TimeoutAfterProviderCreateThenReplayRecoversExistingInvoiceWithoutNewAttempt()
    {
        var store = new InMemoryPaymentStore(availableCredits: 500);
        var handler = new QueueHttpMessageHandler(
            JsonResponse(HttpStatusCode.OK, Pathways("active-pathway-key")),
            Throw(new TaskCanceledException("Synthetic provider timeout after upstream invoice create.")),
            JsonResponse(HttpStatusCode.OK, Pathways("drifted-pathway-key")),
            JsonResponse(HttpStatusCode.OK, Pathways("another-drifted-pathway-key")),
            JsonResponse(HttpStatusCode.OK, CreatedInvoice(RemoteInvoiceId, "Pending")));
        var provider = CreateProvider(store, handler);
        var firstDraft = CreateDraft("FFDEP10001", "ABCDEFGH");
        var replayDraft = CreateDraft("FFDEP99999", "ZZZZ9999");

        var first = await provider.CreateCheckoutAsync(firstDraft, CancellationToken.None);
        var immediateReplay = await provider.CreateCheckoutAsync(replayDraft, CancellationToken.None);

        Assert.NotNull(first.Value);
        Assert.Equal("received", first.Value.Status);
        Assert.Equal(string.Empty, first.Value.ProviderCheckoutId);
        Assert.Equal("active-pathway-key", first.Value.ProviderPathwayKey);
        Assert.NotNull(first.Value.NextProviderSubmissionAtUtc);
        Assert.NotNull(immediateReplay.Value);
        Assert.Equal(first.Value.CheckoutId, immediateReplay.Value.CheckoutId);
        Assert.Equal(string.Empty, immediateReplay.Value.ProviderCheckoutId);

        var postsBeforeBackoff = handler.CapturedRequests
            .Where(request => request.Method == HttpMethod.Post)
            .ToArray();
        Assert.Single(postsBeforeBackoff);

        store.MakeCheckoutProviderSubmissionDue(first.Value.CheckoutId);
        var replay = await provider.CreateCheckoutAsync(replayDraft, CancellationToken.None);

        Assert.NotNull(replay.Value);
        Assert.Equal(first.Value.CheckoutId, replay.Value.CheckoutId);
        Assert.Equal(firstDraft.InvoiceId, replay.Value.InvoiceId);
        Assert.Equal(RemoteInvoiceId.ToString("N"), replay.Value.ProviderCheckoutId);
        Assert.Equal("received", replay.Value.Status);
        Assert.Equal("bound", replay.Value.ProviderSubmissionStatus);
        Assert.Null(replay.Value.ProviderSubmissionLeaseId);
        Assert.Null(replay.Value.NextProviderSubmissionAtUtc);
        Assert.Equal(500, store.AvailableCredits);
        Assert.Equal(0, store.CreditLedgerCount);
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
            Assert.Contains(firstDraft.InvoiceId, request.Body, StringComparison.Ordinal);
            Assert.Contains(firstDraft.Customer.CustomerReference, request.Body, StringComparison.Ordinal);
            Assert.Contains("\"pathwayKey\":\"active-pathway-key\"", request.Body, StringComparison.Ordinal);
            Assert.DoesNotContain(replayDraft.InvoiceId, request.Body, StringComparison.Ordinal);
            Assert.DoesNotContain(replayDraft.Customer.CustomerReference, request.Body, StringComparison.Ordinal);
            Assert.DoesNotContain("drifted-pathway-key", request.Body, StringComparison.Ordinal);
            Assert.DoesNotContain("another-drifted-pathway-key", request.Body, StringComparison.Ordinal);
        });
    }

    [Fact]
    public async Task NullOriginalPathwayRemainsNullOnUncertainInvoiceReplayAndRecoversProviderId()
    {
        var store = new InMemoryPaymentStore(availableCredits: 500);
        var handler = new QueueHttpMessageHandler(
            JsonResponse(HttpStatusCode.OK, Array.Empty<object>()),
            Throw(new TaskCanceledException("Synthetic provider timeout after upstream invoice create.")),
            JsonResponse(HttpStatusCode.OK, Pathways("newly-available-pathway-key")),
            JsonResponse(HttpStatusCode.OK, Pathways("newly-available-pathway-key")),
            JsonResponse(HttpStatusCode.OK, CreatedInvoice(RemoteInvoiceId, "Pending")));
        var provider = CreateProvider(store, handler);
        var firstDraft = CreateDraft("FFDEP20001", "HJKLM234");
        var replayDraft = CreateDraft("FFDEP29999", "PQRST567");

        var first = await provider.CreateCheckoutAsync(firstDraft, CancellationToken.None);
        var immediateReplay = await provider.CreateCheckoutAsync(replayDraft, CancellationToken.None);

        Assert.NotNull(first.Value);
        Assert.Equal("received", first.Value.Status);
        Assert.Null(first.Value.ProviderPathwayKey);
        Assert.Equal(string.Empty, first.Value.ProviderCheckoutId);
        Assert.NotNull(immediateReplay.Value);
        Assert.Equal(first.Value.CheckoutId, immediateReplay.Value.CheckoutId);
        Assert.Equal(string.Empty, immediateReplay.Value.ProviderCheckoutId);

        var postsBeforeBackoff = handler.CapturedRequests
            .Where(request => request.Method == HttpMethod.Post)
            .ToArray();
        Assert.Single(postsBeforeBackoff);

        store.MakeCheckoutProviderSubmissionDue(first.Value.CheckoutId);
        var replay = await provider.CreateCheckoutAsync(replayDraft, CancellationToken.None);

        Assert.NotNull(replay.Value);
        Assert.Equal(first.Value.CheckoutId, replay.Value.CheckoutId);
        Assert.Equal(RemoteInvoiceId.ToString("N"), replay.Value.ProviderCheckoutId);
        Assert.Null(replay.Value.ProviderPathwayKey);
        Assert.Equal(500, store.AvailableCredits);
        Assert.Equal(0, store.CreditLedgerCount);
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
            Assert.Contains(firstDraft.InvoiceId, request.Body, StringComparison.Ordinal);
            Assert.DoesNotContain(replayDraft.InvoiceId, request.Body, StringComparison.Ordinal);
        });
    }

    [Fact]
    public async Task ReconcilePendingAfterUncertainCreateReplaysExactRequestAndRecoversProviderId()
    {
        var store = new InMemoryPaymentStore(availableCredits: 500);
        var handler = new QueueHttpMessageHandler(
            JsonResponse(HttpStatusCode.OK, Pathways("active-pathway-key")),
            Throw(new TaskCanceledException("Synthetic provider timeout after upstream invoice create.")),
            JsonResponse(HttpStatusCode.OK, CreatedInvoice(RemoteInvoiceId, "Pending")));
        var provider = CreateProvider(store, handler);
        var draft = CreateDraft("FFDEP25001", "MNOP3456");

        var first = await provider.CreateCheckoutAsync(draft, CancellationToken.None);
        var skipped = await provider.ReconcilePendingAsync(CancellationToken.None);
        var stillUnbound = await store.FindByInvoiceIdForAdminAsync(
            draft.InvoiceId,
            CancellationToken.None);
        Assert.Equal(1, skipped);
        Assert.NotNull(stillUnbound);
        Assert.Equal(string.Empty, stillUnbound.ProviderCheckoutId);
        Assert.Single(handler.CapturedRequests, request => request.Method == HttpMethod.Post);

        store.MakeCheckoutProviderSubmissionDue(first.Value!.CheckoutId);
        var reconciled = await provider.ReconcilePendingAsync(CancellationToken.None);
        var recovered = await store.FindByInvoiceIdForAdminAsync(
            draft.InvoiceId,
            CancellationToken.None);

        Assert.NotNull(first.Value);
        Assert.Equal(string.Empty, first.Value.ProviderCheckoutId);
        Assert.Equal(1, reconciled);
        Assert.NotNull(recovered);
        Assert.Equal(RemoteInvoiceId.ToString("N"), recovered.ProviderCheckoutId);
        Assert.Equal(500, store.AvailableCredits);
        Assert.Equal(0, store.CreditLedgerCount);
        Assert.Equal(1, store.UncertainMarkCount);
        Assert.Equal(1, store.ProviderUpdateCount);

        var posts = handler.CapturedRequests
            .Where(request => request.Method == HttpMethod.Post)
            .ToArray();
        Assert.Equal(2, posts.Length);
        Assert.Equal(posts[0].Body, posts[1].Body);
        Assert.All(posts, request =>
        {
            Assert.Equal(draft.IdempotencyKey, request.IdempotencyKey);
            Assert.Contains(draft.InvoiceId, request.Body, StringComparison.Ordinal);
            Assert.Contains(draft.Customer.CustomerReference, request.Body, StringComparison.Ordinal);
        });
    }

    [Fact]
    public async Task ConcurrentUnboundInvoicePathsShareOneProviderSubmissionLease()
    {
        var store = new InMemoryPaymentStore(availableCredits: 500);
        var handler = new BlockingInvoiceCreateHttpMessageHandler();
        var provider = CreateProvider(store, handler);
        var draft = CreateDraft("FFDEP26001", "QRS789TU");

        var createTask = provider.CreateCheckoutAsync(draft, CancellationToken.None);
        var started = await Task.WhenAny(
            handler.PostStarted,
            Task.Delay(TimeSpan.FromSeconds(5)));
        Assert.Same(handler.PostStarted, started);

        var checkout = await store.FindByInvoiceIdForAdminAsync(
            draft.InvoiceId,
            CancellationToken.None);
        Assert.NotNull(checkout);
        Assert.Equal("submitting", checkout.ProviderSubmissionStatus);
        Assert.NotNull(checkout.ProviderSubmissionLeaseId);

        var retryTasks = new Task[]
        {
            provider.CreateCheckoutAsync(draft, CancellationToken.None),
            provider.GetInvoiceAsync(draft.InvoiceId, draft.UserId, CancellationToken.None),
            provider.ListInvoicesAsync(draft.UserId, 10, CancellationToken.None),
            provider.ReconcilePendingAsync(CancellationToken.None)
        };

        await Task.WhenAll(retryTasks);
        Assert.Single(handler.CapturedRequests, request => request.Method == HttpMethod.Post);
        Assert.Equal(500, store.AvailableCredits);
        Assert.Equal(0, store.CreditLedgerCount);
        Assert.Equal(0, store.UncertainMarkCount);

        handler.ReleasePost(RemoteInvoiceId);
        var created = await createTask;

        Assert.NotNull(created.Value);
        Assert.Equal(RemoteInvoiceId.ToString("N"), created.Value.ProviderCheckoutId);
        Assert.Equal("bound", created.Value.ProviderSubmissionStatus);
        Assert.Null(created.Value.ProviderSubmissionLeaseId);
        Assert.Null(created.Value.NextProviderSubmissionAtUtc);
        Assert.Single(handler.CapturedRequests, request => request.Method == HttpMethod.Post);
        Assert.Equal(1, store.ProviderUpdateCount);
        Assert.Equal(500, store.AvailableCredits);
        Assert.Equal(0, store.CreditLedgerCount);
    }

    [Fact]
    public async Task SubmissionPendingConflictSchedulesBackoffAndReplaysExactRequestOnlyWhenDue()
    {
        var store = new InMemoryPaymentStore(availableCredits: 500);
        var handler = new QueueHttpMessageHandler(
            JsonResponse(HttpStatusCode.OK, Pathways("active-pathway-key")),
            RawResponse(
                HttpStatusCode.Conflict,
                "Idempotency-Key is already associated with a SubmissionPending invoice."),
            JsonResponse(HttpStatusCode.OK, Array.Empty<object>()),
            JsonResponse(HttpStatusCode.OK, CreatedInvoice(RemoteInvoiceId, "Pending")));
        var provider = CreateProvider(store, handler);
        var draft = CreateDraft("FFDEP27001", "UVWX3456");

        var first = await provider.CreateCheckoutAsync(draft, CancellationToken.None);
        Assert.NotNull(first.Value);
        var uncertain = store.GetCheckout(first.Value.CheckoutId);
        Assert.Equal("received", uncertain.Status);
        Assert.Equal("uncertain", uncertain.ProviderSubmissionStatus);
        Assert.Equal(409, uncertain.LastProviderSubmissionStatusCode);
        Assert.NotNull(uncertain.NextProviderSubmissionAtUtc);
        Assert.True(uncertain.NextProviderSubmissionAtUtc > uncertain.LastProviderSubmissionAtUtc);
        Assert.Equal(string.Empty, uncertain.ProviderCheckoutId);
        Assert.Equal(500, store.AvailableCredits);
        Assert.Equal(0, store.CreditLedgerCount);

        await provider.GetInvoiceAsync(draft.InvoiceId, draft.UserId, CancellationToken.None);
        await provider.ReconcilePendingAsync(CancellationToken.None);
        await provider.ListInvoicesAsync(draft.UserId, 10, CancellationToken.None);
        Assert.Single(handler.CapturedRequests, request => request.Method == HttpMethod.Post);

        store.MakeCheckoutProviderSubmissionDue(first.Value.CheckoutId);
        var reconciled = await provider.ReconcilePendingAsync(CancellationToken.None);
        var recovered = store.GetCheckout(first.Value.CheckoutId);

        Assert.Equal(1, reconciled);
        Assert.Equal(RemoteInvoiceId.ToString("N"), recovered.ProviderCheckoutId);
        Assert.Equal("bound", recovered.ProviderSubmissionStatus);
        Assert.Null(recovered.ProviderSubmissionLeaseId);
        Assert.Null(recovered.NextProviderSubmissionAtUtc);
        Assert.Equal(500, store.AvailableCredits);
        Assert.Equal(0, store.CreditLedgerCount);
        Assert.Equal(1, store.UncertainMarkCount);
        Assert.Equal(1, store.ProviderUpdateCount);

        var posts = handler.CapturedRequests
            .Where(request => request.Method == HttpMethod.Post)
            .ToArray();
        Assert.Equal(2, posts.Length);
        Assert.Equal(posts[0].Body, posts[1].Body);
        Assert.All(posts, request =>
        {
            Assert.Equal(draft.IdempotencyKey, request.IdempotencyKey);
            Assert.Contains(draft.InvoiceId, request.Body, StringComparison.Ordinal);
            Assert.Contains(draft.Customer.CustomerReference, request.Body, StringComparison.Ordinal);
            Assert.Contains("\"pathwayKey\":\"active-pathway-key\"", request.Body, StringComparison.Ordinal);
        });
    }

    [Fact]
    public async Task InvalidSuccessResponseLeavesLocalInvoiceRetryableWithoutCrediting()
    {
        var store = new InMemoryPaymentStore(availableCredits: 500);
        var handler = new QueueHttpMessageHandler(
            JsonResponse(HttpStatusCode.OK, Pathways("active-pathway-key")),
            RawResponse(HttpStatusCode.OK, "{not valid json"));
        var provider = CreateProvider(store, handler);

        var result = await provider.CreateCheckoutAsync(
            CreateDraft("FFDEP30001", "WXYZ6789"),
            CancellationToken.None);

        Assert.NotNull(result.Value);
        Assert.Equal("received", result.Value.Status);
        Assert.Equal(string.Empty, result.Value.ProviderCheckoutId);
        Assert.Equal(500, store.AvailableCredits);
        Assert.Equal(0, store.CreditLedgerCount);
        Assert.Equal(1, store.UncertainMarkCount);
        Assert.Equal(0, store.ProviderUpdateCount);
    }

    [Fact]
    public async Task CompletedInvoiceWebhookWithBalanceFailureStaysRetryableThenRetryCreditsOnce()
    {
        const long startingBalance = 500;
        var remoteInvoiceId = Guid.NewGuid();
        var store = new InMemoryPaymentStore(availableCredits: startingBalance)
        {
            NextStatusUpdateFailure = PaymentError.AccountBalanceNotFound
        };
        var checkout = CreateStoredCheckout(remoteInvoiceId.ToString("N"), "received");
        store.AddCheckout(checkout);
        var handler = new QueueHttpMessageHandler(
            JsonResponse(HttpStatusCode.OK, RemoteInvoice(
                remoteInvoiceId,
                checkout.InvoiceId,
                checkout.Customer.CustomerReference,
                "Completed")),
            JsonResponse(HttpStatusCode.OK, RemoteInvoice(
                remoteInvoiceId,
                checkout.InvoiceId,
                checkout.Customer.CustomerReference,
                "Completed")));
        var provider = CreateProvider(store, handler);
        var service = CreateWebhookService(store, provider);
        var eventId = Guid.NewGuid();

        var first = await SendInvoiceWebhookAsync(
            service,
            eventId,
            "invoice.completed",
            remoteInvoiceId);
        var afterFirst = store.GetCheckout(checkout.CheckoutId);
        Assert.Equal(PaymentWebhookStatus.Retryable, first);
        Assert.Equal("received", afterFirst.Status);
        Assert.Equal(startingBalance, store.AvailableCredits);
        Assert.Equal(0, store.CreditLedgerCount);
        Assert.Equal(1, store.RecordedEventCount);
        Assert.Equal(0, store.AppliedEventCount);

        var second = await SendInvoiceWebhookAsync(
            service,
            eventId,
            "invoice.completed",
            remoteInvoiceId);
        var afterSecond = store.GetCheckout(checkout.CheckoutId);

        Assert.Equal(PaymentWebhookStatus.Accepted, second);
        Assert.Equal("completed", afterSecond.Status);
        Assert.Equal(startingBalance + checkout.Credits, store.AvailableCredits);
        Assert.Equal(1, store.CreditLedgerCount);
        Assert.Equal(1, store.RecordedEventCount);
        Assert.Equal(1, store.AppliedEventCount);
    }

    [Fact]
    public async Task InvoiceCompletedIdentityMismatchIsRetryableAndDoesNotCredit()
    {
        const long startingBalance = 500;
        var remoteInvoiceId = Guid.NewGuid();
        var store = new InMemoryPaymentStore(availableCredits: startingBalance);
        var checkout = CreateStoredCheckout(remoteInvoiceId.ToString("N"), "received");
        store.AddCheckout(checkout);
        var handler = new QueueHttpMessageHandler(
            JsonResponse(HttpStatusCode.OK, RemoteInvoice(
                remoteInvoiceId,
                "DIFFERENT-INVOICE",
                checkout.Customer.CustomerReference,
                "Completed")));
        var provider = CreateProvider(store, handler);
        var service = CreateWebhookService(store, provider);

        var result = await SendInvoiceWebhookAsync(
            service,
            Guid.NewGuid(),
            "invoice.completed",
            remoteInvoiceId);

        Assert.Equal(PaymentWebhookStatus.Retryable, result);
        Assert.Equal("received", store.GetCheckout(checkout.CheckoutId).Status);
        Assert.Equal(startingBalance, store.AvailableCredits);
        Assert.Equal(0, store.CreditLedgerCount);
        Assert.Equal(1, store.RecordedEventCount);
        Assert.Equal(0, store.AppliedEventCount);
    }

    [Fact]
    public async Task InvoiceProcessingInvalidTransitionIsRetryableAndDoesNotApplyEvent()
    {
        const long startingBalance = 600;
        var remoteInvoiceId = Guid.NewGuid();
        var store = new InMemoryPaymentStore(availableCredits: startingBalance);
        var checkout = CreateStoredCheckout(
            remoteInvoiceId.ToString("N"),
            "completed",
            creditedBalance: startingBalance,
            completedAtUtc: DateTime.UtcNow.AddMinutes(-5));
        store.AddCompletedCheckoutWithLedger(checkout);
        var handler = new QueueHttpMessageHandler(
            JsonResponse(HttpStatusCode.OK, RemoteInvoice(
                remoteInvoiceId,
                checkout.InvoiceId,
                checkout.Customer.CustomerReference,
                "Processing")));
        var provider = CreateProvider(store, handler);
        var service = CreateWebhookService(store, provider);

        var result = await SendInvoiceWebhookAsync(
            service,
            Guid.NewGuid(),
            "invoice.processing",
            remoteInvoiceId);

        Assert.Equal(PaymentWebhookStatus.Retryable, result);
        Assert.Equal("completed", store.GetCheckout(checkout.CheckoutId).Status);
        Assert.Equal(startingBalance, store.AvailableCredits);
        Assert.Equal(1, store.CreditLedgerCount);
        Assert.Equal(1, store.RecordedEventCount);
        Assert.Equal(0, store.AppliedEventCount);
    }

    [Fact]
    public async Task AlreadyCompletedInvoiceWebhookIsSafeNoOpAndDoesNotCreditTwice()
    {
        const long startingBalance = 600;
        var remoteInvoiceId = Guid.NewGuid();
        var store = new InMemoryPaymentStore(availableCredits: startingBalance);
        var checkout = CreateStoredCheckout(
            remoteInvoiceId.ToString("N"),
            "completed",
            creditedBalance: startingBalance,
            completedAtUtc: DateTime.UtcNow.AddMinutes(-5));
        store.AddCompletedCheckoutWithLedger(checkout);
        var handler = new QueueHttpMessageHandler(
            JsonResponse(HttpStatusCode.OK, RemoteInvoice(
                remoteInvoiceId,
                checkout.InvoiceId,
                checkout.Customer.CustomerReference,
                "Completed")));
        var provider = CreateProvider(store, handler);
        var service = CreateWebhookService(store, provider);

        var result = await SendInvoiceWebhookAsync(
            service,
            Guid.NewGuid(),
            "invoice.completed",
            remoteInvoiceId);

        Assert.Equal(PaymentWebhookStatus.Accepted, result);
        Assert.Equal("completed", store.GetCheckout(checkout.CheckoutId).Status);
        Assert.Equal(startingBalance, store.AvailableCredits);
        Assert.Equal(1, store.CreditLedgerCount);
        Assert.Equal(1, store.RecordedEventCount);
        Assert.Equal(1, store.AppliedEventCount);
    }

    [Fact]
    public async Task CompletedInvoiceWithoutCreditedBalanceIsRetryableNotSafeNoOp()
    {
        const long startingBalance = 600;
        var remoteInvoiceId = Guid.NewGuid();
        var store = new InMemoryPaymentStore(availableCredits: startingBalance);
        var checkout = CreateStoredCheckout(
            remoteInvoiceId.ToString("N"),
            "completed",
            creditedBalance: null,
            completedAtUtc: DateTime.UtcNow.AddMinutes(-5));
        store.AddCheckout(checkout);
        var handler = new QueueHttpMessageHandler(
            JsonResponse(HttpStatusCode.OK, RemoteInvoice(
                remoteInvoiceId,
                checkout.InvoiceId,
                checkout.Customer.CustomerReference,
                "Completed")));
        var provider = CreateProvider(store, handler);
        var service = CreateWebhookService(store, provider);

        var result = await SendInvoiceWebhookAsync(
            service,
            Guid.NewGuid(),
            "invoice.completed",
            remoteInvoiceId);

        Assert.Equal(PaymentWebhookStatus.Retryable, result);
        Assert.Equal("completed", store.GetCheckout(checkout.CheckoutId).Status);
        Assert.Equal(startingBalance, store.AvailableCredits);
        Assert.Equal(0, store.CreditLedgerCount);
        Assert.Equal(1, store.RecordedEventCount);
        Assert.Equal(0, store.AppliedEventCount);
    }

    [Fact]
    public async Task CompletedInvoiceWebhookAddsCalculatedCreditsAndOneLedger()
    {
        const long startingBalance = 500;
        var remoteInvoiceId = Guid.NewGuid();
        var store = new InMemoryPaymentStore(availableCredits: startingBalance);
        var checkout = CreateStoredCheckout(remoteInvoiceId.ToString("N"), "received");
        store.AddCheckout(checkout);
        var handler = new QueueHttpMessageHandler(
            JsonResponse(HttpStatusCode.OK, RemoteInvoice(
                remoteInvoiceId,
                checkout.InvoiceId,
                checkout.Customer.CustomerReference,
                "Completed")));
        var provider = CreateProvider(store, handler);
        var service = CreateWebhookService(store, provider);

        var result = await SendInvoiceWebhookAsync(
            service,
            Guid.NewGuid(),
            "invoice.completed",
            remoteInvoiceId);

        Assert.Equal(PaymentWebhookStatus.Accepted, result);
        var updated = store.GetCheckout(checkout.CheckoutId);
        Assert.Equal("completed", updated.Status);
        Assert.Equal(startingBalance + checkout.Credits, store.AvailableCredits);
        Assert.Equal(startingBalance + checkout.Credits, updated.CreditedBalance);
        Assert.Equal(1, store.CreditLedgerCount);
        Assert.Equal(1, store.RecordedEventCount);
        Assert.Equal(1, store.AppliedEventCount);
    }

    private static MerchantGatewayPaymentProvider CreateProvider(
        IPaymentStore store,
        HttpMessageHandler handler) =>
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

    private static PaymentWebhookService CreateWebhookService(
        IPaymentStore store,
        MerchantGatewayPaymentProvider provider) =>
        new(
            store,
            provider,
            Options.Create(new PaymentsOptions
            {
                Provider = "merchantgateway",
                MerchantGateway = new MerchantGatewayOptions
                {
                    WebhookSigningSecrets = [SigningSecret],
                    WebhookToleranceSeconds = 300
                }
            }),
            NullLogger<PaymentWebhookService>.Instance);

    private static async Task<PaymentWebhookStatus> SendInvoiceWebhookAsync(
        PaymentWebhookService service,
        Guid eventId,
        string eventType,
        Guid remoteInvoiceId)
    {
        var occurredAtUtc = DateTimeOffset.UtcNow;
        var body = JsonSerializer.SerializeToUtf8Bytes(new
        {
            eventId,
            type = eventType,
            occurredAtUtc,
            data = new
            {
                publicId = remoteInvoiceId
            }
        });
        var timestamp = DateTimeOffset.UtcNow.ToUnixTimeSeconds();
        return await service.HandleMerchantGatewayAsync(
            eventId.ToString("D"),
            eventType,
            timestamp.ToString(CultureInfo.InvariantCulture),
            CreateSignature(timestamp, eventId, body),
            body,
            CancellationToken.None);
    }

    private static string CreateSignature(long timestamp, Guid eventId, byte[] body)
    {
        var prefix = Encoding.UTF8.GetBytes(
            string.Create(CultureInfo.InvariantCulture, $"{timestamp}.{eventId:D}."));
        var input = new byte[prefix.Length + body.Length];
        prefix.CopyTo(input, 0);
        body.CopyTo(input.AsSpan(prefix.Length));
        return $"v1={Convert.ToHexString(HMACSHA256.HashData(
            Encoding.UTF8.GetBytes(SigningSecret),
            input)).ToLowerInvariant()}";
    }

    private static PaymentCheckoutDraft CreateDraft(
        string invoiceId,
        string customerReference)
    {
        var market = PaymentCatalog.Markets.First(candidate => candidate.Code == "ZA");
        var paymentMethod = market.PaymentMethods.First(candidate => candidate.Id == "regional-bank-transfer");
        var createdAtUtc = DateTime.UtcNow;
        return new PaymentCheckoutDraft(
            "fortune-forge-user-123",
            invoiceId,
            "checkout-idempotency-key-123",
            market,
            paymentMethod,
            10,
            1_000,
            100,
            new PaymentCustomerDetails(
                "Test",
                "Customer",
                "test@example.com",
                customerReference,
                customerReference),
            new PaymentBankDetails(
                "Test Customer",
                "Test Bank",
                "1234567890",
                "250655",
                "Cheque"),
            createdAtUtc,
            createdAtUtc.AddMinutes(30));
    }

    private static StoredPaymentCheckout CreateStoredCheckout(
        string providerCheckoutId,
        string status,
        long? creditedBalance = null,
        DateTime? completedAtUtc = null)
    {
        var market = PaymentCatalog.Markets.First(candidate => candidate.Code == "ZA");
        var paymentMethod = market.PaymentMethods.First(candidate => candidate.Id == "regional-bank-transfer");
        var createdAtUtc = DateTime.UtcNow.AddMinutes(-10);
        return new StoredPaymentCheckout(
            Guid.NewGuid().ToString("N"),
            providerCheckoutId,
            "active-pathway-key",
            $"FFDEP{RandomNumberGenerator.GetInt32(10000, 99999)}",
            "fortune-forge-user-123",
            Guid.NewGuid().ToString("N"),
            "merchantgateway-api",
            false,
            market,
            paymentMethod,
            10,
            1_000,
            100,
            status,
            completedAtUtc ?? createdAtUtc,
            createdAtUtc,
            createdAtUtc.AddMinutes(30),
            status == "processing" ? createdAtUtc.AddMinutes(1) : null,
            completedAtUtc,
            creditedBalance,
            new PaymentCustomerDetails(
                "Test",
                "Customer",
                "test@example.com",
                "ABCD2345",
                "ABCD2345"),
            new PaymentBankDetails(
                "Test Customer",
                "Test Bank",
                "1234567890",
                "250655",
                "Cheque"),
            new BankTransferInstructions(
                "Test Bank",
                "Test Account",
                "1234567890",
                "250655",
                "ABCD2345",
                "Transfer exactly 10 ZAR and use ABCD2345 as the payment reference."),
            "Payment confirmation is pending.");
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

    private static object CreatedInvoice(Guid id, string status) => new
    {
        id,
        ourNumber = 1000001,
        status,
        amount = 10,
        currency = "ZAR",
        feeAmount = 0,
        netAmount = 10,
        rowVersion = "row-version",
        idempotentReplay = true
    };

    private static object RemoteInvoice(
        Guid id,
        string theirNumber,
        string customerReference,
        string status) => new
    {
        id,
        ourNumber = 1000001,
        theirNumber,
        customerReference,
        beneficiaryReference = customerReference,
        amount = 10,
        currency = "ZAR",
        feeRate = 0,
        feeAmount = 0,
        netAmount = 10,
        status,
        createdAtUtc = DateTimeOffset.UtcNow.AddMinutes(-5),
        completedAtUtc = status.Equals("Completed", StringComparison.OrdinalIgnoreCase)
            ? DateTimeOffset.UtcNow
            : (DateTimeOffset?)null,
        rowVersion = "row-version"
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
        private readonly object _sync = new();
        private readonly Dictionary<string, StoredPaymentCheckout> _checkouts =
            new(StringComparer.OrdinalIgnoreCase);
        private readonly Dictionary<string, string> _checkoutIdByIdempotency =
            new(StringComparer.Ordinal);
        private readonly Dictionary<string, string> _checkoutIdByInvoiceId =
            new(StringComparer.Ordinal);
        private readonly Dictionary<string, string> _checkoutIdByProviderId =
            new(StringComparer.OrdinalIgnoreCase);
        private readonly Dictionary<string, ProviderEventRecord> _providerEvents =
            new(StringComparer.OrdinalIgnoreCase);
        private readonly HashSet<string> _creditLedger =
            new(StringComparer.OrdinalIgnoreCase);

        public long AvailableCredits { get; private set; } = availableCredits;

        public PaymentError? NextStatusUpdateFailure { get; set; }

        public int CreditLedgerCount => _creditLedger.Count;

        public int RecordedEventCount => _providerEvents.Count;

        public int AppliedEventCount => _providerEvents.Values.Count(providerEvent =>
            providerEvent.State == PaymentProviderEventProcessingState.Applied);

        public int UncertainMarkCount { get; private set; }

        public int ProviderUpdateCount { get; private set; }

        public void AddCheckout(StoredPaymentCheckout checkout)
        {
            lock (_sync)
            {
                _checkouts[checkout.CheckoutId] = checkout;
                _checkoutIdByInvoiceId[checkout.InvoiceId] = checkout.CheckoutId;
                if (!string.IsNullOrWhiteSpace(checkout.ProviderCheckoutId))
                {
                    _checkoutIdByProviderId[
                        ProviderKey(checkout.ProviderId, checkout.ProviderCheckoutId)] = checkout.CheckoutId;
                }
            }
        }

        public void AddCompletedCheckoutWithLedger(StoredPaymentCheckout checkout)
        {
            AddCheckout(checkout);
            _creditLedger.Add(checkout.CheckoutId);
        }

        public StoredPaymentCheckout GetCheckout(string checkoutId)
        {
            lock (_sync)
            {
                return _checkouts[checkoutId];
            }
        }

        public void MakeCheckoutProviderSubmissionDue(string checkoutId)
        {
            lock (_sync)
            {
                var checkout = _checkouts[checkoutId];
                _checkouts[checkoutId] = checkout with
                {
                    ProviderSubmissionLeaseId = null,
                    ProviderSubmissionLeaseUntilUtc = null,
                    NextProviderSubmissionAtUtc = DateTime.UtcNow.AddSeconds(-1)
                };
            }
        }

        public Task<PaymentResult<StoredPaymentCheckout>> CreateAsync(
            StoredPaymentCheckout checkout,
            CancellationToken cancellationToken)
        {
            lock (_sync)
            {
                var idempotencyKey = IdempotencyKey(checkout.UserId, checkout.IdempotencyKey);
                if (_checkoutIdByIdempotency.TryGetValue(idempotencyKey, out var existingCheckoutId))
                {
                    var existing = _checkouts[existingCheckoutId];
                    return Task.FromResult(Matches(existing, checkout)
                        ? PaymentResult<StoredPaymentCheckout>.Success(existing)
                        : PaymentResult<StoredPaymentCheckout>.Failure(PaymentError.IdempotencyConflict));
                }

                if (_checkoutIdByInvoiceId.ContainsKey(checkout.InvoiceId) ||
                    _checkouts.ContainsKey(checkout.CheckoutId))
                {
                    return Task.FromResult(
                        PaymentResult<StoredPaymentCheckout>.Failure(PaymentError.InvoiceConflict));
                }

                _checkouts[checkout.CheckoutId] = checkout;
                _checkoutIdByIdempotency[idempotencyKey] = checkout.CheckoutId;
                _checkoutIdByInvoiceId[checkout.InvoiceId] = checkout.CheckoutId;
                return Task.FromResult(PaymentResult<StoredPaymentCheckout>.Success(checkout));
            }
        }

        public Task<PaymentResult<StoredPaymentCheckout>> UpdateCheckoutProviderAsync(
            string checkoutId,
            string userId,
            string providerCheckoutId,
            string status,
            BankTransferInstructions? bankTransfer,
            DateTime updatedAtUtc,
            CancellationToken cancellationToken)
        {
            lock (_sync)
            {
                if (!_checkouts.TryGetValue(checkoutId, out var checkout) ||
                    checkout.UserId != userId ||
                    string.IsNullOrWhiteSpace(providerCheckoutId))
                {
                    return Task.FromResult(
                        PaymentResult<StoredPaymentCheckout>.Failure(PaymentError.CheckoutNotFound));
                }

                if (!string.IsNullOrWhiteSpace(checkout.ProviderCheckoutId) &&
                    !checkout.ProviderCheckoutId.Equals(providerCheckoutId, StringComparison.OrdinalIgnoreCase))
                {
                    return Task.FromResult(
                        PaymentResult<StoredPaymentCheckout>.Failure(PaymentError.InvalidStatusTransition));
                }

                if (!checkout.Status.Equals(status, StringComparison.Ordinal) &&
                    !CanTransition(checkout.Status, status))
                {
                    return Task.FromResult(
                        PaymentResult<StoredPaymentCheckout>.Failure(PaymentError.InvalidStatusTransition));
                }

                if (string.IsNullOrWhiteSpace(checkout.ProviderCheckoutId))
                {
                    ProviderUpdateCount++;
                }

                var updated = checkout with
                {
                    ProviderCheckoutId = providerCheckoutId,
                    Status = status,
                    StatusUpdatedAtUtc = updatedAtUtc,
                    BankTransfer = bankTransfer ?? checkout.BankTransfer,
                    ProviderSubmissionStatus = "bound",
                    ProviderSubmissionLeaseId = null,
                    ProviderSubmissionLeaseUntilUtc = null,
                    NextProviderSubmissionAtUtc = null
                };

                if (status == "processing")
                {
                    updated = updated with { ProcessingAtUtc = updatedAtUtc };
                }
                else if (status == "completed")
                {
                    if (_creditLedger.Add(checkout.CheckoutId))
                    {
                        AvailableCredits += checkout.Credits;
                    }

                    updated = updated with
                    {
                        CompletedAtUtc = updatedAtUtc,
                        CreditedBalance = AvailableCredits
                    };
                }

                _checkouts[checkoutId] = updated;
                _checkoutIdByProviderId[ProviderKey(updated.ProviderId, providerCheckoutId)] = checkoutId;
                return Task.FromResult(PaymentResult<StoredPaymentCheckout>.Success(updated));
            }
        }

        public Task<PaymentCheckoutProviderSubmissionLease> TryBeginCheckoutProviderSubmissionAsync(
            string checkoutId,
            string userId,
            DateTime nowUtc,
            TimeSpan leaseDuration,
            CancellationToken cancellationToken)
        {
            lock (_sync)
            {
                if (!_checkouts.TryGetValue(checkoutId, out var checkout) ||
                    checkout.UserId != userId)
                {
                    return Task.FromResult(new PaymentCheckoutProviderSubmissionLease(
                        PaymentCheckoutProviderSubmissionLeaseState.NotFound,
                        null,
                        null));
                }

                if (!string.IsNullOrWhiteSpace(checkout.ProviderCheckoutId))
                {
                    return Task.FromResult(new PaymentCheckoutProviderSubmissionLease(
                        PaymentCheckoutProviderSubmissionLeaseState.AlreadyBound,
                        checkout,
                        null));
                }

                if (checkout.Status is "completed" or "failed" or "expired")
                {
                    return Task.FromResult(new PaymentCheckoutProviderSubmissionLease(
                        PaymentCheckoutProviderSubmissionLeaseState.Terminal,
                        checkout,
                        null));
                }

                if (checkout.NextProviderSubmissionAtUtc is { } nextRetryAtUtc &&
                    nextRetryAtUtc > nowUtc)
                {
                    return Task.FromResult(new PaymentCheckoutProviderSubmissionLease(
                        PaymentCheckoutProviderSubmissionLeaseState.NotDue,
                        checkout,
                        null));
                }

                if (checkout.ProviderSubmissionLeaseUntilUtc is { } leaseUntilUtc &&
                    leaseUntilUtc > nowUtc &&
                    !string.IsNullOrWhiteSpace(checkout.ProviderSubmissionLeaseId))
                {
                    return Task.FromResult(new PaymentCheckoutProviderSubmissionLease(
                        PaymentCheckoutProviderSubmissionLeaseState.NotDue,
                        checkout,
                        null));
                }

                var leaseId = Guid.NewGuid().ToString("N");
                var updated = checkout with
                {
                    ProviderSubmissionStatus = "submitting",
                    ProviderSubmissionLeaseId = leaseId,
                    ProviderSubmissionLeaseUntilUtc = nowUtc.Add(leaseDuration),
                    LastProviderSubmissionAtUtc = nowUtc,
                    ProviderSubmissionAttempt = Math.Max(0, checkout.ProviderSubmissionAttempt) + 1
                };
                _checkouts[checkoutId] = updated;
                return Task.FromResult(new PaymentCheckoutProviderSubmissionLease(
                    PaymentCheckoutProviderSubmissionLeaseState.Acquired,
                    updated,
                    leaseId));
            }
        }

        public Task<PaymentResult<StoredPaymentCheckout>> MarkCheckoutProviderSubmissionUncertainAsync(
            string checkoutId,
            string userId,
            string leaseId,
            DateTime updatedAtUtc,
            DateTime nextRetryAtUtc,
            int? providerStatusCode,
            CancellationToken cancellationToken)
        {
            lock (_sync)
            {
                if (!_checkouts.TryGetValue(checkoutId, out var checkout) ||
                    checkout.UserId != userId)
                {
                    return Task.FromResult(
                        PaymentResult<StoredPaymentCheckout>.Failure(PaymentError.CheckoutNotFound));
                }

                if (!string.IsNullOrWhiteSpace(checkout.ProviderCheckoutId) ||
                    checkout.Status is "completed" or "failed" or "expired")
                {
                    return Task.FromResult(PaymentResult<StoredPaymentCheckout>.Success(checkout));
                }

                if (!string.Equals(checkout.ProviderSubmissionLeaseId, leaseId, StringComparison.Ordinal))
                {
                    return Task.FromResult(PaymentResult<StoredPaymentCheckout>.Success(checkout));
                }

                UncertainMarkCount++;
                var updated = checkout with
                {
                    Status = "received",
                    StatusUpdatedAtUtc = updatedAtUtc,
                    ProviderSubmissionStatus = "uncertain",
                    ProviderSubmissionLeaseId = null,
                    ProviderSubmissionLeaseUntilUtc = null,
                    NextProviderSubmissionAtUtc = nextRetryAtUtc,
                    LastProviderSubmissionStatusCode = providerStatusCode,
                    Notice = "Payment invoice was submitted to the payment provider, but confirmation is pending. The same invoice will be retried automatically."
                };
                _checkouts[checkoutId] = updated;
                return Task.FromResult(PaymentResult<StoredPaymentCheckout>.Success(updated));
            }
        }

        public Task<StoredPaymentCheckout?> FindByCheckoutIdAsync(
            string checkoutId,
            string userId,
            CancellationToken cancellationToken)
        {
            lock (_sync)
            {
                var checkout = _checkouts.GetValueOrDefault(checkoutId);
                if (checkout is null &&
                    _checkoutIdByProviderId.TryGetValue(ProviderKey("merchantgateway-api", checkoutId), out var localId))
                {
                    checkout = _checkouts[localId];
                }

                return Task.FromResult(
                    checkout is not null && checkout.UserId == userId ? checkout : null);
            }
        }

        public Task<StoredPaymentCheckout?> FindByCheckoutIdForAdminAsync(
            string checkoutId,
            CancellationToken cancellationToken)
        {
            lock (_sync)
            {
                var checkout = _checkouts.GetValueOrDefault(checkoutId);
                if (checkout is null &&
                    _checkoutIdByProviderId.TryGetValue(ProviderKey("merchantgateway-api", checkoutId), out var localId))
                {
                    checkout = _checkouts[localId];
                }

                return Task.FromResult(checkout);
            }
        }

        public Task<StoredPaymentCheckout?> FindByProviderCheckoutIdForAdminAsync(
            string providerId,
            string providerCheckoutId,
            CancellationToken cancellationToken)
        {
            lock (_sync)
            {
                var checkout = _checkoutIdByProviderId.TryGetValue(
                    ProviderKey(providerId, providerCheckoutId),
                    out var checkoutId)
                    ? _checkouts[checkoutId]
                    : null;
                return Task.FromResult(checkout);
            }
        }

        public Task<StoredPaymentCheckout?> FindByInvoiceIdAsync(
            string invoiceId,
            string userId,
            CancellationToken cancellationToken)
        {
            lock (_sync)
            {
                var checkout = _checkoutIdByInvoiceId.TryGetValue(invoiceId, out var checkoutId)
                    ? _checkouts[checkoutId]
                    : null;
                return Task.FromResult(
                    checkout is not null && checkout.UserId == userId ? checkout : null);
            }
        }

        public Task<StoredPaymentCheckout?> FindByInvoiceIdForAdminAsync(
            string invoiceId,
            CancellationToken cancellationToken)
        {
            lock (_sync)
            {
                var checkout = _checkoutIdByInvoiceId.TryGetValue(invoiceId, out var checkoutId)
                    ? _checkouts[checkoutId]
                    : null;
                return Task.FromResult(checkout);
            }
        }

        public Task<IReadOnlyList<StoredPaymentCheckout>> ListAsync(
            string userId,
            int limit,
            CancellationToken cancellationToken)
        {
            lock (_sync)
            {
                return Task.FromResult<IReadOnlyList<StoredPaymentCheckout>>(
                    _checkouts.Values
                        .Where(checkout => checkout.UserId == userId)
                        .Take(limit)
                        .ToArray());
            }
        }

        public Task<IReadOnlyList<StoredPaymentCheckout>> ListPendingAsync(
            string providerId,
            int limit,
            CancellationToken cancellationToken)
        {
            lock (_sync)
            {
                return Task.FromResult<IReadOnlyList<StoredPaymentCheckout>>(
                    _checkouts.Values
                        .Where(checkout =>
                            checkout.ProviderId == providerId &&
                            checkout.Status is "received" or "processing")
                        .Take(limit)
                        .ToArray());
            }
        }

        public Task<PaymentResult<StoredPaymentCheckout>> UpdateStatusAsync(
            string checkoutId,
            string userId,
            string status,
            DateTime updatedAtUtc,
            CancellationToken cancellationToken)
        {
            if (!_checkouts.TryGetValue(checkoutId, out var checkout) ||
                checkout.UserId != userId)
            {
                return Task.FromResult(
                    PaymentResult<StoredPaymentCheckout>.Failure(PaymentError.CheckoutNotFound));
            }

            if (NextStatusUpdateFailure is { } failure)
            {
                NextStatusUpdateFailure = null;
                return Task.FromResult(
                    PaymentResult<StoredPaymentCheckout>.Failure(failure));
            }

            if (!checkout.Status.Equals(status, StringComparison.Ordinal) &&
                !CanTransition(checkout.Status, status))
            {
                return Task.FromResult(
                    PaymentResult<StoredPaymentCheckout>.Failure(PaymentError.InvalidStatusTransition));
            }

            var updated = checkout with
            {
                Status = status,
                StatusUpdatedAtUtc = updatedAtUtc
            };
            if (status == "completed")
            {
                if (_creditLedger.Add(checkout.CheckoutId))
                {
                    AvailableCredits += checkout.Credits;
                }

                updated = updated with
                {
                    CompletedAtUtc = updatedAtUtc,
                    CreditedBalance = AvailableCredits
                };
            }

            _checkouts[checkoutId] = updated;
            return Task.FromResult(PaymentResult<StoredPaymentCheckout>.Success(updated));
        }

        public Task<PaymentResult<StoredPaymentWithdrawal>> CreateWithdrawalReservationAsync(
            StoredPaymentWithdrawal withdrawal,
            CancellationToken cancellationToken) =>
            throw new NotImplementedException();

        public Task<PaymentResult<StoredPaymentWithdrawal>> UpdateWithdrawalProviderAsync(
            string withdrawalId,
            string userId,
            string providerWithdrawalId,
            string status,
            DateTime updatedAtUtc,
            CancellationToken cancellationToken) =>
            throw new NotImplementedException();

        public Task<PaymentResult<StoredPaymentWithdrawal>> FailWithdrawalReservationAsync(
            string withdrawalId,
            string userId,
            DateTime updatedAtUtc,
            CancellationToken cancellationToken) =>
            throw new NotImplementedException();

        public Task<PaymentResult<StoredPaymentWithdrawal>> MarkWithdrawalProviderSubmissionUncertainAsync(
            string withdrawalId,
            string userId,
            DateTime updatedAtUtc,
            CancellationToken cancellationToken) =>
            throw new NotImplementedException();

        public Task<PaymentResult<StoredPaymentWithdrawal>> ProjectWithdrawalProviderStatusAsync(
            string providerId,
            string providerWithdrawalId,
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
            CancellationToken cancellationToken)
        {
            var key = $"{providerId}:{eventId}";
            if (_providerEvents.TryGetValue(key, out var providerEvent))
            {
                if (!string.Equals(providerEvent.EventType, eventType, StringComparison.Ordinal))
                {
                    return Task.FromResult(new PaymentProviderEventProcessingLease(
                        PaymentProviderEventProcessingState.Conflict,
                        IsRetry: true));
                }

                if (providerEvent.State == PaymentProviderEventProcessingState.Applied)
                {
                    return Task.FromResult(new PaymentProviderEventProcessingLease(
                        PaymentProviderEventProcessingState.Applied,
                        IsRetry: true));
                }

                _providerEvents[key] = providerEvent with
                {
                    State = PaymentProviderEventProcessingState.Processing,
                    Attempts = providerEvent.Attempts + 1
                };
                return Task.FromResult(new PaymentProviderEventProcessingLease(
                    PaymentProviderEventProcessingState.Processing,
                    IsRetry: true));
            }

            _providerEvents[key] = new ProviderEventRecord(
                eventType,
                PaymentProviderEventProcessingState.Processing,
                Attempts: 1);
            return Task.FromResult(new PaymentProviderEventProcessingLease(
                PaymentProviderEventProcessingState.Processing,
                IsRetry: false));
        }

        public Task MarkProviderEventAppliedAsync(
            string providerId,
            string eventId,
            DateTime appliedAtUtc,
            CancellationToken cancellationToken)
        {
            var key = $"{providerId}:{eventId}";
            if (_providerEvents.TryGetValue(key, out var providerEvent))
            {
                _providerEvents[key] = providerEvent with
                {
                    State = PaymentProviderEventProcessingState.Applied
                };
            }

            return Task.CompletedTask;
        }

        private static bool Matches(
            StoredPaymentCheckout existing,
            StoredPaymentCheckout proposed) =>
            string.Equals(existing.UserId, proposed.UserId, StringComparison.Ordinal) &&
            string.Equals(existing.IdempotencyKey, proposed.IdempotencyKey, StringComparison.Ordinal) &&
            string.Equals(existing.Market.Code, proposed.Market.Code, StringComparison.Ordinal) &&
            string.Equals(existing.Market.Currency, proposed.Market.Currency, StringComparison.Ordinal) &&
            string.Equals(existing.PaymentMethod.Id, proposed.PaymentMethod.Id, StringComparison.Ordinal) &&
            existing.Amount == proposed.Amount &&
            existing.Credits == proposed.Credits &&
            string.Equals(existing.Customer.Email, proposed.Customer.Email, StringComparison.OrdinalIgnoreCase) &&
            string.Equals(existing.Customer.FirstName, proposed.Customer.FirstName, StringComparison.Ordinal) &&
            string.Equals(existing.Customer.LastName, proposed.Customer.LastName, StringComparison.Ordinal) &&
            string.Equals(existing.PayerBank.AccountHolder, proposed.PayerBank.AccountHolder, StringComparison.Ordinal) &&
            string.Equals(existing.PayerBank.BankName, proposed.PayerBank.BankName, StringComparison.Ordinal) &&
            string.Equals(existing.PayerBank.AccountNumber, proposed.PayerBank.AccountNumber, StringComparison.Ordinal) &&
            string.Equals(existing.PayerBank.BranchCode, proposed.PayerBank.BranchCode, StringComparison.Ordinal) &&
            string.Equals(existing.PayerBank.AccountType, proposed.PayerBank.AccountType, StringComparison.Ordinal);

        private static bool CanTransition(string current, string next) => current switch
        {
            "received" => next is "processing" or "completed" or "failed" or "expired",
            "processing" => next is "completed" or "failed" or "expired",
            _ => string.Equals(current, next, StringComparison.Ordinal)
        };

        private static string IdempotencyKey(string userId, string idempotencyKey) =>
            $"{userId}:{idempotencyKey}";

        private static string ProviderKey(string providerId, string providerCheckoutId) =>
            $"{providerId}:{providerCheckoutId}";

        private sealed record ProviderEventRecord(
            string EventType,
            PaymentProviderEventProcessingState State,
            int Attempts);
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

    private sealed class BlockingInvoiceCreateHttpMessageHandler : HttpMessageHandler
    {
        private readonly TaskCompletionSource _postStarted =
            new(TaskCreationOptions.RunContinuationsAsynchronously);
        private readonly TaskCompletionSource<Guid> _releasePost =
            new(TaskCreationOptions.RunContinuationsAsynchronously);

        public Task PostStarted => _postStarted.Task;

        public List<CapturedRequest> CapturedRequests { get; } = [];

        public void ReleasePost(Guid remoteInvoiceId) =>
            _releasePost.TrySetResult(remoteInvoiceId);

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
            lock (CapturedRequests)
            {
                CapturedRequests.Add(new CapturedRequest(
                    request.Method,
                    request.RequestUri?.ToString() ?? string.Empty,
                    idempotencyKey,
                    body));
            }

            if (request.Method == HttpMethod.Get &&
                request.RequestUri?.ToString().Contains("api/v1/pathway-configs", StringComparison.OrdinalIgnoreCase) == true)
            {
                return new HttpResponseMessage(HttpStatusCode.OK)
                {
                    Content = JsonContent.Create(Pathways("active-pathway-key"))
                };
            }

            if (request.Method == HttpMethod.Get &&
                request.RequestUri?.ToString().Contains("api/v1/invoices?limit=", StringComparison.OrdinalIgnoreCase) == true)
            {
                return new HttpResponseMessage(HttpStatusCode.OK)
                {
                    Content = JsonContent.Create(Array.Empty<object>())
                };
            }

            if (request.Method == HttpMethod.Post &&
                request.RequestUri?.ToString().Contains("api/v1/invoices", StringComparison.OrdinalIgnoreCase) == true)
            {
                _postStarted.TrySetResult();
                var remoteInvoiceId = await _releasePost.Task.WaitAsync(cancellationToken);
                return new HttpResponseMessage(HttpStatusCode.OK)
                {
                    Content = JsonContent.Create(CreatedInvoice(remoteInvoiceId, "Pending"))
                };
            }

            throw new InvalidOperationException(
                $"Unexpected request in blocking handler: {request.Method} {request.RequestUri}");
        }
    }

    private sealed class TestHttpClientFactory(HttpMessageHandler handler) : IHttpClientFactory
    {
        public HttpClient CreateClient(string name) => new(handler, disposeHandler: false)
        {
            BaseAddress = new Uri("https://gateway.test/")
        };
    }
}
