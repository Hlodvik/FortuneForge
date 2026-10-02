using FortuneForge.Games.Baccarat;
using FortuneForge.Games.Cards;

namespace FortuneForge.Games.Tests.Baccarat;

public sealed class PuntoBancoTableauTests
{
    [Theory]
    [InlineData(0, true)]
    [InlineData(5, true)]
    [InlineData(6, false)]
    [InlineData(7, false)]
    public void PlayerDrawsOnZeroThroughFiveAndStandsOnSixOrSeven(int playerTotal, bool expected)
    {
        Assert.Equal(expected, PuntoBancoTableau.ShouldPlayerDraw(InitialHand(playerTotal), InitialHand(0)));
    }

    [Theory]
    [InlineData(0, 0, true)]
    [InlineData(1, 8, true)]
    [InlineData(2, 9, true)]
    [InlineData(3, 7, true)]
    [InlineData(3, 8, false)]
    [InlineData(4, 1, false)]
    [InlineData(4, 2, true)]
    [InlineData(4, 7, true)]
    [InlineData(4, 8, false)]
    [InlineData(5, 3, false)]
    [InlineData(5, 4, true)]
    [InlineData(5, 7, true)]
    [InlineData(5, 8, false)]
    [InlineData(6, 5, false)]
    [InlineData(6, 6, true)]
    [InlineData(6, 7, true)]
    [InlineData(6, 8, false)]
    [InlineData(7, 0, false)]
    [InlineData(7, 7, false)]
    public void BankerTableauUsesEveryTotalRowAndBoundary(int bankerTotal, int playerThirdCardPoint, bool expected)
    {
        Assert.Equal(
            expected,
            PuntoBancoTableau.ShouldBankerDraw(InitialHand(5), InitialHand(bankerTotal), playerThirdCardPoint));
    }

    [Theory]
    [InlineData(0, true)]
    [InlineData(5, true)]
    [InlineData(6, false)]
    [InlineData(7, false)]
    public void BankerDrawsOnZeroThroughFiveWhenPlayerStands(int bankerTotal, bool expected)
    {
        Assert.Equal(expected, PuntoBancoTableau.ShouldBankerDraw(InitialHand(6), InitialHand(bankerTotal)));
    }

    [Fact]
    public void NaturalsSuppressAllThirdCardDecisions()
    {
        var playerNatural = InitialHand(8);
        var banker = InitialHand(0);

        Assert.False(PuntoBancoTableau.ShouldPlayerDraw(playerNatural, banker));
        Assert.False(PuntoBancoTableau.ShouldBankerDraw(playerNatural, banker));
    }

    [Fact]
    public void NaturalsRejectAValidSuppliedPlayerThirdCardPoint()
    {
        Assert.Throws<ArgumentException>(() =>
            PuntoBancoTableau.ShouldBankerDraw(InitialHand(9), InitialHand(0), playerThirdCardPoint: 3));
    }

    [Theory]
    [InlineData(-1)]
    [InlineData(10)]
    public void NaturalsRejectOutOfRangePlayerThirdCardPointsDeterministically(int playerThirdCardPoint)
    {
        Assert.Throws<ArgumentOutOfRangeException>(() =>
            PuntoBancoTableau.ShouldBankerDraw(InitialHand(9), InitialHand(0), playerThirdCardPoint));
    }

    [Fact]
    public void RejectsHandsThatAreNotInitialTwoCardHands()
    {
        var threeCards = new BaccaratHand(
        [
            new PlayingCard(CardRank.Ace, CardSuit.Clubs),
            new PlayingCard(CardRank.Two, CardSuit.Clubs),
            new PlayingCard(CardRank.Three, CardSuit.Clubs),
        ]);

        Assert.Throws<ArgumentException>(() => PuntoBancoTableau.ShouldPlayerDraw(threeCards, InitialHand(0)));
    }

    [Fact]
    public void RejectsMissingPlayerThirdCardContextWhenThePlayerDraws()
    {
        Assert.Throws<ArgumentException>(() =>
            PuntoBancoTableau.ShouldBankerDraw(InitialHand(5), InitialHand(0)));
    }

    [Fact]
    public void RejectsPlayerThirdCardContextWhenThePlayerStands()
    {
        Assert.Throws<ArgumentException>(() =>
            PuntoBancoTableau.ShouldBankerDraw(InitialHand(6), InitialHand(0), playerThirdCardPoint: 2));
    }

    [Theory]
    [InlineData(-1)]
    [InlineData(10)]
    public void RejectsPlayerThirdCardPointsOutsideZeroThroughNine(int playerThirdCardPoint)
    {
        Assert.Throws<ArgumentOutOfRangeException>(() =>
            PuntoBancoTableau.ShouldBankerDraw(InitialHand(5), InitialHand(0), playerThirdCardPoint));
    }

    private static BaccaratHand InitialHand(int total)
    {
        var firstRank = total == 0 ? CardRank.Ten : (CardRank)total;
        return new BaccaratHand(
        [
            new PlayingCard(firstRank, CardSuit.Clubs),
            new PlayingCard(CardRank.Ten, CardSuit.Diamonds),
        ]);
    }
}
