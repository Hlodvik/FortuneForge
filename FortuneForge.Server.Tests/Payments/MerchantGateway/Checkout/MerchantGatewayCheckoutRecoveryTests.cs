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

public sealed partial class MerchantGatewayPaymentProviderCheckoutTests
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

}
