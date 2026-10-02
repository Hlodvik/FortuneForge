using System.Collections.Immutable;

namespace FortuneForge.Games.Snake;

public enum SnakeDirection
{
    Up,
    Right,
    Down,
    Left,
}

public enum SnakePhase
{
    Playing,
    Won,
    Lost,
}

public enum SnakeEventType
{
    Started,
    Turned,
    Moved,
    AteFood,
    NoOp,
    Won,
    Lost,
}

public readonly record struct SnakePoint(int X, int Y);

public sealed record SnakeState(
    int Width,
    int Height,
    uint Seed,
    uint RandomState,
    ImmutableArray<SnakePoint> Body,
    SnakeDirection Direction,
    SnakePoint? Food,
    int Score,
    int BestScore,
    int Moves,
    SnakePhase Phase)
{
    public SnakePoint Head => Body[0];
    public int Length => Body.Length;
}

public sealed record SnakeTransition(
    SnakeState State,
    SnakeEventType Event,
    int ScoreGained,
    string Message);
