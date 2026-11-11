namespace FortuneForge.Games.GoldFoundry;

/// <summary>Pure crucible progression. Persistence and wallet settlement remain host responsibilities.</summary>
public static class GoldFoundryCrucibleRules
{
    public const string RuleVersion = "gold-foundry-crucible-v2";
    public const long Capacity = 100;
    public const long MaximumDemoWageredCents = 1_000_000_000_000;
    public const decimal PayoutRate = 0.15m;

    public static void ValidateDemoWager(long wageredCents)
    {
        if (wageredCents is < 0 or > MaximumDemoWageredCents)
            throw new ArgumentOutOfRangeException(nameof(wageredCents), "Invalid demo crucible wager total.");
    }

    public static long PourValueCents(long wageredCents)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(wageredCents);
        return checked((long)decimal.Floor(wageredCents * PayoutRate));
    }

    public static GoldFoundryCrucibleSettlement Settle(
        long energyBefore, int energyAwarded, long wageredBeforeCents, long chargedWagerCents)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(wageredBeforeCents);
        ArgumentOutOfRangeException.ThrowIfNegative(chargedWagerCents);
        var startingEnergy = Math.Clamp(energyBefore, 0, Capacity);
        var fullEnergy = Math.Min(Capacity, startingEnergy + Math.Max(0, energyAwarded));
        var qualifyingWagerCents = energyAwarded > 0 ? chargedWagerCents : 0;
        var wageredCents = checked(wageredBeforeCents + qualifyingWagerCents);
        var poured = fullEnergy == Capacity && wageredCents > 0;
        return new(
            poured ? 0 : fullEnergy, fullEnergy, fullEnergy - startingEnergy,
            poured ? 0 : wageredCents, poured, poured ? PourValueCents(wageredCents) : 0);
    }
}
