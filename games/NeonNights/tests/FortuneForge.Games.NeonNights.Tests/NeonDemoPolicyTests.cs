using Xunit;

namespace FortuneForge.Games.NeonNights.Tests;

public sealed class NeonDemoPolicyTests
{
    [Fact]
    public void DemoCarUsesItsLockedWagerWhileQueuedDiscoAndServerPityArePreserved()
    {
        var request = new NeonDemoPlayRequest(100, true, 1, 100, NeonArcadeBonus.PaidCarFeatureMode,
            2, 50, 3, 100);
        var policy = new NeonDemoPolicy(request, 100);
        Assert.True(policy.ChargesWager);
        Assert.True(policy.Engine.IsFreeSpin);
        Assert.False(policy.Engine.IsDiscoPlay);
        Assert.Equal(NeonArcadeBonus.PaidCarFeatureMode, policy.Engine.FeatureMode);
        Assert.Equal(NeonDiscoCascade.InitialMultiplier, policy.Engine.DiscoMultiplier);
        Assert.Null(policy.Engine.PityMissesBefore);
        Assert.Equal(0, policy.Engine.EnergyBalance);

        var progress = policy.Complete(100, 0, null, null);
        Assert.Equal(0, progress.FreeSpinsRemaining);
        Assert.Null(progress.FreeSpinWagerPoints);
        Assert.Null(progress.FreeSpinFeatureMode);
        Assert.Equal(2, progress.NeonDiscoSpinsRemaining);
        Assert.Equal(50, progress.NeonDiscoWagerPoints);
        Assert.Equal(3, progress.NeonDiscoMultiplier);
        Assert.Equal(0, progress.EnergyBalance);
        Assert.False(progress.EnergyMultiplierApplied);
        Assert.Equal(1m, progress.PayoutMultiplier);
    }

    [Fact]
    public void DemoAdmissionKeepsAwardedAndDiscoWagerValidation()
    {
        var request = new NeonDemoPlayRequest(100, false, 0, null, null, 2, 50, 3, 0);
        Assert.Throws<NeonDiscoWagerLockedException>(() => new NeonDemoPolicy(request, 100));
        var policy = new NeonDemoPolicy(request with { WagerPoints = 50 }, 100);
        Assert.True(policy.Engine.IsDiscoPlay);
        Assert.Equal(3, policy.Engine.DiscoMultiplier);
        Assert.Equal(1, policy.Complete(50, 0, null, null).NeonDiscoSpinsRemaining);
        Assert.Throws<ArgumentException>(() => new NeonDemoPolicy(request with { FreeSpinsRemaining = 1 }, 100));
        Assert.Throws<ArgumentException>(() => new NeonDemoPolicy(request with
        {
            UseFreeSpin = true,
            FreeSpinsRemaining = 1,
            FreeSpinWagerPoints = 50
        }, 100));
        Assert.Throws<ArgumentOutOfRangeException>(() => new NeonDemoPolicy(request with
        {
            NeonDiscoSpinsRemaining = 0,
            EnergyBalance = 101
        }, 100));
    }
}
