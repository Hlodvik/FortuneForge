using FortuneForge.Server.Slots.Models;

namespace FortuneForge.Server.Slots.Bonuses;

internal static class GoldFoundryCrucible
{
    public const string LegacyRuleVersion = "gold-foundry-crucible-v1";
    public const string RuleVersion = "gold-foundry-crucible-v2";
    // One charge per visible coal on the existing strips. At the base reel
    // frequency, collecting 100 charges normally takes hundreds of spins.
    public const long Capacity = EnergyBonus.MeterCapacityPoints;
    public const long MaximumDemoWageredCents = 1_000_000_000_000;
    private const decimal PayoutRate = 0.15m;

    public static bool IsGame(string gameId) => gameId == SlotSpecialRoundProfiles.DinoDominionGameId;

    public static bool IsSupportedRuleVersion(string? version) =>
        version is LegacyRuleVersion or RuleVersion;

    public static long PourValueCents(long wageredCents) =>
        checked((long)decimal.Floor(wageredCents * PayoutRate));

    public static GoldFoundryCrucibleSettlement Settle(
        long energyBefore, int energyAwarded, long wageredBeforeCents, long chargedWagerCents,
        string ruleVersion = RuleVersion)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(wageredBeforeCents);
        ArgumentOutOfRangeException.ThrowIfNegative(chargedWagerCents);
        var startingEnergy = Math.Clamp(energyBefore, 0, Capacity);
        var fullEnergy = Math.Min(Capacity, startingEnergy + Math.Max(0, energyAwarded));
        var qualifyingWagerCents = ruleVersion switch
        {
            LegacyRuleVersion => chargedWagerCents,
            RuleVersion => energyAwarded > 0 ? chargedWagerCents : 0,
            _ => throw new ArgumentException("Unknown Gold Foundry crucible rules.", nameof(ruleVersion))
        };
        var wageredCents = checked(wageredBeforeCents + qualifyingWagerCents);
        var poured = fullEnergy == Capacity && wageredCents > 0;
        return new(
            poured ? 0 : fullEnergy, fullEnergy, fullEnergy - startingEnergy,
            poured ? 0 : wageredCents, poured, poured ? PourValueCents(wageredCents) : 0);
    }
}

internal sealed record GoldFoundryCrucibleSettlement(
    long EnergyBalance,
    long MeterBalanceBeforeReset,
    long EnergyAddedToMeter,
    long WageredCents,
    bool Poured,
    long PayoutCents)
{
    public long PourValueCents => GoldFoundryCrucible.PourValueCents(WageredCents);

    public EnergyBonusSettlement AsEnergySettlement(SpinPayout payout) =>
        new(payout, EnergyBalance, MeterBalanceBeforeReset, EnergyAddedToMeter, false, 1m);
}
