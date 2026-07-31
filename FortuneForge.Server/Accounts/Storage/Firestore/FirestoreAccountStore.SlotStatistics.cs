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
        ReadDecimal(snapshot, "creditsWagered"),
        ReadDecimal(snapshot, "creditsWon"),
        ReadDecimal(snapshot, "netCredits"));

    private static SlotStatistics EmptySlotStatistics() => new(0, 0, 0, 0, 0, 0);

    private static SealCollectionSettlement SettleSealCollections(
        DocumentSnapshot guardSnapshot,
        SpinResult result,
        bool energyCompleted)
    {
        var counts = ReadLongMap(guardSnapshot, "sealCounts");
        var wagerCents = ReadSealWagerCents(guardSnapshot);
        if (!string.Equals(result.GameId, LegacyWukongGameId, StringComparison.Ordinal))
        {
            return new SealCollectionSettlement(
                counts,
                wagerCents,
                0,
                null,
                0,
                [],
                false);
        }

        var spinWagerCents = RandMoney.PointsToCents(
            result.WagerPoints,
            result.PointValueInCents);
        var changed = false;

        foreach (var awarded in result.SealsAwarded)
        {
            if (!SealFeatureModesBySymbolId.TryGetValue(awarded.Key, out var featureMode) ||
                awarded.Value <= 0)
            {
                continue;
            }

            counts[featureMode] = checked(counts[featureMode] + awarded.Value);
            wagerCents[featureMode] = checked(
                wagerCents[featureMode] + spinWagerCents * awarded.Value);
            changed = true;
        }

        if (energyCompleted)
        {
            var nearestMode = SealFeatureModes
                .OrderByDescending(mode => Math.Min(counts[mode], SealCollectionRules.CompletionTarget - 1))
                .ThenBy(mode => Array.IndexOf(SealFeatureModes, mode))
                .First();
            var missing = Math.Max(1, SealCollectionRules.CompletionTarget - counts[nearestMode]);
            counts[nearestMode] = checked(counts[nearestMode] + missing);
            wagerCents[nearestMode] = checked(
                wagerCents[nearestMode] + spinWagerCents * missing);
            changed = true;
        }

        var freeSpinsAwarded = 0;
        string? freeSpinFeatureMode = null;
        var freeSpinWagerPoints = 0L;

        foreach (var mode in SealFeatureModes)
        {
            if (counts[mode] < SealCollectionRules.CompletionTarget)
            {
                continue;
            }

            var completedCount = Math.Max(1, counts[mode]);
            var averageWagerPoints = DivideRounded(
                RandMoney.CentsToPoints(wagerCents[mode], result.PointValueInCents),
                completedCount);
            freeSpinsAwarded = checked(freeSpinsAwarded + SealCompletionFreeSpins);
            freeSpinFeatureMode ??= mode;
            if (freeSpinWagerPoints <= 0)
            {
                freeSpinWagerPoints = averageWagerPoints > 0
                    ? averageWagerPoints
                    : result.WagerPoints;
            }

            var overflow = counts[mode] - SealCollectionRules.CompletionTarget;
            counts[mode] = Math.Max(0, overflow);
            wagerCents[mode] = counts[mode] > 0
                ? checked(
                    RandMoney.PointsToCents(averageWagerPoints, result.PointValueInCents) *
                    counts[mode])
                : 0;
            changed = true;
        }

        return new SealCollectionSettlement(
            counts,
            wagerCents,
            freeSpinsAwarded,
            freeSpinFeatureMode,
            freeSpinWagerPoints,
            CreateSealCollections(counts, wagerCents, result.PointValueInCents),
            changed);
    }

    private static IReadOnlyList<SlotSealCollection> CreateSealCollections(
        IReadOnlyDictionary<string, long> counts,
        IReadOnlyDictionary<string, long> wagerCents,
        decimal pointValueInCents) =>
        SealFeatureModes
            .Select(mode =>
            {
                var count = Math.Min(
                    SealCollectionRules.CompletionTarget,
                    Math.Max(0, counts.TryGetValue(mode, out var rawCount) ? rawCount : 0));
                var wagerTotalCents = Math.Max(
                    0,
                    wagerCents.TryGetValue(mode, out var rawTotal) ? rawTotal : 0);
                var averageWager = count <= 0
                    ? 0
                    : DivideRounded(
                        RandMoney.CentsToPoints(wagerTotalCents, pointValueInCents),
                        count);
                return new SlotSealCollection(
                    mode,
                    checked((int)count),
                    averageWager,
                    SealCollectionRules.CompletionTarget);
            })
            .ToArray();

    private static IReadOnlyList<SlotSealCollection> CreateSealCollections(
        DocumentSnapshot guardSnapshot,
        decimal pointValueInCents) =>
        CreateSealCollections(
            ReadLongMap(guardSnapshot, "sealCounts"),
            ReadSealWagerCents(guardSnapshot),
            pointValueInCents);

    private static Dictionary<string, long> ReadSealWagerCents(DocumentSnapshot snapshot)
    {
        if (snapshot.Exists && snapshot.ToDictionary().ContainsKey("sealWagerCents"))
        {
            return ReadLongMap(snapshot, "sealWagerCents");
        }

        return ReadLongMap(snapshot, "sealWagerTotals").ToDictionary(
            pair => pair.Key,
            pair => checked(pair.Value * RandMoney.CentsPerRand),
            StringComparer.Ordinal);
    }

    private static long DivideRounded(long total, long count) =>
        count <= 0 ? 0 : checked((long)Math.Round(total / (decimal)count, MidpointRounding.AwayFromZero));

    private sealed record SealCollectionSettlement(
        IReadOnlyDictionary<string, long> SealCounts,
        IReadOnlyDictionary<string, long> SealWagerCents,
        int FreeSpinsAwarded,
        string? FreeSpinFeatureMode,
        long FreeSpinWagerPoints,
        IReadOnlyList<SlotSealCollection> Collections,
        bool SealsChanged);
}
