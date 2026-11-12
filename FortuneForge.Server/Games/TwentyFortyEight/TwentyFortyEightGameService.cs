using System.Collections.Concurrent;
using System.Security.Cryptography;
using FortuneForge.Games.TwentyFortyEight;
using FortuneForge.Games.Abstractions.Analytics;
using FortuneForge.Games.Abstractions.Auditing;
using FortuneForge.Server.Activity;
using FortuneForge.Server.GameWorkers;
using FortuneForge.Server.SecurityAudit;

namespace FortuneForge.Server.Games.TwentyFortyEight;

/// <summary>Owns account-isolated, free-play 2048 sessions.</summary>
public sealed class TwentyFortyEightGameService(IGameAnalyticsSink? analytics = null, IGameSecurityEventSink? securityEvents = null,
    GameWorkerClient? worker = null)
{
    private const string WorkerId = "2048";
    private readonly ConcurrentDictionary<Guid, TwentyFortyEightSession> games = new();

    public TwentyFortyEightStatusResponse Status() => new(true, TwentyFortyEightEngine.DefaultSize, TwentyFortyEightEngine.TargetTile, "account-free-play");

    public TwentyFortyEightGameResponse Start(string userId, uint? seed) => ToResponse(Create(userId, TwentyFortyEightEngine.Start(Seed(seed))));

    public async Task<TwentyFortyEightGameResponse> StartAuditedAsync(string userId, uint? seed, CancellationToken cancellationToken = default)
    {
        var session = Create(userId, await OpenBoardAsync(userId, seed, cancellationToken));
        try { await RecordAsync(session, "started", cancellationToken); }
        catch { games.TryRemove(session.Id, out _); throw; }
        return ToResponse(session);
    }

    public Task<TwentyFortyEightGameResponse> MoveAuditedAsync(string userId, Guid gameId, string? direction, CancellationToken cancellationToken = default) =>
        ChangeAuditedAsync(userId, gameId, async session =>
        {
            var before = session.State;
            var parsedDirection = ParseDirection(direction);
            var transition = worker?.IsConfigured(WorkerId) == true
                ? await worker.InvokeAsync<object, TwentyFortyEightTransition>(WorkerId, userId, "/internal/v1/games/move",
                    new { State = before, Direction = parsedDirection }, cancellationToken)
                : TwentyFortyEightEngine.Move(before, parsedDirection);
            return Change(userId, gameId, current =>
            {
                if (!ReferenceEquals(current.State, before)) throw new TwentyFortyEightRuleException("The board changed. Refresh and try again.");
                ApplyMove(current, transition);
            });
        }, cancellationToken);

    public Task<TwentyFortyEightGameResponse> UndoAuditedAsync(string userId, Guid gameId, CancellationToken cancellationToken = default) =>
        ChangeAuditedAsync(userId, gameId, _ => Task.FromResult(Undo(userId, gameId)), cancellationToken);

    public Task<TwentyFortyEightGameResponse> ContinueAuditedAsync(string userId, Guid gameId, CancellationToken cancellationToken = default) =>
        ChangeAuditedAsync(userId, gameId, _ => Task.FromResult(Continue(userId, gameId)), cancellationToken);

    public Task<TwentyFortyEightGameResponse> ResetAuditedAsync(string userId, Guid gameId, uint? seed, CancellationToken cancellationToken = default) =>
        ChangeAuditedAsync(userId, gameId, async _ =>
        {
            var board = await OpenBoardAsync(userId, seed, cancellationToken);
            return Change(userId, gameId, session => ApplyReset(session, board));
        }, cancellationToken);

    public TwentyFortyEightGameResponse Get(string userId, Guid gameId)
    {
        if (!games.TryGetValue(gameId, out var session) || !string.Equals(session.UserId, userId, StringComparison.Ordinal))
            throw new TwentyFortyEightAccessException();
        lock (session.SyncRoot)
        {
            TryRecordCompleted(session);
            return ToResponse(session);
        }
    }

    public TwentyFortyEightGameResponse Move(string userId, Guid gameId, string? direction) =>
        Change(userId, gameId, session => ApplyMove(session, TwentyFortyEightEngine.Move(session.State, ParseDirection(direction))));

    private static void ApplyMove(TwentyFortyEightSession session, TwentyFortyEightTransition transition)
    {
        if (transition.Event is not TwentyFortyEightEventType.NoMove)
            session.History.Push(session.State);
        session.State = transition.State;
        session.LastEvent = EventName(transition.Event);
        session.ScoreGained = transition.ScoreGained;
        session.Message = transition.Message;
    }

    public TwentyFortyEightGameResponse Undo(string userId, Guid gameId) => Change(userId, gameId, session =>
    {
        if (session.History.Count == 0)
            throw new TwentyFortyEightRuleException("There is no move to undo.");

        session.State = session.History.Pop();
        session.LastEvent = "undone";
        session.ScoreGained = 0;
        session.Message = "Move undone.";
    });

    public TwentyFortyEightGameResponse Continue(string userId, Guid gameId) => Change(userId, gameId, session =>
    {
        if (session.State.Phase is not TwentyFortyEightPhase.Won)
            throw new TwentyFortyEightRuleException("Continue is only available after reaching 2048.");

        session.State = session.State with { Phase = TwentyFortyEightPhase.Playing };
        session.LastEvent = "continued";
        session.ScoreGained = 0;
        session.Message = "Keep going. Build the largest tile you can.";
    });

    public TwentyFortyEightGameResponse Reset(string userId, Guid gameId, uint? seed) =>
        Change(userId, gameId, session => ApplyReset(session, TwentyFortyEightEngine.Start(Seed(seed))));

    private static void ApplyReset(TwentyFortyEightSession session, TwentyFortyEightState board)
    {
        session.State = board;
        session.RunId = Guid.NewGuid();
        session.CompletionEpisode = 1;
        session.StartedAtUtc = DateTime.UtcNow;
        session.CompletedAtUtc = null;
        session.AnalyticsRecorded = false;
        session.History.Clear();
        session.LastEvent = "started";
        session.ScoreGained = 0;
        session.Message = "New game started.";
    }

    private Task<TwentyFortyEightState> OpenBoardAsync(string userId, uint? seed, CancellationToken cancellationToken)
    {
        var value = Seed(seed);
        return worker?.IsConfigured(WorkerId) == true
            ? worker.InvokeAsync<object, TwentyFortyEightState>(WorkerId, userId, "/internal/v1/games/start", new { Seed = value }, cancellationToken)
            : Task.FromResult(TwentyFortyEightEngine.Start(value));
    }

    private TwentyFortyEightSession Create(string userId, TwentyFortyEightState board)
    {
        var session = new TwentyFortyEightSession(userId, board);
        if (!games.TryAdd(session.Id, session))
            throw new InvalidOperationException("Could not open a 2048 game.");
        return session;
    }

    private TwentyFortyEightGameResponse Change(string userId, Guid gameId, Action<TwentyFortyEightSession> change)
    {
        if (!games.TryGetValue(gameId, out var session) || !string.Equals(session.UserId, userId, StringComparison.Ordinal))
            throw new TwentyFortyEightAccessException();
        lock (session.SyncRoot)
        {
            // Preserve a terminal event before reset, undo or continue changes its state.
            TryRecordCompleted(session);
            var priorRunId = session.RunId;
            var wasTerminal = session.State.Phase is TwentyFortyEightPhase.Won or TwentyFortyEightPhase.Lost;
            change(session);
            if (session.RunId == priorRunId && wasTerminal &&
                session.State.Phase is TwentyFortyEightPhase.Playing)
            {
                session.CompletionEpisode++;
                session.CompletedAtUtc = null;
            }
            session.EventVersion++;
            TryRecordCompleted(session);
            return ToResponse(session);
        }
    }

    private async Task<TwentyFortyEightGameResponse> ChangeAuditedAsync(string userId, Guid gameId,
        Func<TwentyFortyEightSession, Task<TwentyFortyEightGameResponse>> change, CancellationToken cancellationToken)
    {
        if (!games.TryGetValue(gameId, out var session) || session.UserId != userId)
            throw new TwentyFortyEightAccessException();
        await session.AuditGate.WaitAsync(cancellationToken);
        try
        {
            var response = await change(session);
            await RecordAsync(session, response.LastEvent, cancellationToken);
            return response;
        }
        finally { session.AuditGate.Release(); }
    }

    private Task RecordAsync(TwentyFortyEightSession session, string eventKind, CancellationToken cancellationToken)
    {
        object payload;
        string episodeId;
        int episode;
        long version;
        RoundAuditCapture? completion;
        lock (session.SyncRoot)
        {
            episode = session.CompletionEpisode;
            episodeId = $"{session.RunId:N}-episode-{episode}";
            version = session.EventVersion;
            payload = new { session.Id, session.RunId, session.CompletionEpisode, session.StartedAtUtc, session.CompletedAtUtc,
                session.State, session.LastEvent, session.ScoreGained, session.History.Count };
            completion = session.State.Moves > 0 && session.State.Phase is TwentyFortyEightPhase.Won or TwentyFortyEightPhase.Lost
                ? GameSecurityEventSink.FreeCompletion("2048", episodeId, session.Id.ToString("N"),
                    episode, session.StartedAtUtc, session.CompletedAtUtc ?? DateTime.UtcNow, payload,
                    session.UserId, "2048-private-state-v1", PracticeOutcomeAudit.Check(session.State),
                    PracticeOutcomeAudit.TwentyFortyEightVersion)
                : null;
        }
        return GameSecurityEventSink.WriteAsync(securityEvents, "2048", episodeId,
            version, DateTime.UtcNow, payload, [session.UserId], eventKind, session.Id.ToString("N"),
            completion, cancellationToken);
    }

    private void TryRecordCompleted(TwentyFortyEightSession session)
    {
        if (session.State.Moves == 0 || session.State.Phase is not (TwentyFortyEightPhase.Won or TwentyFortyEightPhase.Lost)) return;
        session.CompletedAtUtc ??= DateTime.UtcNow;
        if (session.AnalyticsRecorded) return;
        session.AnalyticsRecorded = analytics?.TryEnqueuePractice(new GameAnalyticsSettlement(
            "2048", session.RunId.ToString("N"), session.UserId, "free", true,
            session.CompletedAtUtc.Value, session.StartedAtUtc, 0, 0, "in-memory-2048-runs", session.RunId.ToString("N"))) ?? false;
    }

    private static uint Seed(uint? seed) => seed is > 0 ? seed.Value : (uint)RandomNumberGenerator.GetInt32(1, int.MaxValue);

    private static TwentyFortyEightDirection ParseDirection(string? direction) => direction?.Trim().ToLowerInvariant() switch
    {
        "up" => TwentyFortyEightDirection.Up,
        "right" => TwentyFortyEightDirection.Right,
        "down" => TwentyFortyEightDirection.Down,
        "left" => TwentyFortyEightDirection.Left,
        _ => throw new TwentyFortyEightRuleException("Direction must be up, right, down, or left."),
    };

    private static string EventName(TwentyFortyEightEventType eventType) => eventType switch
    {
        TwentyFortyEightEventType.Started => "started",
        TwentyFortyEightEventType.Moved => "moved",
        TwentyFortyEightEventType.NoMove => "no-move",
        TwentyFortyEightEventType.Won => "won",
        TwentyFortyEightEventType.Lost => "lost",
        TwentyFortyEightEventType.Undone => "undone",
        TwentyFortyEightEventType.Continued => "continued",
        _ => throw new ArgumentOutOfRangeException(nameof(eventType), eventType, "Unknown 2048 event."),
    };

    private static TwentyFortyEightGameResponse ToResponse(TwentyFortyEightSession session) => new(
        session.Id, session.State.Size, session.State.Tiles, session.State.Score, session.State.Moves,
        session.State.HighestTile, session.State.Phase switch
        {
            TwentyFortyEightPhase.Playing => "playing",
            TwentyFortyEightPhase.Won => "won",
            TwentyFortyEightPhase.Lost => "lost",
            _ => throw new InvalidOperationException("Unknown 2048 phase."),
        }, session.History.Count > 0, session.LastEvent, session.ScoreGained, session.Message) { RunId = session.RunId };

    private sealed class TwentyFortyEightSession(string userId, TwentyFortyEightState state)
    {
        public Guid Id { get; } = Guid.NewGuid();
        public Guid RunId { get; set; } = Guid.NewGuid();
        public int CompletionEpisode { get; set; } = 1;
        public DateTime StartedAtUtc { get; set; } = DateTime.UtcNow;
        public DateTime? CompletedAtUtc { get; set; }
        public bool AnalyticsRecorded { get; set; }
        public object SyncRoot { get; } = new();
        public SemaphoreSlim AuditGate { get; } = new(1, 1);
        public long EventVersion { get; set; }
        public string UserId { get; } = userId;
        public TwentyFortyEightState State { get; set; } = state;
        public Stack<TwentyFortyEightState> History { get; } = new();
        public string LastEvent { get; set; } = "started";
        public int ScoreGained { get; set; }
        public string Message { get; set; } = "Make a move with the arrow keys or controls.";
    }
}

public sealed class TwentyFortyEightAccessException() : InvalidOperationException("That 2048 game is not available.");
public sealed class TwentyFortyEightRuleException(string message) : InvalidOperationException(message);
public sealed record StartTwentyFortyEightRequest(uint? Seed);
public sealed record TwentyFortyEightMoveRequest(string? Direction);
public sealed record TwentyFortyEightStatusResponse(bool Available, int Size, int TargetTile, string Mode);
public sealed record TwentyFortyEightGameResponse(Guid GameId, int Size, IReadOnlyList<int> Tiles, int Score, int Moves, int HighestTile, string Phase, bool CanUndo, string LastEvent, int ScoreGained, string Message)
{
    public Guid RunId { get; init; }
}
public sealed record TwentyFortyEightErrorResponse(string Code, string Message);
