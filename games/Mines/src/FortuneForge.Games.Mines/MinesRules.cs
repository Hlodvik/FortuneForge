using System.Numerics;

namespace FortuneForge.Games.Mines;

/// <summary>Integer-cent returns; multiplier displays must never be used for settlement.</summary>
public static class MinesRules
{
    public const int TileCount = 25;
    public const long MinimumWagerCents = 10;
    public const long MaximumWagerCents = 10_000;
    public const int ReturnNumerator = 99;
    public const int ReturnDenominator = 100;
    public const long MaximumReturnCents = 51_482_970_000;

    public static void ValidateMineCount(int mineCount)
    {
        if (mineCount is < 1 or >= TileCount)
            throw new ArgumentOutOfRangeException(nameof(mineCount), "Choose between 1 and 24 mines.");
    }

    public static void ValidateWager(long wagerCents)
    {
        if (wagerCents is < MinimumWagerCents or > MaximumWagerCents)
            throw new ArgumentOutOfRangeException(nameof(wagerCents), "Wagers must be 10 through 10000 whole cents.");
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
    public static decimal Multiplier(int mineCount, int safeReveals)
    {
        var (numerator, denominator) = SurvivalProbability(mineCount, safeReveals);
        return safeReveals == 0 ? 1m : (decimal)(ReturnNumerator * denominator) / (decimal)(ReturnDenominator * numerator);
    }

    /// <summary>Gross return includes the wager. Floors the exact rational once, to whole cents.</summary>
    public static long ReturnCents(long wagerCents, int mineCount, int safeReveals)
    {
        ValidateWager(wagerCents);
        var (numerator, denominator) = SurvivalProbability(mineCount, safeReveals);
        if (safeReveals == 0) return 0; // No zero-pick cash-out is offered.
        return checked((long)(wagerCents * ReturnNumerator * denominator / (ReturnDenominator * numerator)));
    }

    private static void ValidateCurvePosition(int mineCount, int safeReveals)
    {
        ValidateMineCount(mineCount);
        if (safeReveals < 0 || safeReveals > TileCount - mineCount)
            throw new ArgumentOutOfRangeException(nameof(safeReveals));
    }
}
