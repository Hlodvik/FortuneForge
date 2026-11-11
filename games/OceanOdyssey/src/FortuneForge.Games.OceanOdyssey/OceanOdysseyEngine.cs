using FortuneForge.Games.Slots;
using FortuneForge.Games.Slots.Runtime;

namespace FortuneForge.Games.OceanOdyssey;

public sealed class OceanOdysseyEngine
{
    public const string GameId = "ocean-odyssey-v1";
    public const string EngineVersion = "ocean-odyssey-engine-v1";
    public static SlotProfile Profile { get; } = new(GameId, 4, 6, 28, 6,
        UsesCollections: true, UsesEnergy: true, UsesDirectValueTokens: false,
        CollectionFeatureMode: "sync-rand", PayoutMultiplier: 1.071m);
    private static readonly FrozenSlotDefinitions Definitions = ClassicSlotDefinitionFactory.Create(Profile);
    private readonly ClassicSlotEngine engine;

    public OceanOdysseyEngine(IGameRandomSource random) =>
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
