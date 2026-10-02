using FortuneForge.Games.Cards;
using FortuneForge.Games.VideoPoker;

namespace FortuneForge.Games.Tests.VideoPoker;

public sealed class FullPayJacksOrBetterPaytableTests
{
    [Theory]
    [InlineData(250, "10|spades", "J|spades", "Q|spades", "K|spades", "A|spades")]
    [InlineData(50, "5|clubs", "6|clubs", "7|clubs", "8|clubs", "9|clubs")]
    [InlineData(25, "A|clubs", "A|diamonds", "A|hearts", "A|spades", "3|clubs")]
    [InlineData(9, "A|clubs", "A|diamonds", "A|hearts", "9|spades", "9|clubs")]
    [InlineData(6, "A|clubs", "J|clubs", "9|clubs", "6|clubs", "3|clubs")]
    [InlineData(4, "5|clubs", "6|diamonds", "7|hearts", "8|spades", "9|clubs")]
    [InlineData(3, "A|clubs", "A|diamonds", "A|hearts", "6|spades", "3|clubs")]
    [InlineData(2, "A|clubs", "A|diamonds", "9|hearts", "9|spades", "3|clubs")]
    [InlineData(1, "J|clubs", "J|diamonds", "9|hearts", "6|spades", "3|clubs")]
    public void EvaluatePaysEveryStandardPayoutClass(int expectedCredits, params string[] cardCodes)
    {
        var outcome = FullPayJacksOrBetterPaytable.Evaluate(Deal(cardCodes), coinsWagered: 1);

        Assert.Equal(FullPayJacksOrBetterPaytable.Id, outcome.PaytableId);
        Assert.Equal(1, outcome.CoinsWagered);
        Assert.Equal(expectedCredits, outcome.CreditsWon);
    }

    [Fact]
    public void EvaluateRejectsALowPair()
    {
        var outcome = FullPayJacksOrBetterPaytable.Evaluate(
            Deal("10|clubs", "10|diamonds", "9|hearts", "6|spades", "3|clubs"), coinsWagered: 3);

        Assert.Equal(0, outcome.CreditsWon);
    }

    [Fact]
    public void EvaluateAwardsTheFiveCoinRoyalFlushBonus()
    {
        var outcome = FullPayJacksOrBetterPaytable.Evaluate(
            Deal("10|spades", "J|spades", "Q|spades", "K|spades", "A|spades"), coinsWagered: 5);

        Assert.Equal(4_000, outcome.CreditsWon);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(6)]
    public void EvaluateRejectsWagersOutsideOneThroughFiveCoins(int coinsWagered)
    {
        Assert.Throws<ArgumentOutOfRangeException>(() =>
            FullPayJacksOrBetterPaytable.Evaluate(
                Deal("A|clubs", "K|diamonds", "9|hearts", "6|spades", "3|clubs"), coinsWagered));
    }

    private static VideoPokerDeal Deal(params string[] cardCodes) =>
        new(cardCodes.Select(CardCode.Parse));
}
