using FortuneForge.Server.Arcade.Asteroids;
using Xunit;

namespace FortuneForge.Server.Tests.Arcade.Asteroids;

public sealed class AsteroidsReplayEvaluatorTests
{
    [Fact]
    public void SameSeedAndCanonicalTransitionsProduceTheSameFullEngineResult()
    {
        var replay = new AsteroidsReplay(120, [
            new AsteroidsInputCommand(0, AsteroidsInput.TurnRight | AsteroidsInput.Thrust | AsteroidsInput.Fire),
            new AsteroidsInputCommand(45, AsteroidsInput.TurnLeft | AsteroidsInput.Thrust | AsteroidsInput.Fire),
            new AsteroidsInputCommand(90, AsteroidsInput.None),
        ]);

        var first = AsteroidsReplayEvaluator.Evaluate(Run(), replay);
        var second = AsteroidsReplayEvaluator.Evaluate(Run(), replay);

        Assert.Equal(first, second);
        Assert.Equal(120, first.StepsElapsed);
    }

    [Fact]
    public void SeedFoldUsesBothWordsAndPassesZeroToThePackageNormalizer()
    {
        Assert.Equal(42u, AsteroidsReplayEvaluator.FoldSeed(42));
        Assert.Equal(43u, AsteroidsReplayEvaluator.FoldSeed(0x000000010000002a));
        Assert.Equal(0u, AsteroidsReplayEvaluator.FoldSeed(0x0000002a0000002a));

        var zeroFold = AsteroidsReplayEvaluator.Evaluate(Run(0x0000002a0000002a), new AsteroidsReplay(1, []));
        Assert.Equal(AsteroidsTerminalState.Active, zeroFold.Terminal);
    }

    [Fact]
    public void FullEngineProducesVariedAuthoritativeScoresAndStateEvolution()
    {
        var result = AsteroidsReplayEvaluator.Evaluate(Run(), new AsteroidsReplay(AsteroidsReplayEvaluator.MaximumReplaySteps, [
            new AsteroidsInputCommand(0, AsteroidsInput.Fire),
        ]));

        Assert.Equal(225, result.Score);
        Assert.Equal(AsteroidsTerminalState.GameOver, result.Terminal);
    }

    [Fact]
    public void ShortReplayThatIsStillPlayingRemainsActive()
    {
        var result = AsteroidsReplayEvaluator.Evaluate(Run(), new AsteroidsReplay(1, []));

        Assert.Equal(AsteroidsTerminalState.Active, result.Terminal);
        Assert.Equal(1, result.StepsElapsed);
    }

    [Fact]
    public void UpperSeedBitsInfluenceTheFullEngineReplayOutcome()
    {
        var replay = new AsteroidsReplay(AsteroidsReplayEvaluator.MaximumReplaySteps, [
            new AsteroidsInputCommand(0, AsteroidsInput.Fire),
        ]);

        var lowWordOnly = AsteroidsReplayEvaluator.Evaluate(Run(42), replay);
        var changedUpperWord = AsteroidsReplayEvaluator.Evaluate(Run(0x000000010000002a), replay);

        Assert.NotEqual(
            (lowWordOnly.Score, lowWordOnly.StepsElapsed, lowWordOnly.Terminal),
            (changedUpperWord.Score, changedUpperWord.StepsElapsed, changedUpperWord.Terminal));
    }

    [Fact]
    public void GameOverIsTerminalAndCommandsAfterItAreInvalid()
    {
        var result = AsteroidsReplayEvaluator.Evaluate(Run(), new AsteroidsReplay(AsteroidsReplayEvaluator.MaximumReplaySteps, []));

        Assert.Equal(AsteroidsTerminalState.GameOver, result.Terminal);
        Assert.Throws<ArgumentException>(() => AsteroidsReplayEvaluator.Evaluate(Run(), new AsteroidsReplay(AsteroidsReplayEvaluator.MaximumReplaySteps, [
            new AsteroidsInputCommand(AsteroidsReplayEvaluator.MaximumReplaySteps - 1, AsteroidsInput.Fire),
        ])));
    }

    [Fact]
    public void SurvivingTheExactTimeCapCompletesTheRun()
    {
        var result = AsteroidsReplayEvaluator.Evaluate(Run(), new AsteroidsReplay(AsteroidsReplayEvaluator.MaximumReplaySteps, [
            new AsteroidsInputCommand(0, AsteroidsInput.TurnRight | AsteroidsInput.Thrust),
        ]));

        Assert.Equal(AsteroidsTerminalState.Completed, result.Terminal);
        Assert.Equal(AsteroidsReplayEvaluator.MaximumReplaySteps, result.StepsElapsed);
    }

    [Fact]
    public void PreservesCanonicalValidationAndReplayText()
    {
        var replay = new AsteroidsReplay(3, [new AsteroidsInputCommand(1, AsteroidsInput.Fire)]);
        Assert.Equal("v1|3|1:8", AsteroidsReplayEvaluator.CanonicalizeReplay(replay));
        Assert.Throws<ArgumentException>(() => AsteroidsReplayEvaluator.ValidateReplayInput(new AsteroidsReplay(3, [
            new AsteroidsInputCommand(0, AsteroidsInput.Thrust),
            new AsteroidsInputCommand(1, AsteroidsInput.Thrust),
        ])));
        Assert.Throws<ArgumentException>(() => AsteroidsReplayEvaluator.ValidateReplayInput(new AsteroidsReplay(2, [
            new AsteroidsInputCommand(0, AsteroidsInput.None),
        ])));
        Assert.ThrowsAny<ArgumentException>(() => AsteroidsReplayEvaluator.Evaluate(Run(), new AsteroidsReplay(1, [
            new AsteroidsInputCommand(0, (AsteroidsInput)16),
        ])));
    }

    private static AsteroidsRunIdentity Run(ulong seed = 42) => new("asteroids_run_0001", seed);
}
