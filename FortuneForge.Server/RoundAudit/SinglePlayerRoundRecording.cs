using FortuneForge.Games.Abstractions.Analytics;
using FortuneForge.Games.Abstractions.Auditing;
using Google.Cloud.Firestore;

namespace FortuneForge.Server.RoundAudit;

internal static class SinglePlayerRoundRecording
{
    internal static void Write(Transaction transaction, FirestoreDb database, string gameId, string roundId,
        string userId, long wagerCents, long payoutCents, DateTime? startedAtUtc, DateTime completedAtUtc,
        string sourceCollection, string sourceDocumentId, object payload, string evidenceKind,
        bool? settlementVerified = null, string? outcomeAuditorVersion = null,
        IReadOnlyList<RoundAuditFinding>? additionalFindings = null)
    {
        var record = SinglePlayerRoundRecord.Create(gameId, roundId, userId, wagerCents, payoutCents,
            startedAtUtc, completedAtUtc, sourceCollection, sourceDocumentId, payload, evidenceKind,
            settlementVerified, outcomeAuditorVersion, additionalFindings);
        transaction.Create(database.Collection(GameAnalyticsOutbox.CollectionName).Document(record.Settlement.Id), record.Settlement.Fields);
        RoundAuditFirestore.Write(transaction, database, record.Audit);
        FortuneForge.Server.SecurityAudit.GameSecurityJournal.Write(transaction, database, gameId, roundId,
            1, completedAtUtc, payload, [userId], "round-settled");
    }
}
