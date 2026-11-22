using FortuneForge.Games.Slots;
using FortuneForge.Games.Slots.Runtime;

namespace FortuneForge.Games.RobotRevolution;

public sealed class RobotRevolutionEngine
{
    public const string GameId = "robot-revolution-v1";
    public const string EngineVersion = "sunstone-eclipse-engine-v1";
    public const string LegacyEngineVersion = "robot-revolution-engine-v1";
    public const string EclipseFeatureMode = "rows-arcade";
    public static SlotProfile LegacyProfile { get; } = new(GameId, 3, 7, 24, 8,
        UsesCollections: false, UsesEnergy: false, UsesDirectValueTokens: false,
        ScatterFeatureMode: "rows", TargetHitRate: 0.32m, PayoutMultiplier: 1.126m);
    public static SlotProfile Profile { get; } = LegacyProfile with
    {
        PayoutMultiplier = 0.874m,
        // The existing neutral compound-mode vocabulary carries our version marker.
        ScatterFeatureMode = EclipseFeatureMode
    };
    private static readonly FrozenSlotDefinitions Definitions = ClassicSlotDefinitionFactory.Create(Profile);
    private static readonly FrozenSlotDefinitions LegacyDefinitions = ClassicSlotDefinitionFactory.Create(LegacyProfile);
    private readonly ClassicSlotEngine engine;
    private readonly ClassicSlotEngine freeEngine;
    private readonly ClassicSlotEngine historicalFreeEngine;

    public RobotRevolutionEngine(IGameRandomSource random, string engineVersion = EngineVersion)
    {
        if (!IsSupportedVersion(engineVersion)) throw new ArgumentException("The Sunstone Eclipse math version is unsupported.", nameof(engineVersion));
        var legacy = engineVersion == LegacyEngineVersion;
        var definitions = legacy ? LegacyDefinitions : Definitions;
        var profile = legacy ? LegacyProfile : Profile;
        engine = new(definitions, profile, random, engineVersion, Options(legacy, false));
        freeEngine = new(definitions, profile, random, engineVersion, Options(legacy, true));
        historicalFreeEngine = new(LegacyDefinitions, LegacyProfile, random, LegacyEngineVersion, Options(true, true));
    }

    public static bool IsSupportedVersion(string version) => version is EngineVersion or LegacyEngineVersion;

    private static ClassicSlotEngineOptions Options(bool legacy, bool free) => new()
    {
        AllowFreeSpinCarryWager = true,
        FeaturePayoutCalculator = legacy ? null : (reels, wager, multiplier) =>
            SunstoneEclipse.CalculatePayout(reels, wager, multiplier, free)
    };

    public static decimal WagerPointValueInCents => Definitions.Game.Wagering.PointValueInCents;
    public static long MinimumWagerPoints => Definitions.Game.Wagering.MinimumWagerPoints;
    public static void ValidateWager(long wagerPoints, bool isFreeSpin) =>
        ClassicSlotEngine.ValidateWager(Definitions.Game, wagerPoints, allowCarryWager: isFreeSpin);
    public ClassicSlotSpinOutcome Spin(long wagerPoints, string playerId,
        bool specialBoostApplied = false, long currentEnergyBalance = 0,
        string? featureMode = null, int? persistedPityMisses = null, bool isFreeSpin = false) =>
        (isFreeSpin ? featureMode == EclipseFeatureMode ? freeEngine : historicalFreeEngine : engine)
            .Spin(wagerPoints, playerId, specialBoostApplied, currentEnergyBalance,
            featureMode, persistedPityMisses, isFreeSpin);
    public static GameDefinition CreateGame() => Definitions.CreateGame();
    public static SymbolSetDefinition CreateSymbolSet() => Definitions.CreateSymbolSet();
    public static ReelSetDefinition CreateReelSet() => Definitions.CreateReelSet();
    public static PaytableDefinition CreatePaytable() => Definitions.CreatePaytable();
}
