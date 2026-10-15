using FortuneForge.Games.Cards;
using FortuneForge.Games.HouseHoldem;

namespace FortuneForge.Games.Tests.HouseHoldem;

public sealed class HouseHoldemRulesTests
{
    [Fact]
    public void AnteStopsAtOneHundredRandWhileTripsAndFourTimesPlayRemainIndependent()
    {
        var deck = HouseHoldemRules.CreateShuffledDeck();
        var round = HouseHoldemRules.StartRound(deck, 10_000, 50_000);
        Assert.Equal(40_000, HouseHoldemRules.RequiredAdditionalWagerCents(round, HouseHoldemActions.Play4X));
        Assert.Throws<ArgumentOutOfRangeException>(() => HouseHoldemRules.StartRound(deck, 10_100));
        Assert.Throws<ArgumentOutOfRangeException>(() => HouseHoldemRules.StartRound(deck, 10_000, 50_100));
        Assert.Equal(50_000, HouseHoldemRules.RestoreRound(deck, 50_000).AnteCents);
    }

    [Fact]
    public void NewRoundShowsOnlyThePlayersHoleCards()
    {
        var round = HouseHoldemRules.StartRound(Deck(
            "A|spades", "4|clubs", "K|spades", "4|hearts",
            "Q|spades", "J|spades", "10|spades", "2|clubs", "3|diamonds"), 100, 100);

        var view = HouseHoldemRules.ToPlayerView(round);
        Assert.Equal(HouseHoldemPhases.Preflop, view.Phase);
        Assert.Equal(["A|spades", "K|spades"], view.PlayerCards.Select(card => card.Code));
        Assert.All(view.DealerCards, card => { Assert.True(card.Hidden); Assert.Null(card.Code); });
        Assert.All(view.CommunityCards, card => { Assert.True(card.Hidden); Assert.Null(card.Code); });
        Assert.Equal([HouseHoldemActions.Check, HouseHoldemActions.Play3X, HouseHoldemActions.Play4X], view.LegalActions);
        Assert.Equal(300, view.TotalWagerCents);
        Assert.Null(view.Settlement);
    }

    [Fact]
    public void EarlyPlayRevealsBoardInStagesAndPaysRoyalBlindAndTrips()
    {
        var round = HouseHoldemRules.StartRound(Deck(
            "A|spades", "4|clubs", "K|spades", "4|hearts",
            "Q|spades", "J|spades", "10|spades", "2|clubs", "3|diamonds"), 100, 100);

        Assert.Equal(400, HouseHoldemRules.RequiredAdditionalWagerCents(round, HouseHoldemActions.Play4X));
        HouseHoldemRules.ApplyAction(round, HouseHoldemActions.Play4X);
        var flop = HouseHoldemRules.ToPlayerView(round);
        Assert.Equal(HouseHoldemPhases.Flop, flop.Phase);
        Assert.Equal(3, flop.CommunityCards.Count(card => !card.Hidden));
        Assert.All(flop.DealerCards, card => Assert.Null(card.Code));
        Assert.Empty(flop.LegalActions);
        Assert.True(flop.CanAdvance);

        HouseHoldemRules.Advance(round);
        var river = HouseHoldemRules.ToPlayerView(round);
        Assert.Equal(HouseHoldemPhases.River, river.Phase);
        Assert.All(river.CommunityCards, card => Assert.False(card.Hidden));
        Assert.All(river.DealerCards, card => Assert.True(card.Hidden));

        HouseHoldemRules.Advance(round);
        var settlement = Assert.IsType<HouseHoldemSettlement>(round.Settlement);
        Assert.Equal("win", settlement.Outcome);
        Assert.Equal("royal-flush", settlement.PlayerHandRank);
        Assert.Equal(200, settlement.AntePayoutCents);
        Assert.Equal(50_100, settlement.BlindPayoutCents);
        Assert.Equal(800, settlement.PlayPayoutCents);
        Assert.Equal(5_100, settlement.TripsPayoutCents);
        Assert.Equal(56_200, settlement.PayoutCents);
        Assert.Equal(700, round.TotalWagerCents);
        Assert.All(HouseHoldemRules.ToPlayerView(round).DealerCards, card => Assert.False(card.Hidden));
    }

    [Fact]
    public void DealerNonqualificationReturnsAnteButOtherBetsStillResolve()
    {
        var winner = HouseHoldemRules.StartRound(Deck(
            "A|hearts", "Q|diamonds", "K|clubs", "J|clubs",
            "2|clubs", "4|diamonds", "6|hearts", "8|spades", "10|clubs"), 100);
        HouseHoldemRules.ApplyAction(winner, HouseHoldemActions.Play3X);
        HouseHoldemRules.Advance(winner);
        HouseHoldemRules.Advance(winner);
        Assert.Equal("win", winner.Settlement!.Outcome);
        Assert.False(winner.Settlement.DealerQualifies);
        Assert.Equal((100L, 100L, 600L),
            (winner.Settlement.AntePayoutCents, winner.Settlement.BlindPayoutCents, winner.Settlement.PlayPayoutCents));

        var loser = HouseHoldemRules.StartRound(Deck(
            "3|clubs", "A|hearts", "4|diamonds", "K|spades",
            "2|hearts", "6|clubs", "8|diamonds", "10|spades", "Q|clubs"), 100);
        HouseHoldemRules.ApplyAction(loser, HouseHoldemActions.Check);
        HouseHoldemRules.ApplyAction(loser, HouseHoldemActions.Check);
        HouseHoldemRules.ApplyAction(loser, HouseHoldemActions.Play1X);
        Assert.Equal("lose", loser.Settlement!.Outcome);
        Assert.False(loser.Settlement.DealerQualifies);
        Assert.Equal((100L, 0L, 0L),
            (loser.Settlement.AntePayoutCents, loser.Settlement.BlindPayoutCents, loser.Settlement.PlayPayoutCents));
    }

    [Fact]
    public void FoldForfeitsMainBetsButTripsRemainsLive()
    {
        var round = HouseHoldemRules.StartRound(Deck(
            "7|spades", "4|clubs", "7|hearts", "5|diamonds",
            "7|diamonds", "2|clubs", "3|hearts", "9|spades", "J|diamonds"), 100, 100);
        HouseHoldemRules.ApplyAction(round, HouseHoldemActions.Check);
        HouseHoldemRules.ApplyAction(round, HouseHoldemActions.Check);
        HouseHoldemRules.ApplyAction(round, HouseHoldemActions.Fold);

        Assert.Equal("fold", round.Settlement!.Outcome);
        Assert.Equal("three-of-a-kind", round.Settlement.PlayerHandRank);
        Assert.Equal(0, round.Settlement.AntePayoutCents);
        Assert.Equal(0, round.Settlement.BlindPayoutCents);
        Assert.Equal(0, round.Settlement.PlayPayoutCents);
        Assert.Equal(400, round.Settlement.TripsPayoutCents);
        Assert.Equal(300, round.TotalWagerCents);
    }

    [Fact]
    public void TiePushesMainBetsAndTripsStillPays()
    {
        var round = HouseHoldemRules.StartRound(Deck(
            "A|clubs", "A|hearts", "K|diamonds", "Q|spades",
            "2|clubs", "3|hearts", "4|diamonds", "5|spades", "6|clubs"), 100, 100);
        HouseHoldemRules.ApplyAction(round, HouseHoldemActions.Check);
        HouseHoldemRules.ApplyAction(round, HouseHoldemActions.Play2X);
        HouseHoldemRules.Advance(round);

        Assert.Equal("push", round.Settlement!.Outcome);
        Assert.Equal((100L, 100L, 200L, 500L),
            (round.Settlement.AntePayoutCents, round.Settlement.BlindPayoutCents,
             round.Settlement.PlayPayoutCents, round.Settlement.TripsPayoutCents));
    }

    [Fact]
    public void FlushBlindPaysThreeToTwoIncludingReturnedStake()
    {
        var round = HouseHoldemRules.StartRound(Deck(
            "2|spades", "5|hearts", "4|spades", "5|diamonds",
            "6|spades", "8|spades", "J|spades", "3|diamonds", "K|clubs"), 100);
        HouseHoldemRules.ApplyAction(round, HouseHoldemActions.Check);
        HouseHoldemRules.ApplyAction(round, HouseHoldemActions.Check);
        HouseHoldemRules.ApplyAction(round, HouseHoldemActions.Play1X);

        Assert.Equal("flush", round.Settlement!.PlayerHandRank);
        Assert.Equal(250, round.Settlement.BlindPayoutCents);
        Assert.Equal(650, round.PayoutCents);
    }

    [Fact]
    public void PremiumBlindAndTripsPaytablesMatchThePostedUth01Schedule()
    {
        AssertPremium("straight-flush", 5_100, 4_100, Deck(
            "9|spades", "A|clubs", "8|spades", "A|diamonds",
            "7|spades", "6|spades", "5|spades", "2|clubs", "3|diamonds"));
        AssertPremium("four-of-a-kind", 1_100, 3_100, Deck(
            "9|spades", "A|clubs", "9|hearts", "A|diamonds",
            "9|diamonds", "9|clubs", "2|clubs", "3|diamonds", "4|hearts"));
        AssertPremium("full-house", 400, 1_000, Deck(
            "K|spades", "A|clubs", "K|hearts", "A|diamonds",
            "K|diamonds", "Q|clubs", "Q|spades", "2|clubs", "3|diamonds"));
        AssertPremium("flush", 250, 800, Deck(
            "2|spades", "5|hearts", "4|spades", "5|diamonds",
            "6|spades", "8|spades", "J|spades", "3|diamonds", "K|clubs"));
        AssertPremium("straight", 200, 500, Deck(
            "8|spades", "4|clubs", "9|hearts", "4|diamonds",
            "10|diamonds", "J|clubs", "Q|spades", "2|clubs", "3|diamonds"));
    }

    [Fact]
    public void IllegalActionsAndInvalidDecksCannotChangeRound()
    {
        var deck = Deck(
            "A|spades", "4|clubs", "K|spades", "4|hearts",
            "Q|spades", "J|spades", "10|spades", "2|clubs", "3|diamonds");
        Assert.Throws<ArgumentException>(() => HouseHoldemRules.StartRound(deck.Take(51).ToArray(), 100));
        Assert.Throws<ArgumentOutOfRangeException>(() => HouseHoldemRules.StartRound(deck, 101));
        var round = HouseHoldemRules.StartRound(deck, 100);
        Assert.Throws<InvalidOperationException>(() => HouseHoldemRules.ApplyAction(round, HouseHoldemActions.Play2X));
        Assert.Throws<InvalidOperationException>(() => HouseHoldemRules.Advance(round));
        Assert.Equal(HouseHoldemPhases.Preflop, round.Phase);
        Assert.Equal(0, round.PlayWagerCents);
    }

    private static IReadOnlyList<string> Deck(params string[] firstNine)
    {
        Assert.Equal(9, firstNine.Length);
        Assert.Equal(9, firstNine.Distinct(StringComparer.Ordinal).Count());
        return firstNine.Concat(StandardDeck.Create().Select(card => card.Code)
            .Where(code => !firstNine.Contains(code, StringComparer.Ordinal))).ToArray();
    }

    private static void AssertPremium(string rank, long blindPayout, long tripsPayout, IReadOnlyList<string> deck)
    {
        var round = HouseHoldemRules.StartRound(deck, 100, 100);
        HouseHoldemRules.ApplyAction(round, HouseHoldemActions.Play4X);
        HouseHoldemRules.Advance(round);
        HouseHoldemRules.Advance(round);
        Assert.Equal("win", round.Settlement!.Outcome);
        Assert.Equal(rank, round.Settlement.PlayerHandRank);
        Assert.Equal(blindPayout, round.Settlement.BlindPayoutCents);
        Assert.Equal(tripsPayout, round.Settlement.TripsPayoutCents);
    }
}
