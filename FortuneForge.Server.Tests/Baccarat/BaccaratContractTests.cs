using FortuneForge.Games.Baccarat;
using FortuneForge.Games.Cards;
using FortuneForge.Server.Cards.Baccarat;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace FortuneForge.Server.Tests.Baccarat;

public sealed class BaccaratContractTests
{
    [Fact]
    public void FeatureGate_IsDisabledByDefaultAndRequiresExplicitTrueConfiguration()
    {
        var disabled = new ConfigurationBuilder().Build();
        var enabled = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?> { ["Features:BaccaratEnabled"] = "true" })
            .Build();

        Assert.False(BaccaratController.IsEnabled(disabled));
        Assert.True(BaccaratController.IsEnabled(enabled));
    }

    [Fact]
    public async Task DisabledFeature_ReturnsServiceUnavailableBeforeAccountOrStoreAccess()
    {
        var controller = new BaccaratController(
            null!,
            null!,
            new ConfigurationBuilder().Build(),
            NullLogger<BaccaratController>.Instance);

        var responses = new ActionResult[]
        {
            await controller.Status(CancellationToken.None),
            await controller.Start(new CreateBaccaratRoundRequest("player", 1m), "baccarat_start_0001", CancellationToken.None),
            await controller.Get(new string('a', 64), CancellationToken.None),
        };

        Assert.All(responses, response =>
        {
            var unavailable = Assert.IsType<ObjectResult>(response);
            Assert.Equal(StatusCodes.Status503ServiceUnavailable, unavailable.StatusCode);
            Assert.Equal("baccarat-disabled", Assert.IsType<BaccaratErrorResponse>(unavailable.Value).Code);
        });
    }

    [Fact]
    public async Task Service_SettlesAndRecordsOneEightDeckRoundExactlyOnce()
    {
        var store = new InMemoryBaccaratStore();
        var service = new BaccaratService(store, TimeProvider.System, BankerNaturalShoe);

        var first = await service.StartAsync(
            "player-1",
            new CreateBaccaratRoundRequest("banker", 10m),
            "baccarat_start_0002",
            CancellationToken.None);
        var replay = await service.StartAsync(
            "player-1",
            new CreateBaccaratRoundRequest("banker", 10m),
            "baccarat_start_0002",
            CancellationToken.None);

        Assert.Equal("settled", first.Phase);
        Assert.Equal("banker", first.Outcome);
        Assert.True(first.EndedOnNatural);
        Assert.Equal("win", first.Disposition);
        Assert.Equal(9.5m, first.Profit);
        Assert.Equal(19.5m, first.TotalReturn);
        Assert.Equal(1_009.5m, first.Balance);
        Assert.Equal(first.RoundId, replay.RoundId);
        Assert.Equal(1, store.EventCount);
        Assert.Equal(2, store.LedgerEntryCount);
        Assert.Equal(100_950, store.BalanceCents);
    }

    [Fact]
    public async Task Service_RejectsUnpricedStakeAndChangedIdempotentRequest()
    {
        var store = new InMemoryBaccaratStore();
        var service = new BaccaratService(store, TimeProvider.System, BankerNaturalShoe);

        await Assert.ThrowsAsync<ArgumentOutOfRangeException>(() => service.StartAsync(
            "player-1", new CreateBaccaratRoundRequest("player", 1.50m), "baccarat_start_0003", CancellationToken.None));

        await service.StartAsync(
            "player-1", new CreateBaccaratRoundRequest("player", 1m), "baccarat_start_0004", CancellationToken.None);
        await Assert.ThrowsAsync<BaccaratRoundConflictException>(() => service.StartAsync(
            "player-1", new CreateBaccaratRoundRequest("tie", 1m), "baccarat_start_0004", CancellationToken.None));
    }

    private static IReadOnlyList<PlayingCard> BankerNaturalShoe() =>
    [
        new PlayingCard(CardRank.Two, CardSuit.Clubs),
        new PlayingCard(CardRank.Four, CardSuit.Clubs),
        new PlayingCard(CardRank.Three, CardSuit.Clubs),
        new PlayingCard(CardRank.Four, CardSuit.Diamonds),
    ];

    private sealed class InMemoryBaccaratStore : IBaccaratStore
    {
        private BaccaratStoreRound? round;
        private string? startIdempotencyKey;

        public long BalanceCents { get; private set; } = 100_000;
        public int LedgerEntryCount { get; private set; }
        public int EventCount { get; private set; }

        public Task<BaccaratStoreResult> StartAsync(
            string userId,
            string idempotencyKey,
            BaccaratBetSide betSide,
            long stakeCents,
            IReadOnlyList<PlayingCard> shuffledShoe,
            DateTimeOffset nowUtc,
            CancellationToken cancellationToken)
        {
            if (round is not null)
            {
                if (round.UserId != userId || startIdempotencyKey != idempotencyKey || round.BetSide != betSide ||
                    BaccaratMoney.ToStakeCents(round.Settlement.Stake) != stakeCents)
                {
                    throw new BaccaratRoundConflictException("start conflict");
                }
                return Task.FromResult(new BaccaratStoreResult(round, BalanceCents));
            }
            if (BalanceCents < stakeCents) throw new BaccaratInsufficientCreditsException(BalanceCents, stakeCents);

            var dealt = PuntoBancoRoundDealer.Deal(shuffledShoe);
            var settlement = PuntoBancoPaytable.Settle(betSide, BaccaratMoney.ToRand(stakeCents), dealt);
            var returnCents = checked((long)(settlement.TotalReturn * 100m));
            BalanceCents = checked(BalanceCents - stakeCents + returnCents);
            round = new BaccaratStoreRound(
                BaccaratFirestoreStore.CreateLookupKey($"{userId}\n{idempotencyKey}"),
                userId,
                betSide,
                dealt,
                settlement);
            startIdempotencyKey = idempotencyKey;
            EventCount++;
            LedgerEntryCount += returnCents > 0 ? 2 : 1;
            return Task.FromResult(new BaccaratStoreResult(round, BalanceCents));
        }

        public Task<BaccaratStoreResult?> GetAsync(string userId, string roundId, CancellationToken cancellationToken) =>
            Task.FromResult<BaccaratStoreResult?>(round is not null && round.UserId == userId && round.RoundId == roundId
                ? new BaccaratStoreResult(round, BalanceCents)
                : null);
    }
}
