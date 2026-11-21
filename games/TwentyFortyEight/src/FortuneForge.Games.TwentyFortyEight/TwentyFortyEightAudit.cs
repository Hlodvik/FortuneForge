using FortuneForge.Games.Abstractions.Analytics;
using FortuneForge.Games.Abstractions.Auditing;

namespace FortuneForge.Games.TwentyFortyEight;

public static class TwentyFortyEightAudit
{
    public const string TerminalAuditorVersion = "2048-terminal-outcome-v1";
    public const string TerminalEvidenceKind = "2048-private-state-v1";
    public const string FirestoreEvidenceKind = "2048-firestore-state-v1";

    public static (GameSecurityEventDocument Observation, RoundAuditCapture? Completion) Capture(
        TwentyFortyEightSession session, DateTime nowUtc, string evidenceKind = TerminalEvidenceKind)
    {
        var episodeId = EpisodeId(session);
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
            Count = session.UndoDepth,
        };
        var observation = GameSecurityEventDocuments.Capture(TwentyFortyEightSession.GameId, episodeId,
            session.EventVersion, nowUtc, payload, [session.UserId], session.LastEvent, sessionId);
        var completion = session.CompletedAtUtc is { } completedAt
            ? RoundAuditCapture.Create(TwentyFortyEightSession.GameId, episodeId, sessionId, session.CompletionEpisode,
                session.StartedAtUtc, completedAt, payload, evidenceKind, TerminalAuditorVersion,
                [new RoundAuditParticipant(session.UserId, session.UserId, true, 0, 0, 0)],
                CheckTerminal(session.State))
            : null;
        return (observation, completion);
    }

    public static string EpisodeId(TwentyFortyEightSession session) => FortuneForge.Games.Abstractions.PuzzleSessionIdentity.EpisodeId(session.RunId, session.CompletionEpisode);

    public static GameAnalyticsSettlement? Settlement(TwentyFortyEightSession session, string sourceCollection, string sourceId) =>
        session.CompletedAtUtc is { } completedAt
            ? new(TwentyFortyEightSession.GameId, session.RunId.ToString("N"), session.UserId, "free", true,
                completedAt, session.StartedAtUtc, 0, 0, sourceCollection, sourceId)
            : null;

    public static IReadOnlyList<RoundAuditFinding> CheckTerminal(TwentyFortyEightState state)
    {
        var tiles = state.Tiles;
        var valid = state.Size == 4 && !tiles.IsDefault && tiles.Length == 16 && state.Moves > 0 && state.Score >= 0 &&
            tiles.All(tile => tile == 0 || tile >= 2 && (tile & (tile - 1)) == 0) && state.HighestTile == tiles.Max() &&
            (state.Phase == TwentyFortyEightPhase.Won && state.HighestTile >= 2048 && state.Score >= 2048 ||
             state.Phase == TwentyFortyEightPhase.Lost && !CanSlide(tiles, state.Size));

        return valid
            ? [new("audit-scope", "info",
                "The terminal 2048 board, phase, score and highest tile are internally consistent. Command history, RNG provenance and behavioral cheating remain outside this check.")]
            : [new("2048-terminal-mismatch", "error",
                "The recorded terminal state or settlement disagrees with an independent rule calculation.")];
    }

    private static bool CanSlide(IReadOnlyList<int> tiles, int size)
    {
        for (var index = 0; index < tiles.Count; index++)
        {
            if (tiles[index] == 0) return true;
            if (index % size + 1 < size && tiles[index] == tiles[index + 1]) return true;
            if (index / size + 1 < size && tiles[index] == tiles[index + size]) return true;
        }
        return false;
    }
}
