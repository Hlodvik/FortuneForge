using FortuneForge.Games.Slots;
using FortuneForge.Games.Slots.Runtime;

namespace FortuneForge.Games.RobotRevolution;

public sealed class RobotRevolutionEngine
{
    public const string GameId = "robot-revolution-v1";
    public const string EngineVersion = "fifth-sun-engine-v2";
    public const string HistoricalFifthSunEngineVersion = "fifth-sun-engine-v1";
    public const string SunstoneEngineVersion = "sunstone-eclipse-engine-v1";
    public const string LegacyEngineVersion = "robot-revolution-engine-v1";
    public const string EclipseFeatureMode = "arcade-rows";
    public const string SunstoneFeatureMode = "rows-arcade";
    public static SlotProfile LegacyProfile { get; } = new(GameId, 3, 7, 24, 8,
        UsesCollections: false, UsesEnergy: false, UsesDirectValueTokens: false,
        ScatterFeatureMode: "rows", TargetHitRate: 0.32m, PayoutMultiplier: 1.126m);
    public static SlotProfile SunstoneProfile { get; } = LegacyProfile with
    {
        PayoutMultiplier = 0.874m,
        // The existing neutral compound-mode vocabulary carries our version marker.
        ScatterFeatureMode = SunstoneFeatureMode
    };
    public static SlotProfile HistoricalFifthSunProfile { get; } = SunstoneProfile with
    {
        PayoutMultiplier = 1.14m, ScatterFeatureMode = EclipseFeatureMode, BaseReelSetId = FifthSunDefinitions.ReelSetId
    };
    public static SlotProfile Profile { get; } = HistoricalFifthSunProfile with
    {
        PayoutMultiplier = FifthSunTotalityDefinitions.OrdinaryPayoutMultiplier,
        ScatterRequiredSymbols = 1, ScatterAwardedSpins = 0, ScatterFeatureMode = null,
        BaseReelSetId = FifthSunTotalityDefinitions.ReelSetId
    };
    private static readonly FrozenSlotDefinitions Definitions = FifthSunTotalityDefinitions.Create(Profile);
    private static readonly FrozenSlotDefinitions HistoricalFifthSunDefinitions = FifthSunDefinitions.Create(HistoricalFifthSunProfile);
    private static readonly FrozenSlotDefinitions SunstoneDefinitions = ClassicSlotDefinitionFactory.Create(SunstoneProfile);
    private static readonly FrozenSlotDefinitions LegacyDefinitions = ClassicSlotDefinitionFactory.Create(LegacyProfile);
    private readonly string requestedVersion;
    private readonly Dictionary<(string Version, bool Free), ClassicSlotEngine> engines;
    private readonly Dictionary<string, ClassicSlotEngine> totalityEngines;

    public RobotRevolutionEngine(IGameRandomSource random, string engineVersion = EngineVersion)
    {
        if (!IsSupportedVersion(engineVersion)) throw new ArgumentException("The Fifth Sun math version is unsupported.", nameof(engineVersion));
        requestedVersion = engineVersion;
        engines = new();
        foreach (var version in new[] { HistoricalFifthSunEngineVersion, SunstoneEngineVersion, LegacyEngineVersion })
        foreach (var free in new[] { false, true })
            engines[(version, free)] = new(DefinitionsFor(version), ProfileForVersion(version), random, version, Options(version, free));
        totalityEngines = new();
        foreach (var mode in new string?[] { null, FifthSunTotalityDefinitions.SolarMode, FifthSunTotalityDefinitions.LunarMode })
        {
            var selected = mode;
            var profile = mode is null ? Profile : Profile with { BaseReelSetId = FifthSunTotalityDefinitions.ReelSetId + "-" + mode,
                PayoutMultiplier = FifthSunTotalityDefinitions.EclipsePayoutMultiplier };
            totalityEngines[mode ?? string.Empty] = new(FifthSunTotalityDefinitions.Create(profile, mode), profile, random, EngineVersion,
                new() { UsesPatternBonus = false, FeaturePayoutCalculator = (reels, wager, _) => FifthSunTotalityWheel.CalculatePayout(reels, wager, selected) });
        }
    }

    public static bool IsSupportedVersion(string version) => version is EngineVersion or HistoricalFifthSunEngineVersion or SunstoneEngineVersion or LegacyEngineVersion;

    public static SlotProfile ProfileForVersion(string version) => version switch
    {
        EngineVersion => Profile, HistoricalFifthSunEngineVersion => HistoricalFifthSunProfile,
        SunstoneEngineVersion => SunstoneProfile, LegacyEngineVersion => LegacyProfile,
        _ => throw new ArgumentException("The Fifth Sun math version is unsupported.", nameof(version))
    };
    private static FrozenSlotDefinitions DefinitionsFor(string version) => version switch
    {
        EngineVersion => Definitions, HistoricalFifthSunEngineVersion => HistoricalFifthSunDefinitions,
        SunstoneEngineVersion => SunstoneDefinitions, LegacyEngineVersion => LegacyDefinitions,
        _ => throw new ArgumentException("The Fifth Sun math version is unsupported.", nameof(version))
    };
    public static string VersionForBonus(string? mode) => mode switch
    {
        EclipseFeatureMode => HistoricalFifthSunEngineVersion, SunstoneFeatureMode => SunstoneEngineVersion, _ => LegacyEngineVersion
    };

    private static ClassicSlotEngineOptions Options(string version, bool free) => new()
    {
        AllowFreeSpinCarryWager = true,
        UsesPatternBonus = version != HistoricalFifthSunEngineVersion,
        FeaturePayoutCalculator = version == HistoricalFifthSunEngineVersion
            ? (reels, wager, multiplier) => FifthSunWheel.CalculatePayout(reels, wager, multiplier, free)
            : version == SunstoneEngineVersion
                ? (reels, wager, multiplier) => SunstoneEclipse.CalculatePayout(reels, wager, multiplier, free) : null
    };

    public static decimal WagerPointValueInCents => Definitions.Game.Wagering.PointValueInCents;
    public static long MinimumWagerPoints => Definitions.Game.Wagering.MinimumWagerPoints;
    public static long MinimumHistoricalWagerPoints => LegacyDefinitions.Game.Wagering.MinimumWagerPoints;
    public static long DefaultWagerPoints => FifthSunDefinitions.DefaultWagerPoints;
    public static long MaximumWagerPoints => FifthSunDefinitions.MaximumWagerPoints;
    public static void ValidateWager(long wagerPoints, bool isFreeSpin) =>
        ClassicSlotEngine.ValidateWager(isFreeSpin ? LegacyDefinitions.Game : Definitions.Game, wagerPoints, allowCarryWager: isFreeSpin);
    public static void ValidateRecordedWager(long wagerPoints, bool isFreeSpin, string version) =>
        ClassicSlotEngine.ValidateWager(DefinitionsFor(version).Game, wagerPoints, allowCarryWager: isFreeSpin);
    public ClassicSlotSpinOutcome Spin(long wagerPoints, string playerId,
        bool specialBoostApplied = false, long currentEnergyBalance = 0,
        string? featureMode = null, int? persistedPityMisses = null, bool isFreeSpin = false)
    {
        var version = requestedVersion != EngineVersion ? requestedVersion : isFreeSpin ? VersionForBonus(featureMode) : EngineVersion;
        if (version == EngineVersion)
        {
            if (featureMode is not null && !FifthSunTotalityDefinitions.IsMode(featureMode))
                throw new ArgumentException("The Eclipse mode is unsupported.", nameof(featureMode));
            if (featureMode is not null && specialBoostApplied)
                throw new ArgumentException("Special boosts cannot be combined with a paid Eclipse round.", nameof(specialBoostApplied));
            var outcome = totalityEngines[featureMode ?? string.Empty].Spin(wagerPoints, playerId, specialBoostApplied,
                currentEnergyBalance, featureMode is null ? null : "arcade", persistedPityMisses, false);
            return outcome with { Audit = outcome.Audit with { FeatureMode = featureMode } };
        }
        return engines[(version, isFreeSpin)].Spin(wagerPoints, playerId, specialBoostApplied, currentEnergyBalance,
            featureMode, persistedPityMisses, isFreeSpin);
    }
    public static SunstoneEclipseFeature? DescribeFeature(ClassicSlotSpinOutcome outcome) => outcome.Audit.EngineVersion switch
    {
        EngineVersion => FifthSunTotalityWheel.Resolve(outcome.Reels, outcome.WagerPoints, outcome.Audit.FeatureMode),
        HistoricalFifthSunEngineVersion => FifthSunWheel.Resolve(outcome.Reels, outcome.WagerPoints, outcome.IsFreeSpin),
        SunstoneEngineVersion => SunstoneEclipse.Resolve(outcome.Reels, outcome.WagerPoints, outcome.IsFreeSpin),
        _ => null
    };
    public static GameDefinition CreateGame() => Definitions.CreateGame();
    public static SymbolSetDefinition CreateSymbolSet() => Definitions.CreateSymbolSet();
    public static ReelSetDefinition CreateReelSet() => Definitions.CreateReelSet();
    public static ReelSetDefinition CreateReelSet(string? mode) => mode is null ? Definitions.CreateReelSet()
        : FifthSunTotalityDefinitions.Create(Profile with { PayoutMultiplier = FifthSunTotalityDefinitions.EclipsePayoutMultiplier }, mode).CreateReelSet();
    public static PaytableDefinition CreatePaytable() => Definitions.CreatePaytable();
}
