using FortuneForge.Games.Abstractions.Auditing;
using FortuneForge.Server.RoundAudit;
using FortuneForge.Server.SecurityAudit;
using Google.Cloud.Firestore;

namespace FortuneForge.Server.Arcade;

/// <summary>Durable private replay evidence for the hosted arcade engines.</summary>
internal static class ArcadeRunSecurityAudit
{
    internal static void Started(Transaction transaction, FirestoreDb database, string gameId, string runId,
        string userId, DateTime startedAtUtc, object state)
    {
        GameSecurityJournal.Write(transaction, database, gameId, runId, 1, startedAtUtc,
            state, [userId], "run-started");
    }

    internal static void Completed(Transaction transaction, FirestoreDb database, string gameId, string runId,
        string userId, DateTime startedAtUtc, DateTime completedAtUtc, long entryFeeCents,
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
        RoundAuditFirestore.Write(transaction, database, capture);
        GameSecurityJournal.Write(transaction, database, gameId, runId, 2, completedAtUtc,
            state, [userId], "run-completed");
    }
}
