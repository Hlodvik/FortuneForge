using FortuneForge.Server.Slots.Models;

namespace FortuneForge.Server.Slots.Bonuses;

internal static class EnergyBonus
{
    public const long MeterCapacityPoints = 100;
    public const decimal PayoutMultiplier = 1.5m;

    public static EnergyBonusSettlement Settle(
        long currentEnergy,
        int energyAwarded,
        SpinPayout payout)
    {
        var startingEnergy = Math.Clamp(currentEnergy, 0, MeterCapacityPoints);
        var meterBeforeReset = Math.Min(
            MeterCapacityPoints,
            checked(startingEnergy + Math.Max(0, energyAwarded)));
        var isMeterFull = meterBeforeReset >= MeterCapacityPoints;
        var appliesMultiplier = isMeterFull && payout.TotalPoints > 0;
        var finalEnergy = appliesMultiplier ? 0 : meterBeforeReset;
        var energyAddedToMeter = Math.Max(0, meterBeforeReset - startingEnergy);
        var settledPayout = appliesMultiplier
            ? MultiplyPayout(payout, PayoutMultiplier)
            : payout;

        return new EnergyBonusSettlement(
            settledPayout,
            finalEnergy,
            meterBeforeReset,
            energyAddedToMeter,
            appliesMultiplier,
            appliesMultiplier ? PayoutMultiplier : 1m);
    }

    private static SpinPayout MultiplyPayout(SpinPayout payout, decimal multiplier)
    {
        var paylines = payout.Paylines
            .Select(payline =>
            {
                var matches = payline.Matches
                    .Select(match => match with
                    {
                        AmountPoints = MultiplyPoints(match.AmountPoints, multiplier)
                    })
                    .ToArray();
                return payline with
                {
                    AmountPoints = matches.Sum(match => match.AmountPoints),
                    Matches = matches
                };
            })
            .ToArray();

        return payout with
        {
            TotalPoints = paylines.Sum(payline => payline.AmountPoints),
            Paylines = paylines
        };
    }

    private static long MultiplyPoints(long points, decimal multiplier) =>
        checked((long)Math.Round(points * multiplier, MidpointRounding.AwayFromZero));
}

internal sealed record EnergyBonusSettlement(
    SpinPayout Payout,
    long FinalEnergyBalance,
    long MeterBalanceBeforeReset,
    long EnergyAddedToMeter,
    bool MultiplierApplied,
    decimal PayoutMultiplier);
