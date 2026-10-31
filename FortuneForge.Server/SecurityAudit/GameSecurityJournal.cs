using FortuneForge.Games.Abstractions.Auditing;
using FortuneForge.Games.TexasHoldem;
using Google.Cloud.Firestore;

namespace FortuneForge.Server.SecurityAudit;

internal static class GameSecurityJournal
{
    internal static void Write(Transaction transaction, FirestoreDb database, string gameId, string instanceId,
        long version, DateTime nowUtc, object state, IEnumerable<string> participantIds,
        string eventKind = "engine-state", string? tableId = null)
    {
        var observation = GameSecurityEventDocuments.Capture(gameId, instanceId, version, nowUtc,
            state, participantIds, eventKind, tableId);
        // Content addressing makes a transaction retry write identical bytes. A different state,
        // time or authenticated request gets a different document, preserving the earlier observation.
        transaction.Set(database.Collection(GameSecurityEventDocuments.CollectionName).Document(observation.Id), observation.Fields);
        WriteManifest(transaction, database, gameId, instanceId, observation.Id);
    }

    internal static void WriteManifest(Transaction transaction, FirestoreDb database,
        string gameId, string instanceId, string eventId) => transaction.Set(
        database.Collection(GameSecurityEventDocuments.ManifestCollectionName)
            .Document(GameSecurityEventDocuments.ManifestId(gameId, instanceId)),
        ManifestFields(gameId, instanceId, eventId), SetOptions.MergeAll);

    internal static Dictionary<string, object> ManifestFields(string gameId, string instanceId, string eventId) => new()
    {
        ["gameId"] = gameId, ["instanceId"] = instanceId,
        ["eventIds"] = FieldValue.ArrayUnion(eventId), ["schemaVersion"] = 1L
    };

    internal static void Holdem(Transaction transaction, FirestoreDb database, CreditHoldemMatch match,
        DateTime nowUtc, string eventKind = "engine-state") =>
        Write(transaction, database, "texas-holdem", $"{match.MatchId}-hand-{match.HandNumber}",
            match.Version, nowUtc, match, match.Players.Where(player => player.IsAccountBacked).Select(player => player.ActorId),
            eventKind, match.MatchId);
}
