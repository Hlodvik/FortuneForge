namespace FortuneForge.Games.Slots.Runtime;

public sealed record ClassicSlotPracticeCollection(string TrackId, int Count, long AverageWagerPoints);

public sealed record ClassicSlotPracticeProgress(int FreeSpinsAwarded, string? FeatureMode,
    IReadOnlyList<ClassicSlotPracticeCollection> Collections);

/// <summary>Replays the public practice format, which supplies a rounded average rather than a wager total.</summary>
public static class ClassicSlotPracticeCollections
{
    public static ClassicSlotPracticeProgress Advance(int target, int awardedSpins, string? featureMode,
        bool completesEnergy, IReadOnlyList<ClassicSlotPracticeCollection> before,
        IReadOnlyDictionary<string, int> awarded, long wagerPoints)
    {
        var tracks = ClassicSlotCollections.PracticeTrackIds;
        var states = tracks.ToDictionary(track => track, track => new ClassicSlotPracticeCollection(track, 0, 0),
            StringComparer.Ordinal);
        foreach (var collection in before)
            if (states.ContainsKey(collection.TrackId)) states[collection.TrackId] = collection;

        foreach (var (symbol, count) in awarded)
            if (count > 0 && ClassicSlotCollections.TrackForSymbol(symbol) is { } track)
                states[track] = Add(states[track], count, wagerPoints);

        if (completesEnergy)
        {
            var track = tracks.OrderBy(track => states[track].Count).First();
            states[track] = Add(states[track], Math.Max(1, target - states[track].Count), wagerPoints);
        }

        var freeSpins = 0;
        string? selectedMode = null;
        foreach (var track in tracks)
        {
            var state = states[track];
            if (state.Count < target) continue;

            freeSpins += awardedSpins;
            selectedMode ??= featureMode ?? track;
            states[track] = state with { Count = state.Count % target };
        }

        return new(freeSpins, selectedMode, tracks.Select(track => states[track] with
        {
            Count = Math.Min(states[track].Count, target)
        }).ToArray());
    }

    private static ClassicSlotPracticeCollection Add(ClassicSlotPracticeCollection state, int count, long wagerPoints)
    {
        var nextCount = checked(state.Count + count);
        var total = checked(state.AverageWagerPoints * state.Count + wagerPoints * count);
        return state with
        {
            Count = nextCount,
            AverageWagerPoints = nextCount == 0 ? 0 : (long)Math.Round(total / (decimal)nextCount)
        };
    }
}
