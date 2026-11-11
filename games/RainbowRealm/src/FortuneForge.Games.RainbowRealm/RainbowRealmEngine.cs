using FortuneForge.Games.Slots;
using FortuneForge.Games.Slots.Runtime;

namespace FortuneForge.Games.RainbowRealm;

public sealed class RainbowRealmEngine
{
    public const string GameId = "rainbow-realm-fruits-v1";
    public const string EngineVersion = "rainbow-realm-charm-harvest-v1";
    public const string SymbolSetId = "rainbow-realm-symbols-v1";
    public const string ReelSetId = "rainbow-realm-reels-v1";
    public const string PaytableId = "rainbow-realm-paytable-v1";
    public static SlotProfile Profile { get; } = new(GameId, 3, 10, 60, 10,
        UsesCollections: true, UsesEnergy: false, UsesDirectValueTokens: false,
        BaseReelSetId: ReelSetId);
    private static readonly FrozenSlotDefinitions Definitions = CreateDefinitions();
    private readonly ClassicSlotEngine engine;

    public RainbowRealmEngine(IGameRandomSource random) => engine = new(Definitions, Profile,
        random, EngineVersion, new ClassicSlotEngineOptions
        {
            AlwaysUsesPawCollectors = true,
            SinglePawDivisor = 17,
            CollectionAppearanceChance = _ => 33,
            FeaturePayoutCalculator = RainbowRealmCharmHarvest.Calculate
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
        var game = source.CreateGame();
        game = new GameDefinition
        {
            Id = GameId,
            Layout = game.Layout,
            Symbols = new GameSymbolRules
            {
                SymbolSetId = SymbolSetId,
                WildSymbolId = game.Symbols.WildSymbolId,
                NativeWildMatchLengths = [.. game.Symbols.NativeWildMatchLengths],
                WildSubstitutionMatchLengths = [.. game.Symbols.WildSubstitutionMatchLengths]
            },
            Matching = game.Matching,
            Math = new GameMathDefinition
            {
                ReelSetId = ReelSetId,
                PaytableId = PaytableId,
                PayoutMultiplier = 1m,
                PaylinePayoutSteps = [2,2,1,1,1,1,1,0,0,0,0,0,0,0,0,0,0,0,0,0,0,0,0],
                FiveMatchPityMissLimit = 12,
                Targets = new GameMathTargets { Rtp = 0.88m, HitRate = 0.19m }
            },
            Wagering = game.Wagering,
            FreeGames = new GameFreeGamesDefinition
            {
                SymbolId = "FREE",
                RequiredSymbols = 3,
                AwardedSpins = 10,
                VisibleFrequencyDivisor = 4
            },
            SpecialPoints = game.SpecialPoints,
            Energy = null,
            Paylines = game.Paylines
        };
        var symbols = new SymbolSetDefinition
        {
            Id = SymbolSetId,
            Symbols = RainbowRealmDefinitions.Weights.Select(value =>
                new SymbolDefinition { Id = value.Symbol }).Append(
                new SymbolDefinition { Id = "PAW" }).ToList()
        };
        var reels = new ReelSetDefinition
        {
            Id = ReelSetId,
            SymbolSetId = SymbolSetId,
            Reels = Enumerable.Range(0, 5).Select(RainbowRealmDefinitions.CreateStrip).ToList()
        };
        var paytable = new PaytableDefinition
        {
            Id = PaytableId,
            SymbolSetId = SymbolSetId,
            Rules = RainbowRealmDefinitions.Payouts.SelectMany(value => new[]
            {
                new PayoutRule { SymbolId = value.Symbol, MatchLength = 3, Multiplier = value.Three },
                new PayoutRule { SymbolId = value.Symbol, MatchLength = 5, Multiplier = value.Five }
            }).ToList()
        };
        return new(game, symbols, reels, paytable);
    }
}

internal static class RainbowRealmDefinitions
{
    internal static readonly (string Symbol, int Count)[] Weights =
    [
        ("A",90), ("K",95), ("Q",100), ("J",105), ("2",75), ("3",72),
        ("4",68), ("5",60), ("6",56), ("7",49), ("ACE",38), ("POWER",64),
        ("BANANA",49), ("FREE",35), ("SEAL_SYNC",11), ("SEAL_ROWS",11),
        ("SEAL_PAW",11), ("SEAL_RAND",11)
    ];
    internal static readonly (string Symbol, long Three, long Five)[] Payouts =
    [
        ("J",1,2), ("Q",1,3), ("K",1,4), ("A",1,5), ("2",1,4), ("3",1,2),
        ("4",1,7), ("5",2,6), ("6",2,8), ("7",3,11), ("ACE",5,18), ("POWER",2,4)
    ];

    internal static List<string> CreateStrip(int reel)
    {
        var strip = Weights.SelectMany(value => Enumerable.Repeat(value.Symbol, value.Count)).ToList();
        var state = unchecked(0x5241494eu + (uint)reel * 0x9e3779b9u);
        for (var index = strip.Count - 1; index > 0; index--)
        {
            state ^= state << 13; state ^= state >> 17; state ^= state << 5;
            var other = (int)(state % (uint)(index + 1));
            (strip[index], strip[other]) = (strip[other], strip[index]);
        }
        return strip;
    }
}
