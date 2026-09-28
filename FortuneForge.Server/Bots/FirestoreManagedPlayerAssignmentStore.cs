using Google.Cloud.Firestore;

namespace FortuneForge.Server.Bots;

internal sealed class FirestoreManagedPlayerAssignmentStore(
    FirestoreDb database) : IManagedPlayerAssignmentStore
{
    private static readonly TimeSpan ReservationLease = TimeSpan.FromMinutes(30);

    public Task<bool> TryReserveAsync(
        string profileId,
        string gameId,
        string assignmentId,
        DateTime nowUtc,
        CancellationToken cancellationToken) =>
        database.RunTransactionAsync(async transaction =>
        {
            var reference = Assignment(profileId);
            var snapshots = await Task.WhenAll(
                transaction.GetSnapshotAsync(reference, cancellationToken),
                transaction.GetSnapshotAsync(User(profileId), cancellationToken));
            var assignment = snapshots[0];
            if (!FirestoreManagedPlayerProfileRepository.IsManagedPlayer(snapshots[1])) return false;
            if (assignment.Exists &&
                ReadString(assignment, "assignmentId") != assignmentId &&
                ReadTimestamp(assignment, "leaseUntil") > nowUtc)
                return false;

            transaction.Set(reference, AssignmentData(
                profileId, gameId, assignmentId, nowUtc), SetOptions.MergeAll);
            return true;
        }, cancellationToken: cancellationToken);

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
                profileId, gameId, assignmentId, nowUtc), SetOptions.MergeAll);
            return true;
        }, cancellationToken: cancellationToken);
    }

    public Task<bool> ReleaseAsync(
        string profileId,
        string assignmentId,
        CancellationToken cancellationToken) =>
        database.RunTransactionAsync(async transaction =>
        {
            var reference = Assignment(profileId);
            var snapshots = await Task.WhenAll(
                transaction.GetSnapshotAsync(reference, cancellationToken),
                transaction.GetSnapshotAsync(User(profileId), cancellationToken));
            var snapshot = snapshots[0];
            if (!FirestoreManagedPlayerProfileRepository.IsManagedPlayer(snapshots[1])) return false;
            if (!snapshot.Exists || ReadString(snapshot, "assignmentId") != assignmentId) return false;
            transaction.Delete(reference);
            return true;
        }, cancellationToken: cancellationToken);

    private static Dictionary<string, object> AssignmentData(
        string profileId,
        string gameId,
        string assignmentId,
        DateTime nowUtc) => new()
    {
        ["profileId"] = profileId,
        ["gameId"] = gameId,
        ["assignmentId"] = assignmentId,
        ["reservedAt"] = Timestamp.FromDateTime(nowUtc),
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
    private static DateTime ReadTimestamp(DocumentSnapshot snapshot, string field) =>
        snapshot.Exists && snapshot.TryGetValue<Timestamp>(field, out var value)
            ? value.ToDateTime()
            : DateTime.UnixEpoch;
}
