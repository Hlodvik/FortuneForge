using System.Collections.Immutable;

namespace FortuneForge.Games.Flappy;

public enum FlappyInput
{
    None,
    Flap,
}

public enum FlappyPhase
{
    Playing,
    ObstacleCollision,
    GroundCollision,
    CeilingCollision,
}

public enum FlappyEventType
{
    Advanced,
    Flapped,
    Scored,
    ObstacleCollision,
    GroundCollision,
    CeilingCollision,
}

public sealed record FlappyObstacle(
    int Id,
    double X,
    double Width,
    double GapTop,
    double GapBottom,
    bool Scored);

public sealed record FlappyState(
    int Width,
    int Height,
    uint Seed,
    uint RandomState,
    double BirdY,
    double BirdVelocity,
    ImmutableArray<FlappyObstacle> Obstacles,
    int NextObstacleId,
    int Score,
    int BestScore,
    int Tick,
    FlappyPhase Phase)
{
    public int Level => 1 + (Score / FlappyEngine.ScoresPerLevel);
}

public sealed record FlappyTransition(
    FlappyState State,
    FlappyEventType Event,
    int ScoreGained);

public sealed record FlappyObstacleSnapshot(
    int Id,
    double X,
    double GapTop,
    double GapBottom);

public sealed record FlappySnapshot(
    int Width,
    int Height,
    uint Seed,
    int Tick,
    double BirdX,
    double BirdY,
    double BirdVelocity,
    int Score,
    int BestScore,
    int Level,
    FlappyPhase Phase,
    ImmutableArray<FlappyObstacleSnapshot> Obstacles);

/// <summary>
/// A compact, deterministic terminal-run replay. Flap ticks are zero-based step indexes.
/// </summary>
public sealed record FlappyReplay(
    int TotalTicks,
    ImmutableArray<int> FlapTicks);

public sealed record FlappyReplayResult(
    FlappySnapshot Snapshot,
    ImmutableArray<int> FlapTicks);
