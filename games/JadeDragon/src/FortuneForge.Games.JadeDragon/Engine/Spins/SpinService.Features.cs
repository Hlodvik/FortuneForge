using FortuneForge.Games.JadeDragon.Bonuses;
using FortuneForge.Games.JadeDragon.Models;

namespace FortuneForge.Games.JadeDragon.Spins;

internal sealed partial class SpinService
{
    private ReelOutcome ApplyFeatureSymbols(ReelOutcome outcome, string? featureMode, long currentEnergyBalance)
    {
        var reels = outcome.VisibleReels.Select(reel => reel.ToArray()).ToArray();
        if (JadeDragonRules.HasFeatureMode(featureMode, "copy"))
        {
            var sourceReel = random.Next(reels.Length);
            var destinationReel = (sourceReel + 1 + random.Next(reels.Length - 1)) % reels.Length;
            reels[destinationReel] = reels[sourceReel].ToArray();
        }
        if (JadeDragonRules.HasFeatureMode(featureMode, "wild"))
            InjectSymbols(reels, 2, () => "S07");
        var patternCount = random.Next(100) switch { < 48 => 0, < 88 => 1, < 98 => 2, _ => 3 };
        InjectSymbols(reels, patternCount, () => PatternSymbolId);
        var collectionChance = currentEnergyBalance switch { >= 75 => 67, >= 50 => 50, >= 25 => 40, _ => 33 };
        var collectionCount = random.Next(100) >= collectionChance ? 0 : random.Next(25) == 0 ? 2 : 1;
        InjectSymbols(reels, collectionCount, () => CollectionSymbolIds[random.Next(CollectionSymbolIds.Length)]);
        return outcome with
        {
            VisibleReels = reels.Select(reel => (IReadOnlyList<string>)reel).ToArray()
        };
    }

    internal static GameDefinition CreateEffectiveSpinGame(GameDefinition game, string? featureMode)
    {
        if (!JadeDragonRules.HasFeatureMode(featureMode, "expand")) return game;
        return new GameDefinition
        {
            Id = game.Id,
            Layout = new GameLayoutDefinition
            {
                ReelCount = game.Layout.ReelCount,
                VisibleRows = checked(game.Layout.VisibleRows + 2),
                PaylineCount = game.Layout.PaylineCount
            },
            Symbols = game.Symbols,
            Matching = game.Matching,
            Math = game.Math,
            Wagering = game.Wagering,
            FreeGames = game.FreeGames,
            SpecialPoints = game.SpecialPoints,
            Energy = null,
            Paylines = game.Paylines
        };
    }

    private void InjectSymbols(IReadOnlyList<string[]> reels, int count, Func<string> factory)
    {
        for (var index = 0; index < count; index++)
        {
            var candidates = reels.SelectMany((reel, reelIndex) => reel.Select((symbol, rowIndex) =>
                    new { Symbol = symbol, Position = new GridPosition(reelIndex, rowIndex) }))
                .Where(candidate => CommonReplacementSymbols.Contains(candidate.Symbol, StringComparer.Ordinal))
                .Select(candidate => candidate.Position).ToArray();
            if (candidates.Length == 0) return;
            var position = candidates[random.Next(candidates.Length)];
            reels[position.Reel][position.Row] = factory();
        }
    }
}
