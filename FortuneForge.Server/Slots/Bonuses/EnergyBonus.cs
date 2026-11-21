using FortuneForge.Games.Slots.Runtime;
using FortuneForge.Server.Slots.Models;

namespace FortuneForge.Server.Slots.Bonuses;

internal static class EnergyBonus
{
    public const long MeterCapacityPoints = ClassicSlotEnergy.MeterCapacityPoints;
    public const decimal PayoutMultiplier = ClassicSlotEnergy.PayoutMultiplier;

    public static EnergyBonusSettlement Settle(long currentEnergy, int energyAwarded, SpinPayout payout)
    {
        var settled = ClassicSlotEnergy.Settle(currentEnergy, energyAwarded, payout);
        return new(settled.Payout, settled.FinalEnergyBalance, settled.MeterBalanceBeforeReset,
            settled.EnergyAddedToMeter, settled.MultiplierApplied, settled.PayoutMultiplier);
    }
}

internal sealed record EnergyBonusSettlement(
    SpinPayout Payout,
    long FinalEnergyBalance,
    long MeterBalanceBeforeReset,
    long EnergyAddedToMeter,
    bool MultiplierApplied,
    decimal PayoutMultiplier);
