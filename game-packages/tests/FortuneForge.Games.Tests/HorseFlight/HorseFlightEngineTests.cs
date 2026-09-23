using System.Collections.Immutable;
using System.Text.Json;
using FortuneForge.Games.HorseFlight;

namespace FortuneForge.Games.Tests.HorseFlight;

public sealed class HorseFlightEngineTests
{
    [Fact]
    public void EqualSeedAndInputReplayProducesIdenticalSnapshots()
    {
        var inputs = Enumerable.Range(0, 220)
            .Select(tick => tick % 44 == 0 ? HorseFlightInput.Jump : HorseFlightInput.None)
            .ToArray();

        var first = Replay(42, inputs);
        var second = Replay(42, inputs);
        var differentSeed = HorseFlightEngine.Start(43);

        Assert.Equal(JsonSerializer.Serialize(first), JsonSerializer.Serialize(second));
        Assert.NotEqual(
            HorseFlightEngine.Start(42).Platforms[1].Width,
            differentSeed.Platforms[1].Width);
        Assert.True(first.Tick > 0);
    }

    [Theory]
    [InlineData(1, 129, 164)]
    [InlineData(17, 124, 162)]
    [InlineData(42, 103, 51)]
    [InlineData(99, 119, 159)]
    [InlineData(123456789, 130, 165)]
    public void BrowserSimulationSeedsReachTheSameTerminalTickAndScore(uint seed, int expectedTick, int expectedScore)
    {
        var state = HorseFlightEngine.Start(seed);
        while (state.Phase == HorseFlightPhase.Running)
            state = HorseFlightEngine.Step(state, HorseFlightInput.None).State;

        Assert.Equal(expectedTick, state.Tick);
        Assert.Equal(expectedScore, state.Score);
        Assert.Equal(HorseFlightPhase.ObstacleCollision, state.Phase);
    }

    [Fact]
    public void TimedJumpLeavesPlatformAndLandsAgain()
    {
        var state = HorseFlightEngine.Start(7);

        var jump = HorseFlightEngine.Step(state, HorseFlightInput.Jump);

        Assert.Equal(HorseFlightEventType.Jumped, jump.Event);
        Assert.False(jump.State.IsGrounded);
        Assert.Equal(HorseFlightEngine.JumpVelocity, jump.State.HorseVelocity);
        Assert.True(jump.State.HorseY < state.HorseY);

        var current = jump.State;
        HorseFlightTransition? landing = null;
        for (var tick = 0; tick < 80 && current.Phase == HorseFlightPhase.Running; tick++)
        {
            var transition = HorseFlightEngine.Step(current, HorseFlightInput.None);
            current = transition.State;
            if (transition.Event == HorseFlightEventType.Landed)
            {
                landing = transition;
                break;
            }
        }

        Assert.NotNull(landing);
        Assert.True(landing.State.IsGrounded);
        Assert.Equal(0, landing.State.HorseVelocity);
        Assert.NotNull(landing.State.StandingPlatformId);
        Assert.Equal(HorseFlightEngine.MaximumJumps, landing.State.JumpsRemaining);
    }

    [Fact]
    public void GeneratedPlatformsFormOneFlatContinuousTrack()
    {
        var platforms = HorseFlightEngine.Start(42).Platforms
            .OrderBy(platform => platform.X)
            .ToArray();

        Assert.All(platforms, platform =>
            Assert.Equal(HorseFlightEngine.DefaultHeight - 80, platform.Y, precision: 6));
        for (var index = 1; index < platforms.Length; index++)
        {
            Assert.Equal(
                platforms[index - 1].X + platforms[index - 1].Width,
                platforms[index].X,
                precision: 6);
        }
    }

    [Fact]
    public void HorseKeepsRunningAcrossAContinuousPlatformSeam()
    {
        var source = HorseFlightEngine.Start(42);
        var seam = HorseFlightEngine.HorseX - (HorseFlightEngine.HorseWidth / 2) + 5;
        var state = source with
        {
            StandingPlatformId = 1,
            Platforms = ImmutableArray.Create(
                new HorseFlightPlatform(1, 0, seam, source.HorseY, Cleared: false),
                new HorseFlightPlatform(2, seam, 1_000, source.HorseY, Cleared: false),
                new HorseFlightPlatform(3, seam + 1_000, 1_000, source.HorseY, Cleared: false),
                new HorseFlightPlatform(4, seam + 2_000, 1_000, source.HorseY, Cleared: false)),
            Obstacles = ImmutableArray<HorseFlightObstacle>.Empty,
            NextPlatformId = 5,
            NextObstacleId = 1,
        };

        var transition = HorseFlightEngine.Step(state, HorseFlightInput.None);

        Assert.Equal(HorseFlightPhase.Running, transition.State.Phase);
        Assert.True(transition.State.IsGrounded);
        Assert.Equal(2, transition.State.StandingPlatformId);
    }

    [Fact]
    public void HorseCanDoubleJumpBeforeLandingAndThenRegainsBothJumps()
    {
        var state = SafeOpenRun(7);

        var firstJump = HorseFlightEngine.Step(state, HorseFlightInput.Jump);
        var secondJump = HorseFlightEngine.Step(firstJump.State, HorseFlightInput.Jump);
        var exhaustedJump = HorseFlightEngine.Step(secondJump.State, HorseFlightInput.Jump);

        Assert.Equal(HorseFlightEventType.Jumped, firstJump.Event);
        Assert.Equal(1, firstJump.State.JumpsRemaining);
        Assert.Equal(HorseFlightEngine.JumpVelocity, secondJump.State.HorseVelocity);
        Assert.Equal(0, secondJump.State.JumpsRemaining);
        Assert.NotEqual(HorseFlightEngine.JumpVelocity, exhaustedJump.State.HorseVelocity);
        Assert.Equal(0, exhaustedJump.State.JumpsRemaining);

        var current = exhaustedJump.State;
        HorseFlightTransition? landing = null;
        for (var tick = 0; tick < 80 && current.Phase == HorseFlightPhase.Running; tick++)
        {
            var transition = HorseFlightEngine.Step(current, HorseFlightInput.None);
            current = transition.State;
            if (transition.Event == HorseFlightEventType.Landed)
            {
                landing = transition;
                break;
            }
        }

        Assert.NotNull(landing);
        Assert.Equal(HorseFlightEngine.MaximumJumps, landing.State.JumpsRemaining);
    }

    [Fact]
    public void RightClickSlidesUnderFenceAndFastFallsWhenAirborne()
    {
        var state = SafeOpenRun(29);
        var platform = state.Platforms.Single(item => item.Id == state.StandingPlatformId);
        var fence = new HorseFlightObstacle(
            state.NextObstacleId,
            HorseFlightObstacleKind.Fence,
            platform.Id,
            HorseFlightEngine.HorseX - (HorseFlightEngine.HorseWidth / 2) + 10,
            Width: 84,
            Height: 42);
        state = state with
        {
            Obstacles = ImmutableArray.Create(fence),
            NextObstacleId = state.NextObstacleId + 1,
        };

        var running = HorseFlightEngine.Step(state, HorseFlightInput.None);
        var slide = HorseFlightEngine.Step(state, HorseFlightInput.RightClick);
        var jump = HorseFlightEngine.Step(SafeOpenRun(31), HorseFlightInput.Jump);
        var ordinaryFall = HorseFlightEngine.Step(jump.State, HorseFlightInput.None);
        var fastFall = HorseFlightEngine.Step(jump.State, HorseFlightInput.RightClick);

        Assert.Equal(HorseFlightPhase.ObstacleCollision, running.State.Phase);
        Assert.Equal(HorseFlightPhase.Running, slide.State.Phase);
        Assert.Equal(HorseFlightEngine.SlideDurationTicks, slide.State.SlideTicksRemaining);
        Assert.True(slide.DistanceGained > running.DistanceGained);
        Assert.True(fastFall.State.HorseY > ordinaryFall.State.HorseY);
        Assert.True(fastFall.State.IsGrounded);
    }

    [Fact]
    public void ClearedPlatformAddsProgressionScoreAndDistance()
    {
        var state = HorseFlightEngine.Start(11);
        var prior = new HorseFlightPlatform(
            state.NextPlatformId,
            HorseFlightEngine.HorseX - (HorseFlightEngine.HorseWidth / 2) - 30,
            31,
            state.HorseY - 100,
            Cleared: false);
        state = state with
        {
            Platforms = state.Platforms.Add(prior),
            NextPlatformId = state.NextPlatformId + 1,
        };

        var transition = HorseFlightEngine.Step(state, HorseFlightInput.None);

        Assert.Equal(HorseFlightEventType.Progressed, transition.Event);
        Assert.Equal(1, transition.State.PlatformsCleared);
        Assert.Equal(HorseFlightEngine.PlatformClearBonus, transition.State.Score);
        Assert.Equal(HorseFlightEngine.PlatformClearBonus, transition.ScoreGained);
        Assert.True(transition.State.Distance > state.Distance);
        Assert.True(transition.State.Platforms.Single(platform => platform.Id == prior.Id).Cleared);
    }

    [Fact]
    public void ObstacleContactAndFallingHaveDistinctTerminalStates()
    {
        var state = HorseFlightEngine.Start(19);
        var support = state.Platforms.Single(platform => platform.Id == state.StandingPlatformId);
        var obstacle = new HorseFlightObstacle(
            state.NextObstacleId,
            HorseFlightObstacleKind.Crate,
            support.Id,
            HorseFlightEngine.HorseX - (HorseFlightEngine.HorseWidth / 2) + 1,
            30,
            HorseFlightEngine.HorseHeight);
        var collisionState = state with
        {
            Obstacles = state.Obstacles.Add(obstacle),
            NextObstacleId = state.NextObstacleId + 1,
        };
        var fallingState = state with
        {
            HorseY = state.Height + HorseFlightEngine.HorseHeight + 1,
            HorseVelocity = 2,
            IsGrounded = false,
            StandingPlatformId = null,
            Platforms = ImmutableArray<HorseFlightPlatform>.Empty,
            Obstacles = ImmutableArray<HorseFlightObstacle>.Empty,
        };

        var collision = HorseFlightEngine.Step(collisionState, HorseFlightInput.None);
        var fall = HorseFlightEngine.Step(fallingState, HorseFlightInput.None);

        Assert.Equal(HorseFlightPhase.ObstacleCollision, collision.State.Phase);
        Assert.Equal(HorseFlightEventType.ObstacleCollision, collision.Event);
        Assert.Equal(HorseFlightPhase.Fell, fall.State.Phase);
        Assert.Equal(HorseFlightEventType.Fell, fall.Event);
    }

    [Fact]
    public void InvalidInputsAndStatesAreRejected()
    {
        var state = HorseFlightEngine.Start(1);

        Assert.Throws<ArgumentOutOfRangeException>(() => HorseFlightEngine.Start(1, width: 479));
        Assert.Throws<ArgumentOutOfRangeException>(() => HorseFlightEngine.Start(1, bestScore: -1));
        Assert.Throws<ArgumentOutOfRangeException>(() =>
            HorseFlightEngine.Step(state, (HorseFlightInput)99));
        Assert.Throws<InvalidOperationException>(() => HorseFlightEngine.Step(
            state with { Phase = HorseFlightPhase.Fell },
            HorseFlightInput.None));
        Assert.Throws<ArgumentException>(() => HorseFlightEngine.ToSnapshot(
            state with { RandomState = 0 }));
        Assert.Throws<ArgumentException>(() => HorseFlightEngine.ToSnapshot(
            state with { StandingPlatformId = state.NextPlatformId }));
    }

    [Fact]
    public void SnapshotContainsCompactClientAndReplayState()
    {
        var state = HorseFlightEngine.Step(
            HorseFlightEngine.Start(99),
            HorseFlightInput.Jump).State;

        var snapshot = HorseFlightEngine.ToSnapshot(state);

        Assert.Equal(99u, snapshot.Seed);
        Assert.Equal(1, snapshot.Tick);
        Assert.Equal(HorseFlightEngine.HorseX, snapshot.HorseX);
        Assert.Equal(state.Distance, snapshot.Distance);
        Assert.Equal(state.Platforms.Length, snapshot.Platforms.Length);
        Assert.Equal(state.Obstacles.Length, snapshot.Obstacles.Length);
        Assert.Equal(state.Platforms[0].Id, snapshot.Platforms[0].Id);
        Assert.Equal(state.Obstacles[0].Kind, snapshot.Obstacles[0].Kind);
    }

    [Fact]
    public void OpeningBiomeOmitsHauntedOnlyObstacles()
    {
        var kinds = Enumerable.Range(1, 150)
            .SelectMany(seed => HorseFlightEngine.Start((uint)seed).Obstacles)
            .Select(obstacle => obstacle.Kind)
            .ToHashSet();

        HorseFlightObstacleKind[] openingKinds =
        [
            HorseFlightObstacleKind.Boulder,
            HorseFlightObstacleKind.Carriage,
            HorseFlightObstacleKind.FallenLog,
            HorseFlightObstacleKind.Fence,
            HorseFlightObstacleKind.LassoThrower,
            HorseFlightObstacleKind.NetTower,
            HorseFlightObstacleKind.RollingBarrel,
        ];
        Assert.Equal(openingKinds.Order(), kinds.Order());
    }

    [Fact]
    public void HauntedBiomeIntroducesBatsAndShamblingSkeletonsAfterFiveMinutes()
    {
        var kinds = Enumerable.Range(1, 150)
            .SelectMany(seed =>
            {
                var source = HorseFlightEngine.Start((uint)seed);
                var ready = source with
                {
                    Tick = HorseFlightEngine.HauntedBiomeStartTick,
                    Platforms = source.Platforms.Where(platform => platform.Id == source.StandingPlatformId).ToImmutableArray(),
                    Obstacles = ImmutableArray<HorseFlightObstacle>.Empty,
                    NextPlatformId = 2,
                    NextObstacleId = 1,
                };
                return HorseFlightEngine.Step(ready, HorseFlightInput.None).State.Obstacles;
            })
            .Select(obstacle => obstacle.Kind)
            .ToHashSet();

        HorseFlightObstacleKind[] hauntedKinds =
        [
            HorseFlightObstacleKind.BatFlock,
            HorseFlightObstacleKind.Crate,
            HorseFlightObstacleKind.CrateCluster,
            HorseFlightObstacleKind.Dog,
            HorseFlightObstacleKind.OilSpill,
            HorseFlightObstacleKind.Pit,
            HorseFlightObstacleKind.Skeleton,
            HorseFlightObstacleKind.Well,
        ];
        Assert.Equal(hauntedKinds.Order(), kinds.Order());
    }

    [Fact]
    public void HigherLevelsGenerateMoreObstaclesThanTheOpeningLevel()
    {
        var openingObstacleCount = Enumerable.Range(1, 100)
            .Sum(seed => HorseFlightEngine.Step(
                ReadyToGenerateAtLevel((uint)seed, level: 1),
                HorseFlightInput.None).State.Obstacles.Length);
        var lateLevelObstacleCount = Enumerable.Range(1, 100)
            .Sum(seed => HorseFlightEngine.Step(
                ReadyToGenerateAtLevel((uint)seed, level: 7),
                HorseFlightInput.None).State.Obstacles.Length);

        Assert.True(lateLevelObstacleCount > openingObstacleCount);
    }

    [Fact]
    public void GeneratedObstacleCountsAreConsistentPerLevelWhilePlacementsStayRandomAndSeparated()
    {
        var opening = HorseFlightEngine.Start(42);
        Assert.All(
            opening.Platforms.Where(platform => platform.Id != 1),
            platform => Assert.Single(opening.Obstacles.Where(obstacle => obstacle.PlatformId == platform.Id)));

        var source = HorseFlightEngine.Start(42);
        var late = HorseFlightEngine.Step(source with
        {
            Platforms = source.Platforms.Where(platform => platform.Id == 1).ToImmutableArray(),
            Obstacles = ImmutableArray<HorseFlightObstacle>.Empty,
            NextPlatformId = 2,
            NextObstacleId = 1,
            Distance = 30_000,
            Score = 3_000,
            BestScore = 3_000,
        }, HorseFlightInput.None).State;
        var latePlatforms = late.Platforms.Where(platform => platform.Id != 1).ToArray();
        Assert.All(latePlatforms, platform =>
            Assert.Equal(3, late.Obstacles.Count(obstacle => obstacle.PlatformId == platform.Id)));
        foreach (var platform in latePlatforms)
        {
            var placed = late.Obstacles.Where(obstacle => obstacle.PlatformId == platform.Id)
                .OrderBy(obstacle => obstacle.X)
                .ToArray();
            Assert.True(placed[1].X - (placed[0].X + placed[0].Width) >= 18);
            Assert.True(placed[2].X - (placed[1].X + placed[1].Width) >= 18);
        }
    }

    [Fact]
    public void ObstaclesUseInsetHitboxesAndHeldDashKnocksDownHoundsWhileDodgingNets()
    {
        var state = SafeOpenRun(29);
        var platform = state.Platforms.Single(platform => platform.Id == state.StandingPlatformId);
        var edgeCrate = new HorseFlightObstacle(
            state.NextObstacleId,
            HorseFlightObstacleKind.Crate,
            platform.Id,
            HorseFlightEngine.HorseX + (HorseFlightEngine.HorseWidth / 2) + 3,
            Width: 38,
            Height: 34);
        var lasso = new HorseFlightObstacle(
            state.NextObstacleId,
            HorseFlightObstacleKind.LassoThrower,
            platform.Id,
            HorseFlightEngine.HorseX - (HorseFlightEngine.HorseWidth / 2) + 10,
            Width: 76,
            Height: 64,
            AdditionalSpeed: 0.8);
        var dog = new HorseFlightObstacle(
            state.NextObstacleId + 1,
            HorseFlightObstacleKind.Dog,
            platform.Id,
            HorseFlightEngine.HorseX - (HorseFlightEngine.HorseWidth / 2) + 10,
            Width: 58,
            Height: 38,
            AdditionalSpeed: 2.2);
        var netTower = new HorseFlightObstacle(
            state.NextObstacleId + 2,
            HorseFlightObstacleKind.NetTower,
            platform.Id,
            HorseFlightEngine.HorseX - (HorseFlightEngine.HorseWidth / 2) + 10,
            Width: 92,
            Height: 108);

        var edge = HorseFlightEngine.Step(state with { Obstacles = [edgeCrate], NextObstacleId = state.NextObstacleId + 1 }, HorseFlightInput.None);
        var ordinaryContact = HorseFlightEngine.Step(state with { Obstacles = [lasso], NextObstacleId = state.NextObstacleId + 1 }, HorseFlightInput.None);
        var dash = HorseFlightEngine.Step(state with { Obstacles = [lasso], NextObstacleId = state.NextObstacleId + 1 }, HorseFlightInput.RightClick);
        var dogDash = HorseFlightEngine.Step(state with { Obstacles = [dog], NextObstacleId = state.NextObstacleId + 2 }, HorseFlightInput.RightClick);
        var netContact = HorseFlightEngine.Step(state with { Obstacles = [netTower], NextObstacleId = state.NextObstacleId + 3 }, HorseFlightInput.None);
        var netDash = HorseFlightEngine.Step(state with { Obstacles = [netTower], NextObstacleId = state.NextObstacleId + 3 }, HorseFlightInput.RightClick);
        var sustainedDash = HorseFlightEngine.Step(dash.State, HorseFlightInput.RightClick);

        Assert.Equal(HorseFlightPhase.Running, edge.State.Phase);
        Assert.Equal(HorseFlightPhase.ObstacleCollision, ordinaryContact.State.Phase);
        Assert.Equal(HorseFlightPhase.Running, dash.State.Phase);
        Assert.Equal(1, dash.State.Obstacles.Single().KnockedDownAt);
        Assert.Equal(HorseFlightPhase.Running, dogDash.State.Phase);
        Assert.Equal(1, dogDash.State.Obstacles.Single().KnockedDownAt);
        Assert.Equal(HorseFlightPhase.ObstacleCollision, netContact.State.Phase);
        Assert.Equal(HorseFlightPhase.Running, netDash.State.Phase);
        Assert.Equal(9, sustainedDash.DistanceGained, precision: 6);
    }

    [Fact]
    public void MovingObstaclesCloseFasterAndBatsUseAnElevatedCollisionLane()
    {
        var state = HorseFlightEngine.Start(31);
        var platform = state.Platforms.Single(item => item.Id == state.StandingPlatformId);
        var dog = new HorseFlightObstacle(
            state.NextObstacleId,
            HorseFlightObstacleKind.Dog,
            platform.Id,
            X: 360,
            Width: 58,
            Height: 38,
            Elevation: 0,
            AdditionalSpeed: 2.2);
        var bats = new HorseFlightObstacle(
            state.NextObstacleId + 1,
            HorseFlightObstacleKind.BatFlock,
            platform.Id,
            X: 460,
            Width: 64,
            Height: 42,
            Elevation: 88,
            AdditionalSpeed: 1.4);
        state = state with
        {
            Obstacles = state.Obstacles.Add(dog).Add(bats),
            NextObstacleId = state.NextObstacleId + 2,
        };

        var transition = HorseFlightEngine.Step(state, HorseFlightInput.None);
        var movedDog = transition.State.Obstacles.Single(obstacle => obstacle.Id == dog.Id);
        var movedBats = transition.State.Obstacles.Single(obstacle => obstacle.Id == bats.Id);
        var snapshot = HorseFlightEngine.ToSnapshot(transition.State);
        var batSnapshot = snapshot.Obstacles.Single(obstacle => obstacle.Id == bats.Id);
        var movedPlatform = transition.State.Platforms.Single(item => item.Id == platform.Id);

        Assert.Equal(dog.X - 5 - dog.AdditionalSpeed, movedDog.X, precision: 6);
        Assert.Equal(bats.X - 5 - bats.AdditionalSpeed, movedBats.X, precision: 6);
        Assert.Equal(movedPlatform.Y - bats.Elevation - bats.Height, batSnapshot.Y, precision: 6);
    }

    [Fact]
    public void TerminalReplayIsAuthoritativelyReplayedFromJumpTicks()
    {
        var replay = TerminalNoJumpReplay(17);

        var result = HorseFlightReplayEvaluator.Evaluate(17, replay);

        Assert.Equal(replay.TotalTicks, result.Snapshot.Tick);
        Assert.NotEqual(HorseFlightPhase.Running, result.Snapshot.Phase);
        Assert.Empty(result.JumpTicks);
    }

    [Theory]
    [InlineData(0, new[] { 0 })]
    [InlineData(100, new[] { 100 })]
    [InlineData(100, new[] { 2, 2 })]
    [InlineData(100, new[] { 3, 2 })]
    public void ReplayRejectsInvalidJumpTickStreams(int totalTicks, int[] jumpTicks)
    {
        Assert.ThrowsAny<ArgumentException>(() => HorseFlightReplayEvaluator.Evaluate(
            1,
            new HorseFlightReplay(totalTicks, jumpTicks.ToImmutableArray())));
    }

    [Fact]
    public void ReplayMustEndAtItsTerminalCollisionOrFall()
    {
        Assert.Throws<ArgumentException>(() => HorseFlightReplayEvaluator.Evaluate(
            1,
            new HorseFlightReplay(1, ImmutableArray<int>.Empty)));
    }

    [Fact]
    public void ReplayRejectsDifferentInputsOnTheSameTick()
    {
        Assert.Throws<ArgumentException>(() => HorseFlightReplayEvaluator.Evaluate(
            1,
            new HorseFlightReplay(100, [2]) { RightClickTicks = [2] }));
    }

    [Fact]
    public void ReplayAcceptsAndReproducesRightClickActions()
    {
        const uint seed = 37;
        var state = HorseFlightEngine.Start(seed);
        var tick = 0;
        while (state.Phase == HorseFlightPhase.Running)
        {
            state = HorseFlightEngine.Step(
                state,
                tick == 0 ? HorseFlightInput.RightClick : HorseFlightInput.None).State;
            tick++;
        }

        var replay = new HorseFlightReplay(tick, ImmutableArray<int>.Empty)
        {
            RightClickTicks = [0],
        };
        var result = HorseFlightReplayEvaluator.Evaluate(seed, replay);

        Assert.Equal(state.Phase, result.Snapshot.Phase);
        Assert.Equal(state.Score, result.Snapshot.Score);
        Assert.Equal(replay.RightClickTicks, result.RightClickTicks);
    }

    private static HorseFlightSnapshot Replay(uint seed, IEnumerable<HorseFlightInput> inputs)
    {
        var state = HorseFlightEngine.Start(seed);
        foreach (var input in inputs)
        {
            if (state.Phase != HorseFlightPhase.Running) break;
            state = HorseFlightEngine.Step(state, input).State;
        }
        return HorseFlightEngine.ToSnapshot(state);
    }

    private static HorseFlightState SafeOpenRun(uint seed)
    {
        var state = HorseFlightEngine.Start(seed);
        var y = state.HorseY;
        return state with
        {
            Platforms = ImmutableArray.Create(
                new HorseFlightPlatform(1, 0, 10_000, y, Cleared: false),
                new HorseFlightPlatform(2, 20_000, 10, y, Cleared: false),
                new HorseFlightPlatform(3, 20_020, 10, y, Cleared: false),
                new HorseFlightPlatform(4, 20_040, 10, y, Cleared: false)),
            Obstacles = ImmutableArray<HorseFlightObstacle>.Empty,
            NextPlatformId = 5,
            NextObstacleId = 1,
        };
    }

    private static HorseFlightState ReadyToGenerateAtLevel(uint seed, int level)
    {
        var state = HorseFlightEngine.Start(seed);
        var score = (level - 1) * HorseFlightEngine.ScoresPerLevel;
        return state with
        {
            Platforms = state.Platforms
                .Where(platform => platform.Id == state.StandingPlatformId)
                .ToImmutableArray(),
            Obstacles = ImmutableArray<HorseFlightObstacle>.Empty,
            Distance = score * HorseFlightEngine.DistancePerPoint,
            Score = score,
            BestScore = Math.Max(state.BestScore, score),
        };
    }

    private static HorseFlightReplay TerminalNoJumpReplay(uint seed)
    {
        var state = HorseFlightEngine.Start(seed);
        while (state.Phase == HorseFlightPhase.Running)
            state = HorseFlightEngine.Step(state, HorseFlightInput.None).State;
        return new HorseFlightReplay(state.Tick, ImmutableArray<int>.Empty);
    }
}
