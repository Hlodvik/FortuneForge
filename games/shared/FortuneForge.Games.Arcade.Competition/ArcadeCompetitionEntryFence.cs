using FortuneForge.Games.Abstractions.Auditing;
using FortuneForge.Server.Arcade.Competition;
using Google.Cloud.Firestore;

namespace FortuneForge.Games.Arcade.Competition;

internal static class ArcadeCompetitionEntryFence
{
    internal const string PreparedCollection = "arcadeCompetitionPreparedEntries";
    private const string OperationCollection = "arcadeCompetitionEntryCommands";
    internal const string PendingCountField = "pendingEntryCharges";

    internal static DocumentReference Competition(FirestoreDb database, ArcadeCompetitionIdentity competition) =>
        database.Collection(ArcadeCompetitionFirestoreDocuments.CompetitionsCollection).Document(competition.DocumentId);

    internal static DocumentReference Settlement(FirestoreDb database, ArcadeCompetitionIdentity competition) =>
        database.Collection(ArcadeCompetitionFirestoreDocuments.SettlementsCollection).Document(competition.DocumentId);

    internal static DocumentReference Prepared(FirestoreDb database, string attemptDocumentId) =>
        database.Collection(PreparedCollection).Document(attemptDocumentId);

    internal static DocumentReference Operation(FirestoreDb database, string gameId, string account, string attemptId) =>
        database.Collection(OperationCollection).Document(GameSecurityEventDocuments.Hash($"v1\n{gameId}\n{account}\n{attemptId}"));

    internal static long ReadCount(DocumentSnapshot competition)
    {
        if (!competition.Exists || !competition.ContainsField(PendingCountField)) return 0;
        if (!competition.TryGetValue<long>(PendingCountField, out var count) || count < 0)
            throw new InvalidOperationException("The stored competition entry fence is invalid.");
        return count;
    }

    internal static void Add(Transaction transaction, DocumentSnapshot snapshot, ArcadeCompetitionIdentity competition,
        ArcadeCompetitionRulesOptions terms)
    {
        if (!snapshot.Exists)
        {
            var fields = ArcadeCompetitionFirestoreDocuments.CompetitionData(competition);
            fields[PendingCountField] = 1L;
            fields[ArcadeCompetitionWindowTerms.Field] = ArcadeCompetitionWindowTerms.Encode(terms);
            transaction.Create(snapshot.Reference, fields);
            return;
        }
        var updates = new Dictionary<string, object> { [PendingCountField] = checked(ReadCount(snapshot) + 1) };
        if (!snapshot.ContainsField(ArcadeCompetitionWindowTerms.Field))
            updates[ArcadeCompetitionWindowTerms.Field] = ArcadeCompetitionWindowTerms.Encode(terms);
        transaction.Update(snapshot.Reference, updates);
    }

    internal static void Remove(Transaction transaction, DocumentSnapshot competition)
    {
        var count = ReadCount(competition);
        if (count == 0) throw new ArcadeCompetitionEntryPendingException();
        transaction.Update(competition.Reference, PendingCountField, count - 1);
    }

    internal static void EnsureClear(DocumentSnapshot competition)
    {
        if (ReadCount(competition) != 0) throw new ArcadeCompetitionEntryPendingException();
    }

    internal static void EnsureNotSettled(DocumentSnapshot snapshot, ArcadeCompetitionIdentity competition)
    {
        if (snapshot.Exists && FirestoreArcadeCompetitionStore.ReadSettlementState(snapshot, competition) is
                { HasStoredPlan: true } or { IsCompleted: true })
            throw new ArcadeCompetitionSettlementCompletedException();
    }
}
