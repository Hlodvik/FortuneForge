using System.Collections.Immutable;

namespace FortuneForge.Games.Flappy;

/// <summary>
/// Replays a client input stream through the authoritative Flappy engine.
/// The evaluator deliberately accepts only a bounded terminal run, so callers can persist
/// a compact audit record without trusting a client-reported score or ticking the server
/// for every animation frame.
/// </summary>
public static class FlappyReplayEvaluator
{
    public const int MaximumTicks = 9_000;
    public const int MaximumFlaps = 1_500;

    public static FlappyReplayResult Evaluate(uint seed, FlappyReplay replay)
    {
        ArgumentNullException.ThrowIfNull(replay);
        if (replay.TotalTicks is < 1 or > MaximumTicks)
        {
            throw new ArgumentOutOfRangeException(
                nameof(replay),
                $"A Flappy replay must contain between 1 and {MaximumTicks} ticks.");
        }

        if (replay.FlapTicks.IsDefault || replay.FlapTicks.Length > MaximumFlaps)
        {
            throw new ArgumentException(
                $"A Flappy replay must contain no more than {MaximumFlaps} flaps.",
                nameof(replay));
        }

        ValidateFlapTicks(replay);

        var state = FlappyEngine.Start(seed);
        var flapIndex = 0;
        for (var tick = 0; tick < replay.TotalTicks; tick++)
        {
            var flap = flapIndex < replay.FlapTicks.Length && replay.FlapTicks[flapIndex] == tick;
            if (flap) flapIndex++;

            state = FlappyEngine.Step(state, flap ? FlappyInput.Flap : FlappyInput.None).State;
            if (state.Phase != FlappyPhase.Playing && tick + 1 != replay.TotalTicks)
            {
                throw new ArgumentException(
                    "The replay contains input after its terminal collision.",
                    nameof(replay));
            }
        }

        if (state.Phase == FlappyPhase.Playing)
        {
            throw new ArgumentException(
                "A submitted Flappy replay must end with a collision.",
                nameof(replay));
        }

        return new FlappyReplayResult(
            FlappyEngine.ToSnapshot(state),
            replay.FlapTicks);
    }

    private static void ValidateFlapTicks(FlappyReplay replay)
    {
        var previous = -1;
        foreach (var tick in replay.FlapTicks)
        {
            if (tick < 0 || tick >= replay.TotalTicks)
            {
                throw new ArgumentException(
                    "Every flap tick must be within the replay duration.",
                    nameof(replay));
            }

            if (tick <= previous)
            {
                throw new ArgumentException(
                    "Flap ticks must be strictly ascending and unique.",
                    nameof(replay));
            }

            previous = tick;
        }
    }
}
