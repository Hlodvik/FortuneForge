namespace FortuneForge.Server.Cards.ThreeCardPoker.Table;

internal static class ThreeCardPokerMoney
{
    public const long CentsPerRand = 100;
    public const long MinimumWagerCents = 100;
    public const long MaximumWagerCents = 50_000;
    public const long WagerIncrementCents = 100;

    public static decimal ToRand(long cents) => cents / (decimal)CentsPerRand;

    public static long ToWagerCents(decimal wager)
    {
        var cents = checked(wager * CentsPerRand);
        if (cents != decimal.Truncate(cents))
        {
            throw new ArgumentOutOfRangeException(nameof(wager), "A Three Card Poker wager cannot include a fraction of a cent.");
        }

        var value = checked((long)cents);
        if (value < MinimumWagerCents || value > MaximumWagerCents)
        {
            throw new ArgumentOutOfRangeException(
                nameof(wager),
                $"Choose a Three Card Poker wager from R{ToRand(MinimumWagerCents):0.00} to R{ToRand(MaximumWagerCents):0.00}.");
        }
        if (value % WagerIncrementCents != 0)
        {
            throw new ArgumentOutOfRangeException(
                nameof(wager),
                $"Three Card Poker wagers must use R{ToRand(WagerIncrementCents):0.00} increments.");
        }
        return value;
    }
}
