using FortuneForge.Games.Slots;

namespace FortuneForge.Games.Slots.Runtime;

public sealed record ClassicSlotEngineAudit(
    string EngineVersion,
    DateTime GeneratedAtUtc,
    GameDefinition BaseGame,
    GameDefinition EffectiveGame,
    ReelSetDefinition ReelSet,
    SymbolSetDefinition SymbolSet,
    PaytableDefinition Paytable,
    SlotProfile Profile,
    int PityMissesBefore,
    long EnergyBalanceBefore,
    string? FeatureMode,
    int GenerationAttempts,
    IReadOnlyList<IReadOnlyList<string>> ReelsBeforeFrequency,
    IReadOnlyList<IReadOnlyList<string>> ReelsAfterFrequency);

public sealed record ClassicSlotSpinOutcome(
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
    int PawCount,
    long CollectorPoints,
    long PatternBonusPoints,
    IReadOnlyDictionary<string, int> CollectionsAwarded,
    ClassicSlotEngineAudit Audit);

public sealed record ClassicFeaturePayout(
    int PawCount,
    long CollectorPoints,
    long PatternBonusPoints,
    IReadOnlyList<PaylinePayout> Paylines,
    long OffPaylinePoints = 0);

/// <summary>Optional pure policy hooks. No hook is selected by a game identifier.</summary>
public sealed record ClassicSlotEngineOptions
{
    public bool AllowFreeSpinCarryWager { get; init; }
    public bool AlwaysUsesPawCollectors { get; init; }
    public bool HeavyPawFeature { get; init; }
    public int SinglePawDivisor { get; init; } = 14;
    public Func<int, int>? DirectValueCountFromRoll { get; init; }
    public Func<long, int>? CollectionAppearanceChance { get; init; }
    public Func<IReadOnlyList<IReadOnlyList<string>>, long, decimal, ClassicFeaturePayout>?
        FeaturePayoutCalculator { get; init; }
}

public sealed record SlotCollectionState(string Mode, int Count, long WagerPointTotal);
public sealed record SlotCollectionProgress(
    int FreeSpinsAwarded,
    string? FeatureMode,
    long FreeSpinWagerPoints,
    IReadOnlyList<SlotCollectionState> Collections);

public static class ClassicSlotCollections
{
    private static readonly string[] Modes = ["sync", "rows", "paw", "rand"];
    private static readonly IReadOnlyDictionary<string, string> ModeBySymbol =
        new Dictionary<string, string>(StringComparer.Ordinal)
        {
            ["SEAL_SYNC"] = "sync", ["SEAL_ROWS"] = "rows",
            ["SEAL_PAW"] = "paw", ["SEAL_RAND"] = "rand"
        };

    public static SlotCollectionProgress Advance(SlotProfile profile,
        IReadOnlyList<SlotCollectionState> current, IReadOnlyDictionary<string, int> awarded,
        long wagerPoints, bool energyCompleted)
    {
        ArgumentNullException.ThrowIfNull(profile);
        ArgumentNullException.ThrowIfNull(current);
        ArgumentNullException.ThrowIfNull(awarded);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(wagerPoints);
        if (!profile.UsesCollections) return new(0, null, 0, []);

        var states = Modes.ToDictionary(mode => mode,
            mode => current.FirstOrDefault(value => value.Mode == mode) ?? new(mode, 0, 0),
            StringComparer.Ordinal);
        foreach (var (symbol, count) in awarded)
        {
            if (count <= 0 || !ModeBySymbol.TryGetValue(symbol, out var mode)) continue;
            var state = states[mode];
            states[mode] = state with
            {
                Count = checked(state.Count + count),
                WagerPointTotal = checked(state.WagerPointTotal + wagerPoints * count)
            };
        }
        if (energyCompleted && profile.UsesEnergy)
        {
            var mode = Modes.OrderByDescending(value =>
                Math.Min(states[value].Count, profile.CollectionTarget - 1)).First();
            var missing = Math.Max(1, profile.CollectionTarget - states[mode].Count);
            states[mode] = states[mode] with
            {
                Count = checked(states[mode].Count + missing),
                WagerPointTotal = checked(states[mode].WagerPointTotal + wagerPoints * missing)
            };
        }

        var freeSpins = 0;
        var freeSpinWager = 0L;
        string? featureMode = null;
        foreach (var mode in Modes)
        {
            var state = states[mode];
            if (state.Count < profile.CollectionTarget) continue;
            var average = state.Count == 0 ? 0 :
                (long)Math.Round(state.WagerPointTotal / (decimal)state.Count,
                    MidpointRounding.AwayFromZero);
            freeSpins = checked(freeSpins + profile.CollectionAwardedSpins);
            featureMode ??= profile.CollectionFeatureMode ?? mode;
            if (freeSpinWager == 0) freeSpinWager = Math.Max(1, average);
            var overflow = state.Count - profile.CollectionTarget;
            states[mode] = state with
            {
                Count = overflow,
                WagerPointTotal = checked(overflow * average)
            };
        }
        return new(freeSpins, featureMode, freeSpinWager, Modes.Select(mode => states[mode]).ToArray());
    }
}
