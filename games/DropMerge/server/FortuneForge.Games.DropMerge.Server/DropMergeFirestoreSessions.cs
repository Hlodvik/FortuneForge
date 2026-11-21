using FortuneForge.Games.Abstractions;
using FortuneForge.Games.Abstractions.Analytics;
using FortuneForge.Games.Abstractions.Auditing;
using FortuneForge.Games.Firestore;
using Google.Cloud.Firestore;

namespace FortuneForge.Games.DropMerge.Server;

public sealed class DropMergeFirestoreSessions(FirestoreDb database) :
    FirestorePuzzleSessions<DropMergeSession, DropMergeState, DropMergeGameResponse>(
        database, DropMergeSession.GameId, "Drop Merge", "dropMergeSessions", "dropMergeRequestReceipts")
{
    public Task<DropMergeGameResponse> StartAsync(string userId, uint? seed, CancellationToken token, string? bodySha256 = null)
    {
        var now = DateTime.UtcNow;
        var run = DropMergeSession.Start(PuzzleSessionIdentity.CreateDurableId(now), Guid.NewGuid(), userId,
            DropMergeEngine.Start(PuzzleSessionIdentity.SelectSeed(seed)), now);
        return StartSessionAsync(run, bodySha256, token);
    }

    public Task<DropMergeGameResponse> GetAsync(string userId, Guid id, CancellationToken token) =>
        ReadSessionAsync(userId, id, token);

    public Task<DropMergeGameResponse> DropAsync(string userId, Guid id, int column, CancellationToken token, string? bodySha256 = null) =>
        ChangeSessionAsync(userId, id, bodySha256, (run, now) =>
        {
            DropMergeTransition drop;
            try { drop = DropMergeEngine.Drop(run.State, column); }
            catch (InvalidOperationException exception) { throw new DropMergeRuleException(exception.Message); }
            return run.Drop(drop, now);
        }, token);

    public Task<DropMergeGameResponse> UndoAsync(string userId, Guid id, CancellationToken token, string? bodySha256 = null) =>
        UndoSessionAsync(userId, id, bodySha256, run => run.RequireUndo(), (run, board, _) => run.Undo(board), token);

    public Task<DropMergeGameResponse> ResetAsync(string userId, Guid id, uint? seed, CancellationToken token, string? bodySha256 = null)
    {
        var board = DropMergeEngine.Start(PuzzleSessionIdentity.SelectSeed(seed));
        var runId = Guid.NewGuid();
        return ChangeSessionAsync(userId, id, bodySha256, (run, now) => run.Reset(runId, board, now), token);
    }

    protected override DropMergeGameResponse ToResponse(DropMergeSession run) => run.ToResponse();
    protected override (GameSecurityEventDocument Observation, RoundAuditCapture? Completion) Capture(DropMergeSession run, DateTime nowUtc) =>
        DropMergeAudit.Capture(run, nowUtc, DropMergeAudit.FirestoreEvidenceKind);
    protected override bool HasPlayed(DropMergeSession run) => run.State.Moves > 0;
    protected override GameAnalyticsSettlement? Settlement(DropMergeSession run, string collection, string id) =>
        DropMergeAudit.Settlement(run, collection, id);
    protected override Exception GameNotFound() => new DropMergeAccessException();
    protected override Exception ConflictingReceipt() =>
        new DropMergeRuleException("The request identifier was already used for a different Drop Merge action.");
}
