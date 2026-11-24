using FortuneForge.Games.Slots;
using FortuneForge.Games.Slots.Runtime;

namespace FortuneForge.Games.PharaohsAscent;

public sealed record PharaohsAscentSunfireFunding(int EnergyPoints, long WagerPointTotal);
public sealed record PharaohsAscentSunfireProgress(ClassicSlotEnergySettlement Settlement,
    PharaohsAscentSunfireFunding? FundingAfter, long CompletionWagerPoints);

/// <summary>Prices a blessing from the wagers that funded its meter.</summary>
public static class PharaohsAscentSunfire
{
    private static readonly long MaximumFundedWagerPoints = PharaohsAscentEngine.CreateGame().Wagering.MaximumWagerPoints
        ?? throw new InvalidOperationException("The funded wager limit is missing.");

    public static long AppearanceEnergy(long currentEnergy, long wagerPoints, PharaohsAscentSunfireFunding? funding)
    {
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(wagerPoints);
        if (currentEnergy is < 0 or > ClassicSlotEnergy.MeterCapacityPoints)
            throw new ArgumentOutOfRangeException(nameof(currentEnergy));
        Validate(funding);
        if (funding is null) return currentEnergy;
        if (funding.EnergyPoints != currentEnergy)
            throw new InvalidOperationException("The Sunfire funding does not match its saved meter.");
        return Math.Min(currentEnergy, funding.WagerPointTotal / wagerPoints);
    }

    public static PharaohsAscentSunfireProgress Settle(long currentEnergy, int energyAwarded,
        long wagerPoints, SpinPayout payout, PharaohsAscentSunfireFunding? before)
    {
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(wagerPoints);
        if (currentEnergy is < 0 or > ClassicSlotEnergy.MeterCapacityPoints)
            throw new ArgumentOutOfRangeException(nameof(currentEnergy));
        Validate(before);
        // A previously earned meter retains its original benefit until it resets.
        if (before is null && currentEnergy > 0)
        {
            var legacy = ClassicSlotEnergy.Settle(currentEnergy, energyAwarded, payout);
            return new(legacy, legacy.MultiplierApplied ? new(0, 0) : null, wagerPoints);
        }

        before ??= new(0, 0);
        if (before.EnergyPoints != currentEnergy)
            throw new InvalidOperationException("The Sunfire funding does not match its saved meter.");
        var added = checked((int)Math.Min(ClassicSlotEnergy.MeterCapacityPoints - currentEnergy,
            Math.Max(0, energyAwarded)));
        var energy = checked(before.EnergyPoints + added);
        var total = checked(before.WagerPointTotal + wagerPoints * added);
        var funded = new PharaohsAscentSunfireFunding(energy, total);
        Validate(funded);
        if (energy < ClassicSlotEnergy.MeterCapacityPoints || payout.TotalPoints == 0)
            return new(new(payout, energy, energy, added, false, 1m), funded, wagerPoints);

        var cappedFunding = Math.Min(total, checked(wagerPoints * energy));
        var bonusDenominator = checked(2L * energy * wagerPoints);
        var boostRatio = cappedFunding / (decimal)bonusDenominator;
        var lines = payout.Paylines.Select(line =>
        {
            var matches = line.Matches.Select(match => match with
            {
                AmountPoints = checked(match.AmountPoints + checked(match.AmountPoints * cappedFunding) / bonusDenominator)
            }).ToArray();
            return line with { Matches = matches, AmountPoints = matches.Sum(match => match.AmountPoints) };
        }).ToArray();
        var settled = payout with { Paylines = lines, TotalPoints = lines.Sum(line => line.AmountPoints) };
        var completionWager = total / energy;
        return new(new(settled, 0, energy, added, true, 1m + boostRatio), new(0, 0), completionWager);
    }

    public static void Validate(PharaohsAscentSunfireFunding? funding)
    {
        if (funding is not null && (funding.EnergyPoints < 0 || funding.EnergyPoints > ClassicSlotEnergy.MeterCapacityPoints ||
            funding.WagerPointTotal < funding.EnergyPoints * PharaohsAscentEngine.MinimumWagerPoints ||
            funding.WagerPointTotal > funding.EnergyPoints * MaximumFundedWagerPoints))
            throw new InvalidOperationException("The saved Sunfire funding is invalid.");
    }
}
