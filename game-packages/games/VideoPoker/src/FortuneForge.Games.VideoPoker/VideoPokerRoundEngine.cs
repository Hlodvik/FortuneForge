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
    ImmutableArray<PlayingCard> RemainingDeck,
    int CoinsWagered,
    VideoPokerRoundStatus Status,
    VideoPokerDraw? Draw,
    VideoPokerHandResult? Result);

public static class VideoPokerRoundEngine
{
    public static VideoPokerRound Deal(IReadOnlyList<PlayingCard> orderedDeck, int coinsWagered)
    {
        ArgumentNullException.ThrowIfNull(orderedDeck);
        ValidateWager(coinsWagered);
        ValidateStandardDeck(orderedDeck);

        return new VideoPokerRound(
            new VideoPokerDeal(orderedDeck.Take(VideoPokerDeal.CardCount)),
            [.. orderedDeck.Skip(VideoPokerDeal.CardCount)],
            coinsWagered,
            VideoPokerRoundStatus.AwaitingDraw,
            Draw: null,
            Result: null);
    }

    public static VideoPokerRound Draw(VideoPokerRound round, VideoPokerHeldCardPositions heldCardPositions)
    {
        ArgumentNullException.ThrowIfNull(round);
        ArgumentNullException.ThrowIfNull(heldCardPositions);
        if (round.Status != VideoPokerRoundStatus.AwaitingDraw || round.Draw is not null)
            throw new InvalidOperationException("This Video Poker round has already been drawn.");

        var held = heldCardPositions.Positions.ToHashSet();
        var finalCards = round.InitialDeal.Cards.ToArray();
        var nextCardIndex = 0;
        for (var index = 0; index < finalCards.Length; index++)
        {
            if (held.Contains((VideoPokerCardPosition)index)) continue;

            finalCards[index] = round.RemainingDeck[nextCardIndex++];
        }

        var finalHand = new VideoPokerDeal(finalCards);
        var draw = new VideoPokerDraw(round.InitialDeal, heldCardPositions, finalHand);
        var result = new VideoPokerHandResult(
            finalHand,
            VideoPokerHandEvaluator.Evaluate(finalHand),
            FullPayJacksOrBetterPaytable.Evaluate(finalHand, round.CoinsWagered));

        return round with
        {
            RemainingDeck = [.. round.RemainingDeck.Skip(nextCardIndex)],
            Status = VideoPokerRoundStatus.Completed,
            Draw = draw,
            Result = result,
        };
    }

    private static void ValidateWager(int coinsWagered)
    {
        if (coinsWagered is < 1 or > 5)
            throw new ArgumentOutOfRangeException(nameof(coinsWagered), "Wager from one through five coins.");
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
