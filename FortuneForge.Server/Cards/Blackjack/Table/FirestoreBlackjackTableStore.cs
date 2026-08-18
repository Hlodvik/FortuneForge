using System.Text.Json;
using Google.Cloud.Firestore;
using Grpc.Core;

namespace FortuneForge.Server.Cards.Blackjack.Table;

internal sealed class FirestoreBlackjackTableStore : IBlackjackTableStore
{
    private const string CurrencyId = "slotsCredits";
    private const string FractionField = "availableFractionalCents";
    private const int MaximumShardCandidates = 20;
    private const int MaximumSweepBatch = 25;
    private static readonly TimeSpan GuardRetention = TimeSpan.FromDays(7);
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);
    private readonly FirestoreDb database;
    private readonly BlackjackTableCoordinator coordinator;
    private readonly string leaseOwner = $"blackjack-table-worker-{Guid.NewGuid():N}";

    public FirestoreBlackjackTableStore(FirestoreDb database) : this(database, null, null)
    {
    }

    internal FirestoreBlackjackTableStore(
        FirestoreDb database,
        Func<IReadOnlyList<string>>? deckFactory,
        Func<ulong>? seedFactory)
    {
        this.database = database;
        coordinator = new(deckFactory, seedFactory);
    }

    public async Task<BlackjackTableStoreResult> GetSessionAsync(
        string userId,
        DateTime nowUtc,
        CancellationToken cancellationToken)
    {
        var route = await SessionDocument(userId).GetSnapshotAsync(cancellationToken);
        var stateId = ReadString(route, "stateId");
        if (string.IsNullOrEmpty(stateId))
        {
            var balance = await BalanceDocument(userId).GetSnapshotAsync(cancellationToken);
            return new(new BlackjackTableIdleSessionResponse(), ReadBalance(balance));
        }

        return await ExecuteRoutedAsync(
            stateId,
            userId,
            null,
            false,
            (state, balances) => coordinator.Get(state, balances, userId, nowUtc),
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

        BlackjackTableCoordinatorResult Command(
            BlackjackTableLobbyState state,
            IDictionary<string, long> balances) =>
            coordinator.Join(
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
                cancellationToken);
        }

        var excluded = new HashSet<string>(StringComparer.Ordinal);
        for (var attempt = 0; attempt < 8; attempt++)
        {
            var stateId = await FindAcceptingStateIdAsync(excluded, cancellationToken)
                ?? CohortStateId(nowUtc, attempt);
            try
            {
                return await ExecuteRoutedAsync(
                    stateId,
                    userId,
                    idempotencyKey,
                    true,
                    Command,
                    nowUtc,
                    cancellationToken);
            }
            catch (BlackjackTableShardUnavailableException)
            {
                excluded.Add(stateId);
            }
        }

        throw new BlackjackTableConflictException(
            "Blackjack table matchmaking is busy. Reconnect before joining again.");
    }

    private async Task<BlackjackTableStoreResult> ReadCurrentSessionAsync(
        string userId,
        DateTime nowUtc,
        CancellationToken cancellationToken)
    {
        var routeTask = SessionDocument(userId).GetSnapshotAsync(cancellationToken);
        var balanceTask = BalanceDocument(userId).GetSnapshotAsync(cancellationToken);
        await Task.WhenAll(routeTask, balanceTask);
        var route = await routeTask;
        var balance = ReadBalance(await balanceTask);
        var stateId = ReadString(route, "stateId");
        if (string.IsNullOrEmpty(stateId))
            return new(new BlackjackTableIdleSessionResponse(), balance);

        var stateSnapshot = await StateDocument(stateId).GetSnapshotAsync(cancellationToken);
        if (!stateSnapshot.Exists)
            return new(new BlackjackTableIdleSessionResponse(), balance);
        return new(BlackjackTableProjection.Session(ReadState(stateSnapshot), userId, nowUtc), balance);
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
            (state, balances) => coordinator.Cancel(
                state, balances, userId, ticketId, expectedVersion, idempotencyKey, nowUtc),
            nowUtc,
            cancellationToken);

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
            (state, balances) => coordinator.Wager(
                state, balances, userId, tableId, wagerCents, expectedVersion, idempotencyKey, nowUtc),
            nowUtc,
            cancellationToken);

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
            (state, balances) => coordinator.Action(
                state, balances, userId, tableId, action, expectedVersion, idempotencyKey, nowUtc),
            nowUtc,
            cancellationToken);

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
            (state, balances) => coordinator.Leave(
                state, balances, userId, tableId, expectedVersion, idempotencyKey, nowUtc),
            nowUtc,
            cancellationToken);

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

    private async Task<BlackjackTableStoreResult> ExecuteResolvedAsync(
        string userId,
        string idempotencyKey,
        Func<BlackjackTableLobbyState, IDictionary<string, long>, BlackjackTableCoordinatorResult> command,
        DateTime nowUtc,
        CancellationToken cancellationToken)
    {
        var stateId = await ResolveStateIdAsync(userId, idempotencyKey, cancellationToken);
        if (string.IsNullOrEmpty(stateId))
            throw new BlackjackTableNotFoundException("The Blackjack table session was not found.");
        return await ExecuteRoutedAsync(
            stateId, userId, idempotencyKey, false, command, nowUtc, cancellationToken);
    }

    private async Task<BlackjackTableStoreResult> ExecuteRoutedAsync(
        string initialStateId,
        string userId,
        string? idempotencyKey,
        bool requireAcceptingShard,
        Func<BlackjackTableLobbyState, IDictionary<string, long>, BlackjackTableCoordinatorResult> command,
        DateTime nowUtc,
        CancellationToken cancellationToken)
    {
        var stateId = initialStateId;
        for (var routeAttempt = 0; routeAttempt < 4; routeAttempt++)
        {
            try
            {
                return await ExecuteShardAsync(
                    stateId,
                    userId,
                    idempotencyKey,
                    requireAcceptingShard,
                    command,
                    nowUtc,
                    cancellationToken);
            }
            catch (BlackjackTableShardChangedException changed)
            {
                stateId = changed.StateId;
                requireAcceptingShard = false;
            }
        }
        throw new BlackjackTableConflictException("The Blackjack table session changed. Reconnect and try again.");
    }

    private async Task<BlackjackTableStoreResult> ExecuteShardAsync(
        string stateId,
        string userId,
        string? idempotencyKey,
        bool requireAcceptingShard,
        Func<BlackjackTableLobbyState, IDictionary<string, long>, BlackjackTableCoordinatorResult> command,
        DateTime nowUtc,
        CancellationToken cancellationToken) =>
        await RunTransactionAsync(async transaction =>
        {
            var stateReference = StateDocument(stateId);
            var sessionReference = SessionDocument(userId);
            var guardReference = string.IsNullOrEmpty(idempotencyKey)
                ? null
                : GuardDocument(userId, idempotencyKey);
            var initialReads = new List<Task<DocumentSnapshot>>
            {
                transaction.GetSnapshotAsync(stateReference, cancellationToken),
                transaction.GetSnapshotAsync(sessionReference, cancellationToken)
            };
            if (guardReference is not null)
                initialReads.Add(transaction.GetSnapshotAsync(guardReference, cancellationToken));
            var snapshots = await Task.WhenAll(initialReads);
            var routedStateId = ReadString(snapshots[1], "stateId");
            if (!string.IsNullOrEmpty(routedStateId) && routedStateId != stateId)
                throw new BlackjackTableShardChangedException(routedStateId);

            var state = ReadState(snapshots[0]);
            var durableBefore = DurableStateJson(state);
            var guardSnapshot = guardReference is null ? null : snapshots[2];
            var guardKey = string.IsNullOrEmpty(idempotencyKey) ? null : GuardKey(userId, idempotencyKey);
            if (guardSnapshot?.Exists == true && guardKey is not null)
                state.Guards[guardKey] = ReadGuard(guardSnapshot);
            if (requireAcceptingShard && guardSnapshot?.Exists != true && !CanAcceptHuman(state))
                throw new BlackjackTableShardUnavailableException();

            var prior = StateReferences.Capture(state, userId);
            var loaded = await ReadBalancesAsync(transaction, state, userId, cancellationToken);
            var result = command(state, loaded.Current);
            var commandGuard = guardKey is not null && state.Guards.TryGetValue(guardKey, out var value)
                ? value
                : null;
            Normalize(state);
            state.Guards.Clear();
            var durableAfter = DurableStateJson(state);
            WriteMutation(
                transaction,
                stateId,
                state,
                prior,
                loaded,
                result.Journal,
                commandGuard,
                guardReference,
                durableBefore != durableAfter,
                nowUtc);
            return result.Store;
        }, cancellationToken);

    private async Task SweepShardAsync(
        string stateId,
        DateTime nowUtc,
        CancellationToken cancellationToken) =>
        await RunTransactionAsync(async transaction =>
        {
            var stateReference = StateDocument(stateId);
            var stateSnapshot = await transaction.GetSnapshotAsync(stateReference, cancellationToken);
            if (!stateSnapshot.Exists) return false;
            var state = ReadState(stateSnapshot);
            var durableBefore = DurableStateJson(state);
            var prior = StateReferences.Capture(state, null);
            var loaded = await ReadBalancesAsync(transaction, state, null, cancellationToken);
            var journal = coordinator.Sweep(state, loaded.Current, nowUtc);
            Normalize(state);
            state.Guards.Clear();
            WriteMutation(
                transaction,
                stateId,
                state,
                prior,
                loaded,
                journal,
                null,
                null,
                durableBefore != DurableStateJson(state),
                nowUtc);
            return true;
        }, cancellationToken);

    private async Task<LoadedBalances> ReadBalancesAsync(
        Transaction transaction,
        BlackjackTableLobbyState state,
        string? requestedUserId,
        CancellationToken cancellationToken)
    {
        var users = state.Sessions.Keys
            .Concat(state.Tickets.Select(ticket => ticket.UserId))
            .Concat(state.Tables.Values.SelectMany(table =>
                table.Players.Where(player => !player.IsBot).Select(player => player.ActorId)))
            .Append(requestedUserId ?? string.Empty)
            .Where(value => !string.IsNullOrEmpty(value))
            .Distinct(StringComparer.Ordinal)
            .OrderBy(value => value, StringComparer.Ordinal)
            .ToArray();
        if (users.Length > BlackjackTableEngine.Capacity)
            throw new InvalidOperationException("A Blackjack table persistence shard exceeded five human accounts.");
        var snapshots = await Task.WhenAll(users.Select(value =>
            transaction.GetSnapshotAsync(BalanceDocument(value), cancellationToken)));
        var current = users.Select((value, index) => (userId: value, cents: ReadBalance(snapshots[index])))
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
                transaction.Delete(TableDocument(tableId));
            foreach (var table in state.Tables.Values)
            {
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

        foreach (var (balanceUserId, cents) in balances.Current)
        {
            if (balances.Original.GetValueOrDefault(balanceUserId) == cents) continue;
            transaction.Set(BalanceDocument(balanceUserId), BalanceData(cents, nowUtc), SetOptions.MergeAll);
        }
        foreach (var entry in journal.Ledger)
            transaction.Create(LedgerDocument(entry.Id), LedgerData(entry));
        foreach (var entry in journal.Revenue)
            transaction.Create(RevenueDocument(entry.RoundId), RevenueData(entry));
        foreach (var result in journal.Results)
            transaction.Create(CardGameResultDocument(result.ResultId), CardGameResultData(result));
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
        state.Tables.Count <= 1 && ActiveHumanCount(state) < BlackjackTableEngine.Capacity;

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
            }))
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

    private CollectionReference StateCollection() => database.Collection("blackjackTableState");
    private DocumentReference StateDocument(string stateId) => StateCollection().Document(stateId);
    private DocumentReference LeaseDocument() =>
        database.Collection("blackjackTableLeases").Document("deadline-worker");
    private DocumentReference TableDocument(string tableId) =>
        database.Collection("blackjackTables").Document(tableId);
    private DocumentReference TicketDocument(string ticketId) =>
        database.Collection("blackjackTableTickets").Document(ticketId);
    private DocumentReference SessionDocument(string userId) =>
        database.Collection("blackjackTableSessions").Document(BlackjackTableIds.Hash(userId));
    private DocumentReference GuardDocument(string userId, string idempotencyKey) =>
        database.Collection("blackjackTableCommandGuards").Document(GuardKey(userId, idempotencyKey));
    private DocumentReference BalanceDocument(string userId) =>
        database.Collection("userBalances").Document($"{userId}_{CurrencyId}");
    private DocumentReference LedgerDocument(string id) =>
        database.Collection("balanceTransactions").Document(id);
    private DocumentReference RevenueDocument(string id) =>
        database.Collection("blackjackTableRoundRevenue").Document(BlackjackTableIds.Hash(id));
    private DocumentReference CardGameResultDocument(string resultId) =>
        database.Collection("cardGameResults").Document(resultId);

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

    private static Dictionary<string, object> BalanceData(long cents, DateTime nowUtc) => new()
    {
        ["available"] = cents / BlackjackMoney.CentsPerRand,
        [FractionField] = cents % BlackjackMoney.CentsPerRand,
        ["version"] = FieldValue.Increment(1),
        ["updatedAt"] = Timestamp.FromDateTime(nowUtc)
    };

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
        ["settlementStatus"] = "paid",
        ["wagerCents"] = entry.WagerCents,
        ["payoutCents"] = entry.PayoutCents,
        ["netCents"] = checked(entry.PayoutCents - entry.WagerCents),
        ["completedAt"] = Timestamp.FromDateTime(entry.CompletedAtUtc),
        ["seenAt"] = null!,
        ["financialClassification"] = "real-human-dealer-counterparty-v1",
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

    private static long ReadBalance(DocumentSnapshot snapshot) => checked(
        ReadLong(snapshot, "available") * BlackjackMoney.CentsPerRand +
        Math.Clamp(ReadLong(snapshot, FractionField), 0, 99));
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

    private sealed record StateReferences(
        IReadOnlySet<string> TableIds,
        IReadOnlySet<string> TicketIds,
        IReadOnlySet<string> UserIds)
    {
        public static StateReferences Capture(BlackjackTableLobbyState state, string? requestedUserId) => new(
            state.Tables.Keys.ToHashSet(StringComparer.Ordinal),
            state.Tickets.Select(ticket => ticket.TicketId).ToHashSet(StringComparer.Ordinal),
            state.Sessions.Keys.Append(requestedUserId ?? string.Empty)
                .Where(value => !string.IsNullOrEmpty(value))
                .ToHashSet(StringComparer.Ordinal));
    }
}

internal sealed class BlackjackTableShardUnavailableException : Exception
{
}

internal sealed class BlackjackTableShardChangedException(string stateId) : Exception
{
    public string StateId { get; } = stateId;
}
