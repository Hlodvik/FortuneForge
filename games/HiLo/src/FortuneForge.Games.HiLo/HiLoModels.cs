using System.Collections.Immutable;
using FortuneForge.Games.Cards;

namespace FortuneForge.Games.HiLo;

public enum HiLoDirection { Higher, Lower }
public enum HiLoRoundStatus { Active, Lost, CashedOut, AutoCashedOut }
public enum HiLoHistoryKind { Initial, Win, Loss, Skip }

public sealed record HiLoHistoryEntry(
    PlayingCard Card, HiLoHistoryKind Kind, HiLoDirection? Direction, decimal Multiplier, int Streak);

/// <summary>Persisted immutable state. Contains revealed cards only; never a future draw or random seed.</summary>
public sealed record HiLoRound(
    Guid Id,
    decimal Wager,
    PlayingCard CurrentCard,
    decimal Multiplier,
    int Streak,
    int Skips,
    HiLoRoundStatus Status,
    decimal Payout,
    long Version,
    ImmutableArray<HiLoHistoryEntry> History)
{
    public decimal CurrentReturn => Status == HiLoRoundStatus.Lost ? 0m : HiLoRules.Return(Wager, Multiplier);
    public bool CanCashOut => Status == HiLoRoundStatus.Active;
}

public sealed record HiLoOdds(
    HiLoDirection Direction, int WinningRanks, decimal Probability, decimal RawMultiplier, bool Available);

/// <summary>Exact displayed next-choice return after applying both payout limits.</summary>
public sealed record HiLoOffer(
    HiLoOdds Odds,
    decimal Multiplier,
    decimal Return,
    decimal EffectiveStepMultiplier,
    bool IsCapped,
    bool WillAutoCashOut);

public sealed class HiLoRuleException(string code, string message) : InvalidOperationException(message)
{
    public string Code { get; } = code;
}
