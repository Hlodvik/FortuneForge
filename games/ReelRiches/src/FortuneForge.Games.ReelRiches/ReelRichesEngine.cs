using FortuneForge.Games.Slots;
using FortuneForge.Games.Slots.Runtime;

namespace FortuneForge.Games.ReelRiches;

public sealed class ReelRichesEngine
{
    public const string GameId = "reel-riches-v1";
    public const string EngineVersion = "reel-riches-engine-v1";
    public static SlotProfile Profile { get; } = new(GameId, 3, 5, 40, 10,
        CollectionFeatureMode: "sync-rows-rand", TargetHitRate: 0.29m,
        PaylinePatternIds: [1,2,3,4,5,6,7,8,9,10,11,12,13,14,15,16,21,22,23]);
    private static readonly FrozenSlotDefinitions Definitions = ClassicSlotDefinitionFactory.Create(Profile);
    private readonly ClassicSlotEngine engine;

    public ReelRichesEngine(IGameRandomSource random) => engine = new(Definitions, Profile, random, EngineVersion);
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
