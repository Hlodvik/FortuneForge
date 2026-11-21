using FortuneForge.Games.Abstractions.Analytics;
using FortuneForge.Games.Abstractions.Auditing;
using FortuneForge.Games.Firestore;
using Google.Cloud.Firestore;

namespace FortuneForge.Games.TwentyFortyEight.Server;

public sealed class TwentyFortyEightFirestoreSessions(FirestoreDb database) :
    FirestorePuzzleSessions<TwentyFortyEightSession, TwentyFortyEightState, TwentyFortyEightGameResponse>(
        database, TwentyFortyEightSession.GameId, "2048", "twentyFortyEightSessions", "twentyFortyEightRequestReceipts")
{
    public Task<TwentyFortyEightGameResponse> StartAsync(string userId, uint? seed, CancellationToken token, string? bodySha256 = null)
    {
        var now = DateTime.UtcNow;
        var run = TwentyFortyEightSession.Start(TwentyFortyEightSession.CreateDurableId(now), Guid.NewGuid(), userId,
            TwentyFortyEightEngine.Start(TwentyFortyEightSession.SelectSeed(seed)), now);
        return StartSessionAsync(run, bodySha256, token);
    }

    public Task<TwentyFortyEightGameResponse> GetAsync(string userId, Guid gameId, CancellationToken token) =>
        ReadSessionAsync(userId, gameId, token);

    public Task<TwentyFortyEightGameResponse> MoveAsync(string userId, Guid gameId, string? direction, CancellationToken token, string? bodySha256 = null) =>
        ChangeSessionAsync(userId, gameId, bodySha256, (run, now) =>
        {
            var parsedDirection = TwentyFortyEightSession.ParseDirection(direction);
            TwentyFortyEightTransition move;
            try { move = TwentyFortyEightEngine.Move(run.State, parsedDirection); }
            catch (InvalidOperationException exception) { throw new TwentyFortyEightRuleException(exception.Message); }
            return run.Move(move, now);
        }, token);

    public Task<TwentyFortyEightGameResponse> UndoAsync(string userId, Guid gameId, CancellationToken token, string? bodySha256 = null) =>
        UndoSessionAsync(userId, gameId, bodySha256, run => run.RequireUndo(), (run, board, now) => run.Undo(board, now), token);

    public Task<TwentyFortyEightGameResponse> ContinueAsync(string userId, Guid gameId, CancellationToken token, string? bodySha256 = null) =>
        ChangeSessionAsync(userId, gameId, bodySha256, (run, now) => run.Continue(now), token);

    public Task<TwentyFortyEightGameResponse> ResetAsync(string userId, Guid gameId, uint? seed, CancellationToken token, string? bodySha256 = null)
    {
        var board = TwentyFortyEightEngine.Start(TwentyFortyEightSession.SelectSeed(seed));
        var runId = Guid.NewGuid();
        return ChangeSessionAsync(userId, gameId, bodySha256, (run, now) => run.Reset(runId, board, now), token);
    }

    protected override TwentyFortyEightGameResponse ToResponse(TwentyFortyEightSession run) => run.ToResponse();
    protected override (GameSecurityEventDocument Observation, RoundAuditCapture? Completion) Capture(TwentyFortyEightSession run, DateTime nowUtc) =>
        TwentyFortyEightAudit.Capture(run, nowUtc, TwentyFortyEightAudit.FirestoreEvidenceKind);
    protected override bool HasPlayed(TwentyFortyEightSession run) => run.State.Moves > 0;
    protected override GameAnalyticsSettlement? Settlement(TwentyFortyEightSession run, string collection, string id) =>
        TwentyFortyEightAudit.Settlement(run, collection, id);
    protected override Exception GameNotFound() => new TwentyFortyEightAccessException();
    protected override Exception ConflictingReceipt() =>
        new TwentyFortyEightRuleException("The request identifier was already used for a different 2048 action.");
}
