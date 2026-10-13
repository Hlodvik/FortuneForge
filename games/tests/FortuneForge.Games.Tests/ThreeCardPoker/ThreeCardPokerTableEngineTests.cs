using FortuneForge.Games.ThreeCardPoker;

namespace FortuneForge.Games.Tests.ThreeCardPoker;

public sealed class ThreeCardPokerTableEngineTests
{
    private static readonly DateTime Start = new(2026, 10, 12, 12, 0, 0, DateTimeKind.Utc);

    [Theory]
    [InlineData("K|hearts,8|clubs,4|spades", "J|clubs,9|spades,2|hearts", "dealer-not-qualified", 1500)]
    [InlineData("K|hearts,8|clubs,4|spades", "Q|clubs,9|spades,2|hearts", "player-win", 2000)]
    [InlineData("Q|hearts,9|clubs,2|spades", "K|clubs,8|spades,4|hearts", "dealer-win", 0)]
    [InlineData("Q|hearts,9|clubs,2|spades", "Q|clubs,9|spades,2|hearts", "push", 1000)]
    public void SettlesAnteAndPlayWithReturnedStakes(string playerCards, string dealerCards, string outcome, long payout)
    {
        var table = Table();
        ThreeCardPokerTableEngine.Deal(table, Deck(playerCards, dealerCards), 10, Start);
        ThreeCardPokerTableEngine.ApplyAction(table, "human", ThreeCardPokerActions.Play, Start);
        ThreeCardPokerTableEngine.AdvanceAutomatedTurns(table, Start.AddSeconds(1));
        ThreeCardPokerTableEngine.AdvanceAutomatedTurns(table, Start.AddSeconds(2));
        var player = Assert.Single(table.Players);
        Assert.Equal(1000, player.TotalWagerCents);
        Assert.Equal(500, player.PlayWagerCents);
        Assert.Equal(outcome, player.Outcome);
        Assert.Equal(payout, player.PayoutCents);
        Assert.Equal(ThreeCardPokerTablePhases.Settlement, table.Phase);
    }

    [Fact]
    public void AnteBonusAndPairPlusPayEvenWhenQualifyingDealerWins()
    {
        var table = Table(pairPlus: 100);
        ThreeCardPokerTableEngine.Deal(table, Deck("2|hearts,3|clubs,4|spades", "Q|clubs,Q|spades,Q|hearts"), 10, Start);
        ThreeCardPokerTableEngine.ApplyAction(table, "human", ThreeCardPokerActions.Play, Start);
        ThreeCardPokerTableEngine.AdvanceAutomatedTurns(table, Start.AddSeconds(1));
        ThreeCardPokerTableEngine.AdvanceAutomatedTurns(table, Start.AddSeconds(2));
        var player = Assert.Single(table.Players);
        Assert.Equal("dealer-win", player.Outcome);
        Assert.Equal(500, player.AnteBonusPayoutCents);
        Assert.Equal(700, player.PairPlusPayoutCents);
        Assert.Equal(1200, player.PayoutCents);
    }

    [Fact]
    public void PairPlusPaysIndependentlyAfterAnteFold()
    {
        var table = Table(pairPlus: 100);
        ThreeCardPokerTableEngine.Deal(table, Deck("2|hearts,2|clubs,4|spades", "Q|clubs,9|spades,3|hearts"), 10, Start);
        ThreeCardPokerTableEngine.ApplyAction(table, "human", ThreeCardPokerActions.Fold, Start);
        ThreeCardPokerTableEngine.AdvanceAutomatedTurns(table, Start.AddSeconds(1));
        ThreeCardPokerTableEngine.AdvanceAutomatedTurns(table, Start.AddSeconds(2));
        var player = Assert.Single(table.Players);
        Assert.Equal("fold", player.Outcome);
        Assert.Equal(600, player.TotalWagerCents);
        Assert.Equal(0, player.PlayWagerCents);
        Assert.Equal(0, player.AnteBonusPayoutCents);
        Assert.Equal(200, player.PayoutCents);
    }

    [Theory]
    [InlineData("A|hearts,K|hearts,Q|hearts", 2500, 4100)]
    [InlineData("4|hearts,4|clubs,4|spades", 2000, 3100)]
    [InlineData("4|hearts,3|clubs,2|spades", 500, 700)]
    [InlineData("A|hearts,9|hearts,3|hearts", 0, 400)]
    [InlineData("4|hearts,4|clubs,2|spades", 0, 200)]
    [InlineData("K|hearts,9|clubs,2|spades", 0, 0)]
    public void AppliesTheAdvertisedBonusPaytables(string cards, long anteBonus, long pairPlusPayout)
    {
        var table = Table(pairPlus: 100);
        ThreeCardPokerTableEngine.Deal(table, Deck(cards, "J|diamonds,8|diamonds,3|diamonds"), 10, Start);
        ThreeCardPokerTableEngine.ApplyAction(table, "human", ThreeCardPokerActions.Play, Start);
        ThreeCardPokerTableEngine.AdvanceAutomatedTurns(table, Start.AddSeconds(1));
        ThreeCardPokerTableEngine.AdvanceAutomatedTurns(table, Start.AddSeconds(2));
        var player = Assert.Single(table.Players);
        Assert.Equal(anteBonus, player.AnteBonusPayoutCents);
        Assert.Equal(pairPlusPayout, player.PairPlusPayoutCents);
    }

    [Fact]
    public void AnimatedDealUsesThreePassesAndKeepsDealerFaceDownUntilReveal()
    {
        var table = Table();
        ThreeCardPokerTableEngine.BeginDeal(table, Deck("K|hearts,8|clubs,4|spades", "Q|clubs,9|spades,2|hearts"), 10, Start);
        Assert.Empty(table.Players[0].Cards);
        ThreeCardPokerTableEngine.AdvanceAutomatedTurns(table, Start.Add(ThreeCardPokerTableEngine.InitialCardDuration));
        Assert.Single(table.Players[0].Cards);
        Assert.Empty(table.DealerCards);
        ThreeCardPokerTableEngine.AdvanceAutomatedTurns(table, Start.AddSeconds(2));
        Assert.Equal(3, table.Players[0].Cards.Count);
        Assert.Equal(3, table.DealerCards.Count);
        Assert.Equal(0, table.DealerVisibleCardCount);
        Assert.Equal(new[] { "play", "fold" }, ThreeCardPokerTableEngine.LegalActions(table, table.Players[0]));
    }

    [Fact]
    public void LeavingOnYourTurnFoldsWithoutChargingPlay()
    {
        var table = Table();
        ThreeCardPokerTableEngine.Deal(table, Deck("K|hearts,8|clubs,4|spades", "Q|clubs,9|spades,2|hearts"), 10, Start);
        ThreeCardPokerTableEngine.MarkLeaving(table, table.Players[0], Start);
        Assert.Equal("folded", table.Players[0].Status);
        Assert.Equal(0, table.Players[0].PlayWagerCents);
        Assert.True(table.Players[0].LeavingAfterRound);
    }

    private static ThreeCardPokerTableState Table(long pairPlus = 0) => new()
    {
        TableId = "table", CreatedAtUtc = Start, UpdatedAtUtc = Start,
        Players = [new() { ActorId = "human", PublicSeatId = "seat", DisplayName = "Player", Seat = 0,
            SessionId = "session", SessionStartedAtUtc = Start, NextWagerCents = 500, NextPairPlusCents = pairPlus }]
    };
    private static IReadOnlyList<string> Deck(string playerCards, string dealerCards)
    {
        var player = playerCards.Split(','); var dealer = dealerCards.Split(',');
        var prefix = Enumerable.Range(0, 3).SelectMany(index => new[] { player[index], dealer[index] }).ToArray();
        return prefix.Concat(ThreeCardPokerRules.CreateShuffledDeck().Where(card => !prefix.Contains(card))).ToArray();
    }
}
