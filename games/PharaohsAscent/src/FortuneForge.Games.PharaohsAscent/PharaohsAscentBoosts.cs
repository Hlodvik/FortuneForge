namespace FortuneForge.Games.PharaohsAscent;

public sealed record PharaohsAscentBoostFunding(int LegacyPoints, IReadOnlyDictionary<long, int> PointsByWager);

/// <summary>New boost credits stay with their funded wager; historical credits are redeemed once.</summary>
public static class PharaohsAscentBoosts
{
    private static readonly long MaximumFundedWagerPoints = PharaohsAscentEngine.CreateGame().Wagering.MaximumWagerPoints
        ?? throw new InvalidOperationException("The funded wager limit is missing.");
    private static readonly int ActivationCost = PharaohsAscentEngine.CreateGame().SpecialPoints!.ActivationCost;

    public static int BeforeSpin(SealedTombState state, long wagerPoints, int aggregatePoints)
    {
        ValidateWager(wagerPoints);
        var funding = Existing(state, aggregatePoints);
        return checked(funding.LegacyPoints + funding.PointsByWager.GetValueOrDefault(wagerPoints));
    }

    public static PharaohsAscentBoostFunding AfterSpin(SealedTombState state, long wagerPoints,
        int aggregatePoints, bool boostApplied, int pointsAwarded)
    {
        ValidateWager(wagerPoints);
        ArgumentOutOfRangeException.ThrowIfNegative(pointsAwarded);
        var funding = Existing(state, aggregatePoints);
        var legacy = funding.LegacyPoints;
        var pools = funding.PointsByWager.ToDictionary(pair => pair.Key, pair => pair.Value);
        var available = checked(legacy + pools.GetValueOrDefault(wagerPoints));
        if (boostApplied)
        {
            if (available < ActivationCost)
                throw new InvalidOperationException("The wager has insufficient funded boost credits.");
            var legacySpent = Math.Min(legacy, ActivationCost);
            legacy -= legacySpent;
            var poolAfter = pools.GetValueOrDefault(wagerPoints) - (ActivationCost - legacySpent);
            if (poolAfter == 0) pools.Remove(wagerPoints);
            else pools[wagerPoints] = poolAfter;
        }
        if (pointsAwarded > 0)
            pools[wagerPoints] = checked(pools.GetValueOrDefault(wagerPoints) + pointsAwarded);
        var after = new PharaohsAscentBoostFunding(legacy, pools);
        Validate(after);
        return after;
    }

    public static int Total(PharaohsAscentBoostFunding funding)
    {
        Validate(funding);
        return checked(funding.LegacyPoints + (int)funding.PointsByWager.Sum(pair => (long)pair.Value));
    }

    public static void Validate(PharaohsAscentBoostFunding? funding)
    {
        if (funding is null) return;
        if (funding.LegacyPoints < 0 || funding.PointsByWager is null ||
            funding.PointsByWager.Count > MaximumFundedWagerPoints ||
            funding.PointsByWager.Any(pair => pair.Key < PharaohsAscentEngine.MinimumWagerPoints ||
                pair.Key > MaximumFundedWagerPoints || pair.Value <= 0) ||
            funding.LegacyPoints + funding.PointsByWager.Sum(pair => (long)pair.Value) > int.MaxValue)
            throw new InvalidOperationException("The saved wager-specific boost credits are invalid.");
    }

    private static PharaohsAscentBoostFunding Existing(SealedTombState state, int aggregatePoints)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(aggregatePoints);
        if (state.BoostFunding is not { } funding)
            return new(aggregatePoints, new Dictionary<long, int>());
        if (Total(funding) != aggregatePoints)
            throw new InvalidOperationException("The funded boost credits disagree with their aggregate balance.");
        return funding;
    }

    private static void ValidateWager(long wagerPoints)
    {
        if (wagerPoints < PharaohsAscentEngine.MinimumWagerPoints || wagerPoints > MaximumFundedWagerPoints)
            throw new ArgumentOutOfRangeException(nameof(wagerPoints));
    }
}
