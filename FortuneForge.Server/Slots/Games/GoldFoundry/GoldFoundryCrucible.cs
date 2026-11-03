using FortuneForge.Server.Accounts.Models;
using FortuneForge.Server.Slots.Bonuses;
using FortuneForge.Server.Slots.Models;

namespace FortuneForge.Server.Slots.Games.GoldFoundry;

internal static class GoldFoundryCrucible
{
    public const string RuleVersion = "gold-foundry-crucible-v2";
    // One charge per visible coal on the existing strips. At the base reel
    // frequency, collecting 100 charges normally takes hundreds of spins.
    public const long Capacity = EnergyBonus.MeterCapacityPoints;
    private const long MaximumDemoWageredCents = 1_000_000_000_000;
    private const decimal PayoutRate = 0.15m;

    public static void ValidateDemoWager(long wageredCents)
    {
        if (wageredCents is < 0 or > MaximumDemoWageredCents)
            throw new ArgumentOutOfRangeException(nameof(wageredCents), "Invalid demo crucible wager total.");
    }

    public static SlotStateResponse CreateState(long wageredCents, long energyBalance) =>
        new(0, null, 0, energyBalance, [], null)
        {
            CrucibleWageredCents = wageredCents,
            CruciblePourValueCents = PourValueCents(wageredCents)
        };

    public static void ValidatePaidOutcome(SpinResult result, SlotSpinAdmission admission)
    {
        var audit = result.EngineAudit;
        if (audit is null || !GoldFoundrySlotDefinitions.IsModel(audit.BaseGame) ||
            audit.EngineVersion != GoldFoundryReelCollector.EngineVersion || audit.CrucibleRuleVersion != RuleVersion ||
            admission.IsFreeSpin || result.IsFreeSpin || audit.IsDerivedFreeSpin ||
            audit.FreeSpinFeatureMode is not null || result.FreeSpinsAwarded != 0 || result.SealsAwarded.Count != 0)
            throw new InvalidOperationException("The Gold Foundry outcome differs from its accepted paid spin.");
    }

    public static SpinResult SettleDemo(SpinResult result, long currentEnergy, long wageredCents)
    {
        var settlement = Settle(currentEnergy, result.EnergyAwarded, wageredCents,
            RandMoney.PointsToCents(result.WagerPoints, result.PointValueInCents));
        return result with
        {
            EnergyBalance = settlement.EnergyBalance,
            CrucibleWageredCents = settlement.WageredCents,
            CruciblePourValueCents = settlement.PourValueCents,
            CruciblePoured = settlement.Poured,
            CruciblePayoutCents = settlement.PayoutCents
        };
    }

    public static long PourValueCents(long wageredCents) =>
        checked((long)decimal.Floor(wageredCents * PayoutRate));

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
