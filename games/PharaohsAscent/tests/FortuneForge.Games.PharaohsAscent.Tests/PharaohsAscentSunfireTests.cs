using FortuneForge.Games.Slots;
using Xunit;

namespace FortuneForge.Games.PharaohsAscent.Tests;

public sealed class PharaohsAscentSunfireTests
{
    [Fact]
    public void RaisingTheBetCannotRevalueStoredSealAppearanceBenefits()
    {
        var funding = new PharaohsAscentSunfireFunding(100, 200);
        Assert.Equal(100, PharaohsAscentSunfire.AppearanceEnergy(100, 2, funding));
        Assert.Equal(16, PharaohsAscentSunfire.AppearanceEnergy(100, 12, funding));
        Assert.Equal(0, PharaohsAscentSunfire.AppearanceEnergy(100, 300, funding));
        Assert.Equal(100, PharaohsAscentSunfire.AppearanceEnergy(100, 300, null));
    }

    [Fact]
    public void RaisingTheBetCannotRevalueAFullLowStakeMeter()
    {
        var progress = PharaohsAscentSunfire.Settle(100, 0, 300, Payout(1_200), new(100, 200));
        Assert.Equal(1_204, progress.Settlement.Payout.TotalPoints);
        Assert.Equal(2, progress.CompletionWagerPoints);
        Assert.Equal(new(0, 0), progress.FundingAfter);
        Assert.True(progress.Settlement.MultiplierApplied);
    }

    [Fact]
    public void OnlyEnergyActuallyAddedToTheMeterReceivesTheCurrentStakeFunding()
    {
        var progress = PharaohsAscentSunfire.Settle(99, 4, 300, Payout(1_200), new(99, 198));
        Assert.Equal(1, progress.Settlement.EnergyAddedToMeter);
        Assert.Equal(1_209, progress.Settlement.Payout.TotalPoints);
        Assert.Equal(4, progress.CompletionWagerPoints);
    }

    [Fact]
    public void LossesRetainAFullMeterAndItsFundedValue()
    {
        var funding = new PharaohsAscentSunfireFunding(100, 200);
        var progress = PharaohsAscentSunfire.Settle(100, 4, 300, new(0, []), funding);
        Assert.Equal(funding, progress.FundingAfter);
        Assert.Equal(100, progress.Settlement.FinalEnergyBalance);
        Assert.Equal(0, progress.Settlement.EnergyAddedToMeter);
        Assert.False(progress.Settlement.MultiplierApplied);
    }

    [Fact]
    public void ANewMeterAccumulatesItsActualWagerFunding()
    {
        var first = PharaohsAscentSunfire.Settle(0, 3, 2, new(0, []), null);
        var second = PharaohsAscentSunfire.Settle(3, 2, 300, new(0, []), first.FundingAfter);
        Assert.Equal(new(5, 606), second.FundingAfter);
        Assert.Equal(5, second.Settlement.FinalEnergyBalance);
    }

    [Fact]
    public void EveryLegalFlatStakeRetainsTheExactSameWholeMultiplierBlessing()
    {
        for (long wager = 2; wager <= 300; wager += 2)
            foreach (var multiplier in new long[] { 1, 2, 3, 4, 5, 7, 8, 11, 18 })
            {
                var progress = PharaohsAscentSunfire.Settle(100, 0, wager,
                    Payout(wager * multiplier), new(100, 100 * wager));
                Assert.Equal(wager * multiplier * 3 / 2, progress.Settlement.Payout.TotalPoints);
                Assert.Equal(wager, progress.CompletionWagerPoints);
            }
    }

    [Fact]
    public void FractionalExtraAwardsNeverRoundAnOddFundedStakeUpward()
    {
        var progress = PharaohsAscentSunfire.Settle(100, 0, 3, Payout(3), new(100, 300));
        Assert.Equal(4, progress.Settlement.Payout.TotalPoints);
    }

    [Fact]
    public void ExactFundedFractionsDoNotLoseAWholePointToIntermediateDecimalRounding()
    {
        var progress = PharaohsAscentSunfire.Settle(100, 0, 6, Payout(6), new(100, 200));
        Assert.Equal(7, progress.Settlement.Payout.TotalPoints);
    }

    [Fact]
    public void AnExistingLegacyMeterKeepsItsAlreadyEarnedBenefitUntilItResets()
    {
        var continued = PharaohsAscentSunfire.Settle(40, 1, 300, new(0, []), null);
        Assert.Null(continued.FundingAfter);
        Assert.Equal(41, continued.Settlement.FinalEnergyBalance);
        var completed = PharaohsAscentSunfire.Settle(100, 0, 300, Payout(1_200), null);
        Assert.Equal(1_800, completed.Settlement.Payout.TotalPoints);
        Assert.Equal(300, completed.CompletionWagerPoints);
        Assert.Equal(new(0, 0), completed.FundingAfter);
    }

    [Fact]
    public void ContradictorySavedMeterFundingIsRejected()
    {
        Assert.Throws<InvalidOperationException>(() => PharaohsAscentSunfire.Settle(99, 1, 2, Payout(2), new(98, 196)));
        Assert.Throws<InvalidOperationException>(() => PharaohsAscentSunfire.Validate(new(0, 2)));
        Assert.Throws<InvalidOperationException>(() => PharaohsAscentSunfire.Validate(new(100, 100)));
        Assert.Throws<InvalidOperationException>(() => PharaohsAscentSunfire.Validate(new(100, 200_001)));
    }

    private static SpinPayout Payout(long amount) => new(amount,
        [new PaylinePayout(1, amount, [new PaidMatch(new SymbolMatch(1, "2", 3,
            [new(0, 0), new(1, 0), new(2, 0)], []), 1, amount)])]);
}
