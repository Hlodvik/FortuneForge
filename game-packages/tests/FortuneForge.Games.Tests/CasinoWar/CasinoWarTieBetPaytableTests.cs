using System.Collections.Immutable;
using FortuneForge.Games.Cards;
using FortuneForge.Games.CasinoWar;

namespace FortuneForge.Games.Tests.CasinoWar;

public sealed class CasinoWarTieBetPaytableTests
{
    [Fact]
    public void SettlePaysElevenTimesTheStakeForATieAcrossSuits()
    {
        var opening = CasinoWarOpeningDealer.Deal(
        [
            Card(CardRank.Queen, CardSuit.Clubs),
            Card(CardRank.Queen, CardSuit.Spades),
        ]);

        var settlement = CasinoWarTieBetPaytable.Settle(opening, 2.5m);

        Assert.Equal(2.5m, settlement.Stake);
        Assert.True(settlement.Won);
        Assert.Equal(CasinoWarSettlementDisposition.Win, settlement.Disposition);
        Assert.Equal(CasinoWarOpeningOutcome.TieDecisionRequired, settlement.OpeningOutcome);
        Assert.Equal(27.5m, settlement.TotalReturn);
        Assert.Equal(25m, settlement.Profit);
    }

    [Theory]
    [InlineData(CardRank.Ace, CardRank.King, CasinoWarOpeningOutcome.PlayerWin)]
    [InlineData(CardRank.King, CardRank.Ace, CasinoWarOpeningOutcome.DealerWin)]
    public void SettleLosesForANonTieOpening(
        CardRank playerRank,
        CardRank dealerRank,
        CasinoWarOpeningOutcome expectedOutcome)
    {
        var opening = CasinoWarOpeningDealer.Deal(
        [
            Card(playerRank, CardSuit.Clubs),
            Card(dealerRank, CardSuit.Diamonds),
        ]);

        var settlement = CasinoWarTieBetPaytable.Settle(opening, 3m);

        Assert.False(settlement.Won);
        Assert.Equal(CasinoWarSettlementDisposition.Loss, settlement.Disposition);
        Assert.Equal(expectedOutcome, settlement.OpeningOutcome);
        Assert.Equal(0m, settlement.TotalReturn);
        Assert.Equal(-3m, settlement.Profit);
        Assert.Equal(settlement.TotalReturn - settlement.Stake, settlement.Profit);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-0.01)]
    public void SettleRejectsANonPositiveStake(decimal stake)
    {
        var opening = CasinoWarOpeningDealer.Deal(
        [
            Card(CardRank.Two, CardSuit.Clubs),
            Card(CardRank.Two, CardSuit.Diamonds),
        ]);

        Assert.Throws<ArgumentOutOfRangeException>(() => CasinoWarTieBetPaytable.Settle(opening, stake));
    }

    [Fact]
    public void SettleRejectsANullOpening() =>
        Assert.Throws<ArgumentNullException>(() => CasinoWarTieBetPaytable.Settle(null!, 1m));

    [Fact]
    public void SettleRejectsAMalformedOpeningOutcome()
    {
        var card = Card(CardRank.Ace, CardSuit.Clubs);
        var opening = new CasinoWarOpeningResult(
            card,
            card,
            (CasinoWarOpeningOutcome)99,
            ImmutableArray.Create(card, card));

        Assert.Throws<ArgumentOutOfRangeException>(() => CasinoWarTieBetPaytable.Settle(opening, 1m));
    }

    private static PlayingCard Card(CardRank rank, CardSuit suit) => new(rank, suit);
}
