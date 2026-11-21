using FortuneForge.Games.Abstractions.Auditing;
using Google.Cloud.Firestore;
using SharedArcadeRunSecurityAudit = FortuneForge.Games.Firestore.ArcadeRunSecurityAudit;

namespace FortuneForge.Server.Arcade;

/// <summary>Keeps the hosted arcade engines' legacy participation classifier active.</summary>
internal static class ArcadeRunSecurityAudit
{
    internal static void Started(Transaction transaction, FirestoreDb database, string gameId, string runId,
        string userId, DateTime startedAtUtc, object state) =>
        WriteParticipation(transaction, database,
            SharedArcadeRunSecurityAudit.RecordStarted(transaction, database, gameId, runId, userId, startedAtUtc, state));

    internal static void Completed(Transaction transaction, FirestoreDb database, string gameId, string runId,
        string userId, DateTime startedAtUtc, DateTime completedAtUtc, long entryFeeCents,
        string sourceCollection, string sourceDocumentId, object state, string evidenceKind) =>
        WriteParticipation(transaction, database,
            SharedArcadeRunSecurityAudit.RecordCompleted(transaction, database, gameId, runId, userId, startedAtUtc,
                completedAtUtc, entryFeeCents, sourceCollection, sourceDocumentId, state, evidenceKind));

    private static void WriteParticipation(Transaction transaction, FirestoreDb database, GameSecurityEventDocument observation) =>
        Activity.AcceptedGameTransactionWitness.Write(database, observation,
            (reference, fields) => transaction.Set(reference, fields, SetOptions.MergeAll));
}
