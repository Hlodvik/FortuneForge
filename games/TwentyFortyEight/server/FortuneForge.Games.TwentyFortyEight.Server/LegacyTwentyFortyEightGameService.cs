using System.Collections.Concurrent;
using FortuneForge.Games.Abstractions.Analytics;
using FortuneForge.Games.Abstractions.Auditing;

namespace FortuneForge.Games.TwentyFortyEight.Server;

/// <summary>Runs the legacy in-memory free-play sessions for one application instance.</summary>
public class LegacyTwentyFortyEightGameService(
    IGameAnalyticsSink? analytics = null,
    IGameSecurityEventSink? securityEvents = null,
    Func<string, TwentyFortyEightComputeStart, CancellationToken, Task<TwentyFortyEightState>?>? computeStart = null,
    Func<string, TwentyFortyEightComputeMove, CancellationToken, Task<TwentyFortyEightTransition>?>? computeMove = null)
{
    private readonly ConcurrentDictionary<Guid, InMemorySession> games = new();

    public TwentyFortyEightStatusResponse Status() => TwentyFortyEightSession.Status();

    public TwentyFortyEightGameResponse Start(string userId, uint? seed) =>
        Register(NewRun(userId, TwentyFortyEightEngine.Start(TwentyFortyEightSession.SelectSeed(seed))));

    public async Task<TwentyFortyEightGameResponse> StartAuditedAsync(string userId, uint? seed, CancellationToken cancellationToken = default)
    {
        var run = NewRun(userId, await OpenBoardAsync(userId, seed, cancellationToken));
        await RecordAsync(run, cancellationToken);
        return Register(run);
    }

    public Task<TwentyFortyEightGameResponse> MoveAuditedAsync(string userId, Guid gameId, string? direction, CancellationToken cancellationToken = default) =>
        ChangeAuditedAsync(userId, gameId, async session =>
        {
            var before = session.Run;
            var parsed = TwentyFortyEightSession.ParseDirection(direction);
            var remoteTransition = computeMove?.Invoke(userId, new(before.State, parsed), cancellationToken);
            var transition = remoteTransition is null
                ? TwentyFortyEightEngine.Move(before.State, parsed)
                : await remoteTransition;

            return Change(session, current =>
            {
                if (current.Run.RunId != before.RunId || current.Run.EventVersion != before.EventVersion)
                    throw new TwentyFortyEightRuleException("The board changed. Refresh and try again.");
                return Move(current, transition);
            });
        }, cancellationToken);

    public Task<TwentyFortyEightGameResponse> UndoAuditedAsync(string userId, Guid gameId, CancellationToken cancellationToken = default) =>
        ChangeAuditedAsync(userId, gameId, session => Task.FromResult(Change(session, Undo)), cancellationToken);

    public Task<TwentyFortyEightGameResponse> ContinueAuditedAsync(string userId, Guid gameId, CancellationToken cancellationToken = default) =>
        ChangeAuditedAsync(userId, gameId, session => Task.FromResult(Change(session, Continue)), cancellationToken);

    public Task<TwentyFortyEightGameResponse> ResetAuditedAsync(string userId, Guid gameId, uint? seed, CancellationToken cancellationToken = default) =>
        ChangeAuditedAsync(userId, gameId, async session =>
        {
            var board = await OpenBoardAsync(userId, seed, cancellationToken);
            return Change(session, current => Reset(current, board));
        }, cancellationToken);

    public TwentyFortyEightGameResponse Get(string userId, Guid gameId)
    {
        var session = Find(userId, gameId);
        lock (session.SyncRoot)
        {
            TryRecordCompleted(session);
            return session.Run.ToResponse();
        }
    }

    public TwentyFortyEightGameResponse Move(string userId, Guid gameId, string? direction) =>
        Change(Find(userId, gameId), session => Move(session,
            TwentyFortyEightEngine.Move(session.Run.State, TwentyFortyEightSession.ParseDirection(direction)))).ToResponse();

    public TwentyFortyEightGameResponse Undo(string userId, Guid gameId) => Change(Find(userId, gameId), Undo).ToResponse();

    public TwentyFortyEightGameResponse Continue(string userId, Guid gameId) => Change(Find(userId, gameId), Continue).ToResponse();

    public TwentyFortyEightGameResponse Reset(string userId, Guid gameId, uint? seed) =>
        Change(Find(userId, gameId), session => Reset(session,
            TwentyFortyEightEngine.Start(TwentyFortyEightSession.SelectSeed(seed)))).ToResponse();

    private static TwentyFortyEightSession Move(InMemorySession session, TwentyFortyEightTransition transition)
    {
        var after = session.Run.Move(transition, DateTime.UtcNow);
        if (after.UndoDepth > session.Run.UndoDepth)
            session.History.Push(session.Run.State);
        return after;
    }

    private static TwentyFortyEightSession Undo(InMemorySession session)
    {
        session.Run.RequireUndo();
        var after = session.Run.Undo(session.History.Peek(), DateTime.UtcNow);
        session.History.Pop();
        return after;
    }

    private static TwentyFortyEightSession Continue(InMemorySession session) => session.Run.Continue(DateTime.UtcNow);

    private static TwentyFortyEightSession Reset(InMemorySession session, TwentyFortyEightState board)
    {
        var after = session.Run.Reset(Guid.NewGuid(), board, DateTime.UtcNow);
        session.History.Clear();
        session.AnalyticsRecorded = false;
        return after;
    }

    private Task<TwentyFortyEightState> OpenBoardAsync(string userId, uint? seed, CancellationToken cancellationToken)
    {
        var value = TwentyFortyEightSession.SelectSeed(seed);
        return computeStart?.Invoke(userId, new(value), cancellationToken) ??
            Task.FromResult(TwentyFortyEightEngine.Start(value));
    }

    private static TwentyFortyEightSession NewRun(string userId, TwentyFortyEightState board) =>
        TwentyFortyEightSession.Start(Guid.NewGuid(), Guid.NewGuid(), userId, board, DateTime.UtcNow);

    private TwentyFortyEightGameResponse Register(TwentyFortyEightSession run)
    {
        if (!games.TryAdd(run.Id, new InMemorySession(run)))
            throw new InvalidOperationException("Could not open a 2048 game.");
        return run.ToResponse();
    }

    private InMemorySession Find(string userId, Guid gameId) =>
        games.TryGetValue(gameId, out var session) && string.Equals(session.Run.UserId, userId, StringComparison.Ordinal)
            ? session
            : throw new TwentyFortyEightAccessException();

    private TwentyFortyEightSession Change(InMemorySession session, Func<InMemorySession, TwentyFortyEightSession> change)
    {
        lock (session.SyncRoot)
        {
            TryRecordCompleted(session);
            session.Run = change(session);
            TryRecordCompleted(session);
            return session.Run;
        }
    }

    private async Task<TwentyFortyEightGameResponse> ChangeAuditedAsync(string userId, Guid gameId,
        Func<InMemorySession, Task<TwentyFortyEightSession>> change, CancellationToken cancellationToken)
    {
        var session = Find(userId, gameId);
        await session.AuditGate.WaitAsync(cancellationToken);
        try
        {
            var run = await change(session);
            await RecordAsync(run, cancellationToken);
            return run.ToResponse();
        }
        finally
        {
            session.AuditGate.Release();
        }
    }

    private async Task RecordAsync(TwentyFortyEightSession run, CancellationToken cancellationToken)
    {
        if (securityEvents is null) return;
        try
        {
            var (observation, completion) = TwentyFortyEightAudit.Capture(run, DateTime.UtcNow);
            if (completion is null) await securityEvents.WriteAsync(observation, cancellationToken);
            else await securityEvents.WriteCompletedAsync(observation, completion, cancellationToken);
        }
        catch (Exception exception) when (exception is not OperationCanceledException)
        {
            throw new GameSecurityRecordingException(exception);
        }
    }

    private void TryRecordCompleted(InMemorySession session)
    {
        if (analytics is null || session.AnalyticsRecorded) return;
        var run = session.Run;
        if (TwentyFortyEightAudit.Settlement(run, "in-memory-2048-runs", run.RunId.ToString("N")) is { } settlement)
            session.AnalyticsRecorded = analytics.TryEnqueuePractice(settlement);
    }

    private sealed class InMemorySession(TwentyFortyEightSession run)
    {
        public TwentyFortyEightSession Run { get; set; } = run;
        public bool AnalyticsRecorded { get; set; }
        public object SyncRoot { get; } = new();
        public SemaphoreSlim AuditGate { get; } = new(1, 1);
        public Stack<TwentyFortyEightState> History { get; } = new();
    }
}
