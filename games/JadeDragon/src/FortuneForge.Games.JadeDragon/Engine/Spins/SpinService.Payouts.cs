using System.Collections.Concurrent;
using FortuneForge.Games.JadeDragon.Bonuses;
using FortuneForge.Games.JadeDragon.Configuration;
using FortuneForge.Games.JadeDragon.Evaluation;
using FortuneForge.Games.JadeDragon.Models;
using FortuneForge.Games.JadeDragon.Payouts;
using FortuneForge.Games.JadeDragon.Reels;

namespace FortuneForge.Games.JadeDragon.Spins;

internal sealed partial class SpinService
{
    private static FeaturePayout CalculateFeaturePayout(
        IReadOnlyList<IReadOnlyList<string>> reels,
        long wagerPoints,
        decimal payoutMultiplier,
        bool collectWithEveryCollector = false)
    {
        var valuePositions = VisiblePositions(reels)
            .Select(position => new
            {
                Position = position,
                Multiplier = ValueMultiplier(reels[position.Reel][position.Row])
            })
            .Where(symbol => symbol.Multiplier > 0)
            .ToArray();
        var collectorPositions = VisiblePositions(reels)
            .Where(position => string.Equals(
                reels[position.Reel][position.Row],
                CollectorSymbolId,
                StringComparison.Ordinal))
            .ToArray();
        var collectorPayoutPoints = 0L;
        PaylinePayout? collectorPayout = null;
        if (collectorPositions.Length > 0 && valuePositions.Length > 0)
        {
            var multiplier = valuePositions.Sum(symbol => symbol.Multiplier);
            multiplier *= collectWithEveryCollector ? collectorPositions.Length : Math.Min(collectorPositions.Length, 2);

            var collectedPoints = MultiplyWager(wagerPoints, multiplier);
            // The explicit preview rule collects the displayed wager-linked cash
            // once per collector. Historical outcomes retain their original game scale.
            collectorPayoutPoints = collectWithEveryCollector
                ? collectedPoints
                : ApplyPayoutMultiplier(collectedPoints, payoutMultiplier);
            collectorPayout = new PaylinePayout(
                901,
                collectorPayoutPoints,
                [
                    new PaidMatch(
                        new SymbolMatch(
                            901,
                            CollectorSymbolId,
                            collectorPositions.Length + valuePositions.Length,
                            collectorPositions.Concat(valuePositions.Select(symbol => symbol.Position)).ToArray(),
                            []),
                        checked((long)Math.Ceiling(multiplier)),
                        collectorPayoutPoints)
                ]);
        }

        var patternPayouts = CalculatePatternPayouts(reels, wagerPoints, payoutMultiplier);
        return new FeaturePayout(
            collectorPositions.Length,
            collectorPayoutPoints,
            patternPayouts.Sum(payout => payout.AmountPoints),
            collectorPayout is null ? patternPayouts : [collectorPayout, .. patternPayouts]);
    }

    private static IReadOnlyList<PaylinePayout> CalculatePatternPayouts(
        IReadOnlyList<IReadOnlyList<string>> reels,
        long wagerPoints,
        decimal payoutMultiplier)
    {
        var payouts = new List<PaylinePayout>();
        var seen = new HashSet<string>(StringComparer.Ordinal);
        var rows = reels.Max(reel => reel.Count);
        var paylineId = 801;

        void AddPattern(IReadOnlyList<GridPosition> positions)
        {
            if (positions.Any(position => !IsPatternAt(reels, position)))
            {
                return;
            }

            var key = string.Join('|', positions
                .OrderBy(position => position.Reel)
                .ThenBy(position => position.Row)
                .Select(position => $"{position.Reel}.{position.Row}"));
            if (!seen.Add(key))
            {
                return;
            }

            var amount = ApplyPayoutMultiplier(checked(wagerPoints * 3), payoutMultiplier);
            var currentPaylineId = paylineId++;
            payouts.Add(new PaylinePayout(
                currentPaylineId,
                amount,
                [
                    new PaidMatch(
                        new SymbolMatch(currentPaylineId, PatternSymbolId, 3, positions.ToArray(), []),
                        3,
                        amount)
                ]));
        }

        for (var reel = 0; reel < reels.Count; reel++)
        {
            for (var row = 0; row <= reels[reel].Count - 3; row++)
            {
                AddPattern([
                    new GridPosition(reel, row),
                    new GridPosition(reel, row + 1),
                    new GridPosition(reel, row + 2)
                ]);
            }
        }

        for (var row = 0; row < rows; row++)
        {
            for (var reel = 0; reel <= reels.Count - 3; reel++)
            {
                AddPattern([
                    new GridPosition(reel, row),
                    new GridPosition(reel + 1, row),
                    new GridPosition(reel + 2, row)
                ]);
            }
        }

        for (var reel = 0; reel <= reels.Count - 3; reel++)
        {
            for (var row = 0; row <= rows - 3; row++)
            {
                AddPattern([
                    new GridPosition(reel, row),
                    new GridPosition(reel + 1, row + 1),
                    new GridPosition(reel + 2, row + 2)
                ]);
            }

            for (var row = 2; row < rows; row++)
            {
                AddPattern([
                    new GridPosition(reel, row),
                    new GridPosition(reel + 1, row - 1),
                    new GridPosition(reel + 2, row - 2)
                ]);
            }
        }

        return payouts;
    }

    private static long ApplyPayoutMultiplier(long amount, decimal payoutMultiplier) =>
        checked((long)Math.Round(amount * payoutMultiplier, MidpointRounding.AwayFromZero));

    private static bool IsPatternAt(IReadOnlyList<IReadOnlyList<string>> reels, GridPosition position) =>
        position.Reel >= 0 &&
        position.Reel < reels.Count &&
        position.Row >= 0 &&
        position.Row < reels[position.Reel].Count &&
        string.Equals(reels[position.Reel][position.Row], PatternSymbolId, StringComparison.Ordinal);

    private static SpinPayout AddFeaturePayout(SpinPayout payout, FeaturePayout featurePayout)
    {
        if (featurePayout.Paylines.Count == 0 && featurePayout.OffPaylinePoints == 0)
        {
            return payout;
        }

        var paylines = payout.Paylines.Concat(featurePayout.Paylines).ToArray();
        return payout with
        {
            TotalPoints = checked(paylines.Sum(payline => payline.AmountPoints) + featurePayout.OffPaylinePoints),
            Paylines = paylines
        };
    }
}
