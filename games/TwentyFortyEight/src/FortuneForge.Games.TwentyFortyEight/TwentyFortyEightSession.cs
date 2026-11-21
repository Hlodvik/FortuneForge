using FortuneForge.Games.Abstractions;

namespace FortuneForge.Games.TwentyFortyEight;

public sealed record TwentyFortyEightSession(
    Guid Id, Guid RunId, string UserId, TwentyFortyEightState State, int UndoDepth,
    int CompletionEpisode, long EventVersion, DateTime StartedAtUtc, DateTime? CompletedAtUtc,
    string LastEvent, int ScoreGained, string Message) : IPuzzleSession<TwentyFortyEightState>
{
    public const string GameId = "2048";
    public const string StartComputeRoute = "/internal/v1/games/start";
    public const string MoveComputeRoute = "/internal/v1/games/move";
    public bool IsTerminal => State.Phase is TwentyFortyEightPhase.Won or TwentyFortyEightPhase.Lost;

    public static Guid CreateDurableId(DateTime nowUtc) => PuzzleSessionIdentity.CreateDurableId(nowUtc);
    public static bool IsDurableId(Guid id) => PuzzleSessionIdentity.IsDurableId(id);

    public static TwentyFortyEightStatusResponse Status() =>
        new(true, TwentyFortyEightEngine.DefaultSize, TwentyFortyEightEngine.TargetTile, "account-free-play");

    public static TwentyFortyEightSession Start(Guid id, Guid runId, string userId, TwentyFortyEightState state, DateTime nowUtc) =>
        new(id, runId, userId, state, 0, 1, 0, nowUtc, null, "started", 0, "Make a move with the arrow keys or controls.");

    public static uint SelectSeed(uint? seed) => PuzzleSessionIdentity.SelectSeed(seed);

    public static TwentyFortyEightDirection ParseDirection(string? direction) => direction?.Trim().ToLowerInvariant() switch
    {
        "up" => TwentyFortyEightDirection.Up,
        "right" => TwentyFortyEightDirection.Right,
        "down" => TwentyFortyEightDirection.Down,
        "left" => TwentyFortyEightDirection.Left,
        _ => throw new TwentyFortyEightRuleException("Direction must be up, right, down, or left."),
    };

    public TwentyFortyEightSession Move(TwentyFortyEightTransition transition, DateTime nowUtc)
    {
        var depth = transition.Event == TwentyFortyEightEventType.NoMove ? UndoDepth : checked(UndoDepth + 1);
        var name = transition.Event switch
        {
            TwentyFortyEightEventType.Moved => "moved",
            TwentyFortyEightEventType.NoMove => "no-move",
            TwentyFortyEightEventType.Won => "won",
            TwentyFortyEightEventType.Lost => "lost",
            _ => throw new ArgumentOutOfRangeException(nameof(transition), transition.Event, "Unknown 2048 move event."),
        };
        return Change(transition.State, depth, name, transition.ScoreGained, transition.Message, nowUtc);
    }

    public TwentyFortyEightSession Undo(TwentyFortyEightState previous, DateTime nowUtc)
    {
        RequireUndo();
        return Change(previous, UndoDepth - 1, "undone", 0, "Move undone.", nowUtc);
    }

    public void RequireUndo()
    {
        if (UndoDepth == 0)
            throw new TwentyFortyEightRuleException("There is no move to undo.");
    }

    public TwentyFortyEightSession Continue(DateTime nowUtc)
    {
        if (State.Phase != TwentyFortyEightPhase.Won)
            throw new TwentyFortyEightRuleException("Continue is only available after reaching 2048.");

        return Change(State with { Phase = TwentyFortyEightPhase.Playing }, UndoDepth,
            "continued", 0, "Keep going. Build the largest tile you can.", nowUtc);
    }

    public TwentyFortyEightSession Reset(Guid runId, TwentyFortyEightState state, DateTime nowUtc) =>
        Start(Id, runId, UserId, state, nowUtc) with { Message = "New game started." };

    public TwentyFortyEightGameResponse ToResponse() => new(
        Id, State.Size, State.Tiles, State.Score, State.Moves, State.HighestTile, State.Phase switch
        {
            TwentyFortyEightPhase.Playing => "playing",
            TwentyFortyEightPhase.Won => "won",
            TwentyFortyEightPhase.Lost => "lost",
            _ => throw new InvalidOperationException("Unknown 2048 phase."),
        }, UndoDepth > 0, LastEvent, ScoreGained, Message) { RunId = RunId };

    private TwentyFortyEightSession Change(TwentyFortyEightState state, int depth, string kind,
        int scoreGained, string message, DateTime nowUtc)
    {
        var reopened = IsTerminal && state.Phase == TwentyFortyEightPhase.Playing;
        var completed = state.Moves > 0 && state.Phase is TwentyFortyEightPhase.Won or TwentyFortyEightPhase.Lost;
        return this with
        {
            State = state,
            UndoDepth = depth,
            CompletionEpisode = reopened ? checked(CompletionEpisode + 1) : CompletionEpisode,
            EventVersion = checked(EventVersion + 1),
            CompletedAtUtc = completed ? CompletedAtUtc ?? nowUtc : null,
            LastEvent = kind,
            ScoreGained = scoreGained,
            Message = message,
        };
    }
}
