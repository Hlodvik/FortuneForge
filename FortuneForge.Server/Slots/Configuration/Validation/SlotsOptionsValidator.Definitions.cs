using FortuneForge.Server.Slots.Models;
using Microsoft.Extensions.Options;

namespace FortuneForge.Server.Slots.Configuration;

public sealed partial class SlotsOptionsValidator
{
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
