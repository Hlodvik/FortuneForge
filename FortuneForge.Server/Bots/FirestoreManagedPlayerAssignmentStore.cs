using Google.Cloud.Firestore;
using FortuneForge.Server.Admin.Management;

namespace FortuneForge.Server.Bots;

internal sealed class FirestoreManagedPlayerAssignmentStore(
    FirestoreDb database,
    bool provisionalReservationBatchesEnabled = false) : IManagedPlayerAssignmentStore, IManagedPlayerAdmissionPolicy, IManagedPlayerReservationBatchStore
{
    private static readonly TimeSpan ReservationLease = TimeSpan.FromMinutes(30);
    public bool ProvisionalBatchesEnabled => provisionalReservationBatchesEnabled;

    public async Task<bool> AllowsNewAssignmentsAsync(string gameId, CancellationToken cancellationToken) =>
        FirestoreAdminManagementStore.ReadState(await database.Document(FirestoreAdminManagementStore.StatePath)
            .GetSnapshotAsync(cancellationToken)).AllowsBots(gameId);

    public Task<bool> TryReserveAsync(
        string profileId,
        string gameId,
        string assignmentId,
        DateTime nowUtc,
        CancellationToken cancellationToken) =>
        TryReserveCoreAsync(profileId, gameId, assignmentId, null, nowUtc, cancellationToken);

    public Task<bool> TryReserveBatchAsync(string profileId, string gameId, string assignmentId,
        string batchId, DateTime nowUtc, CancellationToken cancellationToken) =>
        ProvisionalBatchesEnabled
            ? TryReserveCoreAsync(profileId, gameId, assignmentId, batchId, nowUtc, cancellationToken)
            : Task.FromException<bool>(new InvalidOperationException("Provisional reservation batches are not enabled."));

    private Task<bool> TryReserveCoreAsync(string profileId, string gameId, string assignmentId,
        string? batchId, DateTime nowUtc, CancellationToken cancellationToken) =>
        database.RunTransactionAsync(async transaction =>
        {
            var reference = Assignment(profileId);
            var snapshots = await Task.WhenAll(
                transaction.GetSnapshotAsync(reference, cancellationToken),
                transaction.GetSnapshotAsync(User(profileId), cancellationToken),
                transaction.GetSnapshotAsync(database.Document(FirestoreAdminManagementStore.StatePath), cancellationToken));
            var assignment = snapshots[0];
            var profile = snapshots[1];
            // This read shares the reservation transaction with the admin update.
            // A concurrent disable cannot race a new lease past the switch.
            if (!FirestoreAdminManagementStore.ReadState(snapshots[2]).AllowsBots(gameId)) return false;
            if (!FirestoreManagedPlayerProfileRepository.IsManagedPlayer(profile)) return false;
            if (assignment.Exists && ReadTimestamp(assignment, "leaseUntil") > nowUtc)
            {
                if (ReadString(assignment, "assignmentId") != assignmentId ||
                    ReadString(assignment, "gameId") != gameId) return false;
                // Do not reset accounting or take ownership of a same-table lease.
                // Legacy direct callers commit any provisional lease they adopt.
                if (batchId is null && IsPending(assignment))
                    transaction.Set(reference, new Dictionary<string, object> { ["reservationPending"] = false }, SetOptions.MergeAll);
                return true;
            }
            if (!ManagedPlayerAvailabilityPolicy.IsAvailable(
                    profileId,
                    nowUtc,
                    ReadTimestamp(profile, "managedPlayWindowStartedAt"),
                    ReadLong(profile, "managedPlaySecondsInWindow"),
                    ReadTimestamp(profile, "managedNextAvailableAt")))
                return false;

            var data = AssignmentData(profileId, gameId, assignmentId, nowUtc, nowUtc);
            data["reservationPending"] = batchId is not null;
            data["reservationBatchId"] = batchId ?? string.Empty;
            transaction.Set(reference, data, SetOptions.MergeAll);
            return true;
        }, cancellationToken: cancellationToken);

    public Task<bool> CompleteBatchAsync(string gameId, string assignmentId, string batchId,
        IReadOnlyCollection<string> profileIds, DateTime nowUtc, CancellationToken cancellationToken) =>
        !ProvisionalBatchesEnabled ? Task.FromResult(false) : database.RunTransactionAsync(async transaction =>
        {
            var references = profileIds.Distinct(StringComparer.Ordinal).Select(Assignment).ToArray();
            var snapshots = await Task.WhenAll(references.Select(reference => transaction.GetSnapshotAsync(reference, cancellationToken)));
            var policy = await transaction.GetSnapshotAsync(database.Document(FirestoreAdminManagementStore.StatePath), cancellationToken);
            if (snapshots.Any(snapshot => !snapshot.Exists || ReadString(snapshot, "gameId") != gameId ||
                    ReadString(snapshot, "assignmentId") != assignmentId || ReadTimestamp(snapshot, "leaseUntil") <= nowUtc))
                return false;
            // A completed cohort is safe to resume even after admissions turn off.
            if (snapshots.All(snapshot => !IsPending(snapshot))) return true;
            if (!FirestoreAdminManagementStore.ReadState(policy).AllowsBots(gameId)) return false;
            foreach (var reference in references)
                transaction.Set(reference, new Dictionary<string, object> { ["reservationPending"] = false }, SetOptions.MergeAll);
            return true;
        }, cancellationToken: cancellationToken);

    public Task<bool> ReleaseIncompleteBatchAsync(string profileId, string assignmentId,
        string batchId, DateTime nowUtc, CancellationToken cancellationToken) =>
        ReleaseCoreAsync(profileId, assignmentId, batchId, nowUtc, cancellationToken);

    public async Task HeartbeatAsync(
        string profileId,
        string gameId,
        string assignmentId,
        DateTime nowUtc,
        CancellationToken cancellationToken)
    {
        await database.RunTransactionAsync(async transaction =>
        {
            var reference = Assignment(profileId);
            var snapshots = await Task.WhenAll(
                transaction.GetSnapshotAsync(reference, cancellationToken),
                transaction.GetSnapshotAsync(User(profileId), cancellationToken));
            var snapshot = snapshots[0];
            if (!FirestoreManagedPlayerProfileRepository.IsManagedPlayer(snapshots[1])) return false;
            if (!snapshot.Exists || ReadString(snapshot, "assignmentId") != assignmentId) return false;
            transaction.Set(reference, AssignmentData(
                profileId, gameId, assignmentId, ReadTimestamp(snapshot, "reservedAt"), nowUtc), SetOptions.MergeAll);
            return true;
        }, cancellationToken: cancellationToken);
    }

    public Task<bool> TryRenewAsync(
        string gameId,
        string assignmentId,
        IReadOnlyCollection<string> profileIds,
        DateTime nowUtc,
        CancellationToken cancellationToken)
    {
        var ids = profileIds.Distinct(StringComparer.Ordinal).ToArray();
        if (ids.Length == 0) return Task.FromResult(true);

        return database.RunTransactionAsync(async transaction =>
        {
            var references = ids.Select(Assignment).ToArray();
            var assignmentSnapshots = await Task.WhenAll(references.Select(reference =>
                transaction.GetSnapshotAsync(reference, cancellationToken)));
            var profileSnapshots = await Task.WhenAll(ids.Select(profileId =>
                transaction.GetSnapshotAsync(User(profileId), cancellationToken)));

            for (var index = 0; index < ids.Length; index++)
            {
                var snapshot = assignmentSnapshots[index];
                if (!FirestoreManagedPlayerProfileRepository.IsManagedPlayer(profileSnapshots[index]) ||
                    !snapshot.Exists ||
                    ReadString(snapshot, "profileId") != ids[index] ||
                    ReadString(snapshot, "gameId") != gameId ||
                    ReadString(snapshot, "assignmentId") != assignmentId ||
                    ReadTimestamp(snapshot, "leaseUntil") <= nowUtc)
                    return false;
            }

            for (var index = 0; index < ids.Length; index++)
            {
                transaction.Set(references[index], AssignmentData(
                    ids[index], gameId, assignmentId,
                    ReadTimestamp(assignmentSnapshots[index], "reservedAt"), nowUtc),
                    SetOptions.MergeAll);
            }
            return true;
        }, cancellationToken: cancellationToken);
    }

    public Task<bool> ReleaseAsync(
        string profileId,
        string assignmentId,
        DateTime nowUtc,
        CancellationToken cancellationToken) =>
        ReleaseCoreAsync(profileId, assignmentId, null, nowUtc, cancellationToken);

    private Task<bool> ReleaseCoreAsync(string profileId, string assignmentId, string? batchId,
        DateTime nowUtc, CancellationToken cancellationToken) =>
        database.RunTransactionAsync(async transaction =>
        {
            var reference = Assignment(profileId);
            var snapshots = await Task.WhenAll(
                transaction.GetSnapshotAsync(reference, cancellationToken),
                transaction.GetSnapshotAsync(User(profileId), cancellationToken));
            var snapshot = snapshots[0];
            var profile = snapshots[1];
            if (!FirestoreManagedPlayerProfileRepository.IsManagedPlayer(profile)) return false;
            if (!snapshot.Exists || ReadString(snapshot, "assignmentId") != assignmentId) return false;
            if (batchId is not null && (!IsPending(snapshot) || ReadString(snapshot, "reservationBatchId") != batchId))
                return false;
            var storedWindowStart = ReadTimestamp(profile, "managedPlayWindowStartedAt");
            var windowStart = ManagedPlayerAvailabilityPolicy.WindowStart(nowUtc, storedWindowStart);
            var activeSeconds = ManagedPlayerAvailabilityPolicy.ActiveSeconds(
                nowUtc,
                storedWindowStart,
                ReadLong(profile, "managedPlaySecondsInWindow"),
                ReadTimestamp(snapshot, "reservedAt"));
            transaction.Set(User(profileId), new Dictionary<string, object>
            {
                ["managedPlayWindowStartedAt"] = Timestamp.FromDateTime(windowStart),
                ["managedPlaySecondsInWindow"] = activeSeconds,
                ["managedNextAvailableAt"] = Timestamp.FromDateTime(
                    ManagedPlayerAvailabilityPolicy.NextAvailable(profileId, nowUtc)),
                ["updatedAt"] = Timestamp.FromDateTime(nowUtc)
            }, SetOptions.MergeAll);
            transaction.Delete(reference);
            return true;
        }, cancellationToken: cancellationToken);

    private static Dictionary<string, object> AssignmentData(
        string profileId,
        string gameId,
        string assignmentId,
        DateTime reservedAtUtc,
        DateTime nowUtc) => new()
    {
        ["profileId"] = profileId,
        ["gameId"] = gameId,
        ["assignmentId"] = assignmentId,
        ["reservedAt"] = Timestamp.FromDateTime(reservedAtUtc),
        ["heartbeatAt"] = Timestamp.FromDateTime(nowUtc),
        ["leaseUntil"] = Timestamp.FromDateTime(nowUtc.Add(ReservationLease)),
        ["schemaVersion"] = 1L
    };

    private DocumentReference Assignment(string userId) =>
        database.Collection("managedPlayerAssignments").Document(userId);
    private DocumentReference User(string userId) =>
        database.Collection("users").Document(userId);
    private static string ReadString(DocumentSnapshot snapshot, string field) =>
        snapshot.Exists && snapshot.TryGetValue<string>(field, out var value) ? value : string.Empty;
    private static bool IsPending(DocumentSnapshot snapshot) =>
        snapshot.Exists && snapshot.TryGetValue<bool>("reservationPending", out var value) && value;
    private static DateTime ReadTimestamp(DocumentSnapshot snapshot, string field) =>
        snapshot.Exists && snapshot.TryGetValue<Timestamp>(field, out var value)
            ? value.ToDateTime()
            : DateTime.UnixEpoch;
    private static long ReadLong(DocumentSnapshot snapshot, string field) =>
        snapshot.Exists && snapshot.TryGetValue<long>(field, out var value) ? value : 0;
}
