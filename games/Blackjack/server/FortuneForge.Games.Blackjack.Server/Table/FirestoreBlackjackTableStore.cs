using System.Text.Json;
using FortuneForge.Games.Abstractions.ManagedPlayers;
using FortuneForge.Games.Abstractions.Wallet;
using FortuneForge.Games.Firestore;
using Google.Cloud.Firestore;
using FortuneForge.Server.Bots.Blackjack;
using Grpc.Core;
using Microsoft.Extensions.Logging;

namespace FortuneForge.Server.Cards.Blackjack.Table;

internal sealed partial class FirestoreBlackjackTableStore : IBlackjackTableStore
{
    private const string CurrencyId = "slotsCredits";
    private const int MaximumShardCandidates = 20;
    private const int MaximumSweepBatch = 25;
    private static readonly TimeSpan GuardRetention = TimeSpan.FromDays(7);
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);
    private readonly FirestoreDb database;
    private readonly IGameWalletClient wallet;
    private readonly IGameWalletAccountReader accounts;
    private readonly IManagedPlayerRosterClient roster;
    private readonly Func<IReadOnlyList<string>>? deckFactory;
    private readonly Func<ulong>? seedFactory;
    private readonly IManagedTablePopulationDirector population;
    private readonly bool multiplayerEnabled;
    private readonly Func<bool> admissionOpen;
    private readonly ILogger<FirestoreBlackjackTableStore>? logger;
    private readonly string leaseOwner = $"blackjack-table-worker-{Guid.NewGuid():N}";

    internal FirestoreBlackjackTableStore(
        FirestoreDb database,
        IGameWalletClient wallet,
        IGameWalletAccountReader accounts,
        IManagedPlayerRosterClient roster,
        Func<IReadOnlyList<string>>? deckFactory = null,
        Func<ulong>? seedFactory = null,
        IManagedTablePopulationDirector? managedPopulationDirector = null,
        bool multiplayerEnabled = true,
        Func<bool>? allowNewRounds = null,
        ILogger<FirestoreBlackjackTableStore>? logger = null)
    {
        this.database = database;
        this.wallet = wallet;
        this.accounts = accounts;
        this.roster = roster;
        this.deckFactory = deckFactory;
        this.seedFactory = seedFactory;
        population = managedPopulationDirector ?? new ManagedTablePopulationDirector();
        this.multiplayerEnabled = multiplayerEnabled;
        admissionOpen = allowNewRounds ?? (() => true);
        this.logger = logger;
    }

    public async Task<BlackjackTableStoreResult> GetSessionAsync(
        string userId,
        DateTime nowUtc,
        CancellationToken cancellationToken)
    {
        await RecoverAccountAsync(userId, cancellationToken);
        var route = await SessionDocument(userId).GetSnapshotAsync(cancellationToken);
        var stateId = ReadString(route, "stateId");
        if (string.IsNullOrEmpty(stateId))
        {
            return new(new BlackjackTableIdleSessionResponse(), await AccountBalanceAsync(userId, cancellationToken));
        }

        return await ExecuteRoutedAsync(
            stateId,
            userId,
            null,
            false,
            (coordinator, state, balances) => coordinator.GetAsync(state, balances, userId, nowUtc),
            nowUtc,
            cancellationToken);
    }

    public async Task<BlackjackTableStoreResult> JoinAsync(
        string userId,
        string displayName,
        int expectedVersion,
        string idempotencyKey,
        DateTime nowUtc,
        CancellationToken cancellationToken)
    {
        var requestSha = JoinRequestHash(expectedVersion);
        if (await TryReplayJoinAsync(userId, expectedVersion, idempotencyKey, nowUtc, cancellationToken) is { } replay)
            return replay;
        await RecoverAccountAsync(userId, cancellationToken);
        if (await TryReplayJoinAsync(userId, expectedVersion, idempotencyKey, nowUtc, cancellationToken) is { } recovered)
            return recovered;

        if (!admissionOpen()) throw new BlackjackAdmissionPausedException();
        ValueTask<BlackjackTableCoordinatorResult> Command(
            BlackjackTableCoordinator coordinator,
            BlackjackTableLobbyState state,
            IDictionary<string, long> balances) =>
            coordinator.JoinAsync(
                state,
                balances,
                userId,
                displayName,
                expectedVersion,
                idempotencyKey,
                nowUtc);

        var existing = await ResolveStateIdAsync(userId, idempotencyKey, cancellationToken);
        if (!string.IsNullOrEmpty(existing))
        {
            return await ExecuteRoutedAsync(
                existing,
                userId,
                idempotencyKey,
                false,
                Command,
                nowUtc,
                cancellationToken, requestSha);
        }

        var excluded = new HashSet<string>(StringComparer.Ordinal);
        for (var attempt = 0; attempt < 8; attempt++)
        {
            var stateId = multiplayerEnabled
                ? await FindAcceptingStateIdAsync(excluded, cancellationToken) ?? CohortStateId(nowUtc, attempt)
                : BlackjackTableIds.Hash($"blackjack-solo-state\n{userId}\n{idempotencyKey}");
            try
            {
                return await ExecuteRoutedAsync(
                    stateId,
                    userId,
                    idempotencyKey,
                    multiplayerEnabled,
                    Command,
                    nowUtc,
                    cancellationToken, requestSha);
            }
            catch (BlackjackTableShardUnavailableException)
            {
                excluded.Add(stateId);
            }
        }

        throw new BlackjackTableConflictException(
            "Blackjack table matchmaking is busy. Reconnect before joining again.");
    }

    private static string JoinRequestHash(int expectedVersion) => RequestHash("join", expectedVersion);

    internal async Task<BlackjackTableStoreResult?> TryReplayJoinAsync(string userId, int expectedVersion,
        string idempotencyKey, DateTime nowUtc, CancellationToken cancellationToken)
    {
        if (await ReadReplayAsync(userId, idempotencyKey, JoinRequestHash(expectedVersion), cancellationToken) is { } recorded)
            return Reply(recorded);
        var replaySnapshot = await GuardDocument(userId, idempotencyKey)
            .GetSnapshotAsync(cancellationToken);
        if (replaySnapshot.Exists)
        {
            var replay = ReadGuard(replaySnapshot);
            var expectedTicketId = BlackjackTableIds.Hash($"{userId}\n{idempotencyKey}");
            var expectedDetail = expectedVersion.ToString(System.Globalization.CultureInfo.InvariantCulture);
            if (replay.Operation != "join" || replay.Target != expectedTicketId || replay.Detail != expectedDetail)
                throw new BlackjackTableConflictException(
                    "This Idempotency-Key was already used for a different Blackjack table request.");
            return await ReadCurrentSessionAsync(userId, nowUtc, cancellationToken);
        }

        return null;
    }

    private async Task<BlackjackTableStoreResult> ReadCurrentSessionAsync(string userId, DateTime nowUtc, CancellationToken cancellationToken)
    {
        await RecoverAccountAsync(userId, cancellationToken);
        var route = await SessionDocument(userId).GetSnapshotAsync(cancellationToken);
        var balance = await AccountBalanceAsync(userId, cancellationToken);
        var stateId = ReadString(route, "stateId");
        if (string.IsNullOrEmpty(stateId)) return new(new BlackjackTableIdleSessionResponse(), balance);
        await RecoverShardAsync(stateId, cancellationToken);
        var snapshot = await StateDocument(stateId).GetSnapshotAsync(cancellationToken);
        return !snapshot.Exists ? new(new BlackjackTableIdleSessionResponse(), balance)
            : new(BlackjackTableProjection.Session(ReadState(snapshot), userId, nowUtc), balance);
    }

    public Task<BlackjackTableStoreResult> CancelAsync(
        string userId,
        string ticketId,
        int expectedVersion,
        string idempotencyKey,
        DateTime nowUtc,
        CancellationToken cancellationToken) =>
        ExecuteResolvedAsync(
            userId,
            idempotencyKey,
            (coordinator, state, balances) => coordinator.CancelAsync(
                state, balances, userId, ticketId, expectedVersion, idempotencyKey, nowUtc),
            nowUtc,
            cancellationToken, RequestHash("cancel", ticketId, expectedVersion));

    public Task<BlackjackTableStoreResult> WagerAsync(
        string userId,
        string tableId,
        long wagerCents,
        int expectedVersion,
        string idempotencyKey,
        DateTime nowUtc,
        CancellationToken cancellationToken) =>
        ExecuteResolvedAsync(
            userId,
            idempotencyKey,
            (coordinator, state, balances) => coordinator.WagerAsync(
                state, balances, userId, tableId, wagerCents, expectedVersion, idempotencyKey, nowUtc),
            nowUtc,
            cancellationToken, RequestHash("wager", tableId, wagerCents, expectedVersion), allowNewAdmission: true);

    public Task<BlackjackTableStoreResult> SitOutAsync(
        string userId,
        string tableId,
        int expectedVersion,
        string idempotencyKey,
        DateTime nowUtc,
        CancellationToken cancellationToken) =>
        ExecuteResolvedAsync(
            userId,
            idempotencyKey,
            (coordinator, state, balances) => coordinator.SitOutAsync(
                state, balances, userId, tableId, expectedVersion, idempotencyKey, nowUtc),
            nowUtc,
            cancellationToken, RequestHash("sit-out", tableId, expectedVersion));

    public Task<BlackjackTableStoreResult> ActionAsync(
        string userId,
        string tableId,
        string action,
        int expectedVersion,
        string idempotencyKey,
        DateTime nowUtc,
        CancellationToken cancellationToken) =>
        ExecuteResolvedAsync(
            userId,
            idempotencyKey,
            (coordinator, state, balances) => coordinator.ActionAsync(
                state, balances, userId, tableId, action, expectedVersion, idempotencyKey, nowUtc),
            nowUtc,
            cancellationToken, RequestHash("action", tableId, action.Trim().ToLowerInvariant(), expectedVersion));

    public Task<BlackjackTableStoreResult> LeaveAsync(
        string userId,
        string tableId,
        int expectedVersion,
        string idempotencyKey,
        DateTime nowUtc,
        CancellationToken cancellationToken) =>
        ExecuteResolvedAsync(
            userId,
            idempotencyKey,
            (coordinator, state, balances) => coordinator.LeaveAsync(
                state, balances, userId, tableId, expectedVersion, idempotencyKey, nowUtc),
            nowUtc,
            cancellationToken, RequestHash("leave", tableId, expectedVersion));

    public async Task SweepAsync(DateTime nowUtc, CancellationToken cancellationToken)
    {
        var acquired = await RunTransactionAsync(async transaction =>
        {
            var lease = await transaction.GetSnapshotAsync(LeaseDocument(), cancellationToken);
            if (lease.Exists && ReadString(lease, "owner") != leaseOwner &&
                lease.TryGetValue<Timestamp>("leaseUntil", out var timestamp) &&
                timestamp.ToDateTime() > nowUtc)
                return false;
            transaction.Set(LeaseDocument(), new Dictionary<string, object>
            {
                ["owner"] = leaseOwner,
                ["leaseUntil"] = Timestamp.FromDateTime(nowUtc.AddSeconds(2)),
                ["heartbeatAt"] = Timestamp.FromDateTime(nowUtc),
                ["schemaVersion"] = 2L
            }, SetOptions.MergeAll);
            return true;
        }, cancellationToken);
        if (!acquired) return;
        var pending = await PendingLocks().WhereEqualTo("status", "pending").Limit(MaximumSweepBatch).GetSnapshotAsync(cancellationToken);
        foreach (var item in pending.Documents) await RecoverShardAsync(item.Id, cancellationToken);

        var due = await StateCollection()
            .WhereLessThanOrEqualTo("nextDeadlineAt", Timestamp.FromDateTime(nowUtc))
            .Limit(MaximumSweepBatch)
            .GetSnapshotAsync(cancellationToken);
        foreach (var snapshot in due.Documents)
            await SweepShardAsync(snapshot.Id, nowUtc, cancellationToken);
    }

    public async Task<IReadOnlyList<BlackjackTableHistoryItemResponse>> GetHistoryAsync(
        string userId,
        int limit,
        CancellationToken cancellationToken)
    {
        await RecoverAccountAsync(userId, cancellationToken);
        var snapshots = await database.Collection("cardGameResults")
            .WhereEqualTo("userGameHash", BlackjackTableIds.Hash($"{userId}\nblackjack\ncredit-table"))
            .Limit(Math.Min(limit, 50))
            .GetSnapshotAsync(cancellationToken);
        return snapshots.Documents
            .OrderByDescending(snapshot => ReadTimestamp(snapshot, "completedAt"))
            .Take(limit)
            .Select<DocumentSnapshot, BlackjackTableHistoryItemResponse>(snapshot => HistoryResponse(snapshot))
            .ToArray();
    }

    public Task<BlackjackTableHistoryItemResponse> MarkHistorySeenAsync(
        string userId,
        string resultId,
        DateTime nowUtc,
        CancellationToken cancellationToken) =>
        RunTransactionAsync(async transaction =>
        {
            var reference = CardGameResultDocument(resultId);
            var snapshot = await transaction.GetSnapshotAsync(reference, cancellationToken);
            if (!snapshot.Exists || ReadString(snapshot, "userId") != userId ||
                ReadString(snapshot, "game") != "blackjack" || ReadString(snapshot, "mode") != "credit-table")
                throw new BlackjackTableNotFoundException("The Blackjack table result was not found.");
            DateTime seenAt;
            if (!snapshot.ToDictionary().TryGetValue("seenAt", out var seenValue) || seenValue is not Timestamp priorSeen)
            {
                transaction.Update(reference, new Dictionary<string, object>
                {
                    ["seenAt"] = Timestamp.FromDateTime(nowUtc)
                });
                seenAt = nowUtc;
            }
            else seenAt = priorSeen.ToDateTime();
            return HistoryResponse(snapshot, seenAt);
        }, cancellationToken);

    private async Task<BlackjackTableStoreResult> ExecuteResolvedAsync(string userId, string idempotencyKey,
        Func<BlackjackTableCoordinator, BlackjackTableLobbyState, IDictionary<string, long>, ValueTask<BlackjackTableCoordinatorResult>> command,
        DateTime nowUtc, CancellationToken cancellationToken, string requestSha, bool allowNewAdmission = false)
    {
        if (await ReadReplayAsync(userId, idempotencyKey, requestSha, cancellationToken) is { } recorded) return Reply(recorded);
        await RecoverAccountAsync(userId, cancellationToken);
        if (allowNewAdmission && !admissionOpen() && !(await GuardDocument(userId, idempotencyKey).GetSnapshotAsync(cancellationToken)).Exists)
            throw new BlackjackAdmissionPausedException();
        var stateId = await ResolveStateIdAsync(userId, idempotencyKey, cancellationToken);
        if (string.IsNullOrEmpty(stateId)) throw new BlackjackTableNotFoundException("The Blackjack table session was not found.");
        return await ExecuteRoutedAsync(stateId, userId, idempotencyKey, false, command, nowUtc, cancellationToken, requestSha);
    }

    private async Task<BlackjackTableStoreResult> ExecuteRoutedAsync(string initialStateId, string userId, string? idempotencyKey,
        bool requireAcceptingShard,
        Func<BlackjackTableCoordinator, BlackjackTableLobbyState, IDictionary<string, long>, ValueTask<BlackjackTableCoordinatorResult>> command,
        DateTime nowUtc, CancellationToken cancellationToken, string? requestSha = null)
    {
        var stateId = initialStateId;
        for (var routeAttempt = 0; routeAttempt < 4; routeAttempt++)
        {
            try { return await ExecuteShardAsync(stateId, userId, idempotencyKey, requireAcceptingShard, command, nowUtc, cancellationToken, requestSha); }
            catch (BlackjackTableShardChangedException changed) { stateId = changed.StateId; requireAcceptingShard = false; }
        }
        throw new BlackjackTableConflictException("The Blackjack table session changed. Reconnect and try again.");
    }

    private async Task<BlackjackTableStoreResult> ExecuteShardAsync(string stateId, string userId, string? idempotencyKey,
        bool requireAcceptingShard,
        Func<BlackjackTableCoordinator, BlackjackTableLobbyState, IDictionary<string, long>, ValueTask<BlackjackTableCoordinatorResult>> command,
        DateTime nowUtc, CancellationToken cancellationToken, string? requestSha)
    {
        var reservations = new List<BlackjackReservedManagedPlayer>();
        StoredTableCommand? transferred = null;
        var allowManagedArrivals = true;
        try
        {
            while (true)
            {
                try
                {
                    await RecoverShardAsync(stateId, cancellationToken);
                    if (idempotencyKey is not null && await ReadReplayAsync(userId, idempotencyKey, requestSha!, cancellationToken) is { } recorded)
                        return Reply(recorded);
                    var prepared = await RunTransactionAsync(async transaction =>
                    {
                        var pending = await transaction.GetSnapshotAsync(PendingLock(stateId), cancellationToken);
                        ThrowIfPending(pending);
                        var stateReference = StateDocument(stateId);
                        var guardReference = idempotencyKey is null ? null : GuardDocument(userId, idempotencyKey);
                        var snapshots = await transaction.GetAllSnapshotsAsync(guardReference is null
                            ? [stateReference, SessionDocument(userId)] : [stateReference, SessionDocument(userId), guardReference], cancellationToken);
                        var routed = ReadString(snapshots[1], "stateId");
                        if (!string.IsNullOrEmpty(routed) && routed != stateId) throw new BlackjackTableShardChangedException(routed);
                        var state = ReadState(snapshots[0]);
                        var before = DurableStateJson(state);
                        var guardKey = idempotencyKey is null ? null : GuardKey(userId, idempotencyKey);
                        if (guardReference is not null && snapshots[2].Exists) state.Guards[guardKey!] = ReadGuard(snapshots[2]);
                        if (requireAcceptingShard && (guardReference is null || !snapshots[2].Exists) && !CanAcceptHuman(state))
                            throw new BlackjackTableShardUnavailableException();
                        var prior = StateReferences.Capture(state, userId);
                        var loaded = await ReadBalancesAsync(transaction, state, userId, cancellationToken);
                        var coordinator = await CreateCoordinatorAsync(transaction, state, reservations, allowManagedArrivals, cancellationToken);
                        var result = await command(coordinator, state, loaded.Current);
                        var commandGuard = guardKey is not null && state.Guards.TryGetValue(guardKey, out var saved) ? saved : null;
                        Normalize(state); state.Guards.Clear();
                        return await PrepareMutationAsync(transaction, stateId, state, before, prior, loaded, result.Journal,
                            commandGuard, guardReference?.Id, nowUtc, userId, idempotencyKey, requestSha, result.Store, reservations, cancellationToken);
                    }, cancellationToken);
                    transferred = prepared;
                    return Reply(await CompletePreparedAsync(prepared, cancellationToken));
                }
                catch (BlackjackPendingTableMutationException pending) { await RecoverShardAsync(pending.StateId, cancellationToken); }
                catch (BlackjackManagedLeaseRetiredException) { }
                catch (BlackjackManagedPlayerSupplyRequiredException required)
                {
                    var profiles = await ReserveManagedPlayersAsync(required, reservations, cancellationToken);
                    reservations.AddRange(profiles.Select(profile => new BlackjackReservedManagedPlayer(profile, required.AssignmentId)));
                    if (profiles.Count == 0) allowManagedArrivals = false;
                }
            }
        }
        finally { await ReleaseUntransferredReservationsAsync(stateId, userId, idempotencyKey, reservations, transferred); }
    }

    private async Task SweepShardAsync(string stateId, DateTime nowUtc, CancellationToken cancellationToken)
    {
        var reservations = new List<BlackjackReservedManagedPlayer>();
        StoredTableCommand? transferred = null;
        var allowManagedArrivals = true;
        try
        {
            while (true)
            {
                try
                {
                    await RecoverShardAsync(stateId, cancellationToken);
                    var prepared = await RunTransactionAsync<StoredTableCommand?>(async transaction =>
                    {
                        ThrowIfPending(await transaction.GetSnapshotAsync(PendingLock(stateId), cancellationToken));
                        var snapshot = await transaction.GetSnapshotAsync(StateDocument(stateId), cancellationToken);
                        if (!snapshot.Exists) return null;
                        var state = ReadState(snapshot); var before = DurableStateJson(state);
                        var prior = StateReferences.Capture(state, null);
                        var balances = await ReadBalancesAsync(transaction, state, null, cancellationToken);
                        var coordinator = await CreateCoordinatorAsync(transaction, state, reservations, allowManagedArrivals, cancellationToken);
                        var journal = await coordinator.SweepAsync(state, balances.Current, nowUtc);
                        Normalize(state); state.Guards.Clear();
                        if (before == DurableStateJson(state) && journal.Ledger.Count == 0 && journal.Results.Count == 0) return null;
                        return await PrepareMutationAsync(transaction, stateId, state, before, prior, balances, journal,
                            null, null, nowUtc, null, null, null, null, reservations, cancellationToken);
                    }, cancellationToken);
                    transferred = prepared;
                    if (prepared is not null) await CompletePreparedAsync(prepared, cancellationToken);
                    return;
                }
                catch (BlackjackPendingTableMutationException pending) { await RecoverShardAsync(pending.StateId, cancellationToken); }
                catch (BlackjackManagedLeaseRetiredException) { }
                catch (BlackjackManagedPlayerSupplyRequiredException required)
                {
                    var profiles = await ReserveManagedPlayersAsync(required, reservations, cancellationToken);
                    reservations.AddRange(profiles.Select(profile => new BlackjackReservedManagedPlayer(profile, required.AssignmentId)));
                    if (profiles.Count == 0) allowManagedArrivals = false;
                }
            }
        }
        finally { await ReleaseUntransferredReservationsAsync(stateId, null, null, reservations, transferred); }
    }

    private async Task FinalizeManagedPlayersAsync<T>(
        ManagedExecution<T> execution,
        IReadOnlyCollection<BlackjackReservedManagedPlayer> reservations,
        DateTime nowUtc,
        CancellationToken cancellationToken,
        StoredTableCommand prepared)
    {
        foreach (var (assignmentId, profileIds) in execution.ActiveAssignments)
        {
            if (profileIds.Count == 0) continue;
            // A completed financial operation keeps its frozen outcome even if a lease was lost.
            // The next new coordinator execution retires a confirmed lost seat before it can act.
            _ = await roster.TryRenewAsync(assignmentId, profileIds, DateTime.UtcNow, cancellationToken);
        }

        var releases = execution.Releases
            .Concat(reservations
                .Where(reservation => !execution.ActiveAssignments
                    .GetValueOrDefault(reservation.AssignmentId, [])
                    .Contains(reservation.Profile.UserId, StringComparer.Ordinal))
                .Select(reservation => new BlackjackManagedPlayerRelease(
                    reservation.Profile.UserId, reservation.AssignmentId)))
            .DistinctBy(value => $"{value.AssignmentId}\n{value.ProfileId}", StringComparer.Ordinal)
            .GroupBy(value => value.AssignmentId, StringComparer.Ordinal);
        await RetireAndReleaseManagedLeasesAsync(prepared.Terms.StateId, prepared.Terms.UserId, prepared.Terms.Key,
            releases.SelectMany(group => group.Select(value => new BlackjackManagedLeasePair(value.AssignmentId, value.ProfileId))).ToArray(), prepared,
            requireRelease: true);
    }

    private static ManagedExecution<T> CaptureManagedExecution<T>(
        T value,
        BlackjackTableLobbyState state,
        BlackjackTableJournal journal) => new(
        value,
        state.Tables.Values.ToDictionary(
            table => table.TableId,
            table => (IReadOnlyCollection<string>)table.Players
                .Where(player => BlackjackManagedSeat.IsManaged(player) && !BlackjackTableCoordinator.ManagedLeaseLost(player))
                .Select(player => player.ActorId)
                .Distinct(StringComparer.Ordinal)
                .ToArray(),
            StringComparer.Ordinal),
        journal.ManagedPlayerReleases.ToArray());

    private async Task<LoadedBalances> ReadBalancesAsync(
        Transaction transaction,
        BlackjackTableLobbyState state,
        string? requestedUserId,
        CancellationToken cancellationToken)
    {
        var users = state.Sessions.Keys
            .Concat(state.Tickets.Select(ticket => ticket.UserId))
            .Concat(state.Tables.Values.SelectMany(table =>
                table.Players.Where(player => !BlackjackManagedSeat.IsManaged(player)).Select(player => player.ActorId)))
            .Append(requestedUserId ?? string.Empty)
            .Where(value => !string.IsNullOrEmpty(value))
            .Distinct(StringComparer.Ordinal)
            .OrderBy(value => value, StringComparer.Ordinal)
            .ToArray();
        if (users.Length > BlackjackTableEngine.Capacity)
            throw new InvalidOperationException("A Blackjack table persistence shard exceeded five human accounts.");
        var snapshots = await Task.WhenAll(users.Select(value => accounts.GetStateAsync(value, cancellationToken)));
        var current = users.Select((value, index) => (userId: value, cents: snapshots[index].SlotsCreditsBalanceCents ?? throw new GameWalletUnavailableException()))
            .ToDictionary(pair => pair.userId, pair => pair.cents, StringComparer.Ordinal);
        return new LoadedBalances(current, new Dictionary<string, long>(current, StringComparer.Ordinal));
    }

    private void WriteMutation(
        Transaction transaction,
        string stateId,
        BlackjackTableLobbyState state,
        StateReferences prior,
        LoadedBalances balances,
        BlackjackTableJournal journal,
        BlackjackTableCommandGuard? commandGuard,
        DocumentReference? guardReference,
        bool stateChanged,
        DateTime nowUtc)
    {
        if (stateChanged)
        {
            if (IsEmpty(state))
            {
                transaction.Delete(StateDocument(stateId));
            }
            else
            {
                var stateData = new Dictionary<string, object>
                {
                    ["stateJson"] = DurableStateJson(state),
                    ["acceptingHumans"] = CanAcceptHuman(state),
                    ["humanCount"] = ActiveHumanCount(state),
                    ["tableCount"] = state.Tables.Count,
                    ["queuedTicketCount"] = state.Tickets.Count,
                    ["createdAt"] = Timestamp.FromDateTime(StateCreatedAt(state, nowUtc)),
                    ["updatedAt"] = Timestamp.FromDateTime(nowUtc),
                    ["schemaVersion"] = 2L
                };
                if (NextDeadline(state) is { } deadline)
                    stateData["nextDeadlineAt"] = Timestamp.FromDateTime(deadline);
                else
                    stateData["nextDeadlineAt"] = FieldValue.Delete;
                transaction.Set(StateDocument(stateId), stateData, SetOptions.MergeAll);
            }

            foreach (var tableId in prior.TableIds.Except(state.Tables.Keys, StringComparer.Ordinal))
            {
                var previous = prior.TableSnapshots[tableId];
                FirestoreGameJournal.Write(transaction, database, "blackjack",
                    $"{tableId}-round-{previous.RoundNumber}", previous.Version, nowUtc,
                    new { previous, removed = true }, previous.Players.Where(player => !BlackjackManagedSeat.IsManaged(player)).Select(player => player.ActorId),
                    "table-removed", tableId);
                transaction.Delete(TableDocument(tableId));
            }
            foreach (var table in state.Tables.Values)
            {
                FirestoreGameJournal.Write(transaction, database, "blackjack",
                    $"{table.TableId}-round-{table.RoundNumber}", table.Version, nowUtc, table,
                    table.Players.Where(player => !BlackjackManagedSeat.IsManaged(player)).Select(player => player.ActorId), "engine-state", table.TableId);
                transaction.Set(TableDocument(table.TableId), new Dictionary<string, object>
                {
                    ["stateId"] = stateId,
                    ["tableId"] = table.TableId,
                    ["tableJson"] = JsonSerializer.Serialize(table, JsonOptions),
                    ["phase"] = table.Phase,
                    ["round"] = table.RoundNumber,
                    ["version"] = table.Version,
                    ["updatedAt"] = Timestamp.FromDateTime(table.UpdatedAtUtc),
                    ["schemaVersion"] = 2L
                }, SetOptions.MergeAll);
            }

            foreach (var ticketId in prior.TicketIds.Except(
                         state.Tickets.Select(ticket => ticket.TicketId), StringComparer.Ordinal))
                transaction.Delete(TicketDocument(ticketId));
            foreach (var ticket in state.Tickets)
            {
                transaction.Set(TicketDocument(ticket.TicketId), new Dictionary<string, object>
                {
                    ["stateId"] = stateId,
                    ["ticketId"] = ticket.TicketId,
                    ["ticketJson"] = JsonSerializer.Serialize(ticket, JsonOptions),
                    ["status"] = ticket.Status,
                    ["version"] = ticket.Version,
                    ["expiresAt"] = Timestamp.FromDateTime(ticket.GraceEndsAtUtc.AddHours(1)),
                    ["schemaVersion"] = 2L
                }, SetOptions.MergeAll);
            }

            var routeUsers = prior.UserIds.Union(state.Sessions.Keys, StringComparer.Ordinal);
            foreach (var routeUserId in routeUsers)
            {
                if (!state.Sessions.TryGetValue(routeUserId, out var link) ||
                    link.Kind == BlackjackTableSessionKinds.Idle)
                {
                    transaction.Delete(SessionDocument(routeUserId));
                    continue;
                }
                transaction.Set(SessionDocument(routeUserId), new Dictionary<string, object>
                {
                    ["stateId"] = stateId,
                    ["kind"] = link.Kind,
                    ["ticketId"] = link.TicketId ?? string.Empty,
                    ["tableId"] = link.TableId ?? string.Empty,
                    ["updatedAt"] = Timestamp.FromDateTime(nowUtc),
                    ["schemaVersion"] = 2L
                }, SetOptions.MergeAll);
            }
        }

        foreach (var entry in journal.Ledger)
            transaction.Create(database.Collection(BlackjackBackend.TableLedgerEvidence).Document(entry.Id), LedgerData(entry));
        foreach (var entry in journal.Revenue)
            transaction.Create(RevenueDocument(entry.RoundId), RevenueData(entry));
        foreach (var audit in journal.RoundAudits)
        {
            FirestoreGameAudit.WriteCompletion(transaction, database, audit);
            var startKnown = !audit.Summary.Findings.Any(finding => finding.Code == "round-start-unavailable");
            foreach (var player in audit.Summary.Participants.Where(player => player.IsAccountBacked))
            {
                var analytics = FortuneForge.Games.Abstractions.Analytics.GameAnalyticsOutbox.Create(new(
                    audit.Summary.GameId, audit.Summary.RoundId, player.ActorId, "paid", true,
                    audit.Summary.CompletedAtUtc, startKnown ? audit.Summary.StartedAtUtc : null,
                    player.WagerCents, player.PayoutCents, "gameRoundAudits", audit.Summary.Id))!;
                transaction.Set(database.Collection(FortuneForge.Games.Abstractions.Analytics.GameAnalyticsOutbox.CollectionName)
                    .Document(analytics.Id), analytics.Fields);
            }
        }
        foreach (var result in journal.Results)
        {
            transaction.Create(CardGameResultDocument(result.ResultId), CardGameResultData(result));
            if (result.IsManagedPlayer)
            {
                transaction.Set(ManagedProfileDocument(result.UserId), new Dictionary<string, object>
                {
                    ["cardMatchesPlayed"] = FieldValue.Increment(1),
                    ["cardWins"] = FieldValue.Increment(result.PayoutCents > result.WagerCents ? 1 : 0),
                    ["cardLosses"] = FieldValue.Increment(result.PayoutCents < result.WagerCents ? 1 : 0),
                    ["cardPushes"] = FieldValue.Increment(result.PayoutCents == result.WagerCents ? 1 : 0),
                    ["updatedAt"] = Timestamp.FromDateTime(result.CompletedAtUtc)
                }, SetOptions.MergeAll);
            }
        }
        if (guardReference is not null && commandGuard is not null)
        {
            transaction.Set(guardReference, new Dictionary<string, object>
            {
                ["stateId"] = stateId,
                ["guardJson"] = JsonSerializer.Serialize(commandGuard, JsonOptions),
                ["createdAt"] = Timestamp.FromDateTime(commandGuard.CreatedAtUtc),
                ["expiresAt"] = Timestamp.FromDateTime(commandGuard.CreatedAtUtc.Add(GuardRetention)),
                ["schemaVersion"] = 2L
            }, SetOptions.MergeAll);
        }
    }

    private async Task<string?> ResolveStateIdAsync(
        string userId,
        string idempotencyKey,
        CancellationToken cancellationToken)
    {
        var snapshots = await Task.WhenAll(
            SessionDocument(userId).GetSnapshotAsync(cancellationToken),
            GuardDocument(userId, idempotencyKey).GetSnapshotAsync(cancellationToken));
        var sessionState = ReadString(snapshots[0], "stateId");
        if (!string.IsNullOrEmpty(sessionState)) return sessionState;
        var guardState = ReadString(snapshots[1], "stateId");
        return string.IsNullOrEmpty(guardState) ? null : guardState;
    }

    private async Task<string?> FindAcceptingStateIdAsync(
        ISet<string> excluded,
        CancellationToken cancellationToken)
    {
        var candidates = await StateCollection()
            .WhereEqualTo("acceptingHumans", true)
            .Limit(MaximumShardCandidates)
            .GetSnapshotAsync(cancellationToken);
        return candidates.Documents
            .Where(snapshot => !excluded.Contains(snapshot.Id))
            .OrderBy(snapshot => ReadTimestamp(snapshot, "createdAt"))
            .ThenBy(snapshot => snapshot.Id, StringComparer.Ordinal)
            .Select(snapshot => snapshot.Id)
            .FirstOrDefault();
    }

    private async Task<T> RunTransactionAsync<T>(
        Func<Transaction, Task<T>> callback,
        CancellationToken cancellationToken)
    {
        const int maximumAttempts = 12;
        for (var attempt = 1; ; attempt++)
        {
            try
            {
                return await database.RunTransactionAsync(callback, cancellationToken: cancellationToken);
            }
            catch (RpcException exception) when (
                (exception.StatusCode == StatusCode.Aborted ||
                 exception.StatusCode == StatusCode.InvalidArgument &&
                 exception.Status.Detail.Contains("Transaction is invalid or closed", StringComparison.OrdinalIgnoreCase)) &&
                attempt < maximumAttempts)
            {
                var exponential = Math.Min(500, 20 * (1 << Math.Min(attempt - 1, 5)));
                var backoffMilliseconds = exponential + Random.Shared.Next(25, 126);
                await Task.Delay(backoffMilliseconds, cancellationToken);
            }
        }
    }

    private static void Normalize(BlackjackTableLobbyState state)
    {
        foreach (var table in state.Tables.Values.Where(value => value.Phase == BlackjackTablePhases.Closed).ToArray())
        {
            foreach (var ticket in state.Tickets.Where(value =>
                         value.Status == "queued" && value.TargetTableId == table.TableId).ToArray())
            {
                var index = state.Tickets.FindIndex(value => value.TicketId == ticket.TicketId);
                state.Tickets[index] = ticket with { TargetTableId = null, EligibleAfterRound = 0 };
            }
            state.Tables.Remove(table.TableId);
        }
        state.Tickets.RemoveAll(ticket => ticket.Status != "queued");
        foreach (var userId in state.Sessions
                     .Where(pair => pair.Value.Kind == BlackjackTableSessionKinds.Idle)
                     .Select(pair => pair.Key)
                     .ToArray())
            state.Sessions.Remove(userId);
    }

    private static bool CanAcceptHuman(BlackjackTableLobbyState state) =>
        state.Tables.Values.All(table => !table.SoloMode) &&
        state.Tables.Count <= 1 && ActiveHumanCount(state) < BlackjackManagedTablePolicy.MaximumOccupiedSeats;

    private static int ActiveHumanCount(BlackjackTableLobbyState state) => state.Sessions.Count(pair =>
        pair.Value.Kind is BlackjackTableSessionKinds.Queue or BlackjackTableSessionKinds.Table);

    private static bool IsEmpty(BlackjackTableLobbyState state) =>
        state.Sessions.Count == 0 && state.Tickets.Count == 0 && state.Tables.Count == 0;

    private static DateTime? NextDeadline(BlackjackTableLobbyState state)
    {
        var deadlines = state.Tickets.Where(ticket => ticket.Status == "queued" && ticket.TargetTableId is null)
            .Select(ticket => (DateTime?)ticket.GraceEndsAtUtc)
            .Concat(state.Tables.Values.SelectMany(table => new DateTime?[]
            {
                table.ActionDeadlineAtUtc,
                table.WagerDeadlineAtUtc,
                table.NextTransitionAtUtc
            }.Concat(table.Players.SelectMany(player => new DateTime?[]
            {
                BlackjackManagedSeat.ActionReadyAt(player),
                BlackjackManagedSeat.WagerReadyAt(player)
            }))))
            .Where(value => value is not null)
            .Select(value => value!.Value)
            .ToArray();
        return deadlines.Length == 0 ? null : deadlines.Min();
    }

    private static DateTime StateCreatedAt(BlackjackTableLobbyState state, DateTime fallback) =>
        state.Tickets.Select(ticket => ticket.JoinedAtUtc)
            .Concat(state.Tables.Values.Select(table => table.CreatedAtUtc))
            .DefaultIfEmpty(fallback)
            .Min();

    private static string DurableStateJson(BlackjackTableLobbyState state) =>
        JsonSerializer.Serialize(state, JsonOptions);

    private static string CohortStateId(DateTime nowUtc, int overflow) =>
        BlackjackTableIds.Hash(
            $"blackjack-table-shard\n{nowUtc.Ticks / BlackjackTableEngine.HumanGrace.Ticks}\n{overflow}");

    private static string GuardKey(string userId, string idempotencyKey) =>
        BlackjackTableIds.Hash($"{userId}\n{idempotencyKey}");

    private CollectionReference StateCollection() => database.Collection(BlackjackBackend.TableState);
    private DocumentReference StateDocument(string stateId) => StateCollection().Document(stateId);
    private DocumentReference LeaseDocument() =>
        database.Collection(BlackjackBackend.Leases).Document("deadline-worker");
    private DocumentReference TableDocument(string tableId) =>
        database.Collection(BlackjackBackend.Tables).Document(tableId);
    private DocumentReference TicketDocument(string ticketId) =>
        database.Collection(BlackjackBackend.Tickets).Document(ticketId);
    private DocumentReference SessionDocument(string userId) =>
        database.Collection(BlackjackBackend.Sessions).Document(BlackjackTableIds.Hash(userId));
    private DocumentReference GuardDocument(string userId, string idempotencyKey) =>
        database.Collection(BlackjackBackend.CommandGuards).Document(GuardKey(userId, idempotencyKey));
    private DocumentReference RevenueDocument(string id) =>
        database.Collection(BlackjackBackend.RoundRevenue).Document(BlackjackTableIds.Hash(id));
    private DocumentReference CardGameResultDocument(string resultId) =>
        database.Collection("cardGameResults").Document(resultId);
    private DocumentReference ManagedProfileDocument(string userId) =>
        database.Collection(BlackjackBackend.ManagedStatistics).Document(userId);

    private static BlackjackTableLobbyState ReadState(DocumentSnapshot snapshot)
    {
        var json = ReadString(snapshot, "stateJson");
        return string.IsNullOrEmpty(json)
            ? new BlackjackTableLobbyState()
            : JsonSerializer.Deserialize<BlackjackTableLobbyState>(json, JsonOptions)
              ?? throw new InvalidOperationException("The durable Blackjack table shard state is invalid.");
    }

    private static BlackjackTableCommandGuard ReadGuard(DocumentSnapshot snapshot)
    {
        var json = ReadString(snapshot, "guardJson");
        return JsonSerializer.Deserialize<BlackjackTableCommandGuard>(json, JsonOptions)
            ?? throw new InvalidOperationException("The durable Blackjack table command guard is invalid.");
    }

    private static Dictionary<string, object> LedgerData(BlackjackTableLedgerEntry entry) => new()
    {
        ["transactionId"] = entry.Id,
        ["userId"] = entry.UserId,
        ["currencyId"] = CurrencyId,
        ["amount"] = (double)BlackjackMoney.ToRand(entry.AmountCents),
        ["balanceAfter"] = (double)BlackjackMoney.ToRand(entry.BalanceAfterCents),
        ["type"] = entry.Type,
        ["idempotencyKey"] = entry.Reference,
        ["createdAt"] = Timestamp.FromDateTime(entry.CreatedAtUtc)
    };

    private static Dictionary<string, object> RevenueData(BlackjackTableRevenueEntry entry) => new()
    {
        ["roundId"] = entry.RoundId,
        ["tableId"] = entry.TableId,
        ["round"] = entry.RoundNumber,
        ["humanWagerCents"] = entry.HumanWagerCents,
        ["humanPayoutCents"] = entry.HumanPayoutCents,
        ["houseNetCents"] = checked(entry.HumanWagerCents - entry.HumanPayoutCents),
        ["humanPlayerCount"] = entry.HumanPlayerCount,
        ["currencyId"] = CurrencyId,
        ["financialClassification"] = "real-human-dealer-counterparty-v1",
        ["botFinancialContributionCents"] = 0L,
        ["recognizedAt"] = Timestamp.FromDateTime(entry.SettledAtUtc),
        ["settledAt"] = Timestamp.FromDateTime(entry.SettledAtUtc),
        ["schemaVersion"] = 1L
    };

    private static Dictionary<string, object> CardGameResultData(BlackjackTableResultEntry entry) => new()
    {
        ["resultId"] = entry.ResultId,
        ["game"] = "blackjack",
        ["mode"] = "credit-table",
        ["matchId"] = entry.TableId,
        ["tableId"] = entry.TableId,
        ["round"] = entry.RoundNumber,
        ["userId"] = entry.UserId,
        ["userHash"] = BlackjackTableIds.Hash(entry.UserId),
        ["userGameHash"] = BlackjackTableIds.Hash($"{entry.UserId}\nblackjack\ncredit-table"),
        ["claimStatus"] = "completed",
        ["settlementStatus"] = entry.IsManagedPlayer ? "recorded" : "paid",
        ["wagerCents"] = entry.WagerCents,
        ["payoutCents"] = entry.PayoutCents,
        ["netCents"] = checked(entry.PayoutCents - entry.WagerCents),
        ["completedAt"] = Timestamp.FromDateTime(entry.CompletedAtUtc),
        ["seenAt"] = null!,
        ["financialClassification"] = entry.IsManagedPlayer
            ? "managed-player-virtual-v1"
            : "real-human-dealer-counterparty-v1",
        ["schemaVersion"] = 1L
    };

    private static BlackjackTableHistoryItemResponse HistoryResponse(
        DocumentSnapshot snapshot,
        DateTime? seenOverride = null)
    {
        var seenAt = seenOverride;
        if (seenAt is null && snapshot.Exists &&
            snapshot.ToDictionary().TryGetValue("seenAt", out var seenValue) && seenValue is Timestamp timestamp)
            seenAt = timestamp.ToDateTime();
        return new(
            ReadString(snapshot, "resultId"),
            ReadString(snapshot, "game"),
            ReadString(snapshot, "mode"),
            ReadString(snapshot, "matchId"),
            ReadString(snapshot, "tableId"),
            checked((int)ReadLong(snapshot, "round")),
            BlackjackMoney.ToRand(ReadLong(snapshot, "wagerCents")),
            BlackjackMoney.ToRand(ReadLong(snapshot, "payoutCents")),
            BlackjackMoney.ToRand(ReadLong(snapshot, "netCents")),
            ReadString(snapshot, "claimStatus"),
            ReadString(snapshot, "settlementStatus"),
            ReadTimestamp(snapshot, "completedAt"),
            seenAt is not null,
            seenAt);
    }

    private static long ReadLong(DocumentSnapshot snapshot, string field) =>
        snapshot.Exists && snapshot.TryGetValue<long>(field, out var value) ? value : 0;
    private static string ReadString(DocumentSnapshot snapshot, string field) =>
        snapshot.Exists && snapshot.TryGetValue<string>(field, out var value) ? value : string.Empty;
    private static DateTime ReadTimestamp(DocumentSnapshot snapshot, string field) =>
        snapshot.Exists && snapshot.TryGetValue<Timestamp>(field, out var value)
            ? value.ToDateTime()
            : DateTime.MaxValue;

    private sealed record LoadedBalances(
        Dictionary<string, long> Current,
        Dictionary<string, long> Original);

    private sealed record ManagedExecution<T>(
        T Value,
        IReadOnlyDictionary<string, IReadOnlyCollection<string>> ActiveAssignments,
        IReadOnlyCollection<BlackjackManagedPlayerRelease> Releases);

    private sealed record StateReferences(
        HashSet<string> TableIds,
        HashSet<string> TicketIds,
        HashSet<string> UserIds,
        Dictionary<string, BlackjackTableState> TableSnapshots)
    {
        public static StateReferences Capture(BlackjackTableLobbyState state, string? requestedUserId) => new(
            state.Tables.Keys.ToHashSet(StringComparer.Ordinal),
            state.Tickets.Select(ticket => ticket.TicketId).ToHashSet(StringComparer.Ordinal),
            state.Sessions.Keys.Append(requestedUserId ?? string.Empty)
                .Where(value => !string.IsNullOrEmpty(value))
                .ToHashSet(StringComparer.Ordinal),
            state.Tables.ToDictionary(pair => pair.Key,
                pair => JsonSerializer.Deserialize<BlackjackTableState>(JsonSerializer.Serialize(pair.Value, JsonOptions), JsonOptions)!, StringComparer.Ordinal));
    }
}

internal sealed class BlackjackTableShardUnavailableException : Exception
{
}

internal sealed class BlackjackTableShardChangedException(string stateId) : Exception
{
    public string StateId { get; } = stateId;
}
