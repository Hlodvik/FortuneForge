using FortuneForge.Server.Slots.Models;
using Microsoft.Extensions.Options;

namespace FortuneForge.Server.Slots.Configuration;

public sealed partial class SlotsOptionsValidator
{
    private static void ValidatePity(GameDefinition game, ICollection<string> errors)
    {
        if (game.Math.FiveMatchPityMissLimit is <= 0)
        {
            errors.Add($"Game '{game.Id}' five-match pity miss limit must be positive when configured.");
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
        var allowedWagers = game.Wagering.AllowedWagerPoints;
        if (game.Wagering.PointValueInCents <= 0)
        {
            errors.Add($"Game '{game.Id}' must define a positive point value.");
        }
        if (game.Wagering.PointValueInCents != decimal.Truncate(game.Wagering.PointValueInCents))
        {
            errors.Add($"Game '{game.Id}' point value must use whole cents.");
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
        if (game.Wagering.WagerIncrementPoints is <= 0)
        {
            errors.Add($"Game '{game.Id}' wager increment must be positive when configured.");
        }
        if (game.Wagering.MaximumWagerPoints is { } incrementMaximum &&
            game.Wagering.WagerIncrementPoints is { } configuredIncrement &&
            (incrementMaximum - game.Wagering.MinimumWagerPoints) % configuredIncrement != 0)
        {
            errors.Add($"Game '{game.Id}' maximum wager does not align with its wager increment.");
        }
        if (allowedWagers.Count == 0 && game.Wagering.WagerIncrementPoints is null)
        {
            errors.Add($"Game '{game.Id}' must define allowed wagers or a wager increment.");
            return;
        }
        if (allowedWagers.Count == 0)
        {
            return;
        }
        if (allowedWagers.Any(wager => wager <= 0))
        {
            errors.Add($"Game '{game.Id}' contains a non-positive allowed wager.");
        }
        if (allowedWagers.Count != allowedWagers.Distinct().Count())
        {
            errors.Add($"Game '{game.Id}' contains duplicate allowed wagers.");
        }
        if (!allowedWagers.Contains(game.Wagering.MinimumWagerPoints))
        {
            errors.Add($"Game '{game.Id}' allowed wagers must include its minimum wager.");
        }
        if (game.Wagering.MaximumWagerPoints is { } configuredMaximum &&
            !allowedWagers.Contains(configuredMaximum))
        {
            errors.Add($"Game '{game.Id}' allowed wagers must include its maximum wager.");
        }
        if (allowedWagers.Any(wager =>
                wager < game.Wagering.MinimumWagerPoints ||
                (game.Wagering.MaximumWagerPoints is { } maximum && wager > maximum)))
        {
            errors.Add($"Game '{game.Id}' contains an allowed wager outside its configured limits.");
        }
    }
}
