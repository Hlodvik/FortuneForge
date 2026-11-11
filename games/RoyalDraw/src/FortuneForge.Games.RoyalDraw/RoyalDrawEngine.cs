using FortuneForge.Games.Slots;
using FortuneForge.Games.Slots.Runtime;

namespace FortuneForge.Games.RoyalDraw;

public sealed class RoyalDrawEngine
{
    public const string GameId = "royal-draw-v1";
    public const string EngineVersion = "royal-draw-engine-v1";
    public static SlotProfile Profile { get; } = new(GameId, 3, 6, 24, 7,
        UsesCollections: true, UsesEnergy: true, UsesDirectValueTokens: true,
        CollectionFeatureMode: "rows-rand", PayoutMultiplier: 0.942m);
    private static readonly FrozenSlotDefinitions Definitions = ClassicSlotDefinitionFactory.Create(Profile);
    private readonly ClassicSlotEngine engine;

    public RoyalDrawEngine(IGameRandomSource random) =>
        engine = new(Definitions, Profile, random, EngineVersion);
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
