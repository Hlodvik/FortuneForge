using FortuneForge.Games.Slots;
using FortuneForge.Games.Slots.Runtime;

namespace FortuneForge.Games.PharaohsAscent;

public sealed class PharaohsAscentEngine
{
    public const string GameId = "pharaohs-ascent-v1";
    public const string LegacyEngineVersion = "pharaohs-ascent-classic-v0";
    public const string PreviousEngineVersion = "pharaohs-ascent-engine-v1";
    public const string PreviousTombEngineVersion = "pharaohs-ascent-sealed-tomb-v2";
    public static string EngineVersion => PharaohsAscentMathV3.Current.EngineVersion;
    public static SlotProfile Profile { get; } = new(GameId, 3, 6, 24, 7,
        UsesCollections: true, UsesEnergy: true, UsesDirectValueTokens: false,
        PayoutMultiplier: PharaohsAscentMathV3.Current.PayoutMultiplier);
    private static readonly SlotProfile HistoricalProfile = Profile with { PayoutMultiplier = 0.856m };
    private static readonly SlotProfile LegacyProfile = HistoricalProfile with { CollectionFeatureMode = "sync-rows" };
    private static readonly FrozenSlotDefinitions HistoricalDefinitions = ClassicSlotDefinitionFactory.Create(HistoricalProfile);
    private static readonly FrozenSlotDefinitions Definitions = PharaohsAscentMathV3.CreateDefinitions(Profile);
    private readonly ClassicSlotEngine engine;

    public PharaohsAscentEngine(IGameRandomSource random) : this(random, EngineVersion) { }

    public PharaohsAscentEngine(IGameRandomSource random, string engineVersion)
    {
        if (!SupportsEngineVersion(engineVersion))
            throw new ArgumentException("The Pharaoh's Ascent engine version is not supported.", nameof(engineVersion));
        engine = new(engineVersion == EngineVersion ? Definitions : HistoricalDefinitions,
            ProfileForVersion(engineVersion), random, engineVersion, new ClassicSlotEngineOptions
        {
            AllowFreeSpinCarryWager = true,
            PatternCountFromRoll = engineVersion != LegacyEngineVersion ? PharaohsAscentScarabs.CountFromRoll : null,
            FeaturePayoutCalculator = engineVersion != LegacyEngineVersion ? PharaohsAscentScarabs.Calculate : null,
        });
    }

    public static bool SupportsEngineVersion(string? version) => version == EngineVersion ||
        version is PreviousTombEngineVersion or PreviousEngineVersion or LegacyEngineVersion;

    public static bool UsesSealedTombFunding(string? version) => version == EngineVersion || version == PreviousTombEngineVersion;

    public static SlotProfile ProfileForVersion(string version) => version == EngineVersion ? Profile : version switch
    {
        PreviousTombEngineVersion or PreviousEngineVersion => HistoricalProfile,
        LegacyEngineVersion => LegacyProfile,
        _ => throw new ArgumentException("The Pharaoh's Ascent engine version is not supported.", nameof(version)),
    };

    public static decimal WagerPointValueInCents => Definitions.Game.Wagering.PointValueInCents;
    public static long MinimumWagerPoints => Definitions.Game.Wagering.MinimumWagerPoints;
    public static void ValidateWager(long wagerPoints, bool isFreeSpin) =>
        ClassicSlotEngine.ValidateWager(Definitions.Game, wagerPoints, allowCarryWager: isFreeSpin);

    public ClassicSlotSpinOutcome Spin(long wagerPoints, string playerId,
        bool specialBoostApplied = false, long currentEnergyBalance = 0,
        string? featureMode = null, int? persistedPityMisses = null, bool isFreeSpin = false) =>
        engine.Spin(wagerPoints, playerId, specialBoostApplied, currentEnergyBalance,
            featureMode, persistedPityMisses, isFreeSpin);
    public static GameDefinition CreateGame() => Definitions.CreateGame();
    public static SymbolSetDefinition CreateSymbolSet() => Definitions.CreateSymbolSet();
    public static ReelSetDefinition CreateReelSet() => Definitions.CreateReelSet();
    public static PaytableDefinition CreatePaytable() => Definitions.CreatePaytable();
}
