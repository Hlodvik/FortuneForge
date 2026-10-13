using FortuneForge.Games.Cards;

namespace FortuneForge.Games.ThreeCardPoker;

public enum ThreeCardPokerHandCategory
{
    HighCard, Pair, Flush, Straight, ThreeOfAKind, StraightFlush
}

public sealed record ThreeCardPokerHandValue(ThreeCardPokerHandCategory Category, IReadOnlyList<int> Kickers)
    : IComparable<ThreeCardPokerHandValue>
{
    public string Rank => Category switch
    {
        ThreeCardPokerHandCategory.StraightFlush => "straight-flush",
        ThreeCardPokerHandCategory.ThreeOfAKind => "three-of-a-kind",
        ThreeCardPokerHandCategory.HighCard => "high-card",
        _ => Category.ToString().ToLowerInvariant()
    };

    public int CompareTo(ThreeCardPokerHandValue? other)
    {
        if (other is null) return 1;
        var category = Category.CompareTo(other.Category);
        if (category != 0) return category;
        for (var index = 0; index < Kickers.Count; index++)
        {
            var comparison = Kickers[index].CompareTo(other.Kickers[index]);
            if (comparison != 0) return comparison;
        }
        return 0;
    }
}

public static class ThreeCardPokerRules
{
    // Use the shared deck and rank|suit card codes consumed by the existing card games.
    public static IReadOnlyList<string> CreateShuffledDeck()
    {
        var deck = StandardDeck.Create().Select(card => card.Code).ToArray();
        for (var index = deck.Length - 1; index > 0; index--)
        {
            var replacement = System.Security.Cryptography.RandomNumberGenerator.GetInt32(index + 1);
            (deck[index], deck[replacement]) = (deck[replacement], deck[index]);
        }
        return deck;
    }

    public static (string Rank, string Suit) ParseCard(string card)
    {
        _ = CardCode.Parse(card);
        var separator = card.IndexOf('|');
        return (card[..separator], card[(separator + 1)..]);
    }

    public static ThreeCardPokerHandValue Evaluate(IReadOnlyList<string> cards)
    {
        if (cards.Count != 3 || cards.Distinct(StringComparer.Ordinal).Count() != 3)
            throw new ArgumentException("A Three Card Poker hand requires three distinct cards.", nameof(cards));
        var parsed = cards.Select(ParseCard).ToArray();
        var values = parsed.Select(card => Value(card.Rank)).OrderDescending().ToArray();
        var groups = values.GroupBy(value => value).OrderByDescending(group => group.Count())
            .ThenByDescending(group => group.Key).ToArray();
        var flush = parsed.All(card => card.Suit == parsed[0].Suit);
        var straightHigh = values.SequenceEqual(new[] { 14, 3, 2 }) ? 3
            : values[0] - values[1] == 1 && values[1] - values[2] == 1 ? values[0] : 0;
        if (flush && straightHigh > 0) return new(ThreeCardPokerHandCategory.StraightFlush, [straightHigh]);
        if (groups[0].Count() == 3) return new(ThreeCardPokerHandCategory.ThreeOfAKind, [groups[0].Key]);
        if (straightHigh > 0) return new(ThreeCardPokerHandCategory.Straight, [straightHigh]);
        if (flush) return new(ThreeCardPokerHandCategory.Flush, values);
        if (groups[0].Count() == 2) return new(ThreeCardPokerHandCategory.Pair, groups.Select(group => group.Key).ToArray());
        return new(ThreeCardPokerHandCategory.HighCard, values);
    }

    public static bool DealerQualifies(ThreeCardPokerHandValue hand) =>
        hand.Category != ThreeCardPokerHandCategory.HighCard || hand.Kickers[0] >= 12;

    public static bool ShouldPlay(IReadOnlyList<string> cards) =>
        Evaluate(cards).CompareTo(new(ThreeCardPokerHandCategory.HighCard, [12, 6, 4])) >= 0;

    public static int AnteBonusOdds(ThreeCardPokerHandCategory category) => category switch
    {
        ThreeCardPokerHandCategory.StraightFlush => 5,
        ThreeCardPokerHandCategory.ThreeOfAKind => 4,
        ThreeCardPokerHandCategory.Straight => 1,
        _ => 0
    };

    public static int PairPlusOdds(ThreeCardPokerHandCategory category) => category switch
    {
        ThreeCardPokerHandCategory.StraightFlush => 40,
        ThreeCardPokerHandCategory.ThreeOfAKind => 30,
        ThreeCardPokerHandCategory.Straight => 6,
        ThreeCardPokerHandCategory.Flush => 3,
        ThreeCardPokerHandCategory.Pair => 1,
        _ => 0
    };

    private static int Value(string rank) => rank switch
    {
        "A" => 14, "K" => 13, "Q" => 12, "J" => 11,
        _ => int.Parse(rank, System.Globalization.CultureInfo.InvariantCulture)
    };
}
