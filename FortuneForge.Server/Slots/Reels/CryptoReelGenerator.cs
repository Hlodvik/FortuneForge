using FortuneForge.Server.Slots.Models;

namespace FortuneForge.Server.Slots.Reels;

public sealed class CryptoReelGenerator(IRandomIndexSource random) : IReelGenerator
{
    public ReelOutcome Generate(
        GameDefinition game,
        ReelSetDefinition reelSet,
        SymbolSetDefinition symbolSet)
    {
        Validate(game, reelSet, symbolSet);
        var stops = new int[game.Layout.ReelCount];
        var reels = new List<IReadOnlyList<string>>(game.Layout.ReelCount);

        for (var reel = 0; reel < game.Layout.ReelCount; reel++)
        {
            var strip = reelSet.Reels[reel];
            var stop = random.Next(strip.Count);
            stops[reel] = stop;
            var rows = new List<string>(game.Layout.VisibleRows);
            for (var row = 0; row < game.Layout.VisibleRows; row++)
            {
                rows.Add(strip[(stop + row) % strip.Count]);
            }
            reels.Add(rows);
        }

        return new ReelOutcome(stops, reels);
    }

    private static void Validate(
        GameDefinition game,
        ReelSetDefinition reelSet,
        SymbolSetDefinition symbolSet)
    {
        if (!string.Equals(reelSet.SymbolSetId, symbolSet.Id, StringComparison.Ordinal))
        {
            throw new InvalidOperationException(
                $"Reel set '{reelSet.Id}' does not use symbol set '{symbolSet.Id}'.");
        }

        if (reelSet.Reels.Count != game.Layout.ReelCount)
        {
            throw new InvalidOperationException(
                $"Reel set '{reelSet.Id}' must contain exactly {game.Layout.ReelCount} strips for game '{game.Id}'.");
        }

        var symbolIds = symbolSet.Symbols.Select(symbol => symbol.Id).ToHashSet(StringComparer.Ordinal);
        for (var reel = 0; reel < reelSet.Reels.Count; reel++)
        {
            var strip = reelSet.Reels[reel];
            if (strip.Count < game.Layout.VisibleRows)
            {
                throw new InvalidOperationException(
                    $"Strip {reel + 1} in reel set '{reelSet.Id}' must contain at least " +
                    $"{game.Layout.VisibleRows} positions for game '{game.Id}'.");
            }

            var unknown = strip.FirstOrDefault(symbolId => !symbolIds.Contains(symbolId));
            if (unknown is not null)
            {
                throw new InvalidOperationException(
                    $"Strip {reel + 1} in reel set '{reelSet.Id}' references unknown symbol '{unknown}'.");
            }
        }
    }
}
