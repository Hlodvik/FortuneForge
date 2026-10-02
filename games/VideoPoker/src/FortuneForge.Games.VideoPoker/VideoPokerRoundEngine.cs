using System.Collections.Immutable;
using FortuneForge.Games.Cards;

namespace FortuneForge.Games.VideoPoker;

public enum VideoPokerRoundStatus
{
    AwaitingDraw,
    Completed,
}

public sealed record VideoPokerRound(
    VideoPokerDeal InitialDeal,
    ImmutableArray<ImmutableArray<PlayingCard>> RemainingDecks,
    int CoinsWagered,
    int HandCount,
    VideoPokerRoundStatus Status,
    VideoPokerHeldCardPositions? HeldCardPositions,
    ImmutableArray<VideoPokerDraw> Draws,
    ImmutableArray<VideoPokerHandResult> Results)
{
    // Keep the original single-hand API available to older callers and persisted schema readers.
    public ImmutableArray<PlayingCard> RemainingDeck => RemainingDecks[0];
    public VideoPokerDraw? Draw => Draws.IsDefaultOrEmpty ? null : Draws[0];
    public VideoPokerHandResult? Result => Results.IsDefaultOrEmpty ? null : Results[0];
}

public static class VideoPokerRoundEngine
{
    public static VideoPokerRound Deal(IReadOnlyList<PlayingCard> orderedDeck, int coinsWagered)
        => Deal([orderedDeck], coinsWagered);

    public static VideoPokerRound Deal(
        IReadOnlyList<IReadOnlyList<PlayingCard>> orderedDecks,
        int coinsWagered)
    {
        ArgumentNullException.ThrowIfNull(orderedDecks);
        ValidateWager(coinsWagered);
        ValidateHandCount(orderedDecks.Count);
        foreach (var deck in orderedDecks)
        {
            ArgumentNullException.ThrowIfNull(deck);
            ValidateStandardDeck(deck);
        }

        var initialDeal = new VideoPokerDeal(orderedDecks[0].Take(VideoPokerDeal.CardCount));
        var initialCards = initialDeal.Cards.ToHashSet();
        var remainingDecks = orderedDecks
            .Select(deck => deck.Where(card => !initialCards.Contains(card)).ToImmutableArray())
            .ToImmutableArray();
        if (remainingDecks.Any(deck => deck.Length != StandardDeck.Create().Count - VideoPokerDeal.CardCount))
            throw new ArgumentException("Every Video Poker replacement deck must contain the shared dealt cards exactly once.", nameof(orderedDecks));

        return new VideoPokerRound(
            initialDeal,
            remainingDecks,
            coinsWagered,
            orderedDecks.Count,
            VideoPokerRoundStatus.AwaitingDraw,
            HeldCardPositions: null,
            Draws: [],
            Results: []);
    }

    public static VideoPokerRound Draw(VideoPokerRound round, VideoPokerHeldCardPositions heldCardPositions)
    {
        ArgumentNullException.ThrowIfNull(round);
        ArgumentNullException.ThrowIfNull(heldCardPositions);
        if (round.Status != VideoPokerRoundStatus.AwaitingDraw || !round.Draws.IsDefaultOrEmpty)
            throw new InvalidOperationException("This Video Poker round has already been drawn.");

        var held = heldCardPositions.Positions.ToHashSet();
        var cardsDrawn = VideoPokerDeal.CardCount - held.Count;
        var draws = ImmutableArray.CreateBuilder<VideoPokerDraw>(round.HandCount);
        var results = ImmutableArray.CreateBuilder<VideoPokerHandResult>(round.HandCount);
        foreach (var replacementDeck in round.RemainingDecks)
        {
            var finalCards = round.InitialDeal.Cards.ToArray();
            var nextCardIndex = 0;
            for (var index = 0; index < finalCards.Length; index++)
            {
                if (held.Contains((VideoPokerCardPosition)index)) continue;

                finalCards[index] = replacementDeck[nextCardIndex++];
            }

            var finalHand = new VideoPokerDeal(finalCards);
            draws.Add(new VideoPokerDraw(round.InitialDeal, heldCardPositions, finalHand));
            results.Add(new VideoPokerHandResult(
                finalHand,
                VideoPokerHandEvaluator.Evaluate(finalHand),
                FullPayJacksOrBetterPaytable.Evaluate(finalHand, round.CoinsWagered)));
        }

        return round with
        {
            RemainingDecks = [.. round.RemainingDecks.Select(deck => deck.RemoveRange(0, cardsDrawn))],
            Status = VideoPokerRoundStatus.Completed,
            HeldCardPositions = heldCardPositions,
            Draws = draws.MoveToImmutable(),
            Results = results.MoveToImmutable(),
        };
    }

    private static void ValidateWager(int coinsWagered)
    {
        if (coinsWagered is < 1 or > 5)
            throw new ArgumentOutOfRangeException(nameof(coinsWagered), "Wager from one through five coins.");
    }

    private static void ValidateHandCount(int handCount)
    {
        if (handCount is not (1 or 3 or 5))
            throw new ArgumentOutOfRangeException(nameof(handCount), "Choose one, three, or five hands.");
    }

    private static void ValidateStandardDeck(IReadOnlyList<PlayingCard> orderedDeck)
    {
        var standardDeck = StandardDeck.Create();
        if (orderedDeck.Count != standardDeck.Count ||
            !orderedDeck.ToHashSet().SetEquals(standardDeck))
        {
            throw new ArgumentException("A Video Poker deck must be a standard 52-card deck.", nameof(orderedDeck));
        }
    }
}
