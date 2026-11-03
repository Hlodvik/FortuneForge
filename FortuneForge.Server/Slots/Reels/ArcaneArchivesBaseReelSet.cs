using FortuneForge.Server.Slots.Bonuses;
using FortuneForge.Server.Slots.Models;

namespace FortuneForge.Server.Slots.Reels;

internal static class ArcaneArchivesBaseReelSet
{
    internal static ReelSetDefinition Create(ReelSetDefinition source) => new()
    {
        Id = ArcaneLightningCollector.ReelSetId,
        SymbolSetId = source.SymbolSetId,
        Reels = source.Reels.Select((strip, index) =>
            CreateStrip(strip, index, source.Reels.Count / 2)).ToList()
    };

    private static List<string> CreateStrip(IReadOnlyList<string> source, int reel, int primaryCollectorReel)
    {
        var sparseCollector = reel == primaryCollectorReel + 1;
        var hasCollector = reel == primaryCollectorReel || sparseCollector;
        var aetherStops = 0;
        var collectorPlaced = false;
        var strip = new List<string>(source.Count);
        foreach (var symbol in source)
        {
            switch (symbol)
            {
                case "POWER":
                    // The classic strip has eight Aether and three Book stops.
                    // Spread the substitutions so Book leads at five stops versus four.
                    strip.Add((++aetherStops % 4) switch { 0 => "6", 2 => "7", _ => symbol });
                    break;
                case ArcaneLightningCollector.CollectorSymbolId:
                    strip.Add(hasCollector && !collectorPlaced ? symbol : "2");
                    collectorPlaced = true;
                    break;
                default:
                    strip.Add(symbol);
                    break;
            }
        }

        if (!sparseCollector)
            return strip;

        // Keep the second collector scarcer without distorting the other symbol weights on its reel.
        var withoutCollector = strip.Select(symbol => symbol == ArcaneLightningCollector.CollectorSymbolId ? "2" : symbol);
        return [.. withoutCollector, .. strip, .. withoutCollector];
    }
}
