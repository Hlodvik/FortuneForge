using FortuneForge.Games.Abstractions.Analytics;
using FortuneForge.Games.Abstractions.Auditing;
using Google.Cloud.Firestore;

namespace FortuneForge.Games.Firestore;

/// <summary>Writes game-authored observations, completion evidence, and accepted participation.</summary>
public static class FirestoreGameAudit
{
    public static void WriteObservation(Transaction transaction, FirestoreDb database, string gameId, string instanceId,
        long version, DateTime nowUtc, object state, IEnumerable<string> participantIds,
        string eventKind = "engine-state", string? tableId = null) =>
        WriteObservation(transaction, database, GameSecurityEventDocuments.Capture(gameId, instanceId, version,
            nowUtc, state, participantIds, eventKind, tableId));

    public static void WriteObservation(Transaction transaction, FirestoreDb database, GameSecurityEventDocument observation)
    {
        // The observation is content-addressed. Retrying the same observation writes the same document.
        transaction.Set(database.Collection(GameSecurityEventDocuments.CollectionName).Document(observation.Id), observation.Fields);
        WriteManifest(transaction, database, (string)observation.Fields["gameId"],
            (string)observation.Fields["instanceId"], observation.Id);
    }

    public static void WriteManifest(Transaction transaction, FirestoreDb database,
        string gameId, string instanceId, string eventId) => transaction.Set(
        database.Collection(GameSecurityEventDocuments.ManifestCollectionName)
            .Document(GameSecurityEventDocuments.ManifestId(gameId, instanceId)),
        ManifestFields(gameId, instanceId, eventId), SetOptions.MergeAll);

    public static Dictionary<string, object> ManifestFields(string gameId, string instanceId, string eventId) => new()
    {
        ["gameId"] = gameId, ["instanceId"] = instanceId,
        ["eventIds"] = FieldValue.ArrayUnion(eventId), ["schemaVersion"] = 1L,
    };

    public static void WriteCompletion(Transaction transaction, FirestoreDb database, RoundAuditCapture capture)
    {
        var documents = RoundAuditDocuments.Create(capture);
        transaction.Create(database.Collection(RoundAuditDocuments.Summaries).Document(capture.Summary.Id), documents.Summary);
        transaction.Create(database.Collection(RoundAuditDocuments.Evidence).Document(capture.Summary.Id), documents.Evidence);
    }

    public static void WriteAcceptedParticipation(Transaction transaction, FirestoreDb database,
        string gameId, string userId, DateTime nowUtc) => WriteAcceptedParticipation(database, gameId, userId, nowUtc,
            (reference, fields) => transaction.Set(reference, fields, SetOptions.MergeAll));

    public static void WriteAcceptedParticipation(FirestoreDb database, string gameId, string userId, DateTime nowUtc,
        Action<DocumentReference, Dictionary<string, object>> merge)
    {
        var witness = AcceptedGameParticipationDocuments.Create(userId, gameId, nowUtc);
        merge(database.Collection("catalogPlayerActivity").Document(witness.DocOwnerKey)
            .Collection("games").Document(witness.GameId), witness.Fields);
    }
}
