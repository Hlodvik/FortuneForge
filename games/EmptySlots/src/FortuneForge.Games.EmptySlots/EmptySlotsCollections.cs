namespace FortuneForge.Games.EmptySlots;

public sealed record EmptySlotsPaidCollectionProgress(
    IReadOnlyDictionary<string, long> Counts,
    IReadOnlyDictionary<string, long> WagerPointTotals,
    int FreeSpinsAwarded,
    string? FeatureMode,
    long FreeSpinWagerPoints,
    bool Changed);

/// <summary>Empty Slots Game's paid collection policy, independent of wallet currency and storage.</summary>
public static class EmptySlotsCollections
{
    public const int CollectionTarget = 22;
    public const int AwardedSpins = 8;
    public const string CollectionFeatureMode = "copy-expand-wild";
    private static readonly string[] Tracks = ["track1", "track2", "track3", "track4"];
    public static IReadOnlyList<string> TrackIds { get; } = Array.AsReadOnly(Tracks);
    private static readonly IReadOnlyDictionary<string, string> TracksBySymbol =
        new Dictionary<string, string>(StringComparer.Ordinal)
        {
            ["S20"] = "track1", ["S21"] = "track2",
            ["S22"] = "track3", ["S23"] = "track4"
        };

    public static EmptySlotsPaidCollectionProgress AdvancePaid(
        IReadOnlyDictionary<string, long> counts,
        IReadOnlyDictionary<string, long> wagerPointTotals,
        IReadOnlyDictionary<string, int> awarded,
        long spinWagerPoints,
        int scatterAwardedSpins)
    {
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(spinWagerPoints);
        ArgumentOutOfRangeException.ThrowIfNegative(scatterAwardedSpins);
        var nextCounts = Tracks.ToDictionary(mode => mode,
            mode => Math.Max(0, counts.GetValueOrDefault(mode)), StringComparer.Ordinal);
        var nextTotals = Tracks.ToDictionary(mode => mode,
            mode => Math.Max(0, wagerPointTotals.GetValueOrDefault(mode)), StringComparer.Ordinal);
        var changed = false;
        foreach (var (symbolId, count) in awarded)
        {
            if (count <= 0 || !TracksBySymbol.TryGetValue(symbolId, out var mode)) continue;
            nextCounts[mode] = checked(nextCounts[mode] + count);
            nextTotals[mode] = checked(nextTotals[mode] + spinWagerPoints * count);
            changed = true;
        }

        var awardedSpins = 0;
        // Scatter spins have no transformation mode.
        string? featureMode = null;
        var freeSpinWager = 0L;
        foreach (var mode in Tracks)
        {
            if (nextCounts[mode] < CollectionTarget) continue;
            var average = checked((long)Math.Round(
                nextTotals[mode] / (decimal)Math.Max(1, nextCounts[mode]),
                MidpointRounding.AwayFromZero));
            awardedSpins = checked(awardedSpins + AwardedSpins);
            featureMode ??= CollectionFeatureMode;
            if (freeSpinWager <= 0) freeSpinWager = average > 0 ? average : spinWagerPoints;
            nextCounts[mode] = Math.Max(0, nextCounts[mode] - CollectionTarget);
            nextTotals[mode] = nextCounts[mode] > 0 ? checked(average * nextCounts[mode]) : 0;
            changed = true;
        }
        return new EmptySlotsPaidCollectionProgress(nextCounts, nextTotals,
            awardedSpins, featureMode, freeSpinWager, changed);
    }
}
