using System.Collections.Immutable;

namespace FortuneForge.Games.TwentyFortyEight;

public enum TwentyFortyEightDirection
{
    Up,
    Right,
    Down,
    Left,
}

public enum TwentyFortyEightPhase
{
    Playing,
    Won,
    Lost,
}

public enum TwentyFortyEightEventType
{
    Started,
    Moved,
    NoMove,
    Won,
    Lost,
    Undone,
}

public sealed record TwentyFortyEightState(
    int Size,
    uint Seed,
    uint RandomState,
    ImmutableArray<int> Tiles,
    int Score,
    int Moves,
    TwentyFortyEightPhase Phase,
    int HighestTile)
{
    public int EmptyTileCount => Tiles.Count(tile => tile == 0);
}

public sealed record TwentyFortyEightTransition(
    TwentyFortyEightState State,
    TwentyFortyEightEventType Event,
    int ScoreGained,
    string Message);
