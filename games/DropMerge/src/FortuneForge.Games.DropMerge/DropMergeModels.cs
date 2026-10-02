using System.Collections.Immutable;

namespace FortuneForge.Games.DropMerge;

public enum DropMergePhase
{
    Playing,
    Lost,
}

public enum DropMergeEventType
{
    Started,
    Dropped,
    NoMove,
    BigTileReached,
    Lost,
    Undone,
}

public sealed record DropMergeState(
    int Columns,
    int Rows,
    uint Seed,
    uint RandomState,
    ImmutableArray<int> Tiles,
    int CurrentTile,
    int NextTile,
    int Score,
    int Moves,
    int Combo,
    int BestCombo,
    int TotalMerges,
    int SmallestTileClearCount,
    int SmallestQueuedTile,
    int NextBigTile,
    DropMergePhase Phase,
    int HighestTile)
{
    public int EmptyTileCount => Tiles.Count(tile => tile == 0);

    public int TempoLevel => 1 + (Moves / DropMergeEngine.MovesPerTempoLevel);

    public int DropDurationMilliseconds => Math.Max(
        DropMergeEngine.MinimumDropDurationMilliseconds,
        DropMergeEngine.StartingDropDurationMilliseconds - ((TempoLevel - 1) * DropMergeEngine.DropDurationStepMilliseconds));

    public int DropTimerMilliseconds => Math.Max(
        1,
        (int)Math.Round(
            DropMergeEngine.StartingDropTimerMilliseconds * Math.Pow(
                1 - DropMergeEngine.DropTimerReductionPerClear,
                SmallestTileClearCount)));
}

public sealed record DropMergeTransition(
    DropMergeState State,
    DropMergeEventType Event,
    int ScoreGained,
    int MergeCount,
    int DropColumn,
    int DropRow,
    int RemovedTile,
    int IntroducedTile,
    ImmutableArray<DropMergeMergeStep> MergeSteps,
    string Message);

public sealed record DropMergeMergeTile(int Row, int Column, int Value);

public sealed record DropMergeMergeStep(
    ImmutableArray<DropMergeMergeTile> Sources,
    DropMergeMergeTile Target,
    ImmutableArray<int> TilesAfter);
