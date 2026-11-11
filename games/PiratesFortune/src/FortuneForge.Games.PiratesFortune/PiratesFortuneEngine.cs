using FortuneForge.Games.Slots;
using FortuneForge.Games.Slots.Runtime;

namespace FortuneForge.Games.PiratesFortune;

public sealed class PiratesFortuneEngine
{
    public const string GameId = "pirates-fortune-v1";
    public const string EngineVersion = "pirates-fortune-engine-v1";
    public const string ReelSetId = "pirates-fortune-reels-v1";
    public static SlotProfile Profile { get; } = new(GameId, 3, 7,
        PiratesGemBonus.MaximumGems, 10, UsesCollections: true, UsesEnergy: false,
        UsesDirectValueTokens: true, BaseReelSetId: ReelSetId,
        TargetHitRate: 0.32m, PayoutMultiplier: 0.665m);
    private static readonly FrozenSlotDefinitions Definitions = CreateDefinitions();
    private readonly ClassicSlotEngine engine;

    public PiratesFortuneEngine(IGameRandomSource random) => engine = new(Definitions,
        Profile, random, EngineVersion, new ClassicSlotEngineOptions
        {
            DirectValueCountFromRoll = roll => roll switch { < 91 => 0, < 99 => 1, _ => 2 },
            CollectionAppearanceChance = _ => 50
        });

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
        return new(source.CreateGame(), source.CreateSymbolSet(), PiratesFortuneReels.Create(),
            source.CreatePaytable());
    }
}

internal static class PiratesFortuneReels
{
    internal static ReelSetDefinition Create() => new()
    {
        Id = PiratesFortuneEngine.ReelSetId,
        SymbolSetId = ClassicSlotDefinitionFactory.SymbolSetId,
        Reels =
        [
            ["3","2","4","5","2","6","3","4","POWER","2","7","3","4","2","5","FREE","3","6","2","4","3","POWER","2","5","4","3","2","7","6","4","3","2","POWER","5","4","2","3","6","ACE","4","2","5","3","FREE","4","2","7","3","5","2","4","6","POWER","3","2","FREE","4","3","5","2"],
            ["2","4","3","5","2","7","4","3","POWER","6","2","4","3","5","2","FREE","4","6","3","2","5","POWER","4","2","3","7","4","2","6","5","3","POWER","2","4","3","5","6","2","4","ACE","3","2","5","FREE","4","3","2","7","5","4","3","2","POWER","6","4","FREE","2","3","5","4"],
            ["4","2","3","6","5","2","4","7","POWER","3","2","5","4","3","2","FREE","6","4","2","3","5","POWER","2","4","7","3","2","5","4","6","3","2","POWER","4","5","2","3","4","ACE","6","2","5","3","FREE","4","2","7","3","5","4","2","6","POWER","3","4","FREE","2","5","3","4"],
            ["5","3","2","4","6","2","5","7","POWER","4","3","2","5","4","3","FREE","2","6","4","5","2","POWER","3","4","7","2","5","3","4","6","2","POWER","5","3","4","2","6","4","ACE","3","5","2","4","FREE","3","7","2","5","4","3","6","2","POWER","5","3","FREE","4","2","5","3"],
            ["2","5","4","3","6","2","7","4","POWER","5","3","2","4","5","2","FREE","3","6","4","2","5","POWER","3","4","2","7","5","3","4","6","2","POWER","4","5","3","2","6","4","ACE","5","2","3","4","FREE","5","2","7","3","4","6","2","5","POWER","3","4","FREE","2","5","3","4"]
        ]
    };
}
