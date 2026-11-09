using System.Collections.Concurrent;
using FortuneForge.Games.EmptySlots.Bonuses;
using FortuneForge.Games.EmptySlots.Configuration;
using FortuneForge.Games.EmptySlots.Evaluation;
using FortuneForge.Games.EmptySlots.Models;
using FortuneForge.Games.EmptySlots.Payouts;
using FortuneForge.Games.EmptySlots.Reels;

namespace FortuneForge.Games.EmptySlots.Spins;

internal sealed partial class SpinService
{
    private static IReadOnlyList<GridPosition> VisiblePositions(
        IReadOnlyList<IReadOnlyList<string>> reels) =>
        reels
            .SelectMany((reel, reelIndex) => reel.Select((_, rowIndex) =>
                new GridPosition(reelIndex, rowIndex)))
            .ToArray();

    private static Dictionary<string, int> CountCollectionSymbols(
        IReadOnlyList<IReadOnlyList<string>> reels)
    {
        var counts = CollectionSymbolIds.ToDictionary(symbolId => symbolId, _ => 0, StringComparer.Ordinal);
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

    private static decimal ValueMultiplier(string symbolId) => symbolId switch
    {
        "S13" => 0.5m,
        "S14" => 1m,
        "S15" => 1.5m,
        "S16" => 2m,
        "S17" => 3m,
        "S18" => 4m,
        "S19" => 5m,
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

    private static int CountMeterSymbols(
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
        var allowedWagers = game.Wagering.AllowedWagerPoints;
        var matchesIncrement = game.Wagering.WagerIncrementPoints is { } increment &&
            (wagerPoints - game.Wagering.MinimumWagerPoints) % increment == 0;
        if (allowedWagers.Count > 0
                ? !allowedWagers.Contains(wagerPoints)
                : !matchesIncrement)
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
        int CollectorCount,
        long CollectorPayoutPoints,
        long PatternPayoutPoints,
        IReadOnlyList<PaylinePayout> Paylines,
        long OffPaylinePoints = 0);
}
