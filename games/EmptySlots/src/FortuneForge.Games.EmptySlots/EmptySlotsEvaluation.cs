using FortuneForge.Games.EmptySlots.Models;

namespace FortuneForge.Games.EmptySlots;

/// <summary>Deterministic evaluation of the final displayed Empty Slots Game grid.</summary>
public sealed record EmptySlotsEvaluation(
    IReadOnlyList<PaylineEvaluation> Evaluations,
    SpinPayout Payout,
    int FreeSpinsAwarded,
    int SpecialPointsAwarded,
    long PatternPayoutPoints,
    IReadOnlyDictionary<string, int> CollectionSymbolsAwarded,
    bool HasPayingFiveMatch);
