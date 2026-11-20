using Xunit;

namespace FortuneForge.Games.NeonNights.Tests;

public sealed class NeonPlayPolicyTests
{
    [Fact]
    public void TriggerLocksAllTwelvePaidDiscoPlaysAndLastPlayClearsTheLock()
    {
        var trigger = NeonArcadeBonus.Apply(new(0, []), [["POWER"]]).Evidence;
        var remaining = NeonPlayPolicy.NextDiscoSpins(0, false, trigger);
        var locked = NeonPlayPolicy.NextDiscoWager(0, null, false, 300, trigger);
        Assert.Equal(12, remaining);
        Assert.Equal(300, locked);
        for (var spin = 0; spin < 12; spin++)
        {
            NeonPlayPolicy.ValidateDiscoWager(remaining, locked, 300);
            Assert.True(NeonPlayPolicy.ChargesWager(false, null));
            locked = NeonPlayPolicy.NextDiscoWager(remaining, locked, true, 300, null);
            remaining = NeonPlayPolicy.NextDiscoSpins(remaining, true, null);
            Assert.Equal(remaining > 0 ? 300L : (long?)null, locked);
        }
    }

    [Theory]
    [InlineData(50)]
    [InlineData(350)]
    [InlineData(10000)]
    public void LockedDiscoRejectsHigherAndLowerRequestedStakes(long requested)
    {
        var error = Assert.Throws<NeonDiscoWagerLockedException>(() =>
            NeonPlayPolicy.ValidateDiscoWager(12, 300, requested));
        Assert.Equal(300, error.LockedWagerPoints);
    }

    [Fact]
    public void QueuedDiscoKeepsItsOwnWagerThroughCarPlaysAndRetriggers()
    {
        var trigger = NeonArcadeBonus.Apply(new(0, []), [["POWER"]]).Evidence;
        Assert.Equal(300, NeonPlayPolicy.NextDiscoWager(12, 300, false, 500, null));
        Assert.Equal(300, NeonPlayPolicy.NextDiscoWager(12, 300, false, 500, trigger));
        Assert.Equal(12, NeonPlayPolicy.NextDiscoSpins(12, false, trigger));
        Assert.Equal(500, NeonPlayPolicy.NextDiscoWager(0, null, false, 500, trigger));
    }

    [Fact]
    public void LegacyPendingDiscoAdoptsOnlyItsFirstConsumedDiscoWager()
    {
        Assert.Null(NeonPlayPolicy.NextDiscoWager(12, null, false, 500, null));
        NeonPlayPolicy.ValidateDiscoWager(12, null, 300);
        var adopted = NeonPlayPolicy.NextDiscoWager(12, null, true, 300, null);
        Assert.Equal(300, adopted);
        Assert.Equal(300, NeonPlayPolicy.NextDiscoWager(11, adopted, false, 500, null));
        Assert.Throws<NeonDiscoWagerLockedException>(() =>
            NeonPlayPolicy.ValidateDiscoWager(11, adopted, 500));
    }

    [Fact]
    public void InactiveDiscoDoesNotCarryAnObsoleteWagerAndInvalidActiveLocksAreRejected()
    {
        Assert.Null(NeonPlayPolicy.DiscoLockedWager(0, 300));
        NeonPlayPolicy.ValidateDiscoWager(0, 300, 500);
        Assert.Throws<ArgumentOutOfRangeException>(() => NeonPlayPolicy.DiscoLockedWager(13, 300));
        Assert.Throws<ArgumentOutOfRangeException>(() => NeonPlayPolicy.DiscoLockedWager(1, 0));
    }
}
