using FortuneForge.Server.Slots.Models;
using Microsoft.Extensions.Options;

namespace FortuneForge.Server.Slots.Configuration;

public sealed partial class SlotsOptionsValidator : IValidateOptions<SlotsOptions>
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

}
