using System.Collections.Immutable;

namespace FortuneForge.Games.Flappy;

public static class FlappyEngine
{
    public const int DefaultWidth = 800;
    public const int DefaultHeight = 600;
    public const int TickMilliseconds = 20;
    public const double BirdX = 200;
    public const double BirdRadius = 12;
    public const double GravityPerTick = 0.45;
    public const double FlapVelocity = -7.5;
    public const double ObstacleWidth = 70;
    public const int ScoresPerLevel = 5;

    private const int InitialObstacleCount = 3;
    private const double InitialObstacleLead = 120;
    private const double ObstacleSpacing = 260;
    private const double BaseObstacleSpeed = 3.5;
    private const double SpeedIncreasePerLevel = 0.15;
    private const double MaximumObstacleSpeed = 5.3;
    private const double BaseGapHeight = 180;
    private const double GapReductionPerLevel = 4;
    private const double MinimumGapHeight = 132;
    private const double VerticalMargin = 24;

    public static FlappyState Start(
        uint seed,
        int width = DefaultWidth,
        int height = DefaultHeight,
        int bestScore = 0)
    {
        ValidateDimensions(width, height);
        if (bestScore < 0)
            throw new ArgumentOutOfRangeException(nameof(bestScore), "The best score cannot be negative.");

        var random = NormalizeSeed(seed);
        var obstacles = ImmutableArray.CreateBuilder<FlappyObstacle>(InitialObstacleCount);
        var nextObstacleId = 1;
        for (var index = 0; index < InitialObstacleCount; index++)
        {
            obstacles.Add(CreateObstacle(
                nextObstacleId++,
                width + InitialObstacleLead + (index * ObstacleSpacing),
                height,
                level: 1,
                ref random));
        }

        return new FlappyState(
            width,
            height,
            seed,
            random,
            height / 2d,
            0,
            obstacles.MoveToImmutable(),
            nextObstacleId,
            0,
            bestScore,
            0,
            FlappyPhase.Playing);
    }

    public static FlappyTransition Step(FlappyState state, FlappyInput input)
    {
        ValidateState(state);
        if (state.Phase != FlappyPhase.Playing)
            throw new InvalidOperationException("This Flappy run has ended. Start a new run to play again.");
        if (input is not FlappyInput.None and not FlappyInput.Flap)
            throw new ArgumentOutOfRangeException(nameof(input), input, "Unknown Flappy input.");

        var velocity = input == FlappyInput.Flap ? FlapVelocity : state.BirdVelocity;
        velocity += GravityPerTick;
        var birdY = state.BirdY + velocity;
        var speed = ObstacleSpeed(state.Level);
        var scoreGained = 0;
        var moved = new List<FlappyObstacle>(state.Obstacles.Length);
        foreach (var obstacle in state.Obstacles)
        {
            var next = obstacle with { X = obstacle.X - speed };
            if (!next.Scored && next.X + next.Width < BirdX)
            {
                next = next with { Scored = true };
                scoreGained++;
            }
            if (next.X + next.Width > 0)
                moved.Add(next);
        }

        var score = checked(state.Score + scoreGained);
        var level = 1 + (score / ScoresPerLevel);
        var random = state.RandomState;
        var nextObstacleId = state.NextObstacleId;
        var maximumX = moved.Count == 0
            ? state.Width + InitialObstacleLead - ObstacleSpacing
            : moved.Max(obstacle => obstacle.X);
        while (moved.Count < InitialObstacleCount)
        {
            maximumX += ObstacleSpacing;
            moved.Add(CreateObstacle(nextObstacleId++, maximumX, state.Height, level, ref random));
        }

        var obstacles = moved.ToImmutableArray();
        var phase = TerminalPhase(state.Width, state.Height, birdY, obstacles);
        var bestScore = Math.Max(state.BestScore, score);
        var nextState = state with
        {
            RandomState = random,
            BirdY = birdY,
            BirdVelocity = velocity,
            Obstacles = obstacles,
            NextObstacleId = nextObstacleId,
            Score = score,
            BestScore = bestScore,
            Tick = checked(state.Tick + 1),
            Phase = phase,
        };
        var eventType = phase switch
        {
            FlappyPhase.GroundCollision => FlappyEventType.GroundCollision,
            FlappyPhase.CeilingCollision => FlappyEventType.CeilingCollision,
            FlappyPhase.ObstacleCollision => FlappyEventType.ObstacleCollision,
            _ when scoreGained > 0 => FlappyEventType.Scored,
            _ when input == FlappyInput.Flap => FlappyEventType.Flapped,
            _ => FlappyEventType.Advanced,
        };
        return new FlappyTransition(nextState, eventType, scoreGained);
    }

    public static FlappySnapshot ToSnapshot(FlappyState state)
    {
        ValidateState(state);
        return new FlappySnapshot(
            state.Width,
            state.Height,
            state.Seed,
            state.Tick,
            BirdX,
            state.BirdY,
            state.BirdVelocity,
            state.Score,
            state.BestScore,
            state.Level,
            state.Phase,
            state.Obstacles.Select(static obstacle => new FlappyObstacleSnapshot(
                obstacle.Id,
                obstacle.X,
                obstacle.GapTop,
                obstacle.GapBottom)).ToImmutableArray());
    }

    private static FlappyObstacle CreateObstacle(
        int id,
        double x,
        int height,
        int level,
        ref uint random)
    {
        var gapHeight = Math.Max(
            MinimumGapHeight,
            BaseGapHeight - ((level - 1) * GapReductionPerLevel));
        var halfGap = gapHeight / 2;
        var minimumCenter = VerticalMargin + halfGap;
        var maximumCenter = height - VerticalMargin - halfGap;
        var unit = NextRandom(ref random) / (double)uint.MaxValue;
        var center = minimumCenter + ((maximumCenter - minimumCenter) * unit);
        return new FlappyObstacle(
            id,
            x,
            ObstacleWidth,
            center - halfGap,
            center + halfGap,
            Scored: false);
    }

    private static FlappyPhase TerminalPhase(
        int width,
        int height,
        double birdY,
        ImmutableArray<FlappyObstacle> obstacles)
    {
        if (birdY + BirdRadius >= height) return FlappyPhase.GroundCollision;
        if (birdY - BirdRadius <= 0) return FlappyPhase.CeilingCollision;
        foreach (var obstacle in obstacles)
        {
            var horizontalOverlap = BirdX + BirdRadius >= obstacle.X &&
                BirdX - BirdRadius <= obstacle.X + obstacle.Width;
            var outsideGap = birdY - BirdRadius <= obstacle.GapTop ||
                birdY + BirdRadius >= obstacle.GapBottom;
            if (horizontalOverlap && outsideGap) return FlappyPhase.ObstacleCollision;
        }
        return FlappyPhase.Playing;
    }

    private static double ObstacleSpeed(int level) => Math.Min(
        MaximumObstacleSpeed,
        BaseObstacleSpeed + ((level - 1) * SpeedIncreasePerLevel));

    private static uint NormalizeSeed(uint seed) => seed == 0 ? 0x9E3779B9u : seed;

    private static uint NextRandom(ref uint value)
    {
        unchecked
        {
            value ^= value << 13;
            value ^= value >> 17;
            value ^= value << 5;
            return value;
        }
    }

    private static void ValidateState(FlappyState state)
    {
        ArgumentNullException.ThrowIfNull(state);
        ValidateDimensions(state.Width, state.Height);
        if (!Enum.IsDefined(state.Phase))
            throw new ArgumentException("The Flappy phase is invalid.", nameof(state));
        if (!double.IsFinite(state.BirdY) || !double.IsFinite(state.BirdVelocity))
            throw new ArgumentException("Bird position and velocity must be finite.", nameof(state));
        if (state.RandomState == 0 || state.Obstacles.IsDefault || state.NextObstacleId < 1 ||
            state.Score < 0 || state.BestScore < state.Score || state.Tick < 0)
        {
            throw new ArgumentException("The Flappy state is incomplete or inconsistent.", nameof(state));
        }
        if (state.Obstacles.Select(obstacle => obstacle.Id).Distinct().Count() != state.Obstacles.Length ||
            state.Obstacles.Any(obstacle => obstacle.Id < 1 ||
                !double.IsFinite(obstacle.X) ||
                !double.IsFinite(obstacle.Width) || obstacle.Width <= 0 ||
                !double.IsFinite(obstacle.GapTop) || !double.IsFinite(obstacle.GapBottom) ||
                obstacle.GapTop < 0 || obstacle.GapBottom > state.Height ||
                obstacle.GapTop >= obstacle.GapBottom) ||
            state.Obstacles.Any(obstacle => obstacle.Id >= state.NextObstacleId))
        {
            throw new ArgumentException("The Flappy obstacle collection is invalid.", nameof(state));
        }
    }

    private static void ValidateDimensions(int width, int height)
    {
        if (width is < 320 or > 3840)
            throw new ArgumentOutOfRangeException(nameof(width), "Flappy width must be between 320 and 3840.");
        if (height is < 240 or > 2160)
            throw new ArgumentOutOfRangeException(nameof(height), "Flappy height must be between 240 and 2160.");
    }
}
