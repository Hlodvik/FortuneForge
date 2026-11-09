using FortuneForge.Games.EmptySlots.Models;

namespace FortuneForge.Games.EmptySlots.Bonuses;

public sealed record SlotSpecialRoundProfile(
    string GameId, int ScatterRequiredSymbols, int ScatterAwardedSpins, int CollectionTarget,
    int CollectionAwardedSpins, bool UsesCollections = true, bool UsesEnergy = true,
    bool UsesDirectValueTokens = true, string? ScatterFeatureMode = null,
    string? CollectionFeatureMode = null, string? BaseReelSetId = null,
    decimal? TargetHitRate = null, IReadOnlyList<int>? PaylinePatternIds = null,
    decimal PayoutMultiplier = 1m);

public sealed record SlotSpecialRoundProgress(int FreeSpinsAwarded, string? FeatureMode,
    IReadOnlyList<SlotCollectionProgress> Collections);

public static class EmptySlotsRules
{
    public const string EmptySlotsGameId = "empty-slots-v1";
    private static readonly string[] FeatureParts = ["copy", "expand", "wild"];
    private static readonly string[] Tracks = ["track1", "track2", "track3", "track4"];
    private static readonly IReadOnlyDictionary<string, string> TracksBySymbol =
        new Dictionary<string, string>(StringComparer.Ordinal)
        {
            ["S20"] = "track1", ["S21"] = "track2",
            ["S22"] = "track3", ["S23"] = "track4"
        };
    public static SlotSpecialRoundProfile EmptySlots { get; } = new(
        EmptySlotsGameId, 3, 6, 22, 8, true, false, false, null, "copy-expand-wild",
        TargetHitRate: 0.32m, PayoutMultiplier: 0.866m);

    public static bool TryGet(string gameId, out SlotSpecialRoundProfile profile)
    {
        profile = EmptySlots;
        return gameId == EmptySlotsGameId;
    }

    public static bool IsFeatureMode(string? value)
    {
        if (string.IsNullOrWhiteSpace(value)) return false;
        var modes = value.Split('-', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        return modes.Length > 0 && modes.All(mode => FeatureParts.Contains(mode, StringComparer.Ordinal));
    }

    public static bool HasFeatureMode(string? value, string mode) =>
        IsFeatureMode(value) && value!.Split('-', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Contains(mode, StringComparer.Ordinal);

    // Keep demo collection arithmetic frozen separately from paid settlement:
    // demo averages historically use ToEven rounding and modulo carry.
    public static SlotSpecialRoundProgress SettleDemo(SlotSpecialRoundProfile profile,
        IReadOnlyList<SlotCollectionProgress> current, IReadOnlyDictionary<string, int> awarded,
        long wagerPoints, bool energyCompleted, Guid spinId = default)
    {
        var states = Tracks.ToDictionary(mode => mode, _ => new RoundState(), StringComparer.Ordinal);
        foreach (var collection in current.Where(collection => states.ContainsKey(collection.TrackId)))
            states[collection.TrackId] = new RoundState(collection.Count, collection.AverageWagerPoints);
        foreach (var (symbolId, count) in awarded)
        {
            if (count <= 0 || !TracksBySymbol.TryGetValue(symbolId, out var mode)) continue;
            states[mode] = states[mode].Add(count, wagerPoints);
        }
        var freeSpins = 0;
        string? featureMode = null;
        foreach (var mode in Tracks)
        {
            var state = states[mode];
            if (state.Count < profile.CollectionTarget) continue;
            freeSpins += profile.CollectionAwardedSpins;
            featureMode ??= profile.CollectionFeatureMode ?? mode;
            states[mode] = state with { Count = state.Count % profile.CollectionTarget };
        }
        return new SlotSpecialRoundProgress(freeSpins, featureMode,
            Tracks.Select(mode => states[mode].ToCollection(mode, profile.CollectionTarget)).ToArray());
    }

    private sealed record RoundState(int Count = 0, long AverageWagerPoints = 0)
    {
        public RoundState Add(int count, long wagerPoints)
        {
            var nextCount = checked(Count + count);
            var total = checked(AverageWagerPoints * Count + wagerPoints * count);
            return new RoundState(nextCount,
                nextCount == 0 ? 0 : (long)Math.Round(total / (decimal)nextCount));
        }
        public SlotCollectionProgress ToCollection(string mode, int target) =>
            new(mode, Math.Min(Count, target), AverageWagerPoints, target);
    }
}
