using System.Collections.Immutable;
using FortuneForge.Games.Dice;

namespace FortuneForge.Games.Craps;

public enum CrapsPassLinePhase
{
    ComeOut,
    Point,
    Resolved,
}

public enum CrapsRollResult
{
    NaturalWin,
    CrapsLoss,
    PointEstablished,
    PointHit,
    SevenOut,
    NoDecision,
}

public sealed record CrapsPassLineBet(string PlayerId, decimal Stake);

public sealed record CrapsRollOutcome(
    DicePair Dice,
    CrapsRollResult Result,
    bool IsTerminal,
    decimal? TotalReturn);

public sealed record CrapsPassLineState(
    CrapsPassLineBet Bet,
    CrapsPassLinePhase Phase,
    int? Point,
    ImmutableArray<DicePair> Rolls,
    CrapsRollOutcome? LastOutcome);

public sealed record CrapsTransition(CrapsPassLineState State, CrapsRollOutcome Outcome);

public sealed class CrapsRuleException(string message) : InvalidOperationException(message);
