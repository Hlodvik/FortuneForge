// Edited: 2026-11-18T18:14:30-06:00 | Computer: Vinnabok
using FortuneForge.Games.Slots;
using FortuneForge.Games.Slots.Runtime;

namespace FortuneForge.Games.ArcaneArchives;

// Frozen production model from d23a0474. Only the remote worker can use it for
// unversioned requests from the prior account host during a rolling release.
internal static class LegacyArcaneWorkerEngine
{
    private const string EngineVersion = "arcane-lightning-collector-v1";
    private const string ReelSetId = "arcane-archives-lightning-reels-v1";
    private static readonly SlotProfile Profile = new(ArcaneArchivesEngine.GameId, 3, 5, 40, 10,
        UsesEnergy: false, CollectionFeatureMode: "sync-paw-rand",
        BaseReelSetId: ReelSetId,
        PaylinePatternIds: [1,2,3,4,5,6,7,8,9,10,18,19,20,21,22,23],
        PayoutMultiplier: 0.716m, PreserveEnergySymbolsWithoutMeter: true);
    private static readonly FrozenSlotDefinitions Definitions = CreateDefinitions();

    internal static ClassicSlotSpinOutcome Spin(IGameRandomSource random, PaidSlotOutcomeRequest request)
    {
        var engine = new ClassicSlotEngine(Definitions, Profile, random, EngineVersion,
            new ClassicSlotEngineOptions { AllowFreeSpinCarryWager = true });
        var spin = engine.Spin(request.WagerPoints, request.PlayerId, request.SpecialBoostApplied,
            request.EnergyBalance, request.FreeSpinFeatureMode, request.PityMissesBefore, request.IsFreeSpin);
        var lightning = ArcaneLightningCollector.Calculate(spin.Reels, request.WagerPoints);
        if (lightning is null) return spin;
        var line = ArcaneLightningCollector.CreatePayout(lightning, request.WagerPoints);
        return spin with
        {
            Payout = new SpinPayout(checked(spin.Payout.TotalPoints + line.AmountPoints),
                [.. spin.Payout.Paylines, line])
        };
    }

    private static FrozenSlotDefinitions CreateDefinitions()
    {
        var source = ClassicSlotDefinitionFactory.Create(Profile);
        var reels = source.CreateReelSet();
        var primary = reels.Reels.Count / 2;
        reels = new ReelSetDefinition
        {
            Id = ReelSetId,
            SymbolSetId = reels.SymbolSetId,
            Reels = reels.Reels.Select((strip, reel) => CreateStrip(strip, reel, primary)).ToList()
        };
        return new(source.CreateGame(), source.CreateSymbolSet(), reels, source.CreatePaytable());
    }

    private static List<string> CreateStrip(IReadOnlyList<string> source, int reel, int primary)
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
