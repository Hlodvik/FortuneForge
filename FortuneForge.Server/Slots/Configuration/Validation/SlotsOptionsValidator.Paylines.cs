using FortuneForge.Server.Slots.Models;
using Microsoft.Extensions.Options;

namespace FortuneForge.Server.Slots.Configuration;

public sealed partial class SlotsOptionsValidator
{
    private static void ValidatePaylines(GameDefinition game, ICollection<string> errors)
    {
        if (game.Paylines.Count == 0)
        {
            errors.Add($"Game '{game.Id}' must define at least one payline.");
            return;
        }

        if (game.Paylines.Count != game.Layout.PaylineCount)
        {
            errors.Add(
                $"Game '{game.Id}' defines {game.Paylines.Count} paylines but expects " +
                $"{game.Layout.PaylineCount}.");
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

    private static void ValidatePaylinePayoutSteps(GameDefinition game, ICollection<string> errors)
    {
        var steps = game.Math.PaylinePayoutSteps;
        if (steps.Count != game.Paylines.Count)
        {
            errors.Add(
                $"Game '{game.Id}' defines {steps.Count} payline payout steps but has " +
                $"{game.Paylines.Count} paylines.");
            return;
        }

        if (steps.Any(step => step < 0))
        {
            errors.Add($"Game '{game.Id}' contains a negative payline payout step.");
            return;
        }

        var distinctSteps = steps.Distinct().Order().ToArray();
        if (distinctSteps.Length == 0 ||
            !distinctSteps.SequenceEqual(Enumerable.Range(0, distinctSteps[^1] + 1)))
        {
            errors.Add($"Game '{game.Id}' payline payout steps must form a contiguous range starting at zero.");
        }
    }
}
