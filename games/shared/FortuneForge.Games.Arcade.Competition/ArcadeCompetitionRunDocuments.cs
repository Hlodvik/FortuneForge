using FortuneForge.Server.Arcade.Competition;
using Google.Cloud.Firestore;

namespace FortuneForge.Games.Arcade.Competition;

/// <summary>The common identity block for a paid arcade run's historical schema.</summary>
public static class ArcadeCompetitionRunDocuments
{
    public static Dictionary<string, object> IdentityFields(ArcadeCompetitionAttemptRecord attempt, string runId) => new()
    {
        ["runId"] = runId,
        ["status"] = "started",
        ["attemptId"] = attempt.AttemptId,
        ["attemptDocumentId"] = attempt.DocumentId,
        ["competitionId"] = attempt.Competition.DocumentId,
        ["gameId"] = attempt.Competition.GameId,
        ["playerId"] = attempt.PlayerId,
        ["entryFeeCents"] = attempt.EntryFeeCents,
        ["enteredAt"] = Timestamp.FromDateTime(attempt.EnteredAtUtc.UtcDateTime),
        ["startsAt"] = Timestamp.FromDateTime(attempt.Competition.StartsAtUtc.UtcDateTime),
        ["endsAt"] = Timestamp.FromDateTime(attempt.Competition.EndsAtUtc.UtcDateTime),
        ["schemaVersion"] = 1L,
    };

    public static void ValidateIdentity(DocumentSnapshot snapshot, ArcadeCompetitionAttemptRecord attempt, string runId)
    {
        if (!snapshot.Exists || snapshot.Id != attempt.DocumentId ||
            !TextMatches(snapshot, "runId", runId) || !TextMatches(snapshot, "attemptId", attempt.AttemptId) ||
            !TextMatches(snapshot, "attemptDocumentId", attempt.DocumentId) ||
            !TextMatches(snapshot, "competitionId", attempt.Competition.DocumentId) ||
            !TextMatches(snapshot, "gameId", attempt.Competition.GameId) || !TextMatches(snapshot, "playerId", attempt.PlayerId) ||
            !LongMatches(snapshot, "entryFeeCents", attempt.EntryFeeCents) ||
            !TimestampMatches(snapshot, "enteredAt", attempt.EnteredAtUtc) ||
            !TimestampMatches(snapshot, "startsAt", attempt.Competition.StartsAtUtc) ||
            !TimestampMatches(snapshot, "endsAt", attempt.Competition.EndsAtUtc) || !LongMatches(snapshot, "schemaVersion", 1) ||
            !TextMatches(snapshot, "status", attempt.IsCompleted ? "completed" : "started") ||
            attempt.IsCompleted && (!LongMatches(snapshot, "score", attempt.Score!.Value) ||
                !TimestampMatches(snapshot, "completedAt", attempt.CompletedAtUtc!.Value)))
            throw new InvalidOperationException("The recorded arcade run does not match its paid competition attempt.");
    }

    private static bool TextMatches(DocumentSnapshot snapshot, string field, string expected) =>
        snapshot.TryGetValue<string>(field, out var stored) && stored == expected;

    private static bool TimestampMatches(DocumentSnapshot snapshot, string field, DateTimeOffset expected) =>
        snapshot.TryGetValue<Timestamp>(field, out var stored) && stored.ToDateTime() == expected.UtcDateTime;

    private static bool LongMatches(DocumentSnapshot snapshot, string field, long expected) =>
        snapshot.TryGetValue<long>(field, out var stored) && stored == expected;
}
