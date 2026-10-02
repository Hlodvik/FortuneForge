namespace FortuneForge.Games.Cards;

public enum CardSuit
{
    Clubs,
    Diamonds,
    Hearts,
    Spades,
}

public enum CardRank
{
    Ace = 1,
    Two = 2,
    Three = 3,
    Four = 4,
    Five = 5,
    Six = 6,
    Seven = 7,
    Eight = 8,
    Nine = 9,
    Ten = 10,
    Jack = 11,
    Queen = 12,
    King = 13,
}

public readonly record struct PlayingCard(CardRank Rank, CardSuit Suit)
{
    public string Code => CardCode.Format(this);
}

public static class CardCode
{
    public static PlayingCard Parse(string value)
    {
        if (string.IsNullOrWhiteSpace(value))
            throw new ArgumentException("A card code is required.", nameof(value));

        var separator = value.IndexOf('|');
        if (separator <= 0 || separator == value.Length - 1 || value.IndexOf('|', separator + 1) >= 0)
            throw new ArgumentException("A card code must use the rank|suit format.", nameof(value));

        return new PlayingCard(ParseRank(value[..separator]), ParseSuit(value[(separator + 1)..]));
    }

    public static string Format(PlayingCard card) => $"{FormatRank(card.Rank)}|{FormatSuit(card.Suit)}";

    private static CardRank ParseRank(string value) => value switch
    {
        "A" => CardRank.Ace,
        "2" => CardRank.Two,
        "3" => CardRank.Three,
        "4" => CardRank.Four,
        "5" => CardRank.Five,
        "6" => CardRank.Six,
        "7" => CardRank.Seven,
        "8" => CardRank.Eight,
        "9" => CardRank.Nine,
        "10" => CardRank.Ten,
        "J" => CardRank.Jack,
        "Q" => CardRank.Queen,
        "K" => CardRank.King,
        _ => throw new ArgumentException("A card rank is invalid.", nameof(value)),
    };

    private static CardSuit ParseSuit(string value) => value switch
    {
        "clubs" => CardSuit.Clubs,
        "diamonds" => CardSuit.Diamonds,
        "hearts" => CardSuit.Hearts,
        "spades" => CardSuit.Spades,
        _ => throw new ArgumentException("A card suit is invalid.", nameof(value)),
    };

    private static string FormatRank(CardRank rank) => rank switch
    {
        CardRank.Ace => "A",
        CardRank.Jack => "J",
        CardRank.Queen => "Q",
        CardRank.King => "K",
        >= CardRank.Two and <= CardRank.Ten => ((int)rank).ToString(System.Globalization.CultureInfo.InvariantCulture),
        _ => throw new ArgumentOutOfRangeException(nameof(rank)),
    };

    private static string FormatSuit(CardSuit suit) => suit switch
    {
        CardSuit.Clubs => "clubs",
        CardSuit.Diamonds => "diamonds",
        CardSuit.Hearts => "hearts",
        CardSuit.Spades => "spades",
        _ => throw new ArgumentOutOfRangeException(nameof(suit)),
    };
}

public static class StandardDeck
{
    public static IReadOnlyList<PlayingCard> Create() =>
        Enum.GetValues<CardSuit>()
            .SelectMany(suit => Enum.GetValues<CardRank>().Select(rank => new PlayingCard(rank, suit)))
            .ToArray();
}
