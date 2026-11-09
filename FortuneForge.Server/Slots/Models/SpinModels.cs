using System.Text.Json.Serialization;
using FortuneForge.Server.Slots.Bonuses;

namespace FortuneForge.Server.Slots.Models;

// The private, versioned inputs used by the outcome engine. A copy is written with
// the paid round so later audits do not depend on the current slot configuration.
public sealed record SlotSpinEngineAudit(
    string EngineVersion,
    DateTime GeneratedAtUtc,
    GameDefinition BaseGame,
    GameDefinition EffectiveGame,
    ReelSetDefinition ReelSet,
    SymbolSetDefinition SymbolSet,
    PaytableDefinition Paytable,
    SlotSpecialRoundProfile? FeatureProfile,
    int PityMissesBefore,
    long EnergyBalanceBefore,
    string? FreeSpinFeatureMode,
    int GenerationAttempts,
    IReadOnlyList<IReadOnlyList<string>> RawGeneratedReels,
    IReadOnlyList<IReadOnlyList<string>> ReelsAfterFreeGameFrequency)
{
    // Set after checking a stored paid entitlement or a nonfinancial demo round.
    public bool IsDerivedFreeSpin { get; init; }

    // Absent from every existing paid/demo path and from legacy serialized evidence.
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public WukongPawPrototypeEvidence? WukongPawPrototype { get; init; }

    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public NeonArcadeEvidence? NeonArcade { get; init; }

    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public NeonDiscoCascadeAudit? NeonDiscoCascade { get; init; }

    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public OlympusTumbleEvidence? OlympusTumble { get; init; }

    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? CrucibleRuleVersion { get; init; }
}

public sealed record OlympusTumbleEvidence(bool Showcase, IReadOnlyList<int> DrawIndexes);

public sealed record SlotSealCollection(
    string SealId,
    int Count,
    long AverageWagerPoints,
    int RequiredCount);

public sealed record MidnightVaultDemoState(
    IReadOnlyList<MidnightVaultBonusRound> Rounds,
    IReadOnlyDictionary<string, long> CollectionCounts,
    IReadOnlyDictionary<string, long> CollectionWagerPoints);

public sealed record SpinResult(
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
    bool SpecialBoostApplied)
{
    [JsonIgnore]
    public SlotSpinEngineAudit? EngineAudit { get; init; }
    public decimal? SlotsCreditsBalance { get; init; }
    public bool IsFreeSpin { get; init; }
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)]
    public long ChargedWagerCents { get; init; }
    public int NeonDiscoSpinsRemaining { get; init; }
    public int FreeSpinsRemaining { get; init; }
    public long? FreeSpinWagerPoints { get; init; }
    public int SpecialPointsBalance { get; init; }
    public long EnergyBalance { get; init; }
    public bool EnergyMultiplierApplied { get; init; }
    public decimal PayoutMultiplier { get; init; } = 1m;
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public long? CrucibleWageredCents { get; init; }
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public long? CruciblePourValueCents { get; init; }
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public bool? CruciblePoured { get; init; }
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public long? CruciblePayoutCents { get; init; }
    public int MonkeyPawCount { get; init; }
    public long MoneyGrabPoints { get; init; }
    public long BananaBonusPoints { get; init; }
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public ArcaneLightningCollection? LightningCollection { get; init; }
    public IReadOnlyDictionary<string, int> SealsAwarded { get; init; } =
        new Dictionary<string, int>(StringComparer.Ordinal);
    public IReadOnlyList<SlotSealCollection> SealCollections { get; init; } = [];
    public int CollectionFreeSpinsAwarded { get; init; }
    public string? FreeSpinFeatureMode { get; init; }
    public MidnightVaultBonusRound? BonusRound { get; init; }
    public IReadOnlyList<MidnightVaultBonusRound> QueuedBonusRounds { get; init; } = [];
    public IReadOnlyList<MidnightVaultBonusRound> AwardedBonusRounds { get; init; } = [];
    public MidnightVaultDemoState? MidnightBonusState { get; init; }

    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public NeonArcadeEvidence? NeonArcade { get; init; }

    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public NeonDiscoCascadeOutcome? NeonDiscoCascade { get; init; }

    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public OlympusTumbleOutcome? OlympusTumble { get; init; }
}
