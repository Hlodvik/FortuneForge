using FortuneForge.Games.Cards;
using FortuneForge.Games.VideoPoker;
using FortuneForge.Server.Cards.VideoPoker;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace FortuneForge.Server.Tests.VideoPoker;

public sealed class VideoPokerContractTests
{
    [Fact]
    public void FeatureGate_IsDisabledByDefaultAndRequiresExplicitTrueConfiguration()
    {
        var disabled = new ConfigurationBuilder().Build();
        var enabled = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?> { ["Features:VideoPokerEnabled"] = "true" })
            .Build();

        Assert.False(VideoPokerController.IsEnabled(disabled));
        Assert.True(VideoPokerController.IsEnabled(enabled));
    }

    [Fact]
    public async Task DisabledFeature_ReturnsServiceUnavailableBeforeAccountOrStoreAccess()
    {
        var controller = new VideoPokerController(
            null!,
            null!,
            new ConfigurationBuilder().Build(),
            NullLogger<VideoPokerController>.Instance);

        var responses = new ActionResult[]
        {
            await controller.Status(CancellationToken.None),
            await controller.Start(new CreateVideoPokerRoundRequest(1), "video_poker_start_0001", CancellationToken.None),
            await controller.Get(new string('a', 64), CancellationToken.None),
            await controller.Draw(new string('a', 64), new DrawVideoPokerRoundRequest([]), "video_poker_draw_0001", CancellationToken.None),
        };

        Assert.All(responses, response =>
        {
            var unavailable = Assert.IsType<ObjectResult>(response);
            Assert.Equal(StatusCodes.Status503ServiceUnavailable, unavailable.StatusCode);
            var error = Assert.IsType<VideoPokerErrorResponse>(unavailable.Value);
            Assert.Equal("video-poker-disabled", error.Code);
        });
    }

    [Fact]
    public async Task Service_LogsOneRoundAndPaysTheAuthenticatedAccountExactlyOnce()
    {
        var store = new InMemoryVideoPokerStore();
        var service = new VideoPokerService(store, TimeProvider.System, RoyalFlushDeck);

        var started = await service.StartAsync(
            "player-1",
            new CreateVideoPokerRoundRequest(5),
            "video_poker_start_0002",
            CancellationToken.None);

        Assert.Equal("awaiting-draw", started.Phase);
        Assert.Equal(5m, started.Wager);
        Assert.Equal(95m, started.Balance);
        Assert.Equal(5, started.InitialCards.Count);

        var completed = await service.DrawAsync(
            "player-1",
            started.RoundId,
            new DrawVideoPokerRoundRequest([0, 1, 2, 3, 4]),
            "video_poker_draw_0002",
            CancellationToken.None);

        Assert.Equal("completed", completed.Phase);
        Assert.Equal("royal-flush", completed.HandRank);
        Assert.Equal(4_000m, completed.Payout);
        Assert.Equal(4_095m, completed.Balance);
        Assert.Equal(2, store.EventCount);
        Assert.Equal(2, store.LedgerEntryCount);

        var replayed = await service.DrawAsync(
            "player-1",
            started.RoundId,
            new DrawVideoPokerRoundRequest([0, 1, 2, 3, 4]),
            "video_poker_draw_0003",
            CancellationToken.None);

        Assert.Equal(completed.RoundId, replayed.RoundId);
        Assert.Equal(completed.Balance, replayed.Balance);
        Assert.Equal(completed.HeldPositions, replayed.HeldPositions);
        Assert.Equal(completed.FinalCards, replayed.FinalCards);
        Assert.Equal(completed.Payout, replayed.Payout);
        Assert.Equal(409_500, store.BalanceCents);
        Assert.Equal(2, store.EventCount);
        Assert.Equal(2, store.LedgerEntryCount);
    }

    [Fact]
    public async Task Service_RejectsInvalidCoinCountAndChangedHolds()
    {
        var store = new InMemoryVideoPokerStore();
        var service = new VideoPokerService(store, TimeProvider.System, RoyalFlushDeck);

        await Assert.ThrowsAsync<ArgumentOutOfRangeException>(() => service.StartAsync(
            "player-1", new CreateVideoPokerRoundRequest(6), "video_poker_start_0004", CancellationToken.None));

        var started = await service.StartAsync(
            "player-1", new CreateVideoPokerRoundRequest(1), "video_poker_start_0005", CancellationToken.None);
        await service.DrawAsync(
            "player-1", started.RoundId, new DrawVideoPokerRoundRequest([0]), "video_poker_draw_0005", CancellationToken.None);

        await Assert.ThrowsAsync<VideoPokerRoundConflictException>(() => service.DrawAsync(
            "player-1", started.RoundId, new DrawVideoPokerRoundRequest([]), "video_poker_draw_0006", CancellationToken.None));
    }

    private static IReadOnlyList<PlayingCard> RoyalFlushDeck()
    {
        var royal = new[]
        {
            new PlayingCard(CardRank.Ace, CardSuit.Spades),
            new PlayingCard(CardRank.King, CardSuit.Spades),
            new PlayingCard(CardRank.Queen, CardSuit.Spades),
            new PlayingCard(CardRank.Jack, CardSuit.Spades),
            new PlayingCard(CardRank.Ten, CardSuit.Spades),
        };
        return royal.Concat(StandardDeck.Create().Except(royal)).ToArray();
    }

    private sealed class InMemoryVideoPokerStore : IVideoPokerStore
    {
        private string? roundId;
        private string? userId;
        private string? startIdempotencyKey;
        private VideoPokerRound? round;

        public long BalanceCents { get; private set; } = 10_000;
        public int LedgerEntryCount { get; private set; }
        public int EventCount { get; private set; }

        public Task<VideoPokerStoreResult> StartAsync(
            string requestedUserId,
            string idempotencyKey,
            int coinsWagered,
            IReadOnlyList<PlayingCard> shuffledDeck,
            DateTimeOffset nowUtc,
            CancellationToken cancellationToken)
        {
            if (round is not null)
            {
                if (requestedUserId != userId || idempotencyKey != startIdempotencyKey || coinsWagered != round.CoinsWagered)
                    throw new VideoPokerRoundConflictException("start conflict");
                return Task.FromResult(new VideoPokerStoreResult(roundId!, round, BalanceCents));
            }

            var wagerCents = VideoPokerMoney.WagerCents(coinsWagered);
            if (BalanceCents < wagerCents) throw new VideoPokerInsufficientCreditsException(BalanceCents, wagerCents);
            userId = requestedUserId;
            startIdempotencyKey = idempotencyKey;
            roundId = VideoPokerFirestoreStore.CreateLookupKey($"{requestedUserId}\n{idempotencyKey}");
            round = VideoPokerRoundEngine.Deal(shuffledDeck, coinsWagered);
            BalanceCents -= wagerCents;
            LedgerEntryCount++;
            EventCount++;
            return Task.FromResult(new VideoPokerStoreResult(roundId, round, BalanceCents));
        }

        public Task<VideoPokerStoreResult?> GetAsync(string requestedUserId, string requestedRoundId, CancellationToken cancellationToken) =>
            Task.FromResult<VideoPokerStoreResult?>(round is not null && requestedUserId == userId && requestedRoundId == roundId
                ? new VideoPokerStoreResult(roundId!, round, BalanceCents)
                : null);

        public Task<VideoPokerStoreResult> DrawAsync(
            string requestedUserId,
            string requestedRoundId,
            string idempotencyKey,
            VideoPokerHeldCardPositions heldPositions,
            DateTimeOffset nowUtc,
            CancellationToken cancellationToken)
        {
            if (round is null || requestedUserId != userId || requestedRoundId != roundId)
                throw new VideoPokerRoundNotFoundException();
            if (round.Status == VideoPokerRoundStatus.Completed)
            {
                if (!round.Draw!.HeldCardPositions.Positions.SequenceEqual(heldPositions.Positions))
                    throw new VideoPokerRoundConflictException("changed holds");
                return Task.FromResult(new VideoPokerStoreResult(roundId!, round, BalanceCents));
            }

            round = VideoPokerRoundEngine.Draw(round, heldPositions);
            var payoutCents = checked(round.Result!.PaytableOutcome.CreditsWon * VideoPokerMoney.CoinValueCents);
            BalanceCents += payoutCents;
            EventCount++;
            if (payoutCents > 0) LedgerEntryCount++;
            return Task.FromResult(new VideoPokerStoreResult(roundId!, round, BalanceCents));
        }
    }
}
