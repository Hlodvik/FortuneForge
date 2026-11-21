using FortuneForge.Games.Abstractions;
using FortuneForge.Games.Abstractions.Analytics;
using FortuneForge.Games.Abstractions.Auditing;

namespace FortuneForge.Games.DropMerge;

public static class DropMergeAudit
{
    public const string TerminalAuditorVersion = "drop-merge-terminal-outcome-v1";
    public const string TerminalEvidenceKind = "drop-merge-private-state-v1";

    public const string FirestoreEvidenceKind = "drop-merge-firestore-state-v1";

    public static (GameSecurityEventDocument Observation, RoundAuditCapture? Completion) Capture(
        DropMergeSession session, DateTime nowUtc, string evidenceKind = TerminalEvidenceKind)
    {
        var episodeId = PuzzleSessionIdentity.EpisodeId(session.RunId, session.CompletionEpisode);
        var sessionId = session.Id.ToString("N");
        var payload = new
        {
            session.Id,
            session.RunId,
            session.CompletionEpisode,
            session.StartedAtUtc,
            session.CompletedAtUtc,
            session.State,
            session.LastEvent,
            session.ScoreGained,
            session.MergeCount,
            session.DropColumn,
            session.DropRow,
            session.RemovedTile,
            session.IntroducedTile,
            session.MergeSteps,
            HistoryCount = session.UndoDepth,
        };
        var observation = GameSecurityEventDocuments.Capture(DropMergeSession.GameId, episodeId,
            session.EventVersion, nowUtc, payload, [session.UserId], session.LastEvent, sessionId);
        var completion = session.CompletedAtUtc is { } completedAt
            ? RoundAuditCapture.Create(DropMergeSession.GameId, episodeId, sessionId, session.CompletionEpisode,
                session.StartedAtUtc, completedAt, payload, evidenceKind, TerminalAuditorVersion,
                [new RoundAuditParticipant(session.UserId, session.UserId, true, 0, 0, 0)],
                CheckTerminal(session.State, session.DropColumn, session.DropRow))
            : null;
        return (observation, completion);
    }

    public static GameAnalyticsSettlement? Settlement(DropMergeSession session, string sourceCollection, string sourceId) =>
        session.CompletedAtUtc is { } completedAt
            ? new(DropMergeSession.GameId, session.RunId.ToString("N"), session.UserId, "free", true,
                completedAt, session.StartedAtUtc, 0, 0, sourceCollection, sourceId)
            : null;

    public static IReadOnlyList<RoundAuditFinding> CheckTerminal(DropMergeState state, int dropColumn, int dropRow)
    {
        var tiles = state.Tiles;
        var blocked = dropColumn is >= 0 and < 7 && tiles.Length == 49 && tiles[dropColumn] != 0;
        var valid = state.Columns == 7 && state.Rows == 7 && tiles.Length == 49 &&
            state.Moves >= 0 && state.Score >= 0 && state.TotalMerges >= 0 &&
            tiles.All(tile => tile == 0 || PowerOfTwo(tile)) && state.HighestTile == tiles.Max() &&
            PowerOfTwo(state.CurrentTile) && PowerOfTwo(state.NextTile) &&
            state.Phase == DropMergePhase.Lost &&
            (dropRow == -1 && blocked || dropRow >= 0 && tiles.All(tile => tile != 0));

        return valid
            ? [new("audit-scope", "info",
                "The terminal Drop Merge board and loss condition are internally consistent. Command history, RNG provenance and behavioral cheating remain outside this check.")]
            : [new("drop-merge-terminal-mismatch", "error",
                "The recorded terminal state or settlement disagrees with an independent rule calculation.")];
    }

    private static bool PowerOfTwo(int tile) => tile >= 2 && (tile & (tile - 1)) == 0;
}
