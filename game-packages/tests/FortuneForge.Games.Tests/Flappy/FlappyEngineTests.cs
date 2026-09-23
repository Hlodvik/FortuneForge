using System.Collections.Immutable;
using System.Text.Json;
using FortuneForge.Games.Flappy;

namespace FortuneForge.Games.Tests.Flappy;

public sealed class FlappyEngineTests
{
    [Fact]
    public void EqualSeedAndInputReplayProducesIdenticalSnapshots()
    {
        var inputs = Enumerable.Range(0, 240)
            .Select(tick => tick % 16 == 0 ? FlappyInput.Flap : FlappyInput.None)
            .ToArray();

        var first = Replay(42, inputs);
        var second = Replay(42, inputs);
        var differentSeed = FlappyEngine.Start(43);

        Assert.Equal(JsonSerializer.Serialize(first), JsonSerializer.Serialize(second));
        Assert.NotEqual(
            FlappyEngine.Start(42).Obstacles[0].GapTop,
            differentSeed.Obstacles[0].GapTop);
        Assert.True(first.Tick > 0);
    }

    [Fact]
    public void FlapImmediatelyMovesBirdUpwardWithNegativeVelocity()
    {
        var state = FlappyEngine.Start(7);

        var transition = FlappyEngine.Step(state, FlappyInput.Flap);

        Assert.Equal(FlappyEventType.Flapped, transition.Event);
        Assert.True(transition.State.BirdVelocity < 0);
        Assert.True(transition.State.BirdY < state.BirdY);
        Assert.Equal(
            FlappyEngine.FlapVelocity + FlappyEngine.GravityPerTick,
            transition.State.BirdVelocity,
            precision: 10);
    }

    [Fact]
    public void PassingObstacleScoresAndAdvancesProgressionLevel()
    {
        var state = FlappyEngine.Start(7) with
        {
            Obstacles = ImmutableArray.Create(new FlappyObstacle(
                1,
                FlappyEngine.BirdX - FlappyEngine.ObstacleWidth + 1,
                FlappyEngine.ObstacleWidth,
                100,
                500,
                Scored: false)),
            NextObstacleId = 2,
            Score = 4,
            BestScore = 4,
        };

        var transition = FlappyEngine.Step(state, FlappyInput.None);

        Assert.Equal(FlappyEventType.Scored, transition.Event);
        Assert.Equal(1, transition.ScoreGained);
        Assert.Equal(5, transition.State.Score);
        Assert.Equal(5, transition.State.BestScore);
        Assert.Equal(2, transition.State.Level);
        Assert.True(transition.State.Obstacles[0].Scored);
    }

    [Fact]
    public void ObstacleGroundAndCeilingContactsHaveDistinctTerminalStates()
    {
        var obstacleState = FlappyEngine.Start(11) with
        {
            Obstacles = ImmutableArray.Create(new FlappyObstacle(
                1,
                FlappyEngine.BirdX,
                FlappyEngine.ObstacleWidth,
                25,
                100,
                Scored: false)),
            NextObstacleId = 2,
        };
        var groundState = FlappyEngine.Start(11) with
        {
            BirdY = FlappyEngine.DefaultHeight - FlappyEngine.BirdRadius - 1,
            BirdVelocity = 3,
            Obstacles = ImmutableArray<FlappyObstacle>.Empty,
            NextObstacleId = 1,
        };
        var ceilingState = FlappyEngine.Start(11) with
        {
            BirdY = FlappyEngine.BirdRadius + 1,
            BirdVelocity = -3,
            Obstacles = ImmutableArray<FlappyObstacle>.Empty,
            NextObstacleId = 1,
        };

        var obstacle = FlappyEngine.Step(obstacleState, FlappyInput.None);
        var ground = FlappyEngine.Step(groundState, FlappyInput.None);
        var ceiling = FlappyEngine.Step(ceilingState, FlappyInput.None);

        Assert.Equal(FlappyPhase.ObstacleCollision, obstacle.State.Phase);
        Assert.Equal(FlappyEventType.ObstacleCollision, obstacle.Event);
        Assert.Equal(FlappyPhase.GroundCollision, ground.State.Phase);
        Assert.Equal(FlappyEventType.GroundCollision, ground.Event);
        Assert.Equal(FlappyPhase.CeilingCollision, ceiling.State.Phase);
        Assert.Equal(FlappyEventType.CeilingCollision, ceiling.Event);
    }

    [Fact]
    public void InvalidInputsAndStatesAreRejected()
    {
        var state = FlappyEngine.Start(1);

        Assert.Throws<ArgumentOutOfRangeException>(() => FlappyEngine.Start(1, width: 319));
        Assert.Throws<ArgumentOutOfRangeException>(() => FlappyEngine.Start(1, bestScore: -1));
        Assert.Throws<ArgumentOutOfRangeException>(() => FlappyEngine.Step(state, (FlappyInput)99));
        Assert.Throws<InvalidOperationException>(() => FlappyEngine.Step(
            state with { Phase = FlappyPhase.GroundCollision },
            FlappyInput.None));
        Assert.Throws<ArgumentException>(() => FlappyEngine.ToSnapshot(
            state with { RandomState = 0 }));
    }

    [Fact]
    public void SnapshotContainsCompactClientAndReplayState()
    {
        var state = FlappyEngine.Step(FlappyEngine.Start(99), FlappyInput.Flap).State;

        var snapshot = FlappyEngine.ToSnapshot(state);

        Assert.Equal(99u, snapshot.Seed);
        Assert.Equal(1, snapshot.Tick);
        Assert.Equal(FlappyEngine.BirdX, snapshot.BirdX);
        Assert.Equal(state.BirdY, snapshot.BirdY);
        Assert.Equal(state.Obstacles.Length, snapshot.Obstacles.Length);
        Assert.Equal(state.Obstacles[0].Id, snapshot.Obstacles[0].Id);
        Assert.Equal(state.Obstacles[0].GapTop, snapshot.Obstacles[0].GapTop);
    }

    [Fact]
    public void TerminalReplayIsAuthoritativelyReplayedFromFlapTicks()
    {
        var replay = new FlappyReplay(36, ImmutableArray<int>.Empty);

        var result = FlappyReplayEvaluator.Evaluate(17, replay);

        Assert.Equal(36, result.Snapshot.Tick);
        Assert.Equal(FlappyPhase.GroundCollision, result.Snapshot.Phase);
        Assert.Equal(0, result.Snapshot.Score);
        Assert.Empty(result.FlapTicks);
    }

    [Theory]
    [InlineData(0, new[] { 0 })]
    [InlineData(36, new[] { 36 })]
    [InlineData(36, new[] { 2, 2 })]
    [InlineData(36, new[] { 3, 2 })]
    public void ReplayRejectsInvalidFlapTickStreams(int totalTicks, int[] flapTicks)
    {
        Assert.ThrowsAny<ArgumentException>(() => FlappyReplayEvaluator.Evaluate(
            1,
            new FlappyReplay(totalTicks, flapTicks.ToImmutableArray())));
    }

    [Fact]
    public void ReplayMustEndAtItsTerminalCollision()
    {
        Assert.Throws<ArgumentException>(() => FlappyReplayEvaluator.Evaluate(
            1,
            new FlappyReplay(1, ImmutableArray<int>.Empty)));
    }

    private static FlappySnapshot Replay(uint seed, IEnumerable<FlappyInput> inputs)
    {
        var state = FlappyEngine.Start(seed);
        foreach (var input in inputs)
        {
            if (state.Phase != FlappyPhase.Playing) break;
            state = FlappyEngine.Step(state, input).State;
        }
        return FlappyEngine.ToSnapshot(state);
    }
}
