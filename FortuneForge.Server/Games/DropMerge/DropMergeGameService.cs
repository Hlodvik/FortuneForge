using System.Collections.Concurrent;
using System.Security.Cryptography;
using FortuneForge.Games.DropMerge;

namespace FortuneForge.Server.Games.DropMerge;

/// <summary>Owns account-isolated, free-play Drop Merge sessions.</summary>
public sealed class DropMergeGameService
{
    private readonly ConcurrentDictionary<Guid, DropMergeSession> games = new();

    public DropMergeStatusResponse Status() => new(true, DropMergeEngine.DefaultColumns, DropMergeEngine.DefaultRows, DropMergeEngine.FirstBigTile, "account-free-play");

    public DropMergeGameResponse Start(string userId, uint? seed) => ToResponse(Create(userId, seed));

    public DropMergeGameResponse Drop(string userId, Guid gameId, int column) => Change(userId, gameId, session =>
    {
        var transition = DropMergeEngine.Drop(session.State, column);
        if (transition.Event is not DropMergeEventType.NoMove)
            session.History.Push(session.State);
        session.State = transition.State;
        session.LastEvent = EventName(transition.Event);
        session.ScoreGained = transition.ScoreGained;
        session.MergeCount = transition.MergeCount;
        session.DropColumn = transition.DropColumn;
        session.DropRow = transition.DropRow;
        session.RemovedTile = transition.RemovedTile;
        session.IntroducedTile = transition.IntroducedTile;
        session.MergeSteps = transition.MergeSteps;
        session.Message = transition.Message;
    });

    public DropMergeGameResponse Undo(string userId, Guid gameId) => Change(userId, gameId, session =>
    {
        if (session.History.Count == 0)
            throw new DropMergeRuleException("There is no drop to undo.");

        session.State = session.History.Pop();
        session.LastEvent = "undone";
        session.ScoreGained = 0;
        session.MergeCount = 0;
        session.DropColumn = -1;
        session.DropRow = -1;
        session.RemovedTile = 0;
        session.IntroducedTile = 0;
        session.MergeSteps = Array.Empty<DropMergeMergeStep>();
        session.Message = "Drop undone.";
    });

    public DropMergeGameResponse Reset(string userId, Guid gameId, uint? seed) => Change(userId, gameId, session =>
    {
        session.State = DropMergeEngine.Start(Seed(seed));
        session.History.Clear();
        session.LastEvent = "started";
        session.ScoreGained = 0;
        session.MergeCount = 0;
        session.DropColumn = -1;
        session.DropRow = -1;
        session.RemovedTile = 0;
        session.IntroducedTile = 0;
        session.MergeSteps = Array.Empty<DropMergeMergeStep>();
        session.Message = "New run started. Drop the tile into any column.";
    });

    private DropMergeSession Create(string userId, uint? seed)
    {
        var session = new DropMergeSession(userId, DropMergeEngine.Start(Seed(seed)));
        if (!games.TryAdd(session.Id, session))
            throw new InvalidOperationException("Could not open a Drop Merge game.");
        return session;
    }

    private DropMergeGameResponse Change(string userId, Guid gameId, Action<DropMergeSession> change)
    {
        if (!games.TryGetValue(gameId, out var session) || !string.Equals(session.UserId, userId, StringComparison.Ordinal))
            throw new DropMergeAccessException();
        lock (session.SyncRoot)
        {
            change(session);
            return ToResponse(session);
        }
    }

    private static uint Seed(uint? seed) => seed is > 0 ? seed.Value : (uint)RandomNumberGenerator.GetInt32(1, int.MaxValue);

    private static string EventName(DropMergeEventType eventType) => eventType switch
    {
        DropMergeEventType.Started => "started",
        DropMergeEventType.Dropped => "dropped",
        DropMergeEventType.NoMove => "no-move",
        DropMergeEventType.BigTileReached => "big-tile-reached",
        DropMergeEventType.Lost => "lost",
        DropMergeEventType.Undone => "undone",
        _ => throw new ArgumentOutOfRangeException(nameof(eventType), eventType, "Unknown Drop Merge event."),
    };

    private static DropMergeGameResponse ToResponse(DropMergeSession session) => new(
        session.Id, session.State.Columns, session.State.Rows, session.State.Tiles, session.State.CurrentTile,
        session.State.NextTile, session.State.Score, session.State.Moves, session.State.Combo, session.State.BestCombo,
        session.State.TotalMerges, session.State.SmallestTileClearCount, session.State.NextBigTile,
        session.State.HighestTile, session.State.EmptyTileCount, session.State.Phase switch
        {
            DropMergePhase.Playing => "playing",
            DropMergePhase.Lost => "lost",
            _ => throw new InvalidOperationException("Unknown Drop Merge phase."),
        }, session.History.Count > 0, session.LastEvent, session.ScoreGained, session.MergeCount,
        session.DropColumn, session.DropRow, session.RemovedTile, session.IntroducedTile, session.MergeSteps,
        session.State.TempoLevel, session.State.DropDurationMilliseconds, session.State.DropTimerMilliseconds, session.Message);

    private sealed class DropMergeSession(string userId, DropMergeState state)
    {
        public Guid Id { get; } = Guid.NewGuid();
        public object SyncRoot { get; } = new();
        public string UserId { get; } = userId;
        public DropMergeState State { get; set; } = state;
        public Stack<DropMergeState> History { get; } = new();
        public string LastEvent { get; set; } = "started";
        public int ScoreGained { get; set; }
        public int MergeCount { get; set; }
        public int DropColumn { get; set; } = -1;
        public int DropRow { get; set; } = -1;
        public int RemovedTile { get; set; }
        public int IntroducedTile { get; set; }
        public IReadOnlyList<DropMergeMergeStep> MergeSteps { get; set; } = Array.Empty<DropMergeMergeStep>();
        public string Message { get; set; } = "Drop the tile into any column.";
    }
}

public sealed class DropMergeAccessException() : InvalidOperationException("That Drop Merge game is not available.");
public sealed class DropMergeRuleException(string message) : InvalidOperationException(message);
public sealed record StartDropMergeRequest(uint? Seed);
public sealed record DropMergeDropRequest(int Column);
public sealed record DropMergeStatusResponse(bool Available, int Columns, int Rows, int FirstBigTile, string Mode);
public sealed record DropMergeGameResponse(Guid GameId, int Columns, int Rows, IReadOnlyList<int> Tiles, int CurrentTile, int NextTile, int Score, int Moves, int Combo, int BestCombo, int TotalMerges, int SmallestTileClearCount, int NextBigTile, int HighestTile, int EmptyTileCount, string Phase, bool CanUndo, string LastEvent, int ScoreGained, int MergeCount, int DropColumn, int DropRow, int RemovedTile, int IntroducedTile, IReadOnlyList<DropMergeMergeStep> MergeSteps, int TempoLevel, int DropDurationMilliseconds, int DropTimerMilliseconds, string Message);
public sealed record DropMergeErrorResponse(string Code, string Message);
