using System.Text.Json.Serialization;
using FortuneForge.Games.JadeDragon.Bonuses;

namespace FortuneForge.Games.JadeDragon.Models;

public sealed record SlotSpinEngineAudit(
    string EngineVersion, DateTime GeneratedAtUtc,
    GameDefinition BaseGame, GameDefinition EffectiveGame, ReelSetDefinition ReelSet,
    SymbolSetDefinition SymbolSet, PaytableDefinition Paytable, SlotSpecialRoundProfile? FeatureProfile,
    int PityMissesBefore, long EnergyBalanceBefore, string? FreeSpinFeatureMode, int GenerationAttempts,
    IReadOnlyList<IReadOnlyList<string>> RawGeneratedReels,
    IReadOnlyList<IReadOnlyList<string>> ReelsAfterFreeGameFrequency)
{
    public bool IsDerivedFreeSpin { get; init; }
}

public sealed record SlotCollectionProgress(string TrackId, int Count, long AverageWagerPoints, int RequiredCount);

public sealed record SpinResult(
    Guid SpinId, string GameId, string ReelSetId, string SymbolSetId, string PaytableId,
    long WagerPoints, decimal PointValueInCents, IReadOnlyList<int> ReelStops,
    IReadOnlyList<IReadOnlyList<string>> Reels, SpinPayout Payout, int ConsecutiveFiveMisses,
    bool FiveMatchPityTriggered, int FreeSpinsAwarded, int SpecialPointsAwarded,
    int EnergyAwarded, bool SpecialBoostApplied)
{
    [JsonIgnore]
    public SlotSpinEngineAudit? EngineAudit { get; init; }
    public decimal? SlotsCreditsBalance { get; init; }
    public bool IsFreeSpin { get; init; }
    public int FreeSpinsRemaining { get; init; }
    public long? FreeSpinWagerPoints { get; init; }
    public int SpecialPointsBalance { get; init; }
    public long EnergyBalance { get; init; }
    public bool EnergyMultiplierApplied { get; init; }
    public decimal PayoutMultiplier { get; init; } = 1m;
    public int CollectorCount { get; init; }
    public long CollectorPayoutPoints { get; init; }
    public long PatternPayoutPoints { get; init; }
    public IReadOnlyDictionary<string, int> CollectionSymbolsAwarded { get; init; } =
        new Dictionary<string, int>(StringComparer.Ordinal);
    public IReadOnlyList<SlotCollectionProgress> CollectionProgress { get; init; } = [];
    public int CollectionFreeSpinsAwarded { get; init; }
    public string? FreeSpinFeatureMode { get; init; }
}
