using FortuneForge.Games.Slots;
using FortuneForge.Games.Slots.Runtime;

namespace FortuneForge.Games.SamuraiFortune;

public sealed class SamuraiFortuneEngine
{
    public const string GameId = "samurai-fortune-v1";
    public const string EngineVersion = "samurai-fortune-engine-v1";
    public static SlotProfile Profile { get; } = new(GameId, 4, 6, 26, 7,
        UsesCollections: true, UsesEnergy: false, UsesDirectValueTokens: false,
        CollectionFeatureMode: "sync-paw", PayoutMultiplier: 1.057m);
    private static readonly FrozenSlotDefinitions Definitions = ClassicSlotDefinitionFactory.Create(Profile);
    private readonly ClassicSlotEngine engine;

    public SamuraiFortuneEngine(IGameRandomSource random) =>
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
