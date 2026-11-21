using System.Collections.Immutable;

namespace FortuneForge.Games.Flappy;

/// <summary>The historical canonical replay format shared by free and paid recorded Flappy runs.</summary>
public static class FlappyRunReplay
{
    public static string CanonicalizeReplay(FlappyReplay replay)
    {
        FlappyReplayEvaluator.ValidateReplayInput(replay);
        return $"v1\\n{replay.TotalTicks}\\n{string.Join(',', replay.FlapTicks)}";
    }

    public static FlappyReplay ParseCanonicalReplay(string canonical)
    {
        ArgumentNullException.ThrowIfNull(canonical);
        var parts = canonical.Split("\\n", StringSplitOptions.None);
        if (parts.Length != 3 || parts[0] != "v1" ||
            !int.TryParse(parts[1], System.Globalization.NumberStyles.None, System.Globalization.CultureInfo.InvariantCulture, out var ticks))
            throw new ArgumentException("The stored Flappy replay is invalid.", nameof(canonical));
        var flaps = ImmutableArray.CreateBuilder<int>();
        if (parts[2].Length > 0)
            foreach (var value in parts[2].Split(','))
            {
                if (!int.TryParse(value, System.Globalization.NumberStyles.None, System.Globalization.CultureInfo.InvariantCulture, out var tick))
                    throw new ArgumentException("The stored Flappy replay is invalid.", nameof(canonical));
                flaps.Add(tick);
            }
        var replay = new FlappyReplay(ticks, flaps.ToImmutable());
        if (CanonicalizeReplay(replay) != canonical)
            throw new ArgumentException("The stored Flappy replay is not canonical.", nameof(canonical));
        return replay;
    }

    public static string TerminalName(FlappyPhase phase) => phase switch
    {
        FlappyPhase.ObstacleCollision => "obstacle-collision",
        FlappyPhase.GroundCollision => "ground-collision",
        FlappyPhase.CeilingCollision => "ceiling-collision",
        _ => throw new ArgumentOutOfRangeException(nameof(phase), "A Flappy free run must be terminal."),
    };

}
