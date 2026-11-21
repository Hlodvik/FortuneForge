using FortuneForge.Games.Abstractions;

namespace FortuneForge.Games.DropMerge;

public sealed record DropMergeSession(Guid Id, Guid RunId, string UserId, DropMergeState State, int UndoDepth,
    int CompletionEpisode, long EventVersion, DateTime StartedAtUtc, DateTime? CompletedAtUtc,
    string LastEvent, int ScoreGained, int MergeCount, int DropColumn, int DropRow, int RemovedTile,
    int IntroducedTile, IReadOnlyList<DropMergeMergeStep> MergeSteps, string Message) : IPuzzleSession<DropMergeState>
{
    public const string GameId = "drop-merge";
    public const string StartComputeRoute = "/internal/v1/games/start";
    public const string DropComputeRoute = "/internal/v1/games/drop";

    public static DropMergeStatusResponse Status() =>
        new(true, DropMergeEngine.DefaultColumns, DropMergeEngine.DefaultRows, DropMergeEngine.FirstBigTile, "account-free-play");

    public static DropMergeSession Start(Guid id, Guid runId, string userId, DropMergeState state, DateTime nowUtc) =>
        new(id, runId, userId, state, 0, 1, 0, nowUtc, null, "started",
            0, 0, -1, -1, 0, 0, [], "Drop the tile into any column.");

    public DropMergeSession Drop(DropMergeTransition transition, DateTime nowUtc) => this with
    {
        State = transition.State,
        UndoDepth = transition.Event == DropMergeEventType.NoMove ? UndoDepth : checked(UndoDepth + 1),
        EventVersion = checked(EventVersion + 1),
        CompletedAtUtc = transition.State.Moves > 0 && transition.State.Phase == DropMergePhase.Lost
            ? CompletedAtUtc ?? nowUtc : null,
        LastEvent = transition.Event switch
        {
            DropMergeEventType.Dropped => "dropped",
            DropMergeEventType.NoMove => "no-move",
            DropMergeEventType.BigTileReached => "big-tile-reached",
            DropMergeEventType.Lost => "lost",
            _ => throw new ArgumentOutOfRangeException(nameof(transition), transition.Event, "Unknown Drop Merge event."),
        },
        ScoreGained = transition.ScoreGained,
        MergeCount = transition.MergeCount,
        DropColumn = transition.DropColumn,
        DropRow = transition.DropRow,
        RemovedTile = transition.RemovedTile,
        IntroducedTile = transition.IntroducedTile,
        MergeSteps = transition.MergeSteps,
        Message = transition.Message,
    };

    public DropMergeSession Undo(DropMergeState before)
    {
        RequireUndo();
        var reopened = State.Phase == DropMergePhase.Lost && before.Phase == DropMergePhase.Playing;
        return this with
        {
            State = before,
            UndoDepth = UndoDepth - 1,
            CompletionEpisode = reopened ? checked(CompletionEpisode + 1) : CompletionEpisode,
            EventVersion = checked(EventVersion + 1),
            CompletedAtUtc = null,
            LastEvent = "undone",
            ScoreGained = 0,
            MergeCount = 0,
            DropColumn = -1,
            DropRow = -1,
            RemovedTile = 0,
            IntroducedTile = 0,
            MergeSteps = [],
            Message = "Drop undone.",
        };
    }

    public void RequireUndo()
    {
        if (UndoDepth == 0)
            throw new DropMergeRuleException("There is no drop to undo.");
    }

    public DropMergeSession Reset(Guid runId, DropMergeState state, DateTime nowUtc) =>
        Start(Id, runId, UserId, state, nowUtc) with { Message = "New run started. Drop the tile into any column." };

    public DropMergeGameResponse ToResponse() => new(
        Id, State.Columns, State.Rows, State.Tiles, State.CurrentTile, State.NextTile,
        State.Score, State.Moves, State.Combo, State.BestCombo, State.TotalMerges, State.SmallestTileClearCount,
        State.NextBigTile, State.HighestTile, State.EmptyTileCount, State.Phase switch
        {
            DropMergePhase.Playing => "playing",
            DropMergePhase.Lost => "lost",
            _ => throw new InvalidOperationException("Unknown Drop Merge phase."),
        }, UndoDepth > 0, LastEvent, ScoreGained, MergeCount, DropColumn, DropRow, RemovedTile,
        IntroducedTile, MergeSteps, State.TempoLevel, State.DropDurationMilliseconds, State.DropTimerMilliseconds, Message)
        { RunId = RunId };
}
