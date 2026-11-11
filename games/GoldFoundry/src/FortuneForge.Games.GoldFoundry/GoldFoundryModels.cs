using FortuneForge.Games.Slots;

namespace FortuneForge.Games.GoldFoundry;

public sealed record GoldFoundryProfile(
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
    string? BaseReelSetId,
    decimal? TargetHitRate,
    IReadOnlyList<int> PaylinePatternIds,
    decimal PayoutMultiplier);

public sealed record GoldFoundryEngineAudit(
    string EngineVersion,
    DateTime GeneratedAtUtc,
    GameDefinition BaseGame,
    GameDefinition EffectiveGame,
    ReelSetDefinition ReelSet,
    SymbolSetDefinition SymbolSet,
    PaytableDefinition Paytable,
    GoldFoundryProfile FeatureProfile,
    int PityMissesBefore,
    long EnergyBalanceBefore,
    string? FreeSpinFeatureMode,
    int GenerationAttempts,
    IReadOnlyList<IReadOnlyList<string>> RawGeneratedReels,
    IReadOnlyList<IReadOnlyList<string>> ReelsAfterFreeGameFrequency,
    bool IsDerivedFreeSpin,
    string CrucibleRuleVersion);

public sealed record GoldFoundrySpinOutcome(
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
    int CollectorCount,
    long CollectorPayoutPoints,
    long HammerBonusPoints,
    IReadOnlyDictionary<string, int> SealsAwarded,
    GoldFoundryEngineAudit EngineAudit);

public sealed record GoldFoundryReplayFinding(string Code, string Message);

public sealed record GoldFoundryCrucibleSettlement(
    long EnergyBalance,
    long MeterBalanceBeforeReset,
    long EnergyAddedToMeter,
    long WageredCents,
    bool Poured,
    long PayoutCents)
{
    public long PourValueCents => GoldFoundryCrucibleRules.PourValueCents(WageredCents);
}
