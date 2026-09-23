using FortuneForge.Games.Cards;
using FortuneForge.Games.CasinoWar;

namespace FortuneForge.Games.Tests.CasinoWar;

public sealed class CasinoWarOpeningDealerTests
{
    [Theory]
    [InlineData(CardRank.Two, 2)]
    [InlineData(CardRank.Three, 3)]
    [InlineData(CardRank.Four, 4)]
    [InlineData(CardRank.Five, 5)]
    [InlineData(CardRank.Six, 6)]
    [InlineData(CardRank.Seven, 7)]
    [InlineData(CardRank.Eight, 8)]
    [InlineData(CardRank.Nine, 9)]
    [InlineData(CardRank.Ten, 10)]
    [InlineData(CardRank.Jack, 11)]
    [InlineData(CardRank.Queen, 12)]
    [InlineData(CardRank.King, 13)]
    [InlineData(CardRank.Ace, 14)]
    public void RankStrengthMapsEveryRankWithAceHigh(CardRank rank, int expected) =>
        Assert.Equal(expected, CasinoWarRankStrength.Value(Card(rank, CardSuit.Clubs)));

    [Theory]
    [InlineData(CardRank.Three, CardRank.Two, CasinoWarOpeningOutcome.PlayerWin)]
    [InlineData(CardRank.Two, CardRank.Three, CasinoWarOpeningOutcome.DealerWin)]
    [InlineData(CardRank.Ace, CardRank.King, CasinoWarOpeningOutcome.PlayerWin)]
    [InlineData(CardRank.King, CardRank.Ace, CasinoWarOpeningOutcome.DealerWin)]
    public void DealComparesOpeningCardsUsingAceHighRanks(
        CardRank playerRank,
        CardRank dealerRank,
        CasinoWarOpeningOutcome expectedOutcome)
    {
        var result = CasinoWarOpeningDealer.Deal(
        [
            Card(playerRank, CardSuit.Clubs),
            Card(dealerRank, CardSuit.Diamonds),
        ]);

        Assert.Equal(expectedOutcome, result.Outcome);
    }

    [Fact]
    public void DealRequiresATieDecisionForEqualRanksAcrossSuits()
    {
        var result = CasinoWarOpeningDealer.Deal(
        [
            Card(CardRank.Queen, CardSuit.Clubs),
            Card(CardRank.Queen, CardSuit.Spades),
        ]);

        Assert.Equal(CasinoWarOpeningOutcome.TieDecisionRequired, result.Outcome);
    }

    [Fact]
    public void DealUsesPlayerThenDealerOrderAndReturnsTheConsumedPrefix()
    {
        PlayingCard[] shoe =
        [
            Card(CardRank.Ten, CardSuit.Hearts),
            Card(CardRank.Four, CardSuit.Diamonds),
            Card(CardRank.Ace, CardSuit.Spades),
        ];

        var result = CasinoWarOpeningDealer.Deal(shoe);

        Assert.Equal(shoe[0], result.PlayerCard);
        Assert.Equal(shoe[1], result.DealerCard);
        Assert.Equal([shoe[0], shoe[1]], result.ConsumedCards.ToArray());
        Assert.Equal(2, result.CardsConsumed);
    }

    [Fact]
    public void DealAllowsDuplicateCardValuesFromAMultiDeckShoe()
    {
        var duplicate = Card(CardRank.Ace, CardSuit.Spades);

        var result = CasinoWarOpeningDealer.Deal([duplicate, duplicate]);

        Assert.Equal(duplicate, result.PlayerCard);
        Assert.Equal(duplicate, result.DealerCard);
        Assert.Equal(CasinoWarOpeningOutcome.TieDecisionRequired, result.Outcome);
    }

    [Fact]
    public void DealRejectsANullShoe() =>
        Assert.Throws<ArgumentNullException>(() => CasinoWarOpeningDealer.Deal(null!));

    [Fact]
    public void DealRejectsAnInsufficientShoe() =>
        Assert.Throws<ArgumentException>(() => CasinoWarOpeningDealer.Deal([Card(CardRank.Ace, CardSuit.Clubs)]));

    private static PlayingCard Card(CardRank rank, CardSuit suit) => new(rank, suit);
}
