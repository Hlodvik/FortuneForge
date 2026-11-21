using System.Collections.Concurrent;
using FortuneForge.Games.Abstractions;
using FortuneForge.Games.Abstractions.Analytics;
using FortuneForge.Games.Abstractions.Auditing;

namespace FortuneForge.Games.DropMerge.Server;

/// <summary>Runs the legacy in-memory free-play sessions for one application instance.</summary>
public class LegacyDropMergeGameService(
    IGameAnalyticsSink? analytics = null,
    IGameSecurityEventSink? securityEvents = null,
    Func<string, DropMergeComputeStart, CancellationToken, Task<DropMergeState>?>? computeStart = null,
    Func<string, DropMergeComputeDrop, CancellationToken, Task<DropMergeTransition>?>? computeDrop = null)
{
    private readonly ConcurrentDictionary<Guid, InMemorySession> games = new();

    public DropMergeStatusResponse Status() => DropMergeSession.Status();

    public DropMergeGameResponse Start(string userId, uint? seed) =>
        Register(NewRun(userId, DropMergeEngine.Start(PuzzleSessionIdentity.SelectSeed(seed))));

    public async Task<DropMergeGameResponse> StartAuditedAsync(string userId, uint? seed, CancellationToken cancellationToken = default)
    {
        var run = NewRun(userId, await OpenBoardAsync(userId, seed, cancellationToken));
        await RecordAsync(run, cancellationToken);
        return Register(run);
    }

    public Task<DropMergeGameResponse> DropAuditedAsync(string userId, Guid gameId, int column, CancellationToken cancellationToken = default) =>
        ChangeAuditedAsync(userId, gameId, async session =>
        {
            var before = session.Run;
            var remoteTransition = computeDrop?.Invoke(userId, new(before.State, column), cancellationToken);
            var transition = remoteTransition is null
                ? DropMergeEngine.Drop(before.State, column)
                : await remoteTransition;

            return Change(session, current =>
            {
                if (current.Run.RunId != before.RunId || current.Run.EventVersion != before.EventVersion)
                    throw new DropMergeRuleException("The board changed. Refresh and try again.");
                return Drop(current, transition);
            });
        }, cancellationToken);

    public Task<DropMergeGameResponse> UndoAuditedAsync(string userId, Guid gameId, CancellationToken cancellationToken = default) =>
        ChangeAuditedAsync(userId, gameId, session => Task.FromResult(Change(session, Undo)), cancellationToken);

    public Task<DropMergeGameResponse> ResetAuditedAsync(string userId, Guid gameId, uint? seed, CancellationToken cancellationToken = default) =>
        ChangeAuditedAsync(userId, gameId, async session =>
        {
            var board = await OpenBoardAsync(userId, seed, cancellationToken);
            return Change(session, current => Reset(current, board));
        }, cancellationToken);

    public DropMergeGameResponse Get(string userId, Guid gameId)
    {
        var session = Find(userId, gameId);
        lock (session.SyncRoot)
        {
            TryRecordCompleted(session);
            return session.Run.ToResponse();
        }
    }

    public DropMergeGameResponse Drop(string userId, Guid gameId, int column) =>
        Change(Find(userId, gameId), session => Drop(session,
            DropMergeEngine.Drop(session.Run.State, column))).ToResponse();

    public DropMergeGameResponse Undo(string userId, Guid gameId) => Change(Find(userId, gameId), Undo).ToResponse();

    public DropMergeGameResponse Reset(string userId, Guid gameId, uint? seed) =>
        Change(Find(userId, gameId), session => Reset(session,
            DropMergeEngine.Start(PuzzleSessionIdentity.SelectSeed(seed)))).ToResponse();

    private static DropMergeSession Drop(InMemorySession session, DropMergeTransition transition)
    {
        var after = session.Run.Drop(transition, DateTime.UtcNow);
        if (after.UndoDepth > session.Run.UndoDepth)
            session.History.Push(session.Run.State);
        return after;
    }

    private static DropMergeSession Undo(InMemorySession session)
    {
        session.Run.RequireUndo();
        var after = session.Run.Undo(session.History.Peek());
        session.History.Pop();
        return after;
    }

    private static DropMergeSession Reset(InMemorySession session, DropMergeState board)
    {
        var after = session.Run.Reset(Guid.NewGuid(), board, DateTime.UtcNow);
        session.History.Clear();
        session.AnalyticsRecorded = false;
        return after;
    }

    private Task<DropMergeState> OpenBoardAsync(string userId, uint? seed, CancellationToken cancellationToken)
    {
        var value = PuzzleSessionIdentity.SelectSeed(seed);
        return computeStart?.Invoke(userId, new(value), cancellationToken) ??
            Task.FromResult(DropMergeEngine.Start(value));
    }

    private static DropMergeSession NewRun(string userId, DropMergeState board) =>
        DropMergeSession.Start(Guid.NewGuid(), Guid.NewGuid(), userId, board, DateTime.UtcNow);

    private DropMergeGameResponse Register(DropMergeSession run)
    {
        if (!games.TryAdd(run.Id, new InMemorySession(run)))
            throw new InvalidOperationException("Could not open a Drop Merge game.");
        return run.ToResponse();
    }

    private InMemorySession Find(string userId, Guid gameId) =>
        games.TryGetValue(gameId, out var session) && string.Equals(session.Run.UserId, userId, StringComparison.Ordinal)
            ? session
            : throw new DropMergeAccessException();

    private DropMergeSession Change(InMemorySession session, Func<InMemorySession, DropMergeSession> change)
    {
        lock (session.SyncRoot)
        {
            TryRecordCompleted(session);
            session.Run = change(session);
            TryRecordCompleted(session);
            return session.Run;
        }
    }

    private async Task<DropMergeGameResponse> ChangeAuditedAsync(string userId, Guid gameId,
        Func<InMemorySession, Task<DropMergeSession>> change, CancellationToken cancellationToken)
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

    private async Task RecordAsync(DropMergeSession run, CancellationToken cancellationToken)
    {
        if (securityEvents is null) return;
        try
        {
            var (observation, completion) = DropMergeAudit.Capture(run, DateTime.UtcNow);
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
        if (DropMergeAudit.Settlement(run, "in-memory-drop-merge-runs", run.RunId.ToString("N")) is { } settlement)
            session.AnalyticsRecorded = analytics.TryEnqueuePractice(settlement);
    }

    private sealed class InMemorySession(DropMergeSession run)
    {
        public DropMergeSession Run { get; set; } = run;
        public bool AnalyticsRecorded { get; set; }
        public object SyncRoot { get; } = new();
        public SemaphoreSlim AuditGate { get; } = new(1, 1);
        public Stack<DropMergeState> History { get; } = new();
    }
}
