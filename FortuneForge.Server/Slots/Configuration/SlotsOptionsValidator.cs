using FortuneForge.Server.Slots.Models;
using Microsoft.Extensions.Options;

namespace FortuneForge.Server.Slots.Configuration;

public sealed class SlotsOptionsValidator : IValidateOptions<SlotsOptions>
{
    public ValidateOptionsResult Validate(string? name, SlotsOptions options)
    {
        var errors = new List<string>();

        ValidateDefinitionIds(options.GameDefinitions.Select(game => game.Id), "game", errors);
        ValidateDefinitionIds(options.SymbolSets.Select(set => set.Id), "symbol set", errors);
        ValidateDefinitionIds(options.ReelSets.Select(set => set.Id), "reel set", errors);
        ValidateDefinitionIds(options.Paytables.Select(table => table.Id), "paytable", errors);

        var symbolSets = ToLookup(options.SymbolSets, set => set.Id);
        var reelSets = ToLookup(options.ReelSets, set => set.Id);
        var paytables = ToLookup(options.Paytables, table => table.Id);

        foreach (var symbolSet in options.SymbolSets)
        {
            ValidateSymbolSet(symbolSet, errors);
        }

        foreach (var reelSet in options.ReelSets)
        {
            ValidateReelSetReferences(reelSet, symbolSets, errors);
        }

        foreach (var paytable in options.Paytables)
        {
            ValidatePaytableReferences(paytable, symbolSets, errors);
        }

        foreach (var game in options.GameDefinitions)
        {
            ValidateGame(game, symbolSets, reelSets, paytables, errors);
        }

        return errors.Count == 0
            ? ValidateOptionsResult.Success
            : ValidateOptionsResult.Fail(errors);
    }

    private static void ValidateSymbolSet(SymbolSetDefinition symbolSet, ICollection<string> errors)
    {
        ValidateDefinitionIds(
            symbolSet.Symbols.Select(symbol => symbol.Id),
            $"symbol in '{symbolSet.Id}'",
            errors);

        if (symbolSet.Symbols.Count == 0)
        {
            errors.Add($"Symbol set '{symbolSet.Id}' must contain at least one symbol.");
        }
    }

    private static void ValidateReelSetReferences(
        ReelSetDefinition reelSet,
        IReadOnlyDictionary<string, SymbolSetDefinition> symbolSets,
        ICollection<string> errors)
    {
        if (!symbolSets.TryGetValue(reelSet.SymbolSetId, out var symbolSet))
        {
            errors.Add($"Reel set '{reelSet.Id}' references missing symbol set '{reelSet.SymbolSetId}'.");
            return;
        }

        var symbolIds = symbolSet.Symbols.Select(symbol => symbol.Id).ToHashSet(StringComparer.Ordinal);
        for (var index = 0; index < reelSet.Reels.Count; index++)
        {
            foreach (var unknown in reelSet.Reels[index]
                         .Where(symbolId => !symbolIds.Contains(symbolId))
                         .Distinct(StringComparer.Ordinal))
            {
                errors.Add(
                    $"Strip {index + 1} in reel set '{reelSet.Id}' references unknown symbol '{unknown}'.");
            }
        }
    }

    private static void ValidatePaytableReferences(
        PaytableDefinition paytable,
        IReadOnlyDictionary<string, SymbolSetDefinition> symbolSets,
        ICollection<string> errors)
    {
        if (!symbolSets.TryGetValue(paytable.SymbolSetId, out var symbolSet))
        {
            errors.Add($"Paytable '{paytable.Id}' references missing symbol set '{paytable.SymbolSetId}'.");
            return;
        }

        var symbolIds = symbolSet.Symbols.Select(symbol => symbol.Id).ToHashSet(StringComparer.Ordinal);
        foreach (var duplicate in paytable.Rules
                     .GroupBy(rule => (rule.SymbolId, rule.MatchLength))
                     .Where(group => group.Count() > 1))
        {
            errors.Add(
                $"Paytable '{paytable.Id}' contains duplicate {duplicate.Key.MatchLength}-match rules for " +
                $"'{duplicate.Key.SymbolId}'.");
        }

        foreach (var rule in paytable.Rules)
        {
            if (!symbolIds.Contains(rule.SymbolId))
            {
                errors.Add($"Paytable '{paytable.Id}' references unknown symbol '{rule.SymbolId}'.");
            }
            if (rule.MatchLength <= 0)
            {
                errors.Add($"Paytable '{paytable.Id}' contains an invalid match length of {rule.MatchLength}.");
            }
            if (rule.Multiplier < 0)
            {
                errors.Add($"Paytable '{paytable.Id}' contains a negative multiplier.");
            }
        }
    }

    private static void ValidateGame(
        GameDefinition game,
        IReadOnlyDictionary<string, SymbolSetDefinition> symbolSets,
        IReadOnlyDictionary<string, ReelSetDefinition> reelSets,
        IReadOnlyDictionary<string, PaytableDefinition> paytables,
        ICollection<string> errors)
    {
        if (game.Layout.ReelCount <= 0 || game.Layout.VisibleRows <= 0)
        {
            errors.Add($"Game '{game.Id}' must define positive reel and visible-row counts.");
            return;
        }

        if (game.Matching.MinimumRunLength <= 0 ||
            game.Matching.MinimumRunLength > game.Layout.ReelCount)
        {
            errors.Add(
                $"Game '{game.Id}' has an invalid minimum run length of {game.Matching.MinimumRunLength}.");
        }

        ValidatePaylines(game, errors);
        ValidateWagering(game, errors);
        ValidateTargets(game, errors);

        if (!symbolSets.TryGetValue(game.Symbols.SymbolSetId, out var symbolSet))
        {
            errors.Add($"Game '{game.Id}' references missing symbol set '{game.Symbols.SymbolSetId}'.");
            return;
        }

        var symbolIds = symbolSet.Symbols.Select(symbol => symbol.Id).ToHashSet(StringComparer.Ordinal);
        if (!symbolIds.Contains(game.Symbols.WildSymbolId))
        {
            errors.Add(
                $"Game '{game.Id}' references missing wild symbol '{game.Symbols.WildSymbolId}'.");
        }

        foreach (var length in game.Symbols.NativeWildMatchLengths.Distinct())
        {
            if (length < game.Matching.MinimumRunLength || length > game.Layout.ReelCount)
            {
                errors.Add($"Game '{game.Id}' has an invalid native-wild match length of {length}.");
            }
        }

        if (game.Symbols.NativeWildMatchLengths.Count != game.Symbols.NativeWildMatchLengths.Distinct().Count())
        {
            errors.Add($"Game '{game.Id}' contains duplicate native-wild match lengths.");
        }

        if (!reelSets.TryGetValue(game.Math.ReelSetId, out var reelSet))
        {
            errors.Add($"Game '{game.Id}' references missing reel set '{game.Math.ReelSetId}'.");
        }
        else
        {
            ValidateGameReelSet(game, reelSet, errors);
            if (!string.Equals(reelSet.SymbolSetId, symbolSet.Id, StringComparison.Ordinal))
            {
                errors.Add($"Game '{game.Id}' uses a reel set from a different symbol set.");
            }
        }

        if (!paytables.TryGetValue(game.Math.PaytableId, out var paytable))
        {
            errors.Add($"Game '{game.Id}' references missing paytable '{game.Math.PaytableId}'.");
        }
        else
        {
            if (!string.Equals(paytable.SymbolSetId, symbolSet.Id, StringComparison.Ordinal))
            {
                errors.Add($"Game '{game.Id}' uses a paytable from a different symbol set.");
            }

            foreach (var rule in paytable.Rules.Where(rule =>
                         rule.MatchLength < game.Matching.MinimumRunLength ||
                         rule.MatchLength > game.Layout.ReelCount))
            {
                errors.Add(
                    $"Paytable '{paytable.Id}' contains a {rule.MatchLength}-match rule outside game " +
                    $"'{game.Id}' matching limits.");
            }
        }
    }

    private static void ValidatePaylines(GameDefinition game, ICollection<string> errors)
    {
        if (game.Paylines.Count == 0)
        {
            errors.Add($"Game '{game.Id}' must define at least one payline.");
            return;
        }

        foreach (var (payline, index) in game.Paylines.Select((line, index) => (line, index)))
        {
            if (payline.Count != game.Layout.ReelCount)
            {
                errors.Add(
                    $"Payline {index + 1} in game '{game.Id}' must contain {game.Layout.ReelCount} row indexes.");
                continue;
            }

            if (payline.Any(row => row < 0 || row >= game.Layout.VisibleRows))
            {
                errors.Add($"Payline {index + 1} in game '{game.Id}' contains an out-of-range row index.");
            }
        }

        foreach (var duplicate in game.Paylines
                     .Select((line, index) => new { Key = string.Join(',', line), Index = index + 1 })
                     .GroupBy(item => item.Key, StringComparer.Ordinal)
                     .Where(group => group.Count() > 1))
        {
            errors.Add($"Game '{game.Id}' contains duplicate payline '{duplicate.Key}'.");
        }
    }

    private static void ValidateGameReelSet(
        GameDefinition game,
        ReelSetDefinition reelSet,
        ICollection<string> errors)
    {
        if (reelSet.Reels.Count != game.Layout.ReelCount)
        {
            errors.Add(
                $"Reel set '{reelSet.Id}' must contain {game.Layout.ReelCount} strips for game '{game.Id}'.");
            return;
        }

        for (var index = 0; index < reelSet.Reels.Count; index++)
        {
            if (reelSet.Reels[index].Count < game.Layout.VisibleRows)
            {
                errors.Add(
                    $"Strip {index + 1} in reel set '{reelSet.Id}' must contain at least " +
                    $"{game.Layout.VisibleRows} positions for game '{game.Id}'.");
            }
        }
    }

    private static void ValidateWagering(GameDefinition game, ICollection<string> errors)
    {
        if (game.Wagering.PointValueInCents <= 0)
        {
            errors.Add($"Game '{game.Id}' must define a positive point value.");
        }
        if (game.Wagering.MinimumWagerPoints <= 0)
        {
            errors.Add($"Game '{game.Id}' must define a positive minimum wager.");
        }
        if (game.Wagering.MaximumWagerPoints is { } maximum &&
            maximum < game.Wagering.MinimumWagerPoints)
        {
            errors.Add($"Game '{game.Id}' has a maximum wager below its minimum wager.");
        }
    }

    private static void ValidateTargets(GameDefinition game, ICollection<string> errors)
    {
        if (game.Math.Targets.Rtp is { } rtp && rtp is <= 0 or > 1)
        {
            errors.Add($"Game '{game.Id}' target RTP must be greater than zero and at most one.");
        }
        if (game.Math.Targets.HitRate is { } hitRate && hitRate is < 0 or > 1)
        {
            errors.Add($"Game '{game.Id}' target hit rate must be between zero and one.");
        }
        if (game.Math.Targets.Volatility is { } volatility && volatility < 0)
        {
            errors.Add($"Game '{game.Id}' target volatility cannot be negative.");
        }
    }

    private static Dictionary<string, T> ToLookup<T>(
        IEnumerable<T> definitions,
        Func<T, string> idSelector) => definitions
        .Where(definition => !string.IsNullOrWhiteSpace(idSelector(definition)))
        .GroupBy(idSelector, StringComparer.Ordinal)
        .ToDictionary(group => group.Key, group => group.First(), StringComparer.Ordinal);

    private static void ValidateDefinitionIds(
        IEnumerable<string> ids,
        string label,
        ICollection<string> errors)
    {
        var materialized = ids.ToArray();
        foreach (var _ in materialized.Where(string.IsNullOrWhiteSpace))
        {
            errors.Add($"{label} IDs cannot be empty.");
        }

        foreach (var duplicate in materialized
                     .Where(id => !string.IsNullOrWhiteSpace(id))
                     .GroupBy(id => id, StringComparer.Ordinal)
                     .Where(group => group.Count() > 1))
        {
            errors.Add($"Duplicate {label} ID '{duplicate.Key}'.");
        }
    }
}
