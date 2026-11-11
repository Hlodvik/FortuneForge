using FortuneForge.Games.Slots;
using FortuneForge.Games.Slots.Runtime;

namespace FortuneForge.Games.RobotRevolution;

public sealed class RobotRevolutionEngine
{
    public const string GameId = "robot-revolution-v1";
    public const string EngineVersion = "robot-revolution-engine-v1";
    public static SlotProfile Profile { get; } = new(GameId, 3, 7, 24, 8,
        UsesCollections: false, UsesEnergy: false, UsesDirectValueTokens: false,
        ScatterFeatureMode: "rows", TargetHitRate: 0.32m, PayoutMultiplier: 1.126m);
    private static readonly FrozenSlotDefinitions Definitions = ClassicSlotDefinitionFactory.Create(Profile);
    private readonly ClassicSlotEngine engine;

    public RobotRevolutionEngine(IGameRandomSource random) => engine = new(Definitions, Profile, random, EngineVersion);
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
