using FortuneForge.Server.Accounts.Models;
using FortuneForge.Games.GoldFoundry;
using FortuneForge.Server.Slots.Bonuses;
using FortuneForge.Server.Slots.Models;

namespace FortuneForge.Server.Slots.Games.GoldFoundry;

internal static class GoldFoundryCrucible
{
    public const string RuleVersion = GoldFoundryCrucibleRules.RuleVersion;
    public const long Capacity = GoldFoundryCrucibleRules.Capacity;

    public static void ValidateDemoWager(long wageredCents)
    {
        GoldFoundryCrucibleRules.ValidateDemoWager(wageredCents);
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
            audit.EngineVersion != FortuneForge.Games.GoldFoundry.GoldFoundryEngine.Version ||
            audit.CrucibleRuleVersion != RuleVersion ||
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

    public static long PourValueCents(long wageredCents) => GoldFoundryCrucibleRules.PourValueCents(wageredCents);

    public static GoldFoundryCrucibleSettlement Settle(
        long energyBefore, int energyAwarded, long wageredBeforeCents, long chargedWagerCents)
    {
        var settlement = GoldFoundryCrucibleRules.Settle(
            energyBefore, energyAwarded, wageredBeforeCents, chargedWagerCents);
        return new(settlement.EnergyBalance, settlement.MeterBalanceBeforeReset,
            settlement.EnergyAddedToMeter, settlement.WageredCents, settlement.Poured,
            settlement.PayoutCents);
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
