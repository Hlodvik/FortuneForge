using System.Collections.Concurrent;
using FortuneForge.Server.Slots.Bonuses;
using FortuneForge.Server.Slots.Configuration;
using FortuneForge.Server.Slots.Evaluation;
using FortuneForge.Server.Slots.Models;
using FortuneForge.Server.Slots.Payouts;
using FortuneForge.Server.Slots.Reels;

namespace FortuneForge.Server.Slots.Spins;

public sealed partial class SpinService
{
    private static IReadOnlyList<GridPosition> VisiblePositions(
        IReadOnlyList<IReadOnlyList<string>> reels) =>
        reels
            .SelectMany((reel, reelIndex) => reel.Select((_, rowIndex) =>
                new GridPosition(reelIndex, rowIndex)))
            .ToArray();

    private static Dictionary<string, int> CountSealSymbols(
        IReadOnlyList<IReadOnlyList<string>> reels)
    {
        var counts = SealSymbolIds.ToDictionary(symbolId => symbolId, _ => 0, StringComparer.Ordinal);
        foreach (var symbol in reels.SelectMany(reel => reel))
        {
            if (counts.ContainsKey(symbol))
            {
                counts[symbol]++;
            }
        }

        return counts
            .Where(pair => pair.Value > 0)
            .ToDictionary(pair => pair.Key, pair => pair.Value, StringComparer.Ordinal);
    }

    private static decimal RandMultiplier(string symbolId) => symbolId switch
    {
        "RAND_05" => 0.5m,
        "RAND_1" => 1m,
        "RAND_15" => 1.5m,
        "RAND_2" => 2m,
        "RAND_3" => 3m,
        "RAND_4" => 4m,
        "RAND_5" => 5m,
        _ => 0m
    };

    private static long MultiplyWager(long wagerPoints, decimal multiplier) =>
        checked((long)Math.Round(wagerPoints * multiplier, MidpointRounding.AwayFromZero));

    private static bool HasPayingFullMatch(
        IReadOnlyList<PaylineEvaluation> evaluations,
        GameDefinition game,
        PaytableDefinition paytable) =>
        evaluations.Any(evaluation => evaluation.Candidates.Any(candidate =>
            candidate.Matches.Any(match =>
                match.MatchLength == game.Layout.ReelCount &&
                paytable.Rules.Any(rule =>
                    string.Equals(rule.SymbolId, match.SymbolId, StringComparison.Ordinal) &&
                    rule.MatchLength == match.MatchLength &&
                    rule.Multiplier > 0))));

    private static int CountFreeGameSymbols(
        IReadOnlyList<IReadOnlyList<string>> reels,
        GameDefinition game)
    {
        if (game.FreeGames is null)
        {
            return 0;
        }

        return reels.Sum(reel => reel.Count(symbol => string.Equals(
            symbol,
            game.FreeGames.SymbolId,
            StringComparison.Ordinal)));
    }

    private static int CountEnergySymbols(
        IReadOnlyList<IReadOnlyList<string>> reels,
        GameDefinition game)
    {
        if (game.Energy is null)
        {
            return 0;
        }

        return reels.Sum(reel => reel.Count(symbol => string.Equals(
            symbol,
            game.Energy.SymbolId,
            StringComparison.Ordinal)));
    }

    private static void ValidateWager(GameDefinition game, long wagerPoints)
    {
        if (wagerPoints < game.Wagering.MinimumWagerPoints)
        {
            throw new ArgumentOutOfRangeException(
                nameof(wagerPoints),
                $"Game '{game.Id}' requires at least {game.Wagering.MinimumWagerPoints} points per spin.");
        }

        if (game.Wagering.MaximumWagerPoints is { } maximum && wagerPoints > maximum)
        {
            throw new ArgumentOutOfRangeException(
                nameof(wagerPoints),
                $"Game '{game.Id}' allows at most {maximum} points per spin.");
        }
        if (!game.Wagering.AllowedWagerPoints.Contains(wagerPoints))
        {
            throw new ArgumentOutOfRangeException(
                nameof(wagerPoints),
                $"{wagerPoints} is not an available wager for game '{game.Id}'.");
        }
    }

    private sealed class PityState
    {
        public object SyncRoot { get; } = new();
        public int ConsecutiveFiveMisses { get; set; }
    }

    private sealed record FeaturePayout(
        int MonkeyPawCount,
        long MoneyGrabPoints,
        long BananaBonusPoints,
        IReadOnlyList<PaylinePayout> Paylines);
}
