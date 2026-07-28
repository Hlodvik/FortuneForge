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

public sealed partial class MerchantGatewayPaymentProviderWithdrawalTests
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

}
