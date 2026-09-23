using FortuneForge.Games.Cards;

namespace FortuneForge.Games.VideoPoker;

public static class VideoPokerHandEvaluator
{
    public static VideoPokerHandRank Evaluate(VideoPokerDeal finalHand)
    {
        ArgumentNullException.ThrowIfNull(finalHand);

        var ranks = finalHand.Cards.Select(card => card.Rank).ToArray();
        var rankCounts = ranks.GroupBy(rank => rank)
            .Select(group => group.Count())
            .OrderDescending()
            .ToArray();
        var isFlush = finalHand.Cards.Select(card => card.Suit).Distinct().Count() == 1;
        var straightHigh = StraightHigh(ranks);

        if (isFlush && straightHigh > 0)
            return straightHigh == 14
                ? VideoPokerHandRank.RoyalFlush
                : VideoPokerHandRank.StraightFlush;
        if (rankCounts[0] == 4) return VideoPokerHandRank.FourOfAKind;
        if (rankCounts is [3, 2]) return VideoPokerHandRank.FullHouse;
        if (isFlush) return VideoPokerHandRank.Flush;
        if (straightHigh > 0) return VideoPokerHandRank.Straight;
        if (rankCounts[0] == 3) return VideoPokerHandRank.ThreeOfAKind;
        if (rankCounts is [2, 2, 1]) return VideoPokerHandRank.TwoPair;
        if (rankCounts[0] == 2) return VideoPokerHandRank.Pair;
        return VideoPokerHandRank.NoWin;
    }

    private static int StraightHigh(IReadOnlyCollection<CardRank> ranks)
    {
        var values = ranks.Select(rank => rank == CardRank.Ace ? 14 : (int)rank).Order().ToArray();
        if (values.SequenceEqual([2, 3, 4, 5, 14])) return 5;

        return values.Zip(values.Skip(1), (current, next) => next - current)
                .All(difference => difference == 1)
            ? values[^1]
            : 0;
    }
}
