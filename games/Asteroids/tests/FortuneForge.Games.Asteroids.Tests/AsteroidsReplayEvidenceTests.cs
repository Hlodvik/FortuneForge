using Xunit;

namespace FortuneForge.Games.Asteroids.Tests;

public sealed class AsteroidsReplayEvidenceTests
{
    [Fact]
    public void CanonicalEvidenceRoundTripsWithoutChangingTheAuthoritativeOutcome()
    {
        var run = new AsteroidsRunIdentity("asteroids_recovery_0001", 0x000000010000002a);
        var replay = new AsteroidsReplay(AsteroidsReplayEvaluator.MaximumReplaySteps,
            [new(0, AsteroidsInput.Thrust | AsteroidsInput.Fire)]);
        var canonical = AsteroidsReplayEvaluator.CanonicalizeReplay(replay);
        var recovered = AsteroidsReplayEvaluator.ParseCanonicalReplay(canonical);
        Assert.Equal(canonical, AsteroidsReplayEvaluator.CanonicalizeReplay(recovered));
        Assert.Equal(AsteroidsReplayEvaluator.Evaluate(run, replay), AsteroidsReplayEvaluator.Evaluate(run, recovered));
    }

    [Theory]
    [InlineData("v1|2|0:8")]
    [InlineData("v2|02|0:8")]
    [InlineData("v2|2|00:8")]
    [InlineData("v2|2|0:16")]
    [InlineData("v2|2|0:6")]
    [InlineData("v2|2|0:0")]
    [InlineData("v2|2|0:8,1:8")]
    [InlineData("v2|2|0:8|ignored")]
    [InlineData("v2|3601|")]
    public void InvalidOrNoncanonicalPersistedEvidenceIsRejected(string canonical) =>
        Assert.ThrowsAny<ArgumentException>(() => AsteroidsReplayEvaluator.ParseCanonicalReplay(canonical));
}
