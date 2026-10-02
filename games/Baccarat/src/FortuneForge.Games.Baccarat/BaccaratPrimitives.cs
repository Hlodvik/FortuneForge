using System.Collections.Immutable;
using FortuneForge.Games.Cards;

namespace FortuneForge.Games.Baccarat;

public enum BaccaratBetSide
{
    Player,
    Banker,
    Tie,
}

public enum BaccaratRoundOutcome
{
    Player,
    Banker,
    Tie,
}

public static class BaccaratCardPoints
{
    public static int Value(PlayingCard card) => card.Rank switch
    {
        CardRank.Ace => 1,
        >= CardRank.Two and <= CardRank.Nine => (int)card.Rank,
        >= CardRank.Ten and <= CardRank.King => 0,
        _ => throw new ArgumentOutOfRangeException(nameof(card)),
    };
}

public sealed record BaccaratHand
{
    public BaccaratHand(IEnumerable<PlayingCard> cards)
    {
        ArgumentNullException.ThrowIfNull(cards);

        Cards = [.. cards];
        if (Cards.Length is < 2 or > 3)
            throw new ArgumentException("A Baccarat hand must contain exactly two or three cards.", nameof(cards));
    }

    public ImmutableArray<PlayingCard> Cards { get; }

    public int Total => Cards.Sum(BaccaratCardPoints.Value) % 10;

    public bool IsNatural => Cards.Length == 2 && Total is 8 or 9;
}
