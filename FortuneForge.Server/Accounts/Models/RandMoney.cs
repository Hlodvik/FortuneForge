namespace FortuneForge.Server.Accounts.Models;

internal static class RandMoney
{
    public const long CentsPerRand = 100;

    public static long PointsToCents(long points, decimal pointValueInCents)
    {
        var cents = checked(points * pointValueInCents);
        if (cents != decimal.Truncate(cents))
        {
            throw new InvalidOperationException("A slot wager or payout resolved to a fraction of a cent.");
        }
        return checked((long)cents);
    }

    public static decimal CentsToRand(long cents) => cents / (decimal)CentsPerRand;

    public static long CentsToPoints(long cents, decimal pointValueInCents)
    {
        var points = cents / pointValueInCents;
        if (points != decimal.Truncate(points))
        {
            throw new InvalidOperationException("A stored slot wager does not align with the game's point value.");
        }
        return checked((long)points);
    }

    public static long CombineCents(long wholeRand, long fractionalCents) =>
        checked(wholeRand * CentsPerRand + Math.Clamp(fractionalCents, 0, CentsPerRand - 1));
}
