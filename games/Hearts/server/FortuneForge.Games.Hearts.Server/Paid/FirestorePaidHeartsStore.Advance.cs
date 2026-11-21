using System.Text.Json;
using Google.Cloud.Firestore;

namespace FortuneForge.Server.Games.Hearts;

internal sealed partial class FirestorePaidHeartsStore
{
    internal async Task<PaidHeartsSessionResponse> AdvanceCommandAsync(string userId, string id, int? expectedVersion,
        string key, DateTime now, CancellationToken ct)
    {
        id = Identifier(id); Key(key);
        if (expectedVersion < 0) throw new ArgumentException("ExpectedVersion cannot be negative.");
        var detail = JsonSerializer.Serialize(new { expectedVersion }, Json);
        if (await ReplayAsync(userId, key, "advance", id, detail, ct) is { } cached) return cached;
        await RecoverAccountAsync(userId, ct);
        await RecoverMatchAsync(id, ct);
        if (await ReplayAsync(userId, key, "advance", id, detail, ct) is { } replay) return replay;
        var observed = await Match(id).GetSnapshotAsync(ct);
        if (!observed.Exists || !Read(observed).Players.Any(value => value.IsAccountBacked && value.UserId == userId))
            throw new HeartsAccessException();
        if (observed.Exists && Read(observed) is { Status: "match", ReleasePending: false } active &&
            active.Players.Any(value => !value.IsAccountBacked && value.ManagedAssignmentId is not null) &&
            (active.LeaseRenewalAtUtc is null || now >= active.LeaseRenewalAtUtc)) await RenewAsync(active, now, ct);
        var seed = PaidHeartsIds.Seed();
        await TransactionAsync(async tx =>
        {
            var guard = await tx.GetSnapshotAsync(Guard(userId, key), ct);
            if (guard.Exists) { Verify(guard, "advance", id, detail); return false; }
            var saved = await tx.GetSnapshotAsync(Match(id), ct);
            if (!saved.Exists) throw new HeartsAccessException();
            var match = Read(saved);
            if (!match.Players.Any(value => value.IsAccountBacked && value.UserId == userId)) throw new HeartsAccessException();
            var legacySeats = match.Players.Any(value => !value.IsAccountBacked);
            if (match.Status == "queue" && (legacySeats || now >= match.CreatedAtUtc.AddMinutes(2)))
                await ExpireQueueAsync(tx, match, now, legacySeats, ct);
            else if (ActionIsDue(match, now))
                await AdvanceDueActionAsync(tx, match, await BalancesAsync(tx, match, ct), now, seed, userId, ct);
            tx.ResponseMatch = match;
            tx.Create(Guard(userId, key), GuardData("advance", id, detail, now));
            return true;
        }, ct, userId, now);
        await ReleaseIfNeededAsync(id, now, ct);
        return await SavedOrSessionAsync(userId, key, now, ct);
    }
}
