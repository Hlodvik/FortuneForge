using Google.Cloud.Firestore;
using Grpc.Core;

namespace FortuneForge.Server.Activity;

public sealed record RecentGameActivity(string GameId, DateTime PlayedAtUtc);
internal interface IGameActivityStore
{
    Task RecordAsync(string userId, AcceptedGameActivity activity, DateTime nowUtc, CancellationToken ct);
    Task<IReadOnlyList<RecentGameActivity>> ReadAsync(string userId, CancellationToken ct);
}

internal sealed class FirestoreGameActivityStore(FirestoreDb database) : IGameActivityStore
{
    private DocumentReference Account(string userId) => database.Collection("catalogPlayerActivity").Document(AcceptedGameActivityReader.Hash(userId));

    public async Task RecordAsync(string userId, AcceptedGameActivity activity, DateTime nowUtc, CancellationToken ct)
    {
        // Firestore can abort while acquiring read locks, before its own commit
        // retry loop begins. Retry the whole idempotent transaction in that case.
        for (var attempt = 0; ; attempt++)
        {
            try { await RecordOnceAsync(userId, activity, nowUtc, ct); return; }
            catch (RpcException exception) when (exception.StatusCode == StatusCode.Aborted && attempt < 4)
            { await Task.Delay(TimeSpan.FromMilliseconds(Random.Shared.Next(30, 120) * (attempt + 1)), ct); }
        }
    }

    private Task RecordOnceAsync(string userId, AcceptedGameActivity activity, DateTime nowUtc, CancellationToken ct) => database.RunTransactionAsync(async transaction =>
    {
        var account = Account(userId);
        var roundKey = AcceptedGameActivityReader.Hash($"{activity.GameId}\n{activity.RoundKey}");
        var round = account.Collection("plays").Document(roundKey);
        var receipt = round.Collection("receipts").Document(activity.EventKey);
        var metadata = database.Collection("catalogGamePopularityMetadata").Document("tracking");
        var recent = account.Collection("games").Document(activity.GameId);
        var priorReceipt = await transaction.GetSnapshotAsync(receipt, ct);
        if (priorReceipt.Exists) return false;
        var priorRound = await transaction.GetSnapshotAsync(round, ct);
        var priorRecent = await transaction.GetSnapshotAsync(recent, ct);
        var tracking = priorRound.Exists ? null : await transaction.GetSnapshotAsync(metadata, ct);
        var timestamp = Timestamp.FromDateTime(nowUtc);
        transaction.Create(receipt, new Dictionary<string, object> { ["acceptedAt"] = timestamp });
        if (!priorRound.Exists)
        {
            transaction.Create(round, new Dictionary<string, object> { ["gameId"] = activity.GameId, ["startedAt"] = timestamp });
            transaction.Set(database.Collection("catalogGamePopularity").Document(activity.GameId), new Dictionary<string, object>
            {
                ["playCount"] = FieldValue.Increment(1), ["lastPlayedAt"] = FieldValue.ServerTimestamp, ["schemaVersion"] = 1L,
            }, SetOptions.MergeAll);
        }
        if (!priorRecent.TryGetValue<Timestamp>("playedAt", out var priorPlayedAt) || priorPlayedAt.ToDateTime() < nowUtc)
            transaction.Set(recent, new Dictionary<string, object> { ["playedAt"] = timestamp, ["schemaVersion"] = 1L }, SetOptions.MergeAll);
        if (tracking is { Exists: false })
            transaction.Create(metadata, new Dictionary<string, object> { ["startedAt"] = timestamp, ["schemaVersion"] = 1L });
        return true;
    }, cancellationToken: ct);

    public async Task<IReadOnlyList<RecentGameActivity>> ReadAsync(string userId, CancellationToken ct)
    {
        // At most one small document per catalogue game. No composite index,
        // growing ledger scan, or arbitrary first-N history window is involved.
        var snapshot = await Account(userId).Collection("games").GetSnapshotAsync(ct);
        return snapshot.Documents.Where(item => item.TryGetValue<Timestamp>("playedAt", out _))
            .Select(item => new RecentGameActivity(item.Id, item.GetValue<Timestamp>("playedAt").ToDateTime()))
            .OrderByDescending(item => item.PlayedAtUtc).ThenBy(item => item.GameId, StringComparer.Ordinal).ToArray();
    }
}
