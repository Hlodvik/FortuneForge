using FortuneForge.Games.Slots;

namespace FortuneForge.Games.MidnightVault;

public sealed record MidnightVaultProfile(
    string GameId,
    int ScatterRequiredSymbols,
    int ScatterAwardedSpins,
    int CollectionTarget,
    int CollectionAwardedSpins,
    bool UsesCollections,
    bool UsesEnergy,
    bool UsesDirectValueTokens,
    string? ScatterFeatureMode,
    string? CollectionFeatureMode,
    string BaseReelSetId,
    decimal? TargetHitRate,
    decimal PayoutMultiplier);

public sealed record MidnightVaultEngineAudit(
    string EngineVersion,
    DateTime GeneratedAtUtc,
    GameDefinition BaseGame,
    GameDefinition EffectiveGame,
    ReelSetDefinition ReelSet,
    SymbolSetDefinition SymbolSet,
    PaytableDefinition Paytable,
    MidnightVaultProfile FeatureProfile,
    int PityMissesBefore,
    long EnergyBalanceBefore,
    string? FreeSpinFeatureMode,
    int GenerationAttempts,
    IReadOnlyList<IReadOnlyList<string>> RawGeneratedReels,
    IReadOnlyList<IReadOnlyList<string>> ReelsAfterFreeGameFrequency,
    bool IsDerivedFreeSpin);

public sealed record MidnightVaultSpinOutcome(
    Guid SpinId,
    string GameId,
    string ReelSetId,
    string SymbolSetId,
    string PaytableId,
    long WagerPoints,
    decimal PointValueInCents,
    IReadOnlyList<int> ReelStops,
    IReadOnlyList<IReadOnlyList<string>> Reels,
    SpinPayout Payout,
    int ConsecutiveFiveMisses,
    bool FiveMatchPityTriggered,
    int FreeSpinsAwarded,
    int SpecialPointsAwarded,
    int EnergyAwarded,
    bool SpecialBoostApplied,
    bool IsFreeSpin,
    long BananaBonusPoints,
    IReadOnlyDictionary<string, int> SealsAwarded,
    MidnightVaultEngineAudit EngineAudit);

public sealed record MidnightVaultReplayFinding(string Code, string Message);

public sealed record MidnightVaultBonusRound(
    string Id,
    int TotalSpins,
    int RemainingSpins,
    long WagerPoints,
    string? FeatureMode,
    string MathModelId);

public sealed record MidnightVaultBonusProgress(
    IReadOnlyList<MidnightVaultBonusRound> Rounds,
    IReadOnlyDictionary<string, long> CollectionCounts,
    IReadOnlyDictionary<string, long> CollectionWagerPoints,
    IReadOnlyList<MidnightVaultBonusRound> AwardedRounds);

public sealed record MidnightVaultBonusEvidence(
    MidnightVaultBonusRound? AdmittedRound,
    IReadOnlyList<MidnightVaultBonusRound> RoundsBeforeAdmission,
    IReadOnlyList<MidnightVaultBonusRound> RoundsAfterConsumption,
    IReadOnlyList<MidnightVaultBonusRound> RoundsAfterSettlement,
    IReadOnlyDictionary<string, long> CollectionCountsBefore,
    IReadOnlyDictionary<string, long> CollectionWagerPointsBefore,
    IReadOnlyDictionary<string, long> CollectionCountsAfter,
    IReadOnlyDictionary<string, long> CollectionWagerPointsAfter,
    IReadOnlyList<MidnightVaultBonusRound> AwardedRounds);
