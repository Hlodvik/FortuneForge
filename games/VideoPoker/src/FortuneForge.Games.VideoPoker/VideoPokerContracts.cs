using System.Collections.Immutable;
using FortuneForge.Games.Cards;

namespace FortuneForge.Games.VideoPoker;

public enum VideoPokerCardPosition
{
    First,
    Second,
    Third,
    Fourth,
    Fifth,
}

public enum VideoPokerHandRank
{
    NoWin,
    Pair,
    TwoPair,
    ThreeOfAKind,
    Straight,
    Flush,
    FullHouse,
    FourOfAKind,
    StraightFlush,
    RoyalFlush,
}

public sealed record VideoPokerDeal
{
    public const int CardCount = 5;

    public VideoPokerDeal(IEnumerable<PlayingCard> cards)
    {
        ArgumentNullException.ThrowIfNull(cards);

        Cards = [.. cards];
        if (Cards.Length != CardCount)
            throw new ArgumentException("A Video Poker deal must contain exactly five cards.", nameof(cards));

        if (Cards.Distinct().Count() != CardCount)
            throw new ArgumentException("A Video Poker deal cannot contain duplicate cards.", nameof(cards));
    }

    public ImmutableArray<PlayingCard> Cards { get; }
}

public sealed record VideoPokerHeldCardPositions
{
    public VideoPokerHeldCardPositions(IEnumerable<VideoPokerCardPosition> positions)
    {
        ArgumentNullException.ThrowIfNull(positions);

        Positions = [.. positions.Order()];
        if (Positions.Distinct().Count() != Positions.Length)
            throw new ArgumentException("A card position may be held only once.", nameof(positions));

        if (Positions.Any(position => !Enum.IsDefined(position)))
            throw new ArgumentOutOfRangeException(nameof(positions), "A held card position is invalid.");
    }

    public ImmutableArray<VideoPokerCardPosition> Positions { get; }
}

public sealed record VideoPokerDraw(
    VideoPokerDeal InitialDeal,
    VideoPokerHeldCardPositions HeldCardPositions,
    VideoPokerDeal FinalHand);

public sealed record VideoPokerPaytableOutcome(
    string PaytableId,
    int CoinsWagered,
    int CreditsWon);

public sealed record VideoPokerHandResult(
    VideoPokerDeal FinalHand,
    VideoPokerHandRank HandRank,
    VideoPokerPaytableOutcome PaytableOutcome);
