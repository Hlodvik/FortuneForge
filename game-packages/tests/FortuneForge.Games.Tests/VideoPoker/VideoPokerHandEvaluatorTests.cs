using FortuneForge.Games.Cards;
using FortuneForge.Games.VideoPoker;

namespace FortuneForge.Games.Tests.VideoPoker;

public sealed class VideoPokerHandEvaluatorTests
{
    [Theory]
    [InlineData(VideoPokerHandRank.NoWin, "A|clubs", "K|diamonds", "9|hearts", "6|spades", "3|clubs")]
    [InlineData(VideoPokerHandRank.Pair, "A|clubs", "A|diamonds", "9|hearts", "6|spades", "3|clubs")]
    [InlineData(VideoPokerHandRank.TwoPair, "A|clubs", "A|diamonds", "9|hearts", "9|spades", "3|clubs")]
    [InlineData(VideoPokerHandRank.ThreeOfAKind, "A|clubs", "A|diamonds", "A|hearts", "6|spades", "3|clubs")]
    [InlineData(VideoPokerHandRank.Straight, "5|clubs", "6|diamonds", "7|hearts", "8|spades", "9|clubs")]
    [InlineData(VideoPokerHandRank.Flush, "A|clubs", "J|clubs", "9|clubs", "6|clubs", "3|clubs")]
    [InlineData(VideoPokerHandRank.FullHouse, "A|clubs", "A|diamonds", "A|hearts", "9|spades", "9|clubs")]
    [InlineData(VideoPokerHandRank.FourOfAKind, "A|clubs", "A|diamonds", "A|hearts", "A|spades", "3|clubs")]
    [InlineData(VideoPokerHandRank.StraightFlush, "5|clubs", "6|clubs", "7|clubs", "8|clubs", "9|clubs")]
    [InlineData(VideoPokerHandRank.RoyalFlush, "10|spades", "J|spades", "Q|spades", "K|spades", "A|spades")]
    public void EvaluateClassifiesEachHandRank(VideoPokerHandRank expected, params string[] cardCodes)
    {
        var result = VideoPokerHandEvaluator.Evaluate(Deal(cardCodes));

        Assert.Equal(expected, result);
    }

    [Fact]
    public void EvaluateRecognizesTheAceLowStraight()
    {
        var result = VideoPokerHandEvaluator.Evaluate(
            Deal("A|clubs", "2|diamonds", "3|hearts", "4|spades", "5|clubs"));

        Assert.Equal(VideoPokerHandRank.Straight, result);
    }

    private static VideoPokerDeal Deal(params string[] cardCodes) =>
        new(cardCodes.Select(CardCode.Parse));
}
