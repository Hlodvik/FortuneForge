using FortuneForge.Games.Abstractions.ManagedPlayers;
using FortuneForge.Games.Abstractions.Wallet;
using FortuneForge.Games.Hosting;
using FortuneForge.Server.Bots.Blackjack;
using Google.Cloud.Firestore;
using Microsoft.Extensions.Logging;

namespace FortuneForge.Server.Cards.Blackjack.Table;

internal sealed record BlackjackManagedLeasePair(string AssignmentId, string ProfileId)
{
    internal string Key => BlackjackTableCoordinator.ManagedLeaseKey(AssignmentId, ProfileId);
}

internal sealed partial class FirestoreBlackjackTableStore
{
    internal const string RetiredManagedLeases = "blackjackRetiredManagedLeases";

    private DocumentReference RetiredManagedLease(BlackjackManagedLeasePair pair) =>
        database.Collection(RetiredManagedLeases).Document(BlackjackTableIds.Hash(pair.Key));

    private static IEnumerable<BlackjackManagedLeasePair> ManagedLeases(BlackjackTableLobbyState state) =>
        state.Tables.Values.SelectMany(table => table.Players.Where(BlackjackManagedSeat.IsManaged)
            .Select(player => new BlackjackManagedLeasePair(table.TableId, player.ActorId)));

    private async Task<HashSet<string>> ReadRetiredLeasesAsync(Transaction transaction,
        IEnumerable<BlackjackManagedLeasePair> candidates, CancellationToken token)
    {
        var pairs = candidates.Distinct().ToArray();
        if (pairs.Length == 0) return new(StringComparer.Ordinal);
        var snapshots = await transaction.GetAllSnapshotsAsync(pairs.Select(RetiredManagedLease), token);
        return snapshots.Select((snapshot, index) => (snapshot, pair: pairs[index])).Where(value => value.snapshot.Exists)
            .Select(value => value.pair.Key).ToHashSet(StringComparer.Ordinal);
    }

    private async Task CheckManagedAdoptionsAsync(Transaction transaction, BlackjackTableLobbyState after, CancellationToken token)
    {
        var adopted = after.Tables.Values.SelectMany(table => table.Players
            .Where(player => BlackjackManagedSeat.IsManaged(player) && !BlackjackTableCoordinator.ManagedLeaseLost(player))
            .Select(player => new BlackjackManagedLeasePair(table.TableId, player.ActorId)));
        if ((await ReadRetiredLeasesAsync(transaction, adopted, token)).Count > 0)
            throw new BlackjackManagedLeaseRetiredException();
    }

    private async Task<BlackjackTableCoordinator> CreateCoordinatorAsync(Transaction transaction, BlackjackTableLobbyState state,
        IReadOnlyCollection<BlackjackReservedManagedPlayer> reservations, bool allowManagedArrivals, CancellationToken token)
    {
        var lost = await ReadRetiredLeasesAsync(transaction, ManagedLeases(state).Concat(reservations
            .Select(value => new BlackjackManagedLeasePair(value.AssignmentId, value.Profile.UserId))), token);
        await RenewManagedPlayersAsync(state, reservations, lost, token);
        var supply = new BlackjackManagedPlayerSupply(reservations.Where(value => !lost.Contains(
            BlackjackTableCoordinator.ManagedLeaseKey(value.AssignmentId, value.Profile.UserId))).ToArray());
        return new BlackjackTableCoordinator(deckFactory, seedFactory, supply, population,
            allowManagedArrivals && lost.Count == 0, multiplayerEnabled,
            (assignment, profile) => !lost.Contains(BlackjackTableCoordinator.ManagedLeaseKey(assignment, profile)));
    }

    private async Task RenewManagedPlayersAsync(BlackjackTableLobbyState state,
        IReadOnlyCollection<BlackjackReservedManagedPlayer> reservations, HashSet<string> lost, CancellationToken token)
    {
        lost.UnionWith(state.Tables.Values.SelectMany(table => table.Players
            .Where(player => BlackjackManagedSeat.IsManaged(player) && BlackjackTableCoordinator.ManagedLeaseLost(player))
            .Select(player => BlackjackTableCoordinator.ManagedLeaseKey(table.TableId, player.ActorId))));
        var assignments = state.Tables.Values.SelectMany(table => table.Players.Where(BlackjackManagedSeat.IsManaged)
                .Select(player => (Assignment: table.TableId, Profile: player.ActorId)))
            .Concat(reservations.Select(value => (Assignment: value.AssignmentId, Profile: value.Profile.UserId)))
            .Distinct().Where(value => !lost.Contains(BlackjackTableCoordinator.ManagedLeaseKey(value.Assignment, value.Profile)))
            .GroupBy(value => value.Assignment, StringComparer.Ordinal);
        foreach (var assignment in assignments)
        {
            var ids = assignment.Select(value => value.Profile).ToArray();
            if (await roster.TryRenewAsync(assignment.Key, ids, DateTime.UtcNow, token)) continue;
            foreach (var profile in ids)
                if (ids.Length == 1 || !await roster.TryRenewAsync(assignment.Key, [profile], DateTime.UtcNow, token))
                    lost.Add(BlackjackTableCoordinator.ManagedLeaseKey(assignment.Key, profile));
        }
    }

    private async Task<IReadOnlyList<ManagedPlayerRosterProfile>> ReserveManagedPlayersAsync(
        BlackjackManagedPlayerSupplyRequiredException required, IReadOnlyCollection<BlackjackReservedManagedPlayer> reservations,
        CancellationToken token)
    {
        var retired = await database.Collection(RetiredManagedLeases).WhereEqualTo("assignmentId", required.AssignmentId).GetSnapshotAsync(token);
        var excluded = required.ExcludedProfileIds.Concat(reservations.Select(value => value.Profile.UserId))
            .Concat(retired.Documents.Select(document => document.GetValue<string>("profileId"))).Distinct(StringComparer.Ordinal).ToArray();
        // Never truncate permanent exclusions or send an invalid roster request. The existing
        // empty-supply policy keeps the human table available without admitting another managed seat.
        if (excluded.Length > GameManagedPlayerRosterProtocol.MaximumExclusions) return [];
        var now = DateTime.UtcNow;
        var request = new ManagedPlayerReserveRequest(required.AssignmentId, required.Count, excluded, now);
        if (System.Text.Json.JsonSerializer.SerializeToUtf8Bytes(request, GameManagedPlayerRosterProtocol.WireJson).Length >
            GameManagedPlayerRosterProtocol.MaximumBodyBytes) return [];
        return await roster.ReserveAsync(required.AssignmentId, required.Count, excluded, now, token);
    }

    private Task ReleaseUntransferredReservationsAsync(string stateId, string? userId, string? key,
        IReadOnlyCollection<BlackjackReservedManagedPlayer> reservations, StoredTableCommand? transferred) =>
        RetireAndReleaseManagedLeasesAsync(stateId, userId, key, reservations
            .Select(value => new BlackjackManagedLeasePair(value.AssignmentId, value.Profile.UserId)).ToArray(), transferred);

    private async Task RetireAndReleaseManagedLeasesAsync(string stateId, string? userId, string? key,
        IReadOnlyCollection<BlackjackManagedLeasePair> candidates, StoredTableCommand? transferred, bool requireRelease = false)
    {
        if (candidates.Count == 0) return;
        using var cleanup = new CancellationTokenSource(TimeSpan.FromSeconds(10));
        try
        {
            var retired = await database.RunTransactionAsync(async transaction =>
            {
                var protectedProfiles = new HashSet<string>(StringComparer.Ordinal);
                void Protect(StoredTableCommand command)
                {
                    var effective = command.Declined ? new BlackjackTableLobbyState { Tables = command.Terms.Prior.TableSnapshots } : command.Terms.After;
                    protectedProfiles.UnionWith(ManagedLeases(effective).Select(value => value.Key));
                    if (!command.Finalized)
                        protectedProfiles.UnionWith(command.Terms.Reservations.Select(value =>
                            BlackjackTableCoordinator.ManagedLeaseKey(value.AssignmentId, value.Profile.UserId)));
                }
                if (transferred is not null)
                {
                    var original = await transaction.GetSnapshotAsync(PreparedCommand(transferred.Terms.Id), cleanup.Token);
                    if (original.Exists) Protect(ReadPrepared(original));
                    // A read-only coordinator result deliberately has no persisted command or receipts.
                    else if (!transferred.RosterDone || transferred.Receipts.Count != 0)
                        throw new GameWalletUnavailableException();
                }
                var stateIds = new HashSet<string>([stateId], StringComparer.Ordinal);
                var pendingIds = new HashSet<string>(StringComparer.Ordinal);
                if (userId is not null)
                {
                    var account = await transaction.GetSnapshotAsync(PendingAccount(userId), cleanup.Token);
                    var route = await transaction.GetSnapshotAsync(SessionDocument(userId), cleanup.Token);
                    foreach (var snapshot in new[] { account, route })
                        if (ReadString(snapshot, "stateId") is { Length: > 0 } routed) stateIds.Add(routed);
                    if (ReadString(account, "pendingId") is { Length: > 0 } accountPending) pendingIds.Add(accountPending);
                    if (key is not null)
                    {
                        var command = await transaction.GetSnapshotAsync(PreparedCommand(GuardKey(userId, key)), cleanup.Token);
                        if (command.Exists) Protect(ReadPrepared(command));
                    }
                }
                foreach (var candidate in stateIds)
                {
                    var snapshot = await transaction.GetSnapshotAsync(StateDocument(candidate), cleanup.Token);
                    foreach (var table in ReadState(snapshot).Tables.Values)
                        foreach (var player in table.Players.Where(BlackjackManagedSeat.IsManaged))
                            protectedProfiles.Add(BlackjackTableCoordinator.ManagedLeaseKey(table.TableId, player.ActorId));
                    var pending = await transaction.GetSnapshotAsync(PendingLock(candidate), cleanup.Token);
                    if (ReadString(pending, "pendingId") is { Length: > 0 } id) pendingIds.Add(id);
                }
                foreach (var pending in pendingIds)
                    Protect(ReadPrepared(await transaction.GetSnapshotAsync(PreparedCommand(pending), cleanup.Token)));
                var available = candidates.Distinct().Where(pair => !protectedProfiles.Contains(pair.Key)).ToArray();
                var alreadyRetired = await ReadRetiredLeasesAsync(transaction, available, cleanup.Token);
                // Permanent pair tombstones share the transaction with the adoption scan. Every
                // new preparation reads them, so an overlapping adoption either wins first or retries.
                foreach (var pair in available.Where(pair => !alreadyRetired.Contains(pair.Key)))
                    transaction.Create(RetiredManagedLease(pair), new Dictionary<string, object>
                    {
                        ["assignmentId"] = pair.AssignmentId, ["profileId"] = pair.ProfileId,
                        ["retiredAtUtc"] = Timestamp.FromDateTime(DateTime.UtcNow), ["backendOwner"] = BlackjackBackend.Owner,
                    });
                return available;
            }, cancellationToken: cleanup.Token);
            foreach (var assignment in retired.GroupBy(value => value.AssignmentId, StringComparer.Ordinal))
                await roster.ReleaseAsync(assignment.Key, assignment.Select(value => value.ProfileId).ToArray(),
                    DateTime.UtcNow, cleanup.Token);
        }
        catch (Exception error)
        {
            // An ambiguous prepare commit may already own these profiles. Never release an
            // adopted lease merely because storage or cleanup is unavailable; its platform lease is bounded.
            logger?.LogWarning(error, "Blackjack reservation cleanup retained uncertain leases for shard {StateId}.", stateId);
            if (requireRelease) throw;
        }
    }
}

internal sealed class BlackjackManagedLeaseRetiredException : Exception { }
