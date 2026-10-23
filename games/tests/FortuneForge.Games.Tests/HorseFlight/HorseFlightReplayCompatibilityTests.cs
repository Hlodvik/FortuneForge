using System.Collections.Immutable;
using FortuneForge.Games.HorseFlight;
using Xunit;

namespace FortuneForge.Games.Tests.HorseFlight;

public sealed class HorseFlightReplayCompatibilityTests
{
    [Theory]
    [InlineData(HorseFlightEngine.LegacyRulesVersion)]
    [InlineData(HorseFlightEngine.DraftMultiBiomeRulesVersion)]
    [InlineData(HorseFlightEngine.CurrentRulesVersion)]
    public void EvaluatorReplaysTheVersionCapturedWithTheRun(int rulesVersion)
    {
        const uint seed = 271;
        var terminal = TerminalRun(seed, rulesVersion);
        var replay = new HorseFlightReplay(terminal.Tick, ImmutableArray<int>.Empty)
        {
            RulesVersion = rulesVersion,
        };

        var result = HorseFlightReplayEvaluator.Evaluate(seed, replay);

        Assert.Equal(terminal.Tick, result.Snapshot.Tick);
        Assert.Equal(terminal.Score, result.Snapshot.Score);
        Assert.Equal(terminal.Phase, result.Snapshot.Phase);
        Assert.Equal(terminal.TerminalObstacleKind, result.Snapshot.TerminalObstacleKind);
    }

    [Fact]
    public void ReplayCeilingExtendsPastTheSkyBiomeTransition()
    {
        Assert.Equal(30_000, HorseFlightReplayEvaluator.MaximumTicks);
        Assert.True(HorseFlightReplayEvaluator.MaximumTicks >
            HorseFlightEngine.SkyTransitionStartTick + HorseFlightEngine.BiomeTransitionDurationTicks);
    }

    [Fact]
    public void EvaluatorRejectsAnUnsupportedCapturedRulesVersion()
    {
        var replay = new HorseFlightReplay(1, ImmutableArray<int>.Empty)
        {
            RulesVersion = HorseFlightEngine.CurrentRulesVersion + 1,
        };

        Assert.Throws<ArgumentOutOfRangeException>(() =>
            HorseFlightReplayEvaluator.Evaluate(271, replay));
    }

    private static HorseFlightState TerminalRun(uint seed, int rulesVersion)
    {
        var state = HorseFlightEngine.Start(seed, rulesVersion: rulesVersion);
        while (state.Phase == HorseFlightPhase.Running)
        {
            state = HorseFlightEngine.Step(state, HorseFlightInput.None).State;
        }
        return state;
    }
}
