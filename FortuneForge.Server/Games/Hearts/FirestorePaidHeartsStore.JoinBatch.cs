using Google.Cloud.Firestore;

namespace FortuneForge.Server.Games.Hearts;

internal sealed partial class FirestorePaidHeartsStore
{
    /// <summary>
    /// Join admission uses optimistic compare-and-swap writes instead of holding
    /// read locks across several contended account/table/queue documents.
    /// </summary>
    private sealed class PaidHeartsJoinBatch(FirestoreDb database)
    {
        private readonly WriteBatch writes = database.StartBatch();
        private readonly Dictionary<string, DocumentSnapshot> reads = new(StringComparer.Ordinal);

        public async Task<DocumentSnapshot> ReadAsync(DocumentReference reference, CancellationToken ct)
        {
            if (reads.TryGetValue(reference.Path, out var existing)) return existing;
            var snapshot = await reference.GetSnapshotAsync(ct);
            reads.Add(reference.Path, snapshot);
            return snapshot;
        }

        public void Set(DocumentReference reference, Dictionary<string, object> data)
        {
            if (reads.TryGetValue(reference.Path, out var prior))
            {
                if (prior.Exists) writes.Update(reference, data, Precondition.LastUpdated(prior.UpdateTime!.Value));
                else writes.Create(reference, data);
            }
            else writes.Create(reference, data);
        }

        public void Update(DocumentReference reference, Dictionary<string, object> data)
        {
            if (!reads.TryGetValue(reference.Path, out var prior) || !prior.Exists)
                throw new InvalidOperationException("A Hearts admission update requires its recorded snapshot.");
            writes.Update(reference, data, Precondition.LastUpdated(prior.UpdateTime!.Value));
        }

        public void Create(DocumentReference reference, Dictionary<string, object> data) => writes.Create(reference, data);
        // Explicitly limited to derived availability by the only caller below;
        // DocumentReference.Path is fully qualified, not collection-relative.
        public void SetDerived(DocumentReference reference, Dictionary<string, object> data) => writes.Set(reference, data);
        public void MergeDerived(DocumentReference reference, Dictionary<string, object> data) =>
            writes.Set(reference, data, SetOptions.MergeAll);
        public void Delete(DocumentReference reference) => writes.Delete(reference);
        public Task CommitAsync(CancellationToken ct) => writes.CommitAsync(ct);
    }

    private void Write(PaidHeartsJoinBatch batch, PaidHeartsMatch match, DateTime now)
    {
        var observation = FortuneForge.Games.Abstractions.Auditing.GameSecurityEventDocuments.Capture("hearts", match.Id,
            match.Version, now, match, match.Players.Where(player => player.IsAccountBacked).Select(player => player.UserId));
        batch.SetDerived(database.Collection(FortuneForge.Games.Abstractions.Auditing.GameSecurityEventDocuments.CollectionName).Document(observation.Id), observation.Fields);
        batch.MergeDerived(database.Collection(FortuneForge.Games.Abstractions.Auditing.GameSecurityEventDocuments.ManifestCollectionName)
                .Document(FortuneForge.Games.Abstractions.Auditing.GameSecurityEventDocuments.ManifestId("hearts", match.Id)),
            FortuneForge.Server.SecurityAudit.GameSecurityJournal.ManifestFields("hearts", match.Id, observation.Id));
        batch.Set(Match(match.Id), Data(match));
        if (HasJoinableHouseSeat(match))
            batch.SetDerived(HouseTable(match), new Dictionary<string, object> { ["matchId"] = match.Id });
        else batch.Delete(HouseTable(match));
    }

    private void Ledger(PaidHeartsJoinBatch batch, string id, string userId, long amount, long after,
        string type, string key, DateTime now) => batch.Create(database.Collection("balanceTransactions").Document(id),
            LedgerData(id, userId, amount, after, type, key, now));

    private void WriteFunding(PaidHeartsJoinBatch batch, PaidHeartsMatch match, DateTime now) =>
        batch.Create(Funding(match.Id), FundingData(match, now));
}
