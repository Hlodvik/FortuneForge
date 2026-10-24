using Google.Cloud.Firestore;

namespace FortuneForge.Server.GameReports;

internal sealed class FirestoreGameReportsStore(FirestoreDb database) : IGameReportsStore
{
    internal const string CollectionName = "gameReports";
    private CollectionReference Reports => database.Collection(CollectionName);
    public Task<GameReportRecord> CreateAsync(GameReportDraft draft, CancellationToken ct) => database.RunTransactionAsync(async transaction =>
    {
        var reference = Reports.Document(draft.Id);
        var existing = await transaction.GetSnapshotAsync(reference, ct);
        if (existing.Exists)
        {
            if (existing.GetValue<string>("fingerprint") != draft.Fingerprint || existing.GetValue<string>("userId") != draft.UserId)
                throw new GameReportConflictException("This request key was already used for a different report.");
            return Read(existing);
        }
        var record = new GameReportRecord(draft.Id, draft.UserId, draft.GameId, draft.GameName, draft.Path, draft.Category,
            draft.Message, "open", 0, draft.NowUtc, draft.NowUtc, "", null);
        transaction.Create(reference, new Dictionary<string, object>
        {
            ["userId"] = record.UserId, ["gameId"] = record.GameId, ["gameName"] = record.GameName,
            ["path"] = record.Path, ["category"] = record.Category, ["message"] = record.Message,
            ["status"] = record.Status, ["version"] = 0L, ["createdAt"] = Timestamp.FromDateTime(draft.NowUtc),
            ["updatedAt"] = Timestamp.FromDateTime(draft.NowUtc), ["adminNote"] = "", ["updatedBy"] = "",
            ["fingerprint"] = draft.Fingerprint, ["schemaVersion"] = 1L,
        });
        return record;
    }, cancellationToken: ct);

    public async Task<IReadOnlyList<GameReportRecord>> ListAsync(int limit, DateTime? beforeUtc, string? beforeId, CancellationToken ct)
    {
        // Only the standard single-field createdAt index is needed; document ID breaks timestamp ties.
        Query query = Reports.OrderByDescending("createdAt").OrderByDescending(FieldPath.DocumentId);
        if (beforeUtc.HasValue && beforeId is not null) query = query.StartAfter(Timestamp.FromDateTime(beforeUtc.Value), Reports.Document(beforeId));
        var snapshot = await query.Limit(limit).GetSnapshotAsync(ct);
        return snapshot.Documents.Select(Read).ToArray();
    }
    public async Task<GameReportRecord?> GetAsync(string id, CancellationToken ct)
    {
        var snapshot = await Reports.Document(id).GetSnapshotAsync(ct);
        return snapshot.Exists ? Read(snapshot) : null;
    }
    public Task<GameReportRecord?> UpdateAsync(GameReportChange change, CancellationToken ct) => database.RunTransactionAsync<GameReportRecord?>(async transaction =>
    {
        var reference = Reports.Document(change.Id);
        var snapshot = await transaction.GetSnapshotAsync(reference, ct);
        if (!snapshot.Exists) return null;
        var prior = Read(snapshot);
        if (prior.Version != change.ExpectedVersion) throw new GameReportConflictException("This report changed elsewhere. Refresh it before saving.");
        var next = prior with { Status = change.Status, AdminNote = change.Note, UpdatedBy = change.ActorId, UpdatedAtUtc = change.NowUtc, Version = checked(prior.Version + 1) };
        transaction.Update(reference, new Dictionary<string, object>
        {
            ["status"] = next.Status, ["version"] = next.Version, ["adminNote"] = next.AdminNote,
            ["updatedBy"] = change.ActorId, ["updatedAt"] = Timestamp.FromDateTime(change.NowUtc),
        });
        transaction.Create(reference.Collection("statusHistory").Document(next.Version.ToString("D20")), new Dictionary<string, object>
        {
            ["before"] = prior.Status, ["after"] = next.Status, ["note"] = change.Note,
            ["actorId"] = change.ActorId, ["occurredAt"] = Timestamp.FromDateTime(change.NowUtc), ["version"] = next.Version,
        });
        return next;
    }, cancellationToken: ct);

    internal static GameReportRecord Read(DocumentSnapshot item) => new(item.Id,
        item.GetValue<string>("userId"), item.GetValue<string>("gameId"), item.GetValue<string>("gameName"),
        item.GetValue<string>("path"), item.GetValue<string>("category"), item.GetValue<string>("message"),
        item.GetValue<string>("status"), item.GetValue<long>("version"), item.GetValue<Timestamp>("createdAt").ToDateTime(),
        item.GetValue<Timestamp>("updatedAt").ToDateTime(), item.GetValue<string>("adminNote"),
        item.GetValue<string>("updatedBy") is { Length: > 0 } actor ? actor : null);
}
