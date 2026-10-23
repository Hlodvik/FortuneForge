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
    [InlineData(1, 121, 160)]
    [InlineData(17, 120, 160)]
    [InlineData(42, 96, 48)]
    [InlineData(99, 119, 159)]
    [InlineData(123456789, 120, 160)]
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
    public void PreviousRulesReplayKeepsItsOriginalTerminalResult()
    {
        var state = HorseFlightEngine.Start(42, rulesVersion: HorseFlightEngine.PreviousRulesVersion);
        while (state.Phase == HorseFlightPhase.Running)
            state = HorseFlightEngine.Step(state, HorseFlightInput.None).State;

        Assert.Equal(103, state.Tick);
        Assert.Equal(51, state.Score);
        Assert.Equal(HorseFlightObstacleKind.LassoThrower, state.TerminalObstacleKind);
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
        Assert.Equal(HorseFlightObstacleKind.Crate, collision.State.TerminalObstacleKind);
        Assert.Equal(HorseFlightObstacleKind.Crate, HorseFlightEngine.ToSnapshot(collision.State).TerminalObstacleKind);
        Assert.Equal(HorseFlightPhase.Fell, fall.State.Phase);
        Assert.Equal(HorseFlightEventType.Fell, fall.Event);
        Assert.Null(fall.State.TerminalObstacleKind);
        Assert.Null(HorseFlightEngine.ToSnapshot(fall.State).TerminalObstacleKind);
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
        Assert.Throws<ArgumentException>(() => HorseFlightEngine.ToSnapshot(
            state with { TerminalObstacleKind = HorseFlightObstacleKind.Crate }));
        Assert.Throws<ArgumentException>(() => HorseFlightEngine.ToSnapshot(
            state with { Phase = HorseFlightPhase.ObstacleCollision }));
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
    public void BiomeSectionsAdvanceAtExactTransitionAndFullStartTicks()
    {
        Assert.Equal(
            new HorseFlightBiomeSection(HorseFlightBiomePhase.Main, HorseFlightBiome.Mountain,
                HorseFlightBiome.Mountain, HorseFlightBiome.Mountain, 1),
            HorseFlightEngine.BiomeSectionForTick(0, HorseFlightEngine.DraftMultiBiomeRulesVersion));

        (HorseFlightBiome From, HorseFlightBiome To, int StartTick)[] boundaries =
        [
            (HorseFlightBiome.Mountain, HorseFlightBiome.CastleTown, 3_500),
            (HorseFlightBiome.CastleTown, HorseFlightBiome.DarkForest, 7_000),
            (HorseFlightBiome.DarkForest, HorseFlightBiome.Battleground, 10_500),
            (HorseFlightBiome.Battleground, HorseFlightBiome.Haunted, 14_250),
            (HorseFlightBiome.Haunted, HorseFlightBiome.Desert, 17_500),
            (HorseFlightBiome.Desert, HorseFlightBiome.Sky, 21_000),
        ];
        foreach (var (from, to, startTick) in boundaries)
        {
            Assert.Equal(from, HorseFlightEngine.BiomeSectionForTick(startTick - 1, HorseFlightEngine.DraftMultiBiomeRulesVersion).Biome);
            Assert.Equal(HorseFlightBiomePhase.Main, HorseFlightEngine.BiomeSectionForTick(startTick - 1, HorseFlightEngine.DraftMultiBiomeRulesVersion).Phase);
            Assert.Equal(
                new HorseFlightBiomeSection(HorseFlightBiomePhase.Transition, to, from, to, 0),
                HorseFlightEngine.BiomeSectionForTick(startTick, HorseFlightEngine.DraftMultiBiomeRulesVersion));
            Assert.Equal(0.5, HorseFlightEngine.BiomeSectionForTick(startTick + 375, HorseFlightEngine.DraftMultiBiomeRulesVersion).Blend);
            Assert.Equal(HorseFlightBiomePhase.Transition,
                HorseFlightEngine.BiomeSectionForTick(startTick + HorseFlightEngine.BiomeTransitionDurationTicks - 1, HorseFlightEngine.DraftMultiBiomeRulesVersion).Phase);
            Assert.Equal(
                new HorseFlightBiomeSection(HorseFlightBiomePhase.Main, to, to, to, 1),
                HorseFlightEngine.BiomeSectionForTick(startTick + HorseFlightEngine.BiomeTransitionDurationTicks, HorseFlightEngine.DraftMultiBiomeRulesVersion));
        }
        Assert.Equal(HorseFlightBiome.Sky, HorseFlightEngine.BiomeSectionForTick(30_000, HorseFlightEngine.DraftMultiBiomeRulesVersion).Biome);
        Assert.Throws<ArgumentOutOfRangeException>(() => HorseFlightEngine.BiomeSectionForTick(-1));
    }

    [Fact]
    public void EveryBiomeAndBorderHasItsOwnObstaclePoolWithSafeSpacing()
    {
        (int Tick, HorseFlightObstacleKind[] Kinds)[] expectedPools =
        [
            (3_500, [HorseFlightObstacleKind.FallenLog, HorseFlightObstacleKind.Fence, HorseFlightObstacleKind.HangingSign, HorseFlightObstacleKind.MarketCart]),
            (4_250, [HorseFlightObstacleKind.Fence, HorseFlightObstacleKind.HangingSign, HorseFlightObstacleKind.MarketCart, HorseFlightObstacleKind.RollingBarrel, HorseFlightObstacleKind.Well]),
            (7_000, [HorseFlightObstacleKind.FallenLog, HorseFlightObstacleKind.HangingSign, HorseFlightObstacleKind.MarketCart, HorseFlightObstacleKind.ThornBramble]),
            (7_750, [HorseFlightObstacleKind.BatFlock, HorseFlightObstacleKind.Boulder, HorseFlightObstacleKind.FallenLog, HorseFlightObstacleKind.ThornBramble]),
            (10_500, [HorseFlightObstacleKind.FallenLog, HorseFlightObstacleKind.LassoThrower, HorseFlightObstacleKind.SpikeBarricade, HorseFlightObstacleKind.ThornBramble]),
            (11_250, [HorseFlightObstacleKind.Boulder, HorseFlightObstacleKind.LassoThrower, HorseFlightObstacleKind.NetTower, HorseFlightObstacleKind.RollingBarrel, HorseFlightObstacleKind.SpikeBarricade]),
            (14_250, [HorseFlightObstacleKind.BatFlock, HorseFlightObstacleKind.NetTower, HorseFlightObstacleKind.Skeleton, HorseFlightObstacleKind.SpikeBarricade]),
            (15_000, [HorseFlightObstacleKind.BatFlock, HorseFlightObstacleKind.Crate, HorseFlightObstacleKind.CrateCluster, HorseFlightObstacleKind.Dog, HorseFlightObstacleKind.OilSpill, HorseFlightObstacleKind.Pit, HorseFlightObstacleKind.Skeleton, HorseFlightObstacleKind.Well]),
            (17_500, [HorseFlightObstacleKind.Boulder, HorseFlightObstacleKind.Cactus, HorseFlightObstacleKind.Pit, HorseFlightObstacleKind.Skeleton]),
            (18_250, [HorseFlightObstacleKind.Boulder, HorseFlightObstacleKind.Cactus, HorseFlightObstacleKind.Pit, HorseFlightObstacleKind.Well]),
            (21_000, [HorseFlightObstacleKind.BatFlock, HorseFlightObstacleKind.Boulder, HorseFlightObstacleKind.Cactus, HorseFlightObstacleKind.StormCloud]),
            (21_750, [HorseFlightObstacleKind.BatFlock, HorseFlightObstacleKind.Boulder, HorseFlightObstacleKind.StormCloud]),
        ];

        foreach (var (tick, expectedKinds) in expectedPools)
        {
            var generated = Enumerable.Range(1, 150)
                .SelectMany(seed => GeneratedObstaclesAtTick((uint)seed, tick, HorseFlightEngine.DraftMultiBiomeRulesVersion))
                .ToArray();
            Assert.Equal(expectedKinds.Order(), generated.Select(obstacle => obstacle.Kind).Distinct().Order());
            foreach (var seed in Enumerable.Range(1, 12).Select(value => (uint)value))
                AssertRecoveryWindows(GeneratedObstaclesAtTick(seed, tick, HorseFlightEngine.DraftMultiBiomeRulesVersion));
        }

        Assert.Contains(Enumerable.Range(1, 20).SelectMany(seed => GeneratedObstaclesAtTick((uint)seed, 4_250, HorseFlightEngine.DraftMultiBiomeRulesVersion)), obstacle =>
            obstacle.Kind == HorseFlightObstacleKind.HangingSign && obstacle.Elevation == 58);
        Assert.Contains(Enumerable.Range(1, 20).SelectMany(seed => GeneratedObstaclesAtTick((uint)seed, 21_750, HorseFlightEngine.DraftMultiBiomeRulesVersion)), obstacle =>
            obstacle.Kind == HorseFlightObstacleKind.StormCloud && obstacle.Elevation == 76 && obstacle.AdditionalSpeed == 1.4);
    }

    [Fact]
    public void LegacyRulesPreserveTheOriginalMountainToHauntedRoute()
    {
        Assert.Equal(HorseFlightBiome.Mountain,
            HorseFlightEngine.BiomeSectionForTick(4_250, HorseFlightEngine.LegacyRulesVersion).Biome);
        Assert.Equal(HorseFlightBiome.Haunted,
            HorseFlightEngine.BiomeSectionForTick(14_250, HorseFlightEngine.LegacyRulesVersion).Biome);
        Assert.Equal(HorseFlightBiome.Haunted,
            HorseFlightEngine.BiomeSectionForTick(30_000, HorseFlightEngine.LegacyRulesVersion).Biome);

        var legacyKinds = Enumerable.Range(1, 150)
            .SelectMany(seed => GeneratedObstaclesAtTick((uint)seed, 4_250, HorseFlightEngine.LegacyRulesVersion))
            .Select(obstacle => obstacle.Kind)
            .Distinct()
            .Order();
        Assert.Equal(new[]
        {
            HorseFlightObstacleKind.Boulder, HorseFlightObstacleKind.Carriage,
            HorseFlightObstacleKind.FallenLog, HorseFlightObstacleKind.Fence,
            HorseFlightObstacleKind.LassoThrower, HorseFlightObstacleKind.NetTower,
            HorseFlightObstacleKind.RollingBarrel,
        }.Order(), legacyKinds);
        Assert.Throws<ArgumentOutOfRangeException>(() => HorseFlightEngine.Start(1, rulesVersion: HorseFlightEngine.CurrentRulesVersion + 1));
    }

    [Fact]
    public void CurrentRunsUseOnlyTheExistingMountainAndHauntedWorlds()
    {
        Assert.Equal(HorseFlightEngine.CurrentRulesVersion, HorseFlightEngine.Start(42).RulesVersion);
        foreach (var tick in new[] { 0, 3_500, 10_500, HorseFlightEngine.HauntedBiomeTransitionStartTick - 1 })
            Assert.Equal(HorseFlightBiome.Mountain, HorseFlightEngine.BiomeSectionForTick(tick).Biome);
        var transition = HorseFlightEngine.BiomeSectionForTick(HorseFlightEngine.HauntedBiomeTransitionStartTick);
        Assert.Equal(HorseFlightBiomePhase.Transition, transition.Phase);
        Assert.Equal(HorseFlightBiome.Mountain, transition.From);
        Assert.Equal(HorseFlightBiome.Haunted, transition.To);
        foreach (var tick in new[] { HorseFlightEngine.HauntedBiomeStartTick, 17_500, 21_000, 30_000 })
            Assert.Equal(HorseFlightBiome.Haunted, HorseFlightEngine.BiomeSectionForTick(tick).Biome);

        HorseFlightObstacleKind[] mountainKinds =
        [
            HorseFlightObstacleKind.Boulder, HorseFlightObstacleKind.Carriage,
            HorseFlightObstacleKind.FallenLog, HorseFlightObstacleKind.Fence,
            HorseFlightObstacleKind.LassoThrower, HorseFlightObstacleKind.NetTower,
            HorseFlightObstacleKind.RollingBarrel,
        ];
        HorseFlightObstacleKind[] hauntedKinds =
        [
            HorseFlightObstacleKind.BatFlock, HorseFlightObstacleKind.Crate,
            HorseFlightObstacleKind.CrateCluster, HorseFlightObstacleKind.Dog,
            HorseFlightObstacleKind.OilSpill, HorseFlightObstacleKind.Pit,
            HorseFlightObstacleKind.Skeleton, HorseFlightObstacleKind.Well,
        ];
        foreach (var (tick, expected) in new[]
        {
            (3_500, mountainKinds), (10_500, mountainKinds),
            (14_250, hauntedKinds), (17_500, hauntedKinds), (21_750, hauntedKinds),
        })
        {
            var generated = Enumerable.Range(1, 150)
                .SelectMany(seed => GeneratedObstaclesAtTick((uint)seed, tick))
                .ToArray();
            Assert.Equal(expected.Order(), generated.Select(obstacle => obstacle.Kind).Distinct().Order());
            Assert.All(generated, obstacle => Assert.DoesNotContain(obstacle.Kind, new[]
            {
                HorseFlightObstacleKind.MarketCart, HorseFlightObstacleKind.HangingSign,
                HorseFlightObstacleKind.ThornBramble, HorseFlightObstacleKind.SpikeBarricade,
                HorseFlightObstacleKind.Cactus, HorseFlightObstacleKind.StormCloud,
            }));
            foreach (var seed in Enumerable.Range(1, 12).Select(value => (uint)value))
                AssertRecoveryWindows(GeneratedObstaclesAtTick(seed, tick));
        }
    }

    [Fact]
    public void NewSectionsMatchBrowserSeedFortyTwoObstacleFixtures()
    {
        (int Tick, HorseFlightObstacleKind[] Kinds, double SecondX)[] fixtures =
        [
            (3_500, [HorseFlightObstacleKind.MarketCart, HorseFlightObstacleKind.FallenLog, HorseFlightObstacleKind.Fence], 1_504.161469),
            (4_250, [HorseFlightObstacleKind.RollingBarrel, HorseFlightObstacleKind.HangingSign, HorseFlightObstacleKind.MarketCart], 1_342.233541),
            (7_000, [HorseFlightObstacleKind.ThornBramble, HorseFlightObstacleKind.HangingSign, HorseFlightObstacleKind.MarketCart], 1_504.161469),
            (7_750, [HorseFlightObstacleKind.FallenLog, HorseFlightObstacleKind.Boulder, HorseFlightObstacleKind.ThornBramble], 1_508.161469),
            (10_500, [HorseFlightObstacleKind.SpikeBarricade, HorseFlightObstacleKind.FallenLog, HorseFlightObstacleKind.ThornBramble], 1_516.161469),
            (11_250, [HorseFlightObstacleKind.NetTower, HorseFlightObstacleKind.Boulder, HorseFlightObstacleKind.SpikeBarricade], 1_520.161469),
            (14_250, [HorseFlightObstacleKind.BatFlock, HorseFlightObstacleKind.NetTower, HorseFlightObstacleKind.SpikeBarricade], 1_414.127110),
            (15_000, [HorseFlightObstacleKind.Dog, HorseFlightObstacleKind.Pit, HorseFlightObstacleKind.CrateCluster], 1_371.967891),
            (17_500, [HorseFlightObstacleKind.Cactus, HorseFlightObstacleKind.Skeleton, HorseFlightObstacleKind.Pit], 1_677.116993),
            (18_250, [HorseFlightObstacleKind.Pit, HorseFlightObstacleKind.Boulder, HorseFlightObstacleKind.Cactus], 1_508.161469),
            (21_000, [HorseFlightObstacleKind.StormCloud, HorseFlightObstacleKind.Boulder, HorseFlightObstacleKind.Cactus], 1_419.449691),
            (21_750, [HorseFlightObstacleKind.BatFlock, HorseFlightObstacleKind.StormCloud, HorseFlightObstacleKind.StormCloud], 1_549.561469),
        ];
        foreach (var (tick, kinds, secondX) in fixtures)
        {
            var obstacles = GeneratedObstaclesAtTick(42, tick, HorseFlightEngine.DraftMultiBiomeRulesVersion);
            Assert.Equal(kinds, obstacles.Select(obstacle => obstacle.Kind));
            Assert.InRange(Math.Abs(obstacles[1].X - secondX), 0, 0.000_001);
        }
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
    public void GeneratedObstacleSequencesLeaveAFullManeuverRecoveryWindow()
    {
        Assert.True(HorseFlightEngine.SlideDurationTicks < HorseFlightEngine.MinimumObstacleRecoveryTicks);

        foreach (var seed in Enumerable.Range(1, 100).Select(value => (uint)value))
        {
            AssertRecoveryWindows(HorseFlightEngine.Start(seed).Obstacles);
            AssertRecoveryWindows(HorseFlightEngine.Step(
                ReadyToGenerateAtLevel(seed, level: 7),
                HorseFlightInput.None).State.Obstacles);
        }

        var open = SafeOpenRun(701);
        var jump = HorseFlightEngine.Step(open, HorseFlightInput.Jump).State;
        var airborneTicks = 1;
        while (!jump.IsGrounded)
        {
            jump = HorseFlightEngine.Step(jump, HorseFlightInput.None).State;
            airborneTicks++;
        }
        Assert.True(airborneTicks < HorseFlightEngine.MinimumObstacleRecoveryTicks);
    }

    [Fact]
    public void LegacyRunsRetainTheirOriginalCourseAndCollisionBounds()
    {
        var legacy = HorseFlightEngine.Start(42, rulesVersion: HorseFlightEngine.LegacyRulesVersion);
        Assert.Equal(
            [HorseFlightObstacleKind.LassoThrower, HorseFlightObstacleKind.RollingBarrel, HorseFlightObstacleKind.FallenLog],
            legacy.Obstacles.Select(obstacle => obstacle.Kind));
        Assert.InRange(Math.Abs(legacy.Obstacles[0].X - 951.592341576168), 0, 0.000_001);
        Assert.InRange(Math.Abs(legacy.Obstacles[1].X - 1298.101051163531), 0, 0.000_001);
        Assert.InRange(Math.Abs(legacy.Obstacles[2].X - 1799.0740768142534), 0, 0.000_001);

        var edge = new HorseFlightObstacle(
            legacy.NextObstacleId, HorseFlightObstacleKind.Crate, 1,
            HorseFlightEngine.HorseX + 21, Width: 38, Height: 34);
        var contact = legacy with
        {
            Obstacles = [edge],
            NextObstacleId = edge.Id + 1,
        };
        Assert.Equal(HorseFlightPhase.ObstacleCollision,
            HorseFlightEngine.Step(contact, HorseFlightInput.None).State.Phase);
        Assert.Equal(HorseFlightPhase.Running,
            HorseFlightEngine.Step(contact with { RulesVersion = HorseFlightEngine.CurrentRulesVersion },
                HorseFlightInput.None).State.Phase);
    }

    [Fact]
    public void ObstaclesUseInsetHitboxesAndHeldDashKnocksDownHoundsWhileDodgingNets()
    {
        // Rules v3 runs already in progress must keep their body-hitbox result.
        var state = SafeOpenRun(29) with { RulesVersion = HorseFlightEngine.PreviousRulesVersion };
        var platform = state.Platforms.Single(platform => platform.Id == state.StandingPlatformId);
        var edgeCrate = new HorseFlightObstacle(
            state.NextObstacleId,
            HorseFlightObstacleKind.Crate,
            platform.Id,
            HorseFlightEngine.HorseX + (HorseFlightEngine.HorseCollisionWidth / 2),
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
        Assert.Null(edge.State.TerminalObstacleKind);
        Assert.Equal(HorseFlightPhase.ObstacleCollision, ordinaryContact.State.Phase);
        Assert.Equal(HorseFlightObstacleKind.LassoThrower, ordinaryContact.State.TerminalObstacleKind);
        Assert.Equal(HorseFlightPhase.Running, dash.State.Phase);
        Assert.Equal(1, dash.State.Obstacles.Single().KnockedDownAt);
        Assert.Equal(HorseFlightPhase.Running, dogDash.State.Phase);
        Assert.Equal(1, dogDash.State.Obstacles.Single().KnockedDownAt);
        Assert.Equal(HorseFlightPhase.ObstacleCollision, netContact.State.Phase);
        Assert.Equal(HorseFlightObstacleKind.NetTower, netContact.State.TerminalObstacleKind);
        Assert.Equal(HorseFlightPhase.Running, netDash.State.Phase);
        Assert.Equal(9, sustainedDash.DistanceGained, precision: 6);
    }

    [Theory]
    [InlineData(HorseFlightObstacleKind.LassoThrower, 40, 240, -50)]
    [InlineData(HorseFlightObstacleKind.NetTower, 20, 280, -50)]
    public void CurrentRulesCollideWithFlyingCaptureProjectileButNotTheThrower(
        HorseFlightObstacleKind kind,
        double contactDistance,
        double windingDistance,
        double spentDistance)
    {
        var open = SafeOpenRun(29);
        Assert.Equal(HorseFlightEngine.CurrentRulesVersion, open.RulesVersion);

        HorseFlightState AtDistance(double distance, HorseFlightState? source = null)
        {
            var state = source ?? open;
            var additionalSpeed = kind == HorseFlightObstacleKind.LassoThrower ? 0.8 : 0;
            var obstacle = new HorseFlightObstacle(
                state.NextObstacleId,
                kind,
                state.StandingPlatformId ?? 1,
                HorseFlightEngine.HorseX + distance + 5 + additionalSpeed,
                kind == HorseFlightObstacleKind.LassoThrower ? 76 : 92,
                kind == HorseFlightObstacleKind.LassoThrower ? 64 : 108,
                AdditionalSpeed: additionalSpeed);
            return state with
            {
                Obstacles = [obstacle],
                NextObstacleId = obstacle.Id + 1,
            };
        }

        var winding = HorseFlightEngine.Step(AtDistance(windingDistance), HorseFlightInput.None);
        var contact = HorseFlightEngine.Step(AtDistance(contactDistance), HorseFlightInput.None);
        var spent = HorseFlightEngine.Step(AtDistance(spentDistance), HorseFlightInput.None);
        var airborne = open with
        {
            HorseY = open.HorseY - 75,
            IsGrounded = false,
            StandingPlatformId = null,
            JumpsRemaining = 1,
        };
        var jumpedClear = HorseFlightEngine.Step(AtDistance(contactDistance, airborne), HorseFlightInput.None);

        Assert.Equal(HorseFlightPhase.Running, winding.State.Phase);
        Assert.Equal(HorseFlightPhase.ObstacleCollision, contact.State.Phase);
        Assert.Equal(kind, contact.State.TerminalObstacleKind);
        Assert.Equal(HorseFlightPhase.Running, spent.State.Phase);
        Assert.Equal(HorseFlightPhase.Running, jumpedClear.State.Phase);
    }

    [Fact]
    public void CurrentRulesLetSlidePassUnderNetProjectile()
    {
        var state = SafeOpenRun(31);
        var net = new HorseFlightObstacle(
            state.NextObstacleId,
            HorseFlightObstacleKind.NetTower,
            state.StandingPlatformId!.Value,
            HorseFlightEngine.HorseX + 20 + 9,
            Width: 92,
            Height: 108);
        var running = state with { Obstacles = [net], NextObstacleId = net.Id + 1 };

        Assert.Equal(HorseFlightPhase.Running,
            HorseFlightEngine.Step(running, HorseFlightInput.RightClick).State.Phase);
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
        Assert.NotNull(result.Snapshot.TerminalObstacleKind);
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

    private static ImmutableArray<HorseFlightObstacle> GeneratedObstaclesAtTick(
        uint seed,
        int tick,
        int rulesVersion = HorseFlightEngine.CurrentRulesVersion)
    {
        var source = HorseFlightEngine.Start(seed, rulesVersion: rulesVersion);
        var ready = source with
        {
            Tick = tick,
            Platforms = source.Platforms.Where(platform => platform.Id == source.StandingPlatformId).ToImmutableArray(),
            Obstacles = ImmutableArray<HorseFlightObstacle>.Empty,
            NextPlatformId = 2,
            NextObstacleId = 1,
        };
        return HorseFlightEngine.Step(ready, HorseFlightInput.None).State.Obstacles;
    }

    private static HorseFlightReplay TerminalNoJumpReplay(uint seed)
    {
        var state = HorseFlightEngine.Start(seed);
        while (state.Phase == HorseFlightPhase.Running)
            state = HorseFlightEngine.Step(state, HorseFlightInput.None).State;
        return new HorseFlightReplay(state.Tick, ImmutableArray<int>.Empty);
    }

    private static void AssertRecoveryWindows(IEnumerable<HorseFlightObstacle> obstacles)
    {
        var generated = obstacles.OrderBy(obstacle => obstacle.Id).ToArray();
        Func<int, double>[] speedPatterns =
        [
            _ => 5,
            _ => 11,
            tick => tick % 16 < 8 ? 11 : 5,
            tick => tick % 40 < 16 ? 11 : 5,
            tick => tick % 40 < 16 ? 5 : 11,
        ];
        foreach (var speedAt in speedPatterns)
        {
            var travel = 0d;
            var exits = new Dictionary<int, int>();
            var entries = new Dictionary<int, int>();
            for (var tick = 0; tick < 3_000 && exits.Count < generated.Length; tick++)
            {
                travel += speedAt(tick);
                foreach (var obstacle in generated)
                {
                    var x = obstacle.X - travel - (obstacle.AdditionalSpeed * (tick + 1));
                    if (!entries.ContainsKey(obstacle.Id) &&
                        x + HorseFlightEngine.ObstacleHorizontalHitboxInset <
                            HorseFlightEngine.HorseX + (HorseFlightEngine.HorseCollisionWidth / 2))
                        entries.Add(obstacle.Id, tick + 1);
                    if (!exits.ContainsKey(obstacle.Id) &&
                        x + obstacle.Width - HorseFlightEngine.ObstacleHorizontalHitboxInset <=
                            HorseFlightEngine.HorseX - (HorseFlightEngine.HorseCollisionWidth / 2))
                        exits.Add(obstacle.Id, tick + 1);
                }
            }
            Assert.Equal(generated.Length, exits.Count);
            for (var index = 1; index < generated.Length; index++)
            {
                Assert.True(entries[generated[index].Id] - exits[generated[index - 1].Id] >=
                    HorseFlightEngine.MinimumObstacleRecoveryTicks,
                    $"Obstacle {index} followed its predecessor too closely at a variable speed.");
            }
        }
    }
}
