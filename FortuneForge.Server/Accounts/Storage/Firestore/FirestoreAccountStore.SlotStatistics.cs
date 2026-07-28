using System.Security.Cryptography;
using System.Text;
using FortuneForge.Server.Accounts.Models;
using FortuneForge.Server.Slots.Bonuses;
using FortuneForge.Server.Slots.Models;
using Google.Cloud.Firestore;

namespace FortuneForge.Server.Accounts.Storage;

public sealed partial class FirestoreAccountStore
{
    private static SlotStatistics ToSlotStatistics(DocumentSnapshot snapshot) => new(
        ReadLong(snapshot, "spinsPlayed"),
        ReadLong(snapshot, "wins"),
        ReadLong(snapshot, "losses"),
        ReadLong(snapshot, "creditsWagered"),
        ReadLong(snapshot, "creditsWon"),
        ReadLong(snapshot, "netCredits"));

    private static SlotStatistics EmptySlotStatistics() => new(0, 0, 0, 0, 0, 0);

    private static SealCollectionSettlement SettleSealCollections(
        DocumentSnapshot guardSnapshot,
        SpinResult result,
        bool energyCompleted)
    {
        var counts = ReadLongMap(guardSnapshot, "sealCounts");
        var wagerTotals = ReadLongMap(guardSnapshot, "sealWagerTotals");
        var changed = false;

        foreach (var awarded in result.SealsAwarded)
        {
            if (!SealFeatureModesBySymbolId.TryGetValue(awarded.Key, out var featureMode) ||
                awarded.Value <= 0)
            {
                continue;
            }

            counts[featureMode] = checked(counts[featureMode] + awarded.Value);
            wagerTotals[featureMode] = checked(wagerTotals[featureMode] + result.WagerPoints * awarded.Value);
            changed = true;
        }

        if (energyCompleted)
        {
            var nearestMode = SealFeatureModes
                .OrderByDescending(mode => Math.Min(counts[mode], SealCompletionTarget - 1))
                .ThenBy(mode => Array.IndexOf(SealFeatureModes, mode))
                .First();
            var missing = Math.Max(1, SealCompletionTarget - counts[nearestMode]);
            counts[nearestMode] = checked(counts[nearestMode] + missing);
            wagerTotals[nearestMode] = checked(wagerTotals[nearestMode] + result.WagerPoints * missing);
            changed = true;
        }

        var freeSpinsAwarded = 0;
        string? freeSpinFeatureMode = null;
        var freeSpinWagerPoints = 0L;

        foreach (var mode in SealFeatureModes)
        {
            if (counts[mode] < SealCompletionTarget)
            {
                continue;
            }

            var completedCount = Math.Max(1, counts[mode]);
            var averageWager = DivideRounded(wagerTotals[mode], completedCount);
            freeSpinsAwarded = checked(freeSpinsAwarded + SealCompletionFreeSpins);
            freeSpinFeatureMode ??= mode;
            if (freeSpinWagerPoints <= 0)
            {
                freeSpinWagerPoints = averageWager > 0 ? averageWager : result.WagerPoints;
            }

            var overflow = counts[mode] - SealCompletionTarget;
            counts[mode] = Math.Max(0, overflow);
            wagerTotals[mode] = counts[mode] > 0
                ? checked(averageWager * counts[mode])
                : 0;
            changed = true;
        }

        return new SealCollectionSettlement(
            counts,
            wagerTotals,
            freeSpinsAwarded,
            freeSpinFeatureMode,
            freeSpinWagerPoints,
            CreateSealCollections(counts, wagerTotals),
            changed);
    }

    private static IReadOnlyList<SlotSealCollection> CreateSealCollections(
        IReadOnlyDictionary<string, long> counts,
        IReadOnlyDictionary<string, long> wagerTotals) =>
        SealFeatureModes
            .Select(mode =>
            {
                var count = Math.Min(
                    SealCompletionTarget,
                    Math.Max(0, counts.TryGetValue(mode, out var rawCount) ? rawCount : 0));
                var wagerTotal = Math.Max(0, wagerTotals.TryGetValue(mode, out var rawTotal) ? rawTotal : 0);
                var averageWager = count <= 0 ? 0 : DivideRounded(wagerTotal, count);
                return new SlotSealCollection(
                    mode,
                    checked((int)count),
                    averageWager,
                    SealCompletionTarget);
            })
            .ToArray();

    private static long DivideRounded(long total, long count) =>
        count <= 0 ? 0 : checked((long)Math.Round(total / (decimal)count, MidpointRounding.AwayFromZero));

    private sealed record SealCollectionSettlement(
        IReadOnlyDictionary<string, long> SealCounts,
        IReadOnlyDictionary<string, long> SealWagerTotals,
        int FreeSpinsAwarded,
        string? FreeSpinFeatureMode,
        long FreeSpinWagerPoints,
        IReadOnlyList<SlotSealCollection> Collections,
        bool SealsChanged);
}
