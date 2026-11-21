namespace FortuneForge.Games.Keno.Server;

public static class KenoMoney
{
    public const long MinimumWagerCents = 100;
    public const long MaximumWagerCents = 2_000;
    public const long WagerIncrementCents = 100;

    public static long ToWagerCents(decimal wager)
    {
        var cents = checked(wager * 100);
        if (cents != decimal.Truncate(cents))
            throw new ArgumentOutOfRangeException(nameof(wager), "A Keno wager cannot include a fraction of a cent.");
        var value = checked((long)cents);
        if (value < MinimumWagerCents || value > MaximumWagerCents || value % WagerIncrementCents != 0)
            throw new ArgumentOutOfRangeException(nameof(wager), "Choose a whole-rand Keno wager from R1.00 through R20.00.");
        return value;
    }

    public static decimal ToRand(long cents) => cents / 100m;

    public static long PayoutCents(long wagerCents, decimal multiplier)
    {
        var cents = checked(wagerCents * multiplier);
        if (cents != decimal.Truncate(cents))
            throw new InvalidOperationException("A Keno payout cannot include a fraction of a cent.");
        return checked((long)cents);
    }
}
