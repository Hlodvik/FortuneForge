using FortuneForge.Games.Cards;

namespace FortuneForge.Games.TexasHoldem;

public sealed record HoldemHandValue(int Category, string Name, ulong Score);

public static class TexasHoldemHandEvaluator
{
    public static HoldemHandValue Evaluate(IReadOnlyList<string> cardCodes) =>
        Evaluate(cardCodes.Select(CardCode.Parse).ToArray());

    public static HoldemHandValue Evaluate(IReadOnlyList<PlayingCard> cards)
    {
        if (cards.Count is < 5 or > 7)
            throw new ArgumentException("Hold'em evaluation requires 5 through 7 cards.", nameof(cards));

        HoldemHandValue? best = null;
        for (var a = 0; a < cards.Count - 4; a++)
            for (var b = a + 1; b < cards.Count - 3; b++)
                for (var c = b + 1; c < cards.Count - 2; c++)
                    for (var d = c + 1; d < cards.Count - 1; d++)
                        for (var e = d + 1; e < cards.Count; e++)
                        {
                            var value = EvaluateFive([cards[a], cards[b], cards[c], cards[d], cards[e]]);
                            if (best is null || value.Score > best.Score) best = value;
                        }

        return best!;
    }

    private static HoldemHandValue EvaluateFive(IReadOnlyList<PlayingCard> cards)
    {
        var ranks = cards.Select(HighRank).OrderByDescending(value => value).ToArray();
        var groups = ranks.GroupBy(value => value)
            .Select(group => (Rank: group.Key, Count: group.Count()))
            .OrderByDescending(group => group.Count)
            .ThenByDescending(group => group.Rank)
            .ToArray();
        var flush = cards.Select(card => card.Suit).Distinct().Count() == 1;
        var straightHigh = StraightHigh(ranks.Distinct().OrderByDescending(value => value).ToArray());

        if (flush && straightHigh > 0) return Value(8, "straight-flush", straightHigh);
        if (groups[0].Count == 4) return Value(7, "four-of-a-kind", groups[0].Rank, groups[1].Rank);
        if (groups[0].Count == 3 && groups[1].Count == 2)
            return Value(6, "full-house", groups[0].Rank, groups[1].Rank);
        if (flush) return Value(5, "flush", ranks);
        if (straightHigh > 0) return Value(4, "straight", straightHigh);
        if (groups[0].Count == 3)
            return Value(3, "three-of-a-kind", [groups[0].Rank, .. groups.Skip(1).Select(group => group.Rank).OrderByDescending(value => value)]);
        if (groups[0].Count == 2 && groups[1].Count == 2)
        {
            var pairs = groups.Where(group => group.Count == 2).Select(group => group.Rank).OrderByDescending(value => value).ToArray();
            var kicker = groups.Single(group => group.Count == 1).Rank;
            return Value(2, "two-pair", pairs[0], pairs[1], kicker);
        }
        if (groups[0].Count == 2)
            return Value(1, "pair", [groups[0].Rank, .. groups.Skip(1).Select(group => group.Rank).OrderByDescending(value => value)]);
        return Value(0, "high-card", ranks);
    }

    private static int HighRank(PlayingCard card) => card.Rank == CardRank.Ace ? 14 : (int)card.Rank;

    private static int StraightHigh(IReadOnlyList<int> ranks)
    {
        var values = ranks.Contains(14) ? ranks.Append(1).ToArray() : ranks.ToArray();
        for (var index = 0; index <= values.Length - 5; index++)
            if (values[index] - values[index + 4] == 4) return values[index];
        return 0;
    }

    private static HoldemHandValue Value(int category, string name, params int[] kickers)
    {
        ulong score = (ulong)category;
        foreach (var kicker in kickers.Take(5)) score = score * 15 + (uint)kicker;
        for (var index = kickers.Length; index < 5; index++) score *= 15;
        return new HoldemHandValue(category, name, score);
    }
}
