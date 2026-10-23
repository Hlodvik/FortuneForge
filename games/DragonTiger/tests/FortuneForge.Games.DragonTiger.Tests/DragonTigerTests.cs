using FortuneForge.Games.Cards;
using FortuneForge.Games.DragonTiger.Server;

namespace FortuneForge.Games.DragonTiger.Tests;

public sealed class DragonTigerTests
{
    [Fact]
    public void RanksAndSuits_CompareAllStandardRanksWithAceLow()
    {
        foreach (var dragon in Enum.GetValues<CardRank>())
        foreach (var tiger in Enum.GetValues<CardRank>())
        foreach (var suit in Enum.GetValues<CardSuit>())
        {
            var round = new DragonTigerRound(new(dragon, suit), new(tiger, CardSuit.Spades));
            Assert.Equal((int)dragon, round.DragonValue);
            Assert.Equal((int)tiger, round.TigerValue);
            Assert.Equal(dragon > tiger ? DragonTigerSide.Dragon : dragon < tiger ? DragonTigerSide.Tiger : DragonTigerSide.Tie, round.Outcome);
        }
    }

    [Theory]
    [InlineData(DragonTigerSide.Dragon, CardRank.King, CardRank.Ace, DragonTigerDisposition.Win, 100)]
    [InlineData(DragonTigerSide.Tiger, CardRank.Ace, CardRank.King, DragonTigerDisposition.Win, 100)]
    [InlineData(DragonTigerSide.Dragon, CardRank.Ace, CardRank.King, DragonTigerDisposition.Loss, 0)]
    [InlineData(DragonTigerSide.Tie, CardRank.King, CardRank.King, DragonTigerDisposition.Win, 450)]
    [InlineData(DragonTigerSide.Tie, CardRank.Ace, CardRank.King, DragonTigerDisposition.Loss, 0)]
    [InlineData(DragonTigerSide.Dragon, CardRank.Queen, CardRank.Queen, DragonTigerDisposition.HalfLoss, 25)]
    [InlineData(DragonTigerSide.Tiger, CardRank.Queen, CardRank.Queen, DragonTigerDisposition.HalfLoss, 25)]
    public void Paytable_ExactCentReturns(DragonTigerSide side, CardRank dragon, CardRank tiger, DragonTigerDisposition disposition, long totalReturn)
    {
        var settled = DragonTigerRules.Settle(side, 50, new(new(dragon, CardSuit.Hearts), new(tiger, CardSuit.Clubs)));
        Assert.Equal(disposition, settled.Disposition);
        Assert.Equal(totalReturn, settled.TotalReturnCents);
        Assert.Equal(totalReturn - 50, settled.ProfitCents);
    }

    [Fact]
    public void Rules_RejectInvalidCardsAndFractionalHalfCentStakes()
    {
        Assert.Throws<ArgumentException>(() => DragonTigerRules.Deal([]));
        Assert.Throws<ArgumentException>(() => DragonTigerRules.CardValue(new((CardRank)14, CardSuit.Clubs)));
        Assert.Throws<ArgumentOutOfRangeException>(() => DragonTigerRules.Settle(DragonTigerSide.Dragon, 51, new(new(CardRank.Ace, CardSuit.Clubs), new(CardRank.Ace, CardSuit.Hearts))));
        DragonTigerModule.Descriptor.Validate();
    }

    [Theory]
    [InlineData(-1, 0, 0)] [InlineData(0, 0, 0)] [InlineData(0.25, 0, 0)]
    [InlineData(0.501, 0, 0)] [InlineData(100.5, 0, 0)] [InlineData(50, 50, 0.50)]
    public void Limits_RejectUnpricedOrExcessCombinedStakes(decimal dragon, decimal tiger, decimal tie) =>
        Assert.Throws<ArgumentOutOfRangeException>(() => DragonTigerMoney.Parse(new(dragon, tiger, tie)));

    [Fact]
    public void CryptoShoe_HasExactlyEightOfEveryStandardCard()
    {
        var shoe = DragonTigerService.CreateShuffledShoe();
        Assert.Equal(416, shoe.Count);
        Assert.All(StandardDeck.Create(), card => Assert.Equal(8, shoe.Count(value => value == card)));
    }

    [Fact]
    public async Task Practice_ConcurrentReplayDebitsOnceAndRecoveryUsesSameCards()
    {
        var store = new PracticeDragonTigerStore();
        var service = new DragonTigerService(store, TimeProvider.System, () => Shoe(CardRank.King, CardRank.Ace));
        var request = new CreateDragonTigerRoundRequest(10, 0, 0.50m);
        var tasks = Enumerable.Range(0, 16).Select(_ => service.StartAsync("player", request, "dragon_tiger_same_0001"));
        var rounds = await Task.WhenAll(tasks);
        Assert.Single(rounds.Select(round => round.RoundId).Distinct());
        Assert.Equal(1, store.RoundCount);
        Assert.All(rounds, round => Assert.Equal(10_009.50m, round.Balance));
        Assert.Equal(2, rounds[0].ShoeCardsUsed);
        Assert.Equal(9.50m, rounds[0].Profit);
        var restored = await service.GetAsync("player", rounds[0].RoundId);
        var latest = await service.LatestAsync("player");
        Assert.Equal(rounds[0].DragonCard, restored.DragonCard);
        Assert.Equal(rounds[0].RoundId, latest!.RoundId);
        await Assert.ThrowsAsync<DragonTigerRoundConflictException>(() => service.StartAsync("player", request with { TieStake = 1 }, "dragon_tiger_same_0001"));
        await Assert.ThrowsAsync<DragonTigerRoundNotFoundException>(() => service.GetAsync("other", rounds[0].RoundId));
        Assert.Null(await service.LatestAsync("other"));
        Assert.Equal(1, store.RoundCount);
    }

    [Fact]
    public async Task Practice_TiedMainStakesReturnHalfAndSeparateTieWins()
    {
        var store = new PracticeDragonTigerStore();
        var service = new DragonTigerService(store, TimeProvider.System, () => Shoe(CardRank.Queen, CardRank.Queen));
        var result = await service.StartAsync("tie-player", new(0.50m, 0.50m, 0.50m), "dragon_tiger_tie_0001");
        Assert.Equal("tie", result.Outcome);
        Assert.Equal(5m, result.TotalReturn);
        Assert.Equal(3.50m, result.Profit);
        Assert.Equal(10_003.50m, result.Balance);
        Assert.Equal("half-loss", result.Bets[0].Disposition);
        Assert.Equal(0.25m, result.Bets[0].TotalReturn);
        Assert.Equal("win", result.Bets[2].Disposition);
        Assert.Equal(4.50m, result.Bets[2].TotalReturn);
    }

    [Fact]
    public async Task Practice_InsufficientFundsAndInvalidKeysHaveNoEffect()
    {
        var store = new PracticeDragonTigerStore(50);
        var service = new DragonTigerService(store, TimeProvider.System);
        await Assert.ThrowsAsync<DragonTigerInsufficientCreditsException>(() => service.StartAsync("poor", new(1), "dragon_tiger_poor_0001"));
        await Assert.ThrowsAsync<ArgumentException>(() => service.StartAsync("poor", new(0.50m), "bad"));
        Assert.Equal(50, store.GetBalanceCents("poor"));
        Assert.Equal(0, store.RoundCount);
    }

    [Fact]
    public async Task Practice_ShoePersistsAndReshufflesOnlyAfter312Cards()
    {
        var store = new PracticeDragonTigerStore();
        var service = new DragonTigerService(store, TimeProvider.System, () => Shoe(CardRank.King, CardRank.Ace));
        DragonTigerRoundResponse? round = null;
        for (var index = 0; index < 156; index++)
            round = await service.StartAsync("shoe-player", new(0.50m), $"dragon_tiger_shoe_{index:0000}");
        Assert.Equal(312, round!.ShoeCardsUsed);
        var replay = await service.StartAsync("shoe-player", new(0.50m), "dragon_tiger_shoe_0155");
        Assert.Equal(312, replay.ShoeCardsUsed);
        var next = await service.StartAsync("shoe-player", new(0.50m), "dragon_tiger_shoe_0156");
        Assert.Equal(2, next.ShoeCardsUsed);
        Assert.Equal(414, next.ShoeCardsRemaining);
    }

    internal static IReadOnlyList<PlayingCard> Shoe(CardRank dragon, CardRank tiger)
    {
        var prefix = new[] { new PlayingCard(dragon, CardSuit.Clubs), new PlayingCard(tiger, CardSuit.Hearts) };
        var cards = Enumerable.Range(0, 8).SelectMany(_ => StandardDeck.Create()).ToList();
        foreach (var card in prefix) cards.Remove(card);
        return [.. prefix, .. cards];
    }
}
