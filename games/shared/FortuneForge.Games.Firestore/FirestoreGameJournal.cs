using Google.Cloud.Firestore;

namespace FortuneForge.Games.Firestore;

/// <summary>Compatibility entry point for packages that record engine observations as a journal.</summary>
public static class FirestoreGameJournal
{
    public static void Write(Transaction transaction, FirestoreDb database, string gameId, string instanceId,
        long version, DateTime nowUtc, object state, IEnumerable<string> participantIds,
        string eventKind = "engine-state", string? tableId = null) =>
        FirestoreGameAudit.WriteObservation(transaction, database, gameId, instanceId, version,
            nowUtc, state, participantIds, eventKind, tableId);
}
