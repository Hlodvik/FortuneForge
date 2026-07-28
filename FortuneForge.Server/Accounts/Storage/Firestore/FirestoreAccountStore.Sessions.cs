using System.Security.Cryptography;
using System.Text;
using FortuneForge.Server.Accounts.Models;
using FortuneForge.Server.Slots.Bonuses;
using FortuneForge.Server.Slots.Models;
using Google.Cloud.Firestore;

namespace FortuneForge.Server.Accounts.Storage;

public sealed partial class FirestoreAccountStore
{
    public Task CreateSessionAsync(
        string tokenHash,
        string userId,
        DateTime createdAtUtc,
        DateTime expiresAtUtc,
        string? ipAddress,
        CancellationToken cancellationToken)
    {
        var session = new Dictionary<string, object>
        {
            ["userId"] = userId,
            ["createdAt"] = Timestamp.FromDateTime(createdAtUtc),
            ["expiresAt"] = Timestamp.FromDateTime(expiresAtUtc),
            ["lastSeenAt"] = Timestamp.FromDateTime(createdAtUtc),
            ["revoked"] = false
        };
        if (!string.IsNullOrWhiteSpace(ipAddress))
        {
            session["createdIp"] = ipAddress;
            session["lastSeenIp"] = ipAddress;
        }

        return database.Collection("accountSessions").Document(tokenHash).CreateAsync(
            session,
            cancellationToken);
    }

    public async Task<string?> ResolveSessionAsync(
        string tokenHash,
        DateTime nowUtc,
        string? ipAddress,
        CancellationToken cancellationToken)
    {
        var reference = database.Collection("accountSessions").Document(tokenHash);
        var snapshot = await reference.GetSnapshotAsync(cancellationToken);
        if (!snapshot.Exists ||
            snapshot.GetValue<bool>("revoked") ||
            snapshot.GetValue<Timestamp>("expiresAt").ToDateTime() <= nowUtc)
        {
            return null;
        }

        var shouldRefreshLastSeen = !snapshot.TryGetValue<Timestamp>("lastSeenAt", out var lastSeenAt) ||
            nowUtc - lastSeenAt.ToDateTime() >= TimeSpan.FromMinutes(15);
        var ipChanged = !string.IsNullOrWhiteSpace(ipAddress) &&
            (!snapshot.TryGetValue<string>("lastSeenIp", out var lastSeenIp) ||
                !string.Equals(lastSeenIp, ipAddress, StringComparison.Ordinal));
        if (shouldRefreshLastSeen || ipChanged)
        {
            var updates = new Dictionary<string, object>
            {
                ["lastSeenAt"] = Timestamp.FromDateTime(nowUtc)
            };
            if (!string.IsNullOrWhiteSpace(ipAddress))
            {
                updates["lastSeenIp"] = ipAddress;
            }

            await reference.UpdateAsync(updates, cancellationToken: cancellationToken);
        }

        return snapshot.GetValue<string>("userId");
    }

    public async Task RevokeSessionAsync(
        string tokenHash,
        DateTime revokedAtUtc,
        CancellationToken cancellationToken)
    {
        var reference = database.Collection("accountSessions").Document(tokenHash);
        var snapshot = await reference.GetSnapshotAsync(cancellationToken);
        if (!snapshot.Exists)
        {
            return;
        }

        await reference.UpdateAsync(
            new Dictionary<string, object>
            {
                ["revoked"] = true,
                ["revokedAt"] = Timestamp.FromDateTime(revokedAtUtc)
            },
            cancellationToken: cancellationToken);
    }

    private async Task RevokeSessionsAsync(
        Query query,
        DateTime revokedAtUtc,
        CancellationToken cancellationToken)
    {
        var snapshots = await query.GetSnapshotAsync(cancellationToken);
        foreach (var chunk in snapshots.Documents.Chunk(450))
        {
            var batch = database.StartBatch();
            foreach (var snapshot in chunk)
            {
                batch.Update(snapshot.Reference, new Dictionary<string, object>
                {
                    ["revoked"] = true,
                    ["revokedAt"] = Timestamp.FromDateTime(revokedAtUtc)
                });
            }

            await batch.CommitAsync(cancellationToken);
        }
    }
}
