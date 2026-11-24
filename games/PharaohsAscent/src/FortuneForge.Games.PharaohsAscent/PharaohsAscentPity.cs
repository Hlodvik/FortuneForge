namespace FortuneForge.Games.PharaohsAscent;

/// <summary>A miss streak can only guarantee a match at the wager that earned it.</summary>
public static class PharaohsAscentPity
{
    private static readonly long MaximumFundedWagerPoints = PharaohsAscentEngine.CreateGame().Wagering.MaximumWagerPoints
        ?? throw new InvalidOperationException("The funded wager limit is missing.");

    public static int BeforeSpin(SealedTombState state, long wagerPoints, int legacyMisses)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(legacyMisses);
        ValidateWager(wagerPoints);
        Validate(state.PityByWager);
        return state.PityByWager is null ? legacyMisses : state.PityByWager.GetValueOrDefault(wagerPoints);
    }

    public static IReadOnlyDictionary<long, int> AfterSpin(SealedTombState state, long wagerPoints, int missesAfter)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(missesAfter);
        ValidateWager(wagerPoints);
        Validate(state.PityByWager);
        var counters = state.PityByWager?.ToDictionary(pair => pair.Key, pair => pair.Value) ?? [];
        if (missesAfter == 0) counters.Remove(wagerPoints);
        else counters[wagerPoints] = missesAfter;
        return counters;
    }

    public static void Validate(IReadOnlyDictionary<long, int>? counters)
    {
        if (counters is null) return;
        if (counters.Count > MaximumFundedWagerPoints ||
            counters.Any(pair => pair.Key < PharaohsAscentEngine.MinimumWagerPoints ||
                pair.Key > MaximumFundedWagerPoints || pair.Value < 0))
            throw new InvalidOperationException("The saved wager-specific match counters are invalid.");
    }

    private static void ValidateWager(long wagerPoints)
    {
        if (wagerPoints < PharaohsAscentEngine.MinimumWagerPoints || wagerPoints > MaximumFundedWagerPoints)
            throw new ArgumentOutOfRangeException(nameof(wagerPoints));
    }
}
