using FortuneForge.Games.Slots;
using FortuneForge.Games.Slots.Runtime;

namespace FortuneForge.Games.DesertTreasures;

public sealed class DesertTreasuresEngine
{
    public const string GameId = "desert-treasures-v1";
    public const string EngineVersion = "desert-treasures-engine-v1";
    public static SlotProfile Profile { get; } = new(GameId, 3, 7, 24, 7,
        UsesCollections: true, UsesEnergy: true, UsesDirectValueTokens: true,
        CollectionFeatureMode: "rows", PayoutMultiplier: 0.977m);
    private static readonly FrozenSlotDefinitions Definitions = ClassicSlotDefinitionFactory.Create(Profile);
    private readonly ClassicSlotEngine engine;

    public DesertTreasuresEngine(IGameRandomSource random) => engine = new(Definitions, Profile, random, EngineVersion);
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
