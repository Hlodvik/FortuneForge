using FortuneForge.Games.Slots;

namespace FortuneForge.Games.Slots.Runtime;

public sealed record ClassicSlotEnergySettlement(SpinPayout Payout, long FinalEnergyBalance,
    long MeterBalanceBeforeReset, long EnergyAddedToMeter, bool MultiplierApplied, decimal PayoutMultiplier);

public static class ClassicSlotEnergy
{
    public const long MeterCapacityPoints = 100;
    public const decimal PayoutMultiplier = 1.5m;

    public static ClassicSlotEnergySettlement Settle(long currentEnergy, int energyAwarded, SpinPayout payout)
    {
        var startingEnergy = Math.Clamp(currentEnergy, 0, MeterCapacityPoints);
        var meterBeforeReset = Math.Min(MeterCapacityPoints, checked(startingEnergy + Math.Max(0, energyAwarded)));
        var appliesMultiplier = meterBeforeReset == MeterCapacityPoints && payout.TotalPoints > 0;
        return new(appliesMultiplier ? MultiplyPayout(payout) : payout,
            appliesMultiplier ? 0 : meterBeforeReset, meterBeforeReset, Math.Max(0, meterBeforeReset - startingEnergy),
            appliesMultiplier, appliesMultiplier ? PayoutMultiplier : 1m);
    }

    private static SpinPayout MultiplyPayout(SpinPayout payout)
    {
        var paylines = payout.Paylines.Select(payline =>
        {
            var matches = payline.Matches.Select(match => match with
            {
                AmountPoints = checked((long)Math.Round(match.AmountPoints * PayoutMultiplier, MidpointRounding.AwayFromZero))
            }).ToArray();
            return payline with { AmountPoints = matches.Sum(match => match.AmountPoints), Matches = matches };
        }).ToArray();
        return payout with { TotalPoints = paylines.Sum(payline => payline.AmountPoints), Paylines = paylines };
    }
}
