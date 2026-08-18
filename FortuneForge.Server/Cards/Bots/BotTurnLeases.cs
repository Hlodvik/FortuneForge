using System.Security.Cryptography;
using System.Text;
using Google.Cloud.Firestore;

namespace FortuneForge.Server.Cards.Bots;

internal sealed record BotTurnKey(string Game, string MatchId, string SeatId, int ExpectedVersion);
internal sealed record BotTurnLease(BotTurnKey Key, string OwnerId, string Token, DateTime ExpiresAtUtc);

internal interface IBotTurnLeaseStore
{
    Task<BotTurnLease?> TryAcquireAsync(
        BotTurnKey key,
        string ownerId,
        DateTime nowUtc,
        TimeSpan duration,
        CancellationToken cancellationToken);

    Task<bool> CompleteAsync(
        BotTurnLease lease,
        int resultVersion,
        DateTime nowUtc,
        CancellationToken cancellationToken);
}

/// <summary>
/// Firestore transaction guard shared across server instances. A completed expectedVersion
/// can never be acquired again; abandoned claims become available only after lease expiry.
/// </summary>
internal sealed class FirestoreBotTurnLeaseStore(FirestoreDb database) : IBotTurnLeaseStore
{
    public Task<BotTurnLease?> TryAcquireAsync(
        BotTurnKey key,
        string ownerId,
        DateTime nowUtc,
        TimeSpan duration,
        CancellationToken cancellationToken)
    {
        var reference = Document(key);
        return database.RunTransactionAsync(async transaction =>
        {
            var snapshot = await transaction.GetSnapshotAsync(reference, cancellationToken);
            if (snapshot.Exists)
            {
                var completed = snapshot.TryGetValue<long>("completedExpectedVersion", out var value)
                    ? checked((int)value)
                    : 0;
                if (completed >= key.ExpectedVersion) return null;
                var expiry = snapshot.TryGetValue<Timestamp>("expiresAt", out var timestamp)
                    ? timestamp.ToDateTime()
                    : DateTime.MinValue;
                if (expiry > nowUtc) return null;
            }

            var token = Guid.NewGuid().ToString("N");
            var lease = new BotTurnLease(key, ownerId, token, nowUtc.Add(duration));
            transaction.Set(reference, new Dictionary<string, object>
            {
                ["game"] = key.Game,
                ["matchId"] = key.MatchId,
                ["seatId"] = key.SeatId,
                ["expectedVersion"] = key.ExpectedVersion,
                ["ownerId"] = ownerId,
                ["token"] = token,
                ["expiresAt"] = Timestamp.FromDateTime(lease.ExpiresAtUtc),
                ["updatedAt"] = Timestamp.FromDateTime(nowUtc)
            }, SetOptions.MergeAll);
            return lease;
        }, cancellationToken: cancellationToken);
    }

    public Task<bool> CompleteAsync(
        BotTurnLease lease,
        int resultVersion,
        DateTime nowUtc,
        CancellationToken cancellationToken)
    {
        var reference = Document(lease.Key);
        return database.RunTransactionAsync(async transaction =>
        {
            var snapshot = await transaction.GetSnapshotAsync(reference, cancellationToken);
            if (!snapshot.Exists ||
                !string.Equals(snapshot.GetValue<string>("token"), lease.Token, StringComparison.Ordinal))
                return false;

            transaction.Set(reference, new Dictionary<string, object>
            {
                ["completedExpectedVersion"] = lease.Key.ExpectedVersion,
                ["resultVersion"] = resultVersion,
                ["completedAt"] = Timestamp.FromDateTime(nowUtc),
                ["expiresAt"] = Timestamp.FromDateTime(nowUtc),
                ["updatedAt"] = Timestamp.FromDateTime(nowUtc)
            }, SetOptions.MergeAll);
            return true;
        }, cancellationToken: cancellationToken);
    }

    private DocumentReference Document(BotTurnKey key)
    {
        var source = $"{key.Game}\n{key.MatchId}\n{key.SeatId}\n{key.ExpectedVersion}";
        var id = Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(source)));
        return database.Collection("cardBotTurnLeases").Document(id);
    }
}

internal sealed class InMemoryBotTurnLeaseStore : IBotTurnLeaseStore
{
    private readonly object gate = new();
    private readonly Dictionary<BotTurnKey, LeaseState> leases = [];

    public Task<BotTurnLease?> TryAcquireAsync(
        BotTurnKey key,
        string ownerId,
        DateTime nowUtc,
        TimeSpan duration,
        CancellationToken cancellationToken)
    {
        lock (gate)
        {
            if (leases.TryGetValue(key, out var state) &&
                (state.Completed || state.Lease.ExpiresAtUtc > nowUtc))
                return Task.FromResult<BotTurnLease?>(null);
            var lease = new BotTurnLease(key, ownerId, Guid.NewGuid().ToString("N"), nowUtc.Add(duration));
            leases[key] = new LeaseState(lease, false);
            return Task.FromResult<BotTurnLease?>(lease);
        }
    }

    public Task<bool> CompleteAsync(
        BotTurnLease lease,
        int resultVersion,
        DateTime nowUtc,
        CancellationToken cancellationToken)
    {
        lock (gate)
        {
            if (!leases.TryGetValue(lease.Key, out var state) || state.Lease.Token != lease.Token)
                return Task.FromResult(false);
            leases[lease.Key] = state with { Completed = true };
            return Task.FromResult(true);
        }
    }

    private sealed record LeaseState(BotTurnLease Lease, bool Completed);
}
