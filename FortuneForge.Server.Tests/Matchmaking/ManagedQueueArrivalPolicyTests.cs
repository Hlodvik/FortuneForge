using FortuneForge.Server.Matchmaking;
using Xunit;

namespace FortuneForge.Server.Tests.Matchmaking;

public sealed class ManagedQueueArrivalPolicyTests
{
    [Fact]
    public void NextGap_IsStableAndWithinStaggeredArrivalRange()
    {
        var gaps = Enumerable.Range(1, 8)
            .Select(ordinal => ManagedQueueArrivalPolicy.NextGap(
                "solitaire", "queue-42", ordinal))
            .ToArray();

        Assert.All(gaps, gap => Assert.InRange(
            gap, TimeSpan.FromMilliseconds(2_200), TimeSpan.FromMilliseconds(3_400)));
        Assert.Equal(gaps, Enumerable.Range(1, 8)
            .Select(ordinal => ManagedQueueArrivalPolicy.NextGap(
                "solitaire", "queue-42", ordinal)));
        Assert.Equal(TimeSpan.FromMilliseconds(2_309), gaps[0]);
        Assert.True(gaps.Distinct().Count() > 1);
    }

    [Fact]
    public void NextGap_ChangesWithQueueIdentity()
    {
        var firstQueue = Enumerable.Range(1, 5)
            .Select(ordinal => ManagedQueueArrivalPolicy.NextGap(
                "solitaire", "queue-42", ordinal))
            .ToArray();
        var secondQueue = Enumerable.Range(1, 5)
            .Select(ordinal => ManagedQueueArrivalPolicy.NextGap(
                "solitaire", "queue-43", ordinal))
            .ToArray();

        Assert.False(firstQueue.SequenceEqual(secondQueue));
    }

    [Fact]
    public void FinalPause_IsStableAndAllowsTheLastArrivalToSettle()
    {
        var pause = ManagedQueueArrivalPolicy.FinalPause("solitaire", "queue-42");

        Assert.InRange(pause, TimeSpan.FromMilliseconds(2_000),
            TimeSpan.FromMilliseconds(2_600));
        Assert.Equal(pause, ManagedQueueArrivalPolicy.FinalPause(
            "solitaire", "queue-42"));
    }

    [Theory]
    [InlineData("", "queue-42")]
    [InlineData("solitaire", " ")]
    public void RejectsMissingQueueIdentity(string gameId, string cohortId)
    {
        Assert.Throws<ArgumentException>(() =>
            ManagedQueueArrivalPolicy.NextGap(gameId, cohortId, 1));
        Assert.Throws<ArgumentException>(() =>
            ManagedQueueArrivalPolicy.FinalPause(gameId, cohortId));
    }

    [Fact]
    public void NextGap_RequiresOneBasedArrivalOrdinal()
    {
        Assert.Throws<ArgumentOutOfRangeException>(() =>
            ManagedQueueArrivalPolicy.NextGap("solitaire", "queue-42", 0));
    }
}
