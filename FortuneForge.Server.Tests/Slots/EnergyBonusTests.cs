using FortuneForge.Server.Slots.Bonuses;
using FortuneForge.Server.Slots.Models;
using Xunit;

namespace FortuneForge.Server.Tests.Slots;

public sealed class EnergyBonusTests
{
    [Fact]
    public void Settle_WhenAwardFillsMeterAndSpinWins_AppliesOnePointFiveMultiplierAndResets()
    {
        var settlement = EnergyBonus.Settle(99, 1, Payout(100));

        Assert.True(settlement.MultiplierApplied);
        Assert.Equal(1.5m, settlement.PayoutMultiplier);
        Assert.Equal(150, settlement.Payout.TotalPoints);
        Assert.Equal(0, settlement.FinalEnergyBalance);
        Assert.Equal(100, settlement.MeterBalanceBeforeReset);
        Assert.Equal(1, settlement.EnergyAddedToMeter);
    }

    [Fact]
    public void Settle_WhenAwardFillsMeterButSpinDoesNotWin_HoldsFullMeterForNextWin()
    {
        var settlement = EnergyBonus.Settle(99, 3, Payout(0));

        Assert.False(settlement.MultiplierApplied);
        Assert.Equal(1m, settlement.PayoutMultiplier);
        Assert.Equal(0, settlement.Payout.TotalPoints);
        Assert.Equal(100, settlement.FinalEnergyBalance);
        Assert.Equal(100, settlement.MeterBalanceBeforeReset);
        Assert.Equal(1, settlement.EnergyAddedToMeter);
    }

    [Fact]
    public void Settle_WhenMeterIsAlreadyFullAndSpinWins_AppliesMultiplierAndResets()
    {
        var settlement = EnergyBonus.Settle(100, 0, Payout(200));

        Assert.True(settlement.MultiplierApplied);
        Assert.Equal(300, settlement.Payout.TotalPoints);
        Assert.Equal(0, settlement.FinalEnergyBalance);
        Assert.Equal(100, settlement.MeterBalanceBeforeReset);
        Assert.Equal(0, settlement.EnergyAddedToMeter);
    }

    [Fact]
    public void Settle_WhenMeterIsBelowCapacity_KeepsBasePayoutAndAddsEnergy()
    {
        var settlement = EnergyBonus.Settle(30, 5, Payout(100));

        Assert.False(settlement.MultiplierApplied);
        Assert.Equal(100, settlement.Payout.TotalPoints);
        Assert.Equal(35, settlement.FinalEnergyBalance);
        Assert.Equal(35, settlement.MeterBalanceBeforeReset);
        Assert.Equal(5, settlement.EnergyAddedToMeter);
    }

    private static SpinPayout Payout(long amountPoints)
    {
        var match = new PaidMatch(
            new SymbolMatch(
                1,
                "A",
                3,
                [new GridPosition(0, 0), new GridPosition(1, 0), new GridPosition(2, 0)],
                []),
            1,
            amountPoints);
        var payline = new PaylinePayout(1, amountPoints, [match]);
        return new SpinPayout(amountPoints, [payline]);
    }
}
