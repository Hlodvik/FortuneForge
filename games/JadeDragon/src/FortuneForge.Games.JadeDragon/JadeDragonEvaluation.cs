using FortuneForge.Games.JadeDragon.Models;

namespace FortuneForge.Games.JadeDragon;

/// <summary>Deterministic evaluation of the final displayed Jade Dragon grid.</summary>
public sealed record JadeDragonEvaluation(
    IReadOnlyList<PaylineEvaluation> Evaluations,
    SpinPayout Payout,
    int FreeSpinsAwarded,
    int SpecialPointsAwarded,
    long PatternPayoutPoints,
    IReadOnlyDictionary<string, int> CollectionSymbolsAwarded,
    bool HasPayingFiveMatch);
