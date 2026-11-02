using System.Numerics;

namespace FortuneForge.Games.Mines;

/// <summary>Integer-cent returns; multiplier displays must never be used for settlement.</summary>
public static class MinesRules
{
    public const int TileCount = 25;
    public const long MinimumWagerCents = 100;
    public const long MaximumWagerCents = 500;
    public const long HistoricalMinimumWagerCents = 10;
    public const long HistoricalMaximumWagerCents = 10_000;
    public const int HouseEdgeBasisPoints = 1_000;
    public const int LegacyHouseEdgeBasisPoints = 100;
    public const int BasisPointsDenominator = 10_000;
    public const int ReturnNumerator = 90;
    public const int ReturnDenominator = 100;
    public const int LegacyPayoutPolicyVersion = 1;
    public const int PayoutPolicyVersion = 2;
    public const int MaximumMultiplier = 50;
    public const long MaximumReturnCents = MaximumWagerCents * MaximumMultiplier;
    public const long HistoricalCurrentMaximumReturnCents = 46_802_700_000;
    public const long LegacyMaximumReturnCents = 51_482_970_000;

    /// <summary>Only accepted, versioned payout schedules may be restored from persistence.</summary>
    public static void ValidateHouseEdge(int houseEdgeBasisPoints)
    {
        if (houseEdgeBasisPoints is not (LegacyHouseEdgeBasisPoints or HouseEdgeBasisPoints))
            throw new ArgumentOutOfRangeException(nameof(houseEdgeBasisPoints), "The round payout schedule is unsupported.");
    }

    public static void ValidatePayoutPolicy(int houseEdgeBasisPoints, int payoutPolicyVersion)
    {
        ValidateHouseEdge(houseEdgeBasisPoints);
        if (payoutPolicyVersion is not (LegacyPayoutPolicyVersion or PayoutPolicyVersion))
            throw new ArgumentOutOfRangeException(nameof(payoutPolicyVersion), "The round payout policy is unsupported.");
        if (payoutPolicyVersion == PayoutPolicyVersion && houseEdgeBasisPoints != HouseEdgeBasisPoints)
            throw new ArgumentOutOfRangeException(nameof(houseEdgeBasisPoints), "The current payout policy requires its accepted 90% schedule.");
    }

    public static void ValidateMineCount(int mineCount)
    {
        if (mineCount is < 1 or >= TileCount)
            throw new ArgumentOutOfRangeException(nameof(mineCount), "Choose between 1 and 24 mines.");
    }

    public static void ValidateWager(long wagerCents)
    {
        if (wagerCents is < MinimumWagerCents or > MaximumWagerCents)
            throw new ArgumentOutOfRangeException(nameof(wagerCents), "New wagers must be 100 through 500 whole cents.");
    }

    /// <summary>Previously accepted rounds retain their original wider wager limits during settlement or recovery.</summary>
    public static void ValidateAcceptedWager(long wagerCents)
    {
        if (wagerCents is < HistoricalMinimumWagerCents or > HistoricalMaximumWagerCents)
            throw new ArgumentOutOfRangeException(nameof(wagerCents), "The accepted wager must be 10 through 10000 whole cents.");
    }

    public static BigInteger Combinations(int n, int k)
    {
        if (n < 0 || k < 0 || k > n) throw new ArgumentOutOfRangeException(nameof(k));
        k = Math.Min(k, n - k);
        var result = BigInteger.One;
        for (var i = 1; i <= k; i++) result = result * (n - k + i) / i;
        return result;
    }

    /// <summary>Exact numerator and denominator of survival probability after distinct safe picks.</summary>
    public static (BigInteger Numerator, BigInteger Denominator) SurvivalProbability(int mineCount, int safeReveals)
    {
        ValidateCurvePosition(mineCount, safeReveals);
        return (Combinations(TileCount - mineCount, safeReveals), Combinations(TileCount, safeReveals));
    }

    /// <summary>Informational decimal only. Before any selection, 1 is the initial stake multiplier.</summary>
    public static decimal Multiplier(int mineCount, int safeReveals, int houseEdgeBasisPoints = HouseEdgeBasisPoints,
        int payoutPolicyVersion = PayoutPolicyVersion)
    {
        var fraction = SafeMultiplier(mineCount, safeReveals, houseEdgeBasisPoints, payoutPolicyVersion);
        return safeReveals == 0 ? 1m : fraction.AsDecimal();
    }

    /// <summary>Gross return includes the wager. Floors the exact rational once, to whole cents.</summary>
    public static long ReturnCents(long wagerCents, int mineCount, int safeReveals, int houseEdgeBasisPoints = HouseEdgeBasisPoints,
        int payoutPolicyVersion = PayoutPolicyVersion)
    {
        ValidatePayoutWager(wagerCents, payoutPolicyVersion);
        var fraction = SafeMultiplier(mineCount, safeReveals, houseEdgeBasisPoints, payoutPolicyVersion);
        if (safeReveals == 0) return 0; // No zero-pick cash-out is offered.
        return fraction.ReturnCents(wagerCents);
    }

    /// <summary>Gross consolation on the next pick hitting a mine after this many prior safe picks.</summary>
    public static decimal MineMultiplier(int mineCount, int safeReveals, int houseEdgeBasisPoints = HouseEdgeBasisPoints,
        int payoutPolicyVersion = PayoutPolicyVersion) =>
        MineMultiplierFraction(mineCount, safeReveals, houseEdgeBasisPoints, payoutPolicyVersion).AsDecimal();

    public static long MineReturnCents(long wagerCents, int mineCount, int safeReveals,
        int houseEdgeBasisPoints = HouseEdgeBasisPoints, int payoutPolicyVersion = PayoutPolicyVersion)
    {
        ValidatePayoutWager(wagerCents, payoutPolicyVersion);
        return MineMultiplierFraction(mineCount, safeReveals, houseEdgeBasisPoints, payoutPolicyVersion).ReturnCents(wagerCents);
    }

    private static void ValidatePayoutWager(long wagerCents, int payoutPolicyVersion)
    {
        if (payoutPolicyVersion == PayoutPolicyVersion) ValidateWager(wagerCents);
        else ValidateAcceptedWager(wagerCents);
    }

    private static Rational SafeMultiplier(int mineCount, int safeReveals, int houseEdgeBasisPoints, int payoutPolicyVersion)
    {
        ValidatePayoutPolicy(houseEdgeBasisPoints, payoutPolicyVersion);
        ValidateCurvePosition(mineCount, safeReveals);
        var raw = UncappedMultiplier(mineCount, safeReveals, houseEdgeBasisPoints);
        if (payoutPolicyVersion == LegacyPayoutPolicyVersion || safeReveals == 0) return raw;
        var safeTotal = TileCount - mineCount;
        var last = UncappedMultiplier(mineCount, safeTotal, houseEdgeBasisPoints);
        if (last.Numerator <= MaximumMultiplier * last.Denominator) return raw;

        var knee = 0;
        var kneeMultiplier = UncappedMultiplier(mineCount, 0, houseEdgeBasisPoints);
        for (var safe = 1; safe <= safeTotal; safe++)
        {
            var candidate = UncappedMultiplier(mineCount, safe, houseEdgeBasisPoints);
            if (candidate.Numerator > MaximumMultiplier * candidate.Denominator) break;
            knee = safe;
            kneeMultiplier = candidate;
        }
        if (safeReveals <= knee) return raw;
        // Linear tail from the last original step <=50x through exactly50x at full-board clear.
        var tailLength = safeTotal - knee;
        return Rational.Create(kneeMultiplier.Numerator * tailLength
            + (MaximumMultiplier * kneeMultiplier.Denominator - kneeMultiplier.Numerator) * (safeReveals - knee),
            kneeMultiplier.Denominator * tailLength);
    }

    private static Rational UncappedMultiplier(int mineCount, int safeReveals, int houseEdgeBasisPoints)
    {
        var (numerator, denominator) = SurvivalProbability(mineCount, safeReveals);
        return Rational.Create((BasisPointsDenominator - houseEdgeBasisPoints) * denominator,
            BasisPointsDenominator * numerator);
    }

    private static Rational MineMultiplierFraction(int mineCount, int safeReveals, int houseEdgeBasisPoints, int payoutPolicyVersion)
    {
        ValidatePayoutPolicy(houseEdgeBasisPoints, payoutPolicyVersion);
        ValidateCurvePosition(mineCount, safeReveals);
        if (safeReveals == TileCount - mineCount) throw new ArgumentOutOfRangeException(nameof(safeReveals), "No next pick remains.");
        if (payoutPolicyVersion == LegacyPayoutPolicyVersion) return Rational.Create(0, 1);
        var current = SafeMultiplier(mineCount, safeReveals, houseEdgeBasisPoints, payoutPolicyVersion);
        var next = SafeMultiplier(mineCount, safeReveals + 1, houseEdgeBasisPoints, payoutPolicyVersion);
        // L_k=(V_k-q_k*V_(k+1))/(1-q_k), q_k=(safeTotal-k)/(25-k).
        // V_0 is internally0.90, although the pre-pick UI displays the initial stake as1x.
        return Rational.Create((TileCount - safeReveals) * current.Numerator * next.Denominator
            - (TileCount - mineCount - safeReveals) * next.Numerator * current.Denominator,
            mineCount * current.Denominator * next.Denominator);
    }

    private readonly record struct Rational(BigInteger Numerator, BigInteger Denominator)
    {
        public static Rational Create(BigInteger numerator, BigInteger denominator)
        {
            if (numerator < 0 || denominator <= 0) throw new InvalidOperationException("The payout policy produced an invalid return.");
            var divisor = BigInteger.GreatestCommonDivisor(numerator, denominator);
            return new(numerator / divisor, denominator / divisor);
        }

        public decimal AsDecimal() => (decimal)Numerator / (decimal)Denominator;
        public long ReturnCents(long wagerCents) => checked((long)(wagerCents * Numerator / Denominator));
    }

    private static void ValidateCurvePosition(int mineCount, int safeReveals)
    {
        ValidateMineCount(mineCount);
        if (safeReveals < 0 || safeReveals > TileCount - mineCount)
            throw new ArgumentOutOfRangeException(nameof(safeReveals));
    }
}
