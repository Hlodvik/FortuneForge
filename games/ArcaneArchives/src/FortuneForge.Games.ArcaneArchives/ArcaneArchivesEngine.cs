using FortuneForge.Games.Slots;
using FortuneForge.Games.Slots.Runtime;

namespace FortuneForge.Games.ArcaneArchives;

public sealed class ArcaneArchivesEngine
{
    public const string GameId = "arcane-archives-v1";
    public const string EngineVersion = "arcane-lightning-collector-v1";
    public const string ReelSetId = "arcane-archives-lightning-reels-v1";
    public static SlotProfile Profile { get; } = new(GameId, 3, 5, 40, 10,
        UsesEnergy: false, CollectionFeatureMode: "sync-paw-rand",
        BaseReelSetId: ReelSetId,
        PaylinePatternIds: [1,2,3,4,5,6,7,8,9,10,18,19,20,21,22,23],
        PayoutMultiplier: 0.716m, PreserveEnergySymbolsWithoutMeter: true);
    private static readonly FrozenSlotDefinitions Definitions = CreateDefinitions();
    private readonly ClassicSlotEngine engine;

    public ArcaneArchivesEngine(IGameRandomSource random) =>
        engine = new(Definitions, Profile, random, EngineVersion,
            new ClassicSlotEngineOptions { AllowFreeSpinCarryWager = true });

    public ClassicSlotSpinOutcome Spin(long wagerPoints, string playerId,
        bool specialBoostApplied = false, long currentEnergyBalance = 0,
        string? featureMode = null, int? persistedPityMisses = null,
        bool isFreeSpin = false) => SpinDetailed(wagerPoints, playerId,
            specialBoostApplied, currentEnergyBalance, featureMode,
            persistedPityMisses, isFreeSpin).Spin;

    public ArcaneArchivesSpinOutcome SpinDetailed(long wagerPoints, string playerId,
        bool specialBoostApplied = false, long currentEnergyBalance = 0,
        string? featureMode = null, int? persistedPityMisses = null,
        bool isFreeSpin = false)
    {
        var spin = engine.Spin(wagerPoints, playerId, specialBoostApplied,
            currentEnergyBalance, featureMode, persistedPityMisses, isFreeSpin);
        var lightning = ArcaneLightningCollector.Calculate(spin.Reels, wagerPoints);
        if (lightning is null) return new(spin, null);
        var line = ArcaneLightningCollector.CreatePayout(lightning, wagerPoints);
        spin = spin with
        {
            Payout = new SpinPayout(checked(spin.Payout.TotalPoints + line.AmountPoints),
                [.. spin.Payout.Paylines, line])
        };
        return new(spin, lightning);
    }

    public static GameDefinition CreateGame() => Definitions.CreateGame();
    public static SymbolSetDefinition CreateSymbolSet() => Definitions.CreateSymbolSet();
    public static ReelSetDefinition CreateReelSet() => Definitions.CreateReelSet();
    public static PaytableDefinition CreatePaytable() => Definitions.CreatePaytable();

    private static FrozenSlotDefinitions CreateDefinitions()
    {
        var source = ClassicSlotDefinitionFactory.Create(Profile);
        var reels = source.CreateReelSet();
        var primary = reels.Reels.Count / 2;
        reels = new ReelSetDefinition
        {
            Id = ReelSetId,
            SymbolSetId = reels.SymbolSetId,
            Reels = reels.Reels.Select((strip, reel) =>
                CreateStrip(strip, reel, primary)).ToList()
        };
        return new(source.CreateGame(), source.CreateSymbolSet(), reels, source.CreatePaytable());
    }

    private static List<string> CreateStrip(IReadOnlyList<string> source,
        int reel, int primary)
    {
        var sparse = reel == primary + 1;
        var hasCollector = reel == primary || sparse;
        var aetherStops = 0;
        var collectorPlaced = false;
        var strip = new List<string>(source.Count);
        foreach (var symbol in source)
        {
            if (symbol == "POWER")
                strip.Add((++aetherStops % 4) switch { 0 => "6", 2 => "7", _ => symbol });
            else if (symbol == ArcaneLightningCollector.CollectorSymbolId)
            {
                strip.Add(hasCollector && !collectorPlaced ? symbol : "2");
                collectorPlaced = true;
            }
            else strip.Add(symbol);
        }
        if (!sparse) return strip;
        var without = strip.Select(symbol => symbol == ArcaneLightningCollector.CollectorSymbolId
            ? "2" : symbol).ToArray();
        return [.. without, .. strip, .. without];
    }
}

public sealed record ArcaneArchivesSpinOutcome(
    ClassicSlotSpinOutcome Spin, ArcaneLightningCollection? LightningCollection);
