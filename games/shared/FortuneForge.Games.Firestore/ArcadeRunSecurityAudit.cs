using FortuneForge.Games.Abstractions.Auditing;
using Google.Cloud.Firestore;

namespace FortuneForge.Games.Firestore;

/// <summary>Persists the standard journal and private replay evidence for arcade runs.</summary>
public static class ArcadeRunSecurityAudit
{
    public static void Started(Transaction transaction, FirestoreDb database, string gameId, string runId,
        string userId, DateTime startedAtUtc, object state) =>
        RecordStarted(transaction, database, gameId, runId, userId, startedAtUtc, state);

    public static void Completed(Transaction transaction, FirestoreDb database, string gameId, string runId,
        string userId, DateTime startedAtUtc, DateTime completedAtUtc, long entryFeeCents,
        string sourceCollection, string sourceDocumentId, object state, string evidenceKind) =>
        RecordCompleted(transaction, database, gameId, runId, userId, startedAtUtc, completedAtUtc,
            entryFeeCents, sourceCollection, sourceDocumentId, state, evidenceKind);

    public static GameSecurityEventDocument RecordStarted(Transaction transaction, FirestoreDb database, string gameId,
        string runId, string userId, DateTime startedAtUtc, object state) =>
        RecordObservation(transaction, database, gameId, runId, userId, 1, startedAtUtc, state, "run-started");

    public static GameSecurityEventDocument RecordCompleted(Transaction transaction, FirestoreDb database, string gameId,
        string runId, string userId, DateTime startedAtUtc, DateTime completedAtUtc, long entryFeeCents,
        string sourceCollection, string sourceDocumentId, object state, string evidenceKind)
    {
        var findings = new List<RoundAuditFinding>
        {
            new("arcade-replay-second-pass-pending", "warning",
                "The server replayed this input at completion; an independent database replay has not yet certified it.")
        };
        if (entryFeeCents > 0)
            findings.Add(new("competition-prize-settlement-separate", "warning",
                "The paid entry debit is recorded here; competition prize or refund settlement is a separate later transaction."));
        var capture = RoundAuditCapture.Create(gameId, runId, runId, 1, startedAtUtc, completedAtUtc,
            new { sourceCollection, sourceDocumentId, entryFeeCents, data = state },
            evidenceKind, "arcade-server-replay-v1",
            [new RoundAuditParticipant(userId, userId, true, entryFeeCents, 0, -entryFeeCents)], findings);
        FirestoreGameAudit.WriteCompletion(transaction, database, capture);
        return RecordObservation(transaction, database, gameId, runId, userId, 2, completedAtUtc, state, "run-completed");
    }

    private static GameSecurityEventDocument RecordObservation(Transaction transaction, FirestoreDb database, string gameId,
        string runId, string userId, long version, DateTime nowUtc, object state, string eventKind)
    {
        var observation = GameSecurityEventDocuments.Capture(gameId, runId, version, nowUtc, state, [userId], eventKind);
        FirestoreGameAudit.WriteObservation(transaction, database, observation);
        return observation;
    }
}
