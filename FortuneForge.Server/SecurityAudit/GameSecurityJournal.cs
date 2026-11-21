using FortuneForge.Games.Abstractions.Auditing;
using FortuneForge.Games.Firestore;
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
        FirestoreGameAudit.WriteObservation(transaction, database, observation);
        Activity.AcceptedGameTransactionWitness.Write(database, observation,
            (reference, fields) => transaction.Set(reference, fields, SetOptions.MergeAll));
    }

    internal static void WriteManifest(Transaction transaction, FirestoreDb database,
        string gameId, string instanceId, string eventId) =>
        FirestoreGameAudit.WriteManifest(transaction, database, gameId, instanceId, eventId);

    internal static Dictionary<string, object> ManifestFields(string gameId, string instanceId, string eventId) =>
        FirestoreGameAudit.ManifestFields(gameId, instanceId, eventId);

    internal static void Holdem(Transaction transaction, FirestoreDb database, CreditHoldemMatch match,
        DateTime nowUtc, string eventKind = "engine-state") =>
        Write(transaction, database, "texas-holdem", $"{match.MatchId}-hand-{match.HandNumber}",
            match.Version, nowUtc, match, match.Players.Where(player => player.IsAccountBacked).Select(player => player.ActorId),
            eventKind, match.MatchId);
}
