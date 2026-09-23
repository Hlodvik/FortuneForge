using System.Collections.Immutable;

namespace FortuneForge.Games.HorseFlight;

/// <summary>
/// Replays browser input timelines through the authoritative Horse Flight engine.
/// This permits a single final score submission instead of trusting a client score or
/// sending a network request on every simulation frame.
/// </summary>
public static class HorseFlightReplayEvaluator
{
    // A complete scenic run lasts five minutes at 20ms per simulation tick.
    // Keep enough headroom for a player who dashes through the whole transition.
    public const int MaximumTicks = 20_000;
    public const int MaximumJumps = 1_500;
    public const int MaximumRightClicks = 20_000;

    public static HorseFlightReplayResult Evaluate(uint seed, HorseFlightReplay replay)
    {
        ArgumentNullException.ThrowIfNull(replay);
        if (replay.TotalTicks is < 1 or > MaximumTicks)
        {
            throw new ArgumentOutOfRangeException(
                nameof(replay),
                $"A Horse Flight replay must contain between 1 and {MaximumTicks} ticks.");
        }
        if (replay.JumpTicks.IsDefault || replay.JumpTicks.Length > MaximumJumps)
        {
            throw new ArgumentException(
                $"A Horse Flight replay must contain no more than {MaximumJumps} jumps.",
                nameof(replay));
        }
        if (replay.RightClickTicks.IsDefault || replay.RightClickTicks.Length > MaximumRightClicks)
        {
            throw new ArgumentException(
                $"A Horse Flight replay must contain no more than {MaximumRightClicks} right-click actions.",
                nameof(replay));
        }

        ValidateTicks(replay.JumpTicks, replay.TotalTicks, "jump", nameof(replay));
        ValidateTicks(replay.RightClickTicks, replay.TotalTicks, "right-click", nameof(replay));
        if (replay.JumpTicks.Intersect(replay.RightClickTicks).Any())
        {
            throw new ArgumentException(
                "A replay cannot use a jump and a right-click action on the same tick.",
                nameof(replay));
        }

        var state = HorseFlightEngine.Start(seed);
        var jumpIndex = 0;
        var rightClickIndex = 0;
        for (var tick = 0; tick < replay.TotalTicks; tick++)
        {
            var jump = jumpIndex < replay.JumpTicks.Length && replay.JumpTicks[jumpIndex] == tick;
            if (jump) jumpIndex++;
            var rightClick = rightClickIndex < replay.RightClickTicks.Length && replay.RightClickTicks[rightClickIndex] == tick;
            if (rightClick) rightClickIndex++;

            var input = jump
                ? HorseFlightInput.Jump
                : rightClick
                    ? HorseFlightInput.RightClick
                    : HorseFlightInput.None;
            state = HorseFlightEngine.Step(state, input).State;
            if (state.Phase != HorseFlightPhase.Running && tick + 1 != replay.TotalTicks)
            {
                throw new ArgumentException(
                    "The replay contains input after its terminal collision.",
                    nameof(replay));
            }
        }

        if (state.Phase == HorseFlightPhase.Running)
        {
            throw new ArgumentException(
                "A submitted Horse Flight replay must end with a collision or fall.",
                nameof(replay));
        }

        return new HorseFlightReplayResult(
            HorseFlightEngine.ToSnapshot(state),
            replay.JumpTicks)
        {
            RightClickTicks = replay.RightClickTicks,
        };
    }

    private static void ValidateTicks(
        ImmutableArray<int> ticks,
        int totalTicks,
        string actionName,
        string parameterName)
    {
        var previous = -1;
        foreach (var tick in ticks)
        {
            if (tick < 0 || tick >= totalTicks)
            {
                throw new ArgumentException(
                    $"Every {actionName} tick must be within the replay duration.",
                    parameterName);
            }
            if (tick <= previous)
            {
                throw new ArgumentException(
                    $"{char.ToUpperInvariant(actionName[0]) + actionName[1..]} ticks must be strictly ascending and unique.",
                    parameterName);
            }
            previous = tick;
        }
    }
}
