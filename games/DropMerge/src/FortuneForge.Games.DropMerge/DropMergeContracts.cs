namespace FortuneForge.Games.DropMerge;

public sealed class DropMergeAccessException() : InvalidOperationException("That Drop Merge game is not available.");
public sealed class DropMergeRuleException(string message) : InvalidOperationException(message);
public sealed record StartDropMergeRequest(uint? Seed);
public sealed record DropMergeDropRequest(int Column);
public sealed record DropMergeStatusResponse(bool Available, int Columns, int Rows, int FirstBigTile, string Mode);
public sealed record DropMergeGameResponse(Guid GameId, int Columns, int Rows, IReadOnlyList<int> Tiles, int CurrentTile, int NextTile, int Score, int Moves, int Combo, int BestCombo, int TotalMerges, int SmallestTileClearCount, int NextBigTile, int HighestTile, int EmptyTileCount, string Phase, bool CanUndo, string LastEvent, int ScoreGained, int MergeCount, int DropColumn, int DropRow, int RemovedTile, int IntroducedTile, IReadOnlyList<DropMergeMergeStep> MergeSteps, int TempoLevel, int DropDurationMilliseconds, int DropTimerMilliseconds, string Message)
{
    public Guid RunId { get; init; }
}
public sealed record DropMergeErrorResponse(string Code, string Message);

public static class DropMergeErrors
{
    public static DropMergeErrorResponse AuthenticationRequired() =>
        new("drop-merge-authentication-required", "Sign in to play Drop Merge.");

    public static DropMergeErrorResponse GameNotFound(DropMergeAccessException exception) =>
        new("drop-merge-game-not-found", exception.Message);

    public static DropMergeErrorResponse InvalidAction(string message) => new("drop-merge-invalid-action", message);

    public static DropMergeErrorResponse WorkerUnavailable() =>
        new("drop-merge-worker-unavailable", "The drop-merge game service is temporarily unavailable.");
}

public sealed record DropMergeComputeStart(uint Seed);

public sealed record DropMergeComputeDrop(DropMergeState State, int Column)
{
    public DropMergeTransition Apply()
    {
        ArgumentNullException.ThrowIfNull(State);
        if (State.Tiles.IsDefault)
            throw new ArgumentException("A Drop Merge board is required.");

        return DropMergeEngine.Drop(State, Column);
    }
}
