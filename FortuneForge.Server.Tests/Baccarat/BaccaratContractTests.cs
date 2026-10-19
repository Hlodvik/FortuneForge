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
            "player-1", new CreateBaccaratRoundRequest("player", 1.10m), "baccarat_start_0003", CancellationToken.None));

        await service.StartAsync(
            "player-1", new CreateBaccaratRoundRequest("player", 1m), "baccarat_start_0004", CancellationToken.None);
        await Assert.ThrowsAsync<BaccaratRoundConflictException>(() => service.StartAsync(
            "player-1", new CreateBaccaratRoundRequest("tie", 1m), "baccarat_start_0004", CancellationToken.None));
        await Assert.ThrowsAsync<BaccaratRoundConflictException>(() => service.StartAsync(
            "player-1", new CreateBaccaratRoundRequest("player", 1m, 0.25m), "baccarat_start_0004", CancellationToken.None));
    }

    [Fact]
    public async Task PracticeStore_SettlesIndependentPairBetsAndDebitsAllStakesOnce()
    {
        var store = new PracticeBaccaratStore();
        var service = new BaccaratService(store, TimeProvider.System, PairShoe);
        var request = new CreateBaccaratRoundRequest("banker", 0.25m, 0.25m, 0.50m);

        var first = await service.StartAsync("pair-player", request, "baccarat_pair_0001", CancellationToken.None);
        var replay = await service.StartAsync("pair-player", request, "baccarat_pair_0001", CancellationToken.None);

        Assert.Equal("banker", first.Outcome);
        Assert.Equal(0.25m, first.Stake);
        Assert.Equal(0.49m, first.TotalReturn); // Banker commission rounds once to a whole cent.
        Assert.Equal("win", first.PlayerPairDisposition);
        Assert.Equal(2.75m, first.PlayerPairProfit);
        Assert.Equal(3m, first.PlayerPairTotalReturn);
        Assert.Equal("loss", first.BankerPairDisposition);
        Assert.Equal(-0.50m, first.BankerPairProfit);
        Assert.Equal(0m, first.BankerPairTotalReturn);
        Assert.Equal(1m, first.TotalStake);
        Assert.Equal(2.49m, first.CombinedProfit);
        Assert.Equal(3.49m, first.CombinedTotalReturn);
        Assert.Equal(10_002.49m, first.Balance);
        Assert.Equal(first.RoundId, replay.RoundId);
        Assert.Equal(first.Balance, replay.Balance);
        Assert.Equal(1_000_249, store.GetBalanceCents("pair-player"));

        await Assert.ThrowsAsync<BaccaratRoundConflictException>(() => service.StartAsync(
            "pair-player", request with { BankerPairStake = 0m }, "baccarat_pair_0001", CancellationToken.None));
    }

    [Fact]
    public void PairPaytable_UsesOnlyInitialTwoRanksAndPaysElevenToOneProfit()
    {
        var matched = new BaccaratHand([
            new PlayingCard(CardRank.Seven, CardSuit.Clubs),
            new PlayingCard(CardRank.Seven, CardSuit.Hearts),
            new PlayingCard(CardRank.Two, CardSuit.Spades),
        ]);
        var thirdCardMatchOnly = new BaccaratHand([
            new PlayingCard(CardRank.Seven, CardSuit.Clubs),
            new PlayingCard(CardRank.Two, CardSuit.Hearts),
            new PlayingCard(CardRank.Seven, CardSuit.Spades),
        ]);

        var win = PuntoBancoPaytable.SettlePair(1m, matched);
        var loss = PuntoBancoPaytable.SettlePair(1m, thirdCardMatchOnly);
        var notPlaced = PuntoBancoPaytable.SettlePair(0m, matched);

        Assert.Equal(BaccaratBetDisposition.Win, win.Disposition);
        Assert.Equal(11m, win.Profit);
        Assert.Equal(12m, win.TotalReturn);
        Assert.Equal(BaccaratBetDisposition.Loss, loss.Disposition);
        Assert.Equal(-1m, loss.Profit);
        Assert.Equal(0m, loss.TotalReturn);
        Assert.Equal(BaccaratBetDisposition.NotPlaced, notPlaced.Disposition);
    }

    [Fact]
    public async Task PlayerPair_WinsEvenWhenMainBankerBetLoses()
    {
        var service = new BaccaratService(new PracticeBaccaratStore(), TimeProvider.System, () =>
        [
            new PlayingCard(CardRank.Four, CardSuit.Clubs),
            new PlayingCard(CardRank.King, CardSuit.Clubs),
            new PlayingCard(CardRank.Four, CardSuit.Hearts),
            new PlayingCard(CardRank.Seven, CardSuit.Clubs),
        ]);

        var result = await service.StartAsync("independent-player",
            new CreateBaccaratRoundRequest("banker", 0.25m, 0.25m),
            "baccarat_pair_independent_0001", CancellationToken.None);

        Assert.Equal("player", result.Outcome);
        Assert.Equal("loss", result.Disposition);
        Assert.Equal("win", result.PlayerPairDisposition);
        Assert.Equal(3m, result.PlayerPairTotalReturn);
        Assert.Equal(2.50m, result.CombinedProfit);
        Assert.Equal(10_002.50m, result.Balance);
    }

    [Fact]
    public async Task Service_ValidatesPairIncrementAndCombinedBalance()
    {
        var store = new InMemoryBaccaratStore { BalanceCents = 100 };
        var service = new BaccaratService(store, TimeProvider.System, PairShoe);

        await Assert.ThrowsAsync<ArgumentOutOfRangeException>(() => service.StartAsync(
            "pair-player", new CreateBaccaratRoundRequest("player", 0.25m, 0.10m), "baccarat_pair_0002", CancellationToken.None));
        await Assert.ThrowsAsync<ArgumentOutOfRangeException>(() => service.StartAsync(
            "pair-player", new CreateBaccaratRoundRequest("player", 0.25m, -0.25m), "baccarat_pair_0003", CancellationToken.None));
        await Assert.ThrowsAsync<BaccaratInsufficientCreditsException>(() => service.StartAsync(
            "pair-player", new CreateBaccaratRoundRequest("player", 0.50m, 0.50m, 0.50m), "baccarat_pair_0004", CancellationToken.None));
    }

    [Fact]
    public async Task PracticeStore_ConsumesOnePersistentShoeAndReplayDoesNotAdvanceIt()
    {
        var service = new BaccaratService(
            new PracticeBaccaratStore(),
            TimeProvider.System,
            () => Enumerable.Range(0, 8).SelectMany(_ => StandardDeck.Create()).ToArray());

        var first = await service.StartAsync(
            "practice-player", new CreateBaccaratRoundRequest("player", 1m), "baccarat_shoe_0001", CancellationToken.None);
        var replay = await service.StartAsync(
            "practice-player", new CreateBaccaratRoundRequest("player", 1m), "baccarat_shoe_0001", CancellationToken.None);
        var second = await service.StartAsync(
            "practice-player", new CreateBaccaratRoundRequest("banker", 1m), "baccarat_shoe_0002", CancellationToken.None);

        Assert.Equal(first.RoundId, replay.RoundId);
        Assert.Equal(first.ShoeCardsUsed, replay.ShoeCardsUsed);
        Assert.True(first.ShoeCardsUsed is >= 4 and <= 6);
        Assert.True(second.ShoeCardsUsed > first.ShoeCardsUsed);
        Assert.Equal(416, second.ShoeCardsUsed + second.ShoeCardsRemaining);
    }

    [Fact]
    public async Task PracticeStatus_ReportsCurrentPerPlayerBalanceAndLeavesWalletStatusUntouched()
    {
        var store = new PracticeBaccaratStore();
        var service = new BaccaratService(store, TimeProvider.System, BankerNaturalShoe);

        Assert.Equal(10_000m, BaccaratController.CreateStatusResponse("practice-player", 123m, store).Balance);
        var round = await service.StartAsync(
            "practice-player", new CreateBaccaratRoundRequest("banker", 10m), "baccarat_status_0001", CancellationToken.None);

        var practiceStatus = BaccaratController.CreateStatusResponse("practice-player", 123m, store);
        Assert.True(practiceStatus.SupportsPairBets);
        Assert.Equal(round.Balance, practiceStatus.Balance);
        Assert.Equal(10_009.5m, practiceStatus.Balance);
        Assert.Equal("practice-eight-deck-punto-banco", practiceStatus.Mode);
        Assert.Equal(10_000m, BaccaratController.CreateStatusResponse("other-player", 123m, store).Balance);

        var walletStatus = BaccaratController.CreateStatusResponse("practice-player", 123m, null);
        Assert.True(walletStatus.SupportsPairBets);
        Assert.Equal(123m, walletStatus.Balance);
        Assert.Equal(0.25m, walletStatus.MinimumStake);
        Assert.Equal(0.25m, walletStatus.StakeIncrement);
        Assert.Equal(100m, walletStatus.MaximumStake);
        Assert.Equal("eight-deck-punto-banco", walletStatus.Mode);
    }

    private static IReadOnlyList<PlayingCard> BankerNaturalShoe() =>
    [
        new PlayingCard(CardRank.Two, CardSuit.Clubs),
        new PlayingCard(CardRank.Four, CardSuit.Clubs),
        new PlayingCard(CardRank.Three, CardSuit.Clubs),
        new PlayingCard(CardRank.Four, CardSuit.Diamonds),
    ];

    private static IReadOnlyList<PlayingCard> PairShoe() =>
    [
        new PlayingCard(CardRank.Four, CardSuit.Clubs),
        new PlayingCard(CardRank.King, CardSuit.Clubs),
        new PlayingCard(CardRank.Four, CardSuit.Hearts),
        new PlayingCard(CardRank.Nine, CardSuit.Clubs),
    ];

    private sealed class InMemoryBaccaratStore : IBaccaratStore
    {
        private BaccaratStoreRound? round;
        private string? startIdempotencyKey;

        public long BalanceCents { get; set; } = 100_000;
        public int LedgerEntryCount { get; private set; }
        public int EventCount { get; private set; }

        public Task<BaccaratStoreResult> StartAsync(
            string userId,
            string idempotencyKey,
            BaccaratBetSide betSide,
            long stakeCents,
            long playerPairStakeCents,
            long bankerPairStakeCents,
            IReadOnlyList<PlayingCard> shuffledShoe,
            DateTimeOffset nowUtc,
            CancellationToken cancellationToken)
        {
            if (round is not null)
            {
                if (round.UserId != userId || startIdempotencyKey != idempotencyKey || round.BetSide != betSide ||
                    BaccaratMoney.ToStakeCents(round.Settlement.Stake) != stakeCents ||
                    BaccaratMoney.ToOptionalStakeCents(round.PlayerPairSettlement.Stake) != playerPairStakeCents ||
                    BaccaratMoney.ToOptionalStakeCents(round.BankerPairSettlement.Stake) != bankerPairStakeCents)
                {
                    throw new BaccaratRoundConflictException("start conflict");
                }
                return Task.FromResult(new BaccaratStoreResult(round, BalanceCents));
            }
            var totalStakeCents = checked(stakeCents + playerPairStakeCents + bankerPairStakeCents);
            if (BalanceCents < totalStakeCents) throw new BaccaratInsufficientCreditsException(BalanceCents, totalStakeCents);

            var dealt = PuntoBancoRoundDealer.Deal(shuffledShoe);
            var settlement = PuntoBancoPaytable.Settle(betSide, BaccaratMoney.ToRand(stakeCents), dealt);
            var playerPairSettlement = PuntoBancoPaytable.SettlePair(BaccaratMoney.ToRand(playerPairStakeCents), dealt.PlayerHand);
            var bankerPairSettlement = PuntoBancoPaytable.SettlePair(BaccaratMoney.ToRand(bankerPairStakeCents), dealt.BankerHand);
            var returnCents = BaccaratMoney.ToCents(
                settlement.TotalReturn + playerPairSettlement.TotalReturn + bankerPairSettlement.TotalReturn);
            BalanceCents = checked(BalanceCents - totalStakeCents + returnCents);
            round = new BaccaratStoreRound(
                BaccaratFirestoreStore.CreateLookupKey($"{userId}\n{idempotencyKey}"),
                userId,
                betSide,
                dealt,
                settlement,
                playerPairSettlement,
                bankerPairSettlement);
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
