using Xunit;

namespace FortuneForge.Games.PharaohsAscent.Tests;

public sealed class PharaohsAscentPityTests
{
    [Fact]
    public void LowBetMissesDoNotGuaranteeAHighBetWin()
    {
        var state = SealedTombState.Empty with { PityByWager = new Dictionary<long, int> { [2] = 12 } };
        Assert.Equal(0, PharaohsAscentPity.BeforeSpin(state, 300, 12));
        Assert.Equal(12, PharaohsAscentPity.BeforeSpin(state, 2, 0));
    }

    [Fact]
    public void EachWagerKeepsItsStreakWhenOtherWagersArePlayed()
    {
        var state = SealedTombState.Empty with { PityByWager = new Dictionary<long, int> { [2] = 12, [300] = 4 } };
        var after = state with { PityByWager = PharaohsAscentPity.AfterSpin(state, 300, 0) };
        Assert.Equal(12, PharaohsAscentPity.BeforeSpin(after, 2, 0));
        Assert.Equal(0, PharaohsAscentPity.BeforeSpin(after, 300, 0));
        Assert.Equal(4, state.PityByWager![300]);
    }

    [Fact]
    public void LegacyStreakIsPreservedOnceThenPinnedToItsPlayedWager()
    {
        var first = SealedTombState.Empty;
        Assert.Equal(12, PharaohsAscentPity.BeforeSpin(first, 12, 12));
        var after = first with { PityByWager = PharaohsAscentPity.AfterSpin(first, 12, 0) };
        Assert.Equal(0, PharaohsAscentPity.BeforeSpin(after, 300, 12));
    }

    [Fact]
    public void OddHistoricalFundedWagersRetainTheirOwnCounters()
    {
        var state = SealedTombState.Empty with { PityByWager = new Dictionary<long, int> { [1_717] = 12 } };
        Assert.Equal(12, PharaohsAscentPity.BeforeSpin(state, 1_717, 0));
        Assert.Equal(0, PharaohsAscentPity.BeforeSpin(state, 300, 0));
    }

    [Fact]
    public void InvalidFundingKeysAndNegativeStreaksAreRejected()
    {
        Assert.Throws<InvalidOperationException>(() => PharaohsAscentPity.Validate(new Dictionary<long, int> { [1] = 12 }));
        Assert.Throws<InvalidOperationException>(() => PharaohsAscentPity.Validate(new Dictionary<long, int> { [2_001] = 12 }));
        Assert.Throws<InvalidOperationException>(() => PharaohsAscentPity.Validate(new Dictionary<long, int> { [2] = -1 }));
    }
}
