using FortuneForge.Games.Slots;
using FortuneForge.Games.Slots.Runtime;

namespace FortuneForge.Games.Wukong;

public sealed class WukongEngine
{
    public const string GameId = "wukong-journey-v1";
    public const string EngineVersion = "wukong-journey-engine-v1";
    public const string ReelSetId = "wukong-reels-v1";
    public static SlotProfile Profile { get; } = new(GameId, 3, 0, 40, 10,
        BaseReelSetId: ReelSetId, PayoutMultiplier: 0.946m);
    private static readonly FrozenSlotDefinitions Definitions = CreateDefinitions();
    private readonly ClassicSlotEngine engine;

    public WukongEngine(IGameRandomSource random) => engine = new(Definitions, Profile,
        random, EngineVersion, new ClassicSlotEngineOptions { HeavyPawFeature = true });

    public ClassicSlotSpinOutcome Spin(long wagerPoints, string playerId,
        bool specialBoostApplied = false, long currentEnergyBalance = 0,
        string? featureMode = null, int? persistedPityMisses = null,
        bool isFreeSpin = false) => engine.Spin(wagerPoints, playerId,
        specialBoostApplied, currentEnergyBalance, featureMode, persistedPityMisses, isFreeSpin);

    public static GameDefinition CreateGame() => Definitions.CreateGame();
    public static SymbolSetDefinition CreateSymbolSet() => Definitions.CreateSymbolSet();
    public static ReelSetDefinition CreateReelSet() => Definitions.CreateReelSet();
    public static PaytableDefinition CreatePaytable() => Definitions.CreatePaytable();

    private static FrozenSlotDefinitions CreateDefinitions()
    {
        var source = ClassicSlotDefinitionFactory.Create(Profile);
        var replacements = new[] { "5", "6", "7" };
        var reels = source.CreateReelSet();
        reels = new ReelSetDefinition
        {
            Id = ReelSetId,
            SymbolSetId = reels.SymbolSetId,
            Reels = reels.Reels.Select(strip =>
            {
                var replacement = 0;
                return strip.Select(symbol => symbol == "FREE"
                    ? replacements[replacement++ % replacements.Length] : symbol).ToList();
            }).ToList()
        };
        return new(source.CreateGame(), source.CreateSymbolSet(), reels, source.CreatePaytable());
    }
}
