using FortuneForge.Games.Baccarat;
using FortuneForge.Games.Cards;

namespace FortuneForge.Games.Tests.Baccarat;

public sealed class BaccaratPrimitivesTests
{
    [Theory]
    [InlineData(CardRank.Ace, 1)]
    [InlineData(CardRank.Two, 2)]
    [InlineData(CardRank.Three, 3)]
    [InlineData(CardRank.Four, 4)]
    [InlineData(CardRank.Five, 5)]
    [InlineData(CardRank.Six, 6)]
    [InlineData(CardRank.Seven, 7)]
    [InlineData(CardRank.Eight, 8)]
    [InlineData(CardRank.Nine, 9)]
    [InlineData(CardRank.Ten, 0)]
    [InlineData(CardRank.Jack, 0)]
    [InlineData(CardRank.Queen, 0)]
    [InlineData(CardRank.King, 0)]
    public void CardPointsMapsEveryRank(CardRank rank, int expected) =>
        Assert.Equal(expected, BaccaratCardPoints.Value(new PlayingCard(rank, CardSuit.Clubs)));

    [Fact]
    public void HandTotalUsesModuloTen()
    {
        var hand = Hand(CardRank.Nine, CardRank.Eight, CardRank.Five);

        Assert.Equal(2, hand.Total);
    }

    [Fact]
    public void HandAcceptsTwoCards()
    {
        var hand = Hand(CardRank.Four, CardRank.Three);

        Assert.Equal(2, hand.Cards.Length);
        Assert.Equal(7, hand.Total);
    }

    [Fact]
    public void HandAcceptsThreeCards()
    {
        var hand = Hand(CardRank.Four, CardRank.Three, CardRank.Two);

        Assert.Equal(3, hand.Cards.Length);
        Assert.Equal(9, hand.Total);
    }

    [Theory]
    [InlineData(1)]
    [InlineData(4)]
    public void HandRejectsCardCountsOtherThanTwoOrThree(int cardCount)
    {
        var cards = Enumerable.Range(0, cardCount)
            .Select(index => new PlayingCard((CardRank)(index + 1), CardSuit.Clubs));

        Assert.Throws<ArgumentException>(() => new BaccaratHand(cards));
    }

    [Theory]
    [InlineData(CardRank.Eight, CardRank.King, true)]
    [InlineData(CardRank.Nine, CardRank.King, true)]
    [InlineData(CardRank.Seven, CardRank.King, false)]
    public void NaturalRequiresAInitialTwoCardTotalOfEightOrNine(CardRank first, CardRank second, bool expected)
    {
        Assert.Equal(expected, Hand(first, second).IsNatural);
    }

    [Fact]
    public void HandAllowsDuplicateCardValuesForMultiDeckShoes()
    {
        var duplicate = new PlayingCard(CardRank.Ace, CardSuit.Spades);
        var hand = new BaccaratHand([duplicate, duplicate]);

        Assert.Equal(2, hand.Cards.Length);
        Assert.Equal(2, hand.Total);
    }

    private static BaccaratHand Hand(params CardRank[] ranks) =>
        new(ranks.Select(rank => new PlayingCard(rank, CardSuit.Clubs)));
}
