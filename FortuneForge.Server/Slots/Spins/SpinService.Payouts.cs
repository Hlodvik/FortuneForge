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
    private static FeaturePayout CalculateFeaturePayout(
        IReadOnlyList<IReadOnlyList<string>> reels,
        long wagerPoints)
    {
        var moneyPositions = VisiblePositions(reels)
            .Select(position => new
            {
                Position = position,
                Multiplier = RandMultiplier(reels[position.Reel][position.Row])
            })
            .Where(symbol => symbol.Multiplier > 0)
            .ToArray();
        var pawPositions = VisiblePositions(reels)
            .Where(position => string.Equals(
                reels[position.Reel][position.Row],
                MonkeyPawSymbolId,
                StringComparison.Ordinal))
            .ToArray();
        var moneyGrabPoints = 0L;
        PaylinePayout? moneyGrabPayout = null;
        if (pawPositions.Length > 0 && moneyPositions.Length > 0)
        {
            var multiplier = moneyPositions.Sum(symbol => symbol.Multiplier);
            if (pawPositions.Length >= 2)
            {
                multiplier *= 2;
            }

            moneyGrabPoints = MultiplyWager(wagerPoints, multiplier);
            moneyGrabPayout = new PaylinePayout(
                901,
                moneyGrabPoints,
                [
                    new PaidMatch(
                        new SymbolMatch(
                            901,
                            MonkeyPawSymbolId,
                            pawPositions.Length + moneyPositions.Length,
                            pawPositions.Concat(moneyPositions.Select(symbol => symbol.Position)).ToArray(),
                            []),
                        checked((long)Math.Ceiling(multiplier)),
                        moneyGrabPoints)
                ]);
        }

        var bananaPayouts = CalculateBananaPayouts(reels, wagerPoints);
        return new FeaturePayout(
            pawPositions.Length,
            moneyGrabPoints,
            bananaPayouts.Sum(payout => payout.AmountPoints),
            moneyGrabPayout is null ? bananaPayouts : [moneyGrabPayout, .. bananaPayouts]);
    }

    private static IReadOnlyList<PaylinePayout> CalculateBananaPayouts(
        IReadOnlyList<IReadOnlyList<string>> reels,
        long wagerPoints)
    {
        var payouts = new List<PaylinePayout>();
        var seen = new HashSet<string>(StringComparer.Ordinal);
        var rows = reels.Max(reel => reel.Count);
        var paylineId = 801;

        void AddPattern(IReadOnlyList<GridPosition> positions)
        {
            if (positions.Any(position => !IsBananaAt(reels, position)))
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

            var amount = checked(wagerPoints * 3);
            var currentPaylineId = paylineId++;
            payouts.Add(new PaylinePayout(
                currentPaylineId,
                amount,
                [
                    new PaidMatch(
                        new SymbolMatch(currentPaylineId, BananaSymbolId, 3, positions.ToArray(), []),
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

    private static bool IsBananaAt(IReadOnlyList<IReadOnlyList<string>> reels, GridPosition position) =>
        position.Reel >= 0 &&
        position.Reel < reels.Count &&
        position.Row >= 0 &&
        position.Row < reels[position.Reel].Count &&
        string.Equals(reels[position.Reel][position.Row], BananaSymbolId, StringComparison.Ordinal);

    private static SpinPayout AddFeaturePayout(SpinPayout payout, FeaturePayout featurePayout)
    {
        if (featurePayout.Paylines.Count == 0)
        {
            return payout;
        }

        var paylines = payout.Paylines.Concat(featurePayout.Paylines).ToArray();
        return payout with
        {
            TotalPoints = paylines.Sum(payline => payline.AmountPoints),
            Paylines = paylines
        };
    }
}
