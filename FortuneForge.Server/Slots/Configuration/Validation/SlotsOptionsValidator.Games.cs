using FortuneForge.Server.Slots.Models;
using Microsoft.Extensions.Options;

namespace FortuneForge.Server.Slots.Configuration;

public sealed partial class SlotsOptionsValidator
{
    private static void ValidateGame(
        GameDefinition game,
        IReadOnlyDictionary<string, SymbolSetDefinition> symbolSets,
        IReadOnlyDictionary<string, ReelSetDefinition> reelSets,
        IReadOnlyDictionary<string, PaytableDefinition> paytables,
        ICollection<string> errors)
    {
        if (game.Layout.ReelCount <= 0 ||
            game.Layout.VisibleRows <= 0 ||
            game.Layout.PaylineCount <= 0)
        {
            errors.Add($"Game '{game.Id}' must define positive reel, visible-row, and payline counts.");
            return;
        }

        if (game.Matching.MinimumRunLength <= 0 ||
            game.Matching.MinimumRunLength > game.Layout.ReelCount)
        {
            errors.Add(
                $"Game '{game.Id}' has an invalid minimum run length of {game.Matching.MinimumRunLength}.");
        }

        ValidatePaylines(game, errors);
        ValidatePaylinePayoutSteps(game, errors);
        ValidatePity(game, errors);
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

        if (game.FreeGames is { } freeGames)
        {
            if (!symbolIds.Contains(freeGames.SymbolId))
            {
                errors.Add(
                    $"Game '{game.Id}' references missing free-game symbol '{freeGames.SymbolId}'.");
            }
            if (string.Equals(freeGames.SymbolId, game.Symbols.WildSymbolId, StringComparison.Ordinal))
            {
                errors.Add($"Game '{game.Id}' cannot use its wild symbol as its free-game symbol.");
            }
            if (freeGames.RequiredSymbols <= 0 || freeGames.AwardedSpins <= 0)
            {
                errors.Add($"Game '{game.Id}' must define positive free-game trigger and award counts.");
            }
        }

        if (game.SpecialPoints is { } specialPoints)
        {
            if (!symbolIds.Contains(specialPoints.SymbolId))
            {
                errors.Add(
                    $"Game '{game.Id}' references missing special-point symbol '{specialPoints.SymbolId}'.");
            }
            if (string.Equals(specialPoints.SymbolId, game.Symbols.WildSymbolId, StringComparison.Ordinal) ||
                string.Equals(specialPoints.SymbolId, game.FreeGames?.SymbolId, StringComparison.Ordinal))
            {
                errors.Add(
                    $"Game '{game.Id}' must use a distinct special-point symbol.");
            }
            if (specialPoints.ThreeMatchPoints <= 0 ||
                specialPoints.FiveMatchPoints <= 0 ||
                specialPoints.ActivationCost <= 0)
            {
                errors.Add(
                    $"Game '{game.Id}' must define positive special-point awards and activation cost.");
            }
            if (specialPoints.CommonSymbolIds.Count == 0)
            {
                errors.Add($"Game '{game.Id}' must define at least one common symbol for its boost.");
            }
            if (specialPoints.CommonSymbolIds.Count !=
                specialPoints.CommonSymbolIds.Distinct(StringComparer.Ordinal).Count())
            {
                errors.Add($"Game '{game.Id}' contains duplicate common boost symbols.");
            }
            foreach (var commonSymbolId in specialPoints.CommonSymbolIds)
            {
                if (!symbolIds.Contains(commonSymbolId))
                {
                    errors.Add(
                        $"Game '{game.Id}' references missing common boost symbol '{commonSymbolId}'.");
                }
                if (string.Equals(commonSymbolId, specialPoints.SymbolId, StringComparison.Ordinal) ||
                    string.Equals(commonSymbolId, game.Energy?.SymbolId, StringComparison.Ordinal) ||
                    string.Equals(commonSymbolId, game.Symbols.WildSymbolId, StringComparison.Ordinal) ||
                    string.Equals(commonSymbolId, game.FreeGames?.SymbolId, StringComparison.Ordinal))
                {
                    errors.Add(
                        $"Game '{game.Id}' cannot reduce special, energy, wild, or free-game symbols during a boost.");
                }
            }
        }

        if (game.Energy is { } energy)
        {
            if (!symbolIds.Contains(energy.SymbolId))
            {
                errors.Add(
                    $"Game '{game.Id}' references missing energy symbol '{energy.SymbolId}'.");
            }
            if (string.Equals(energy.SymbolId, game.Symbols.WildSymbolId, StringComparison.Ordinal) ||
                string.Equals(energy.SymbolId, game.FreeGames?.SymbolId, StringComparison.Ordinal) ||
                string.Equals(energy.SymbolId, game.SpecialPoints?.SymbolId, StringComparison.Ordinal))
            {
                errors.Add($"Game '{game.Id}' must use a distinct energy symbol.");
            }
            if (energy.PointsPerVisibleSymbol <= 0)
            {
                errors.Add($"Game '{game.Id}' must award positive energy per visible symbol.");
            }
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

        foreach (var length in game.Symbols.WildSubstitutionMatchLengths.Distinct())
        {
            if (length < game.Matching.MinimumRunLength || length > game.Layout.ReelCount)
            {
                errors.Add($"Game '{game.Id}' has an invalid wild-substitution match length of {length}.");
            }
        }

        if (game.Symbols.WildSubstitutionMatchLengths.Count !=
            game.Symbols.WildSubstitutionMatchLengths.Distinct().Count())
        {
            errors.Add($"Game '{game.Id}' contains duplicate wild-substitution match lengths.");
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
}
