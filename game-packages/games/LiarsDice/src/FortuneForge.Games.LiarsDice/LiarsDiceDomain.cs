using System.Collections.Immutable;
using FortuneForge.Games.Dice;

namespace FortuneForge.Games.LiarsDice;

public enum LiarsDiceRoundPhase
{
    Bidding,
    Resolved,
}

public enum LiarsDiceVariant
{
    ExactFace,
}

public sealed record LiarsDiceBid(int Quantity, DieValue Face)
{
    public void Validate()
    {
        if (Quantity <= 0)
            throw new ArgumentOutOfRangeException(nameof(Quantity), "A bid quantity must be positive.");
        Face.Validate();
    }

    public bool IsHigherThan(LiarsDiceBid other) =>
        Quantity > other.Quantity || (Quantity == other.Quantity && Face.Value > other.Face.Value);
}

public abstract record LiarsDiceCommand(string PlayerId);

public sealed record PlaceLiarsDiceBid(string PlayerId, LiarsDiceBid Bid) : LiarsDiceCommand(PlayerId);

public sealed record ChallengeLiarsDiceBid(string PlayerId) : LiarsDiceCommand(PlayerId);

public sealed record LiarsDiceChallengeOutcome(
    string ChallengerId,
    string BidderId,
    string LoserId,
    LiarsDiceBid Bid,
    int MatchingDice);

public sealed record LiarsDiceRoundState(
    LiarsDiceVariant Variant,
    ImmutableArray<string> TurnOrder,
    ImmutableDictionary<string, ImmutableArray<DieValue>> Hands,
    int CurrentPlayerIndex,
    LiarsDiceBid? CurrentBid,
    string? CurrentBidderId,
    LiarsDiceRoundPhase Phase,
    LiarsDiceChallengeOutcome? Outcome)
{
    public string CurrentPlayerId => TurnOrder[CurrentPlayerIndex];
}

public sealed record LiarsDiceTransition(LiarsDiceRoundState State, LiarsDiceChallengeOutcome? Outcome);

public sealed record LiarsDiceMatchState(
    int StartingDicePerPlayer,
    ImmutableArray<string> AllPlayerIds,
    ImmutableDictionary<string, int> DiceCounts,
    int RoundNumber,
    LiarsDiceRoundState Round,
    LiarsDiceChallengeOutcome? LastOutcome,
    string? Winner)
{
    public string CurrentPlayerId => Round.CurrentPlayerId;
}

public sealed record LiarsDiceMatchTransition(
    LiarsDiceMatchState State,
    LiarsDiceChallengeOutcome? Outcome,
    string Message);

public sealed record LiarsDiceBotObservation(
    string PlayerId,
    ImmutableArray<DieValue> Hand,
    LiarsDiceBid? CurrentBid,
    string? CurrentBidderId,
    int TotalDiceInPlay,
    ImmutableArray<string> TurnOrder);

public sealed record LiarsDiceBotDecision(bool Challenge, LiarsDiceBid? Bid);

public sealed class LiarsDiceRuleException(string message) : InvalidOperationException(message);
