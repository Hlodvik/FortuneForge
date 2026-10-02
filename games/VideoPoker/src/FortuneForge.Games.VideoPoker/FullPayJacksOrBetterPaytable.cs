using FortuneForge.Games.Cards;

namespace FortuneForge.Games.VideoPoker;

public static class FullPayJacksOrBetterPaytable
{
    public const string Id = "jacks-or-better-9-6-full-pay";

    public static VideoPokerPaytableOutcome Evaluate(VideoPokerDeal finalHand, int coinsWagered)
    {
        ArgumentNullException.ThrowIfNull(finalHand);
        if (coinsWagered is < 1 or > 5)
            throw new ArgumentOutOfRangeException(nameof(coinsWagered), "Wager from one through five coins.");

        var handRank = VideoPokerHandEvaluator.Evaluate(finalHand);
        var creditsWon = handRank switch
        {
            VideoPokerHandRank.RoyalFlush when coinsWagered == 5 => 4_000,
            VideoPokerHandRank.RoyalFlush => 250 * coinsWagered,
            VideoPokerHandRank.StraightFlush => 50 * coinsWagered,
            VideoPokerHandRank.FourOfAKind => 25 * coinsWagered,
            VideoPokerHandRank.FullHouse => 9 * coinsWagered,
            VideoPokerHandRank.Flush => 6 * coinsWagered,
            VideoPokerHandRank.Straight => 4 * coinsWagered,
            VideoPokerHandRank.ThreeOfAKind => 3 * coinsWagered,
            VideoPokerHandRank.TwoPair => 2 * coinsWagered,
            VideoPokerHandRank.Pair when HasJacksOrBetterPair(finalHand) => coinsWagered,
            _ => 0,
        };

        return new VideoPokerPaytableOutcome(Id, coinsWagered, creditsWon);
    }

    private static bool HasJacksOrBetterPair(VideoPokerDeal finalHand) => finalHand.Cards
        .GroupBy(card => card.Rank)
        .Any(group => group.Count() == 2 && group.Key is CardRank.Ace or >= CardRank.Jack);
}
