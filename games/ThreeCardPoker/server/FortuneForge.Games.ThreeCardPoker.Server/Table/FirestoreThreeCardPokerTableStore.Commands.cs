using System.Globalization;
using Google.Cloud.Firestore;

namespace FortuneForge.Server.Cards.ThreeCardPoker.Table;

internal sealed partial class FirestoreThreeCardPokerTableStore
{
    public async Task<ThreeCardPokerTableStoreResult> GetSessionAsync(string userId, DateTime nowUtc, CancellationToken token)
    {
        await RecoverAccountAsync(userId, token);
        var route = await SessionDocument(userId).GetSnapshotAsync(token);
        var stateId = ReadString(route, "stateId");
        return stateId.Length == 0 ? new(new ThreeCardPokerTableIdleSessionResponse(), await BalanceAsync(userId, token))
            : await ExecuteRoutedAsync(stateId, userId, null, null, (coordinator, state, balances) => coordinator.GetAsync(state, balances, userId, nowUtc), nowUtc, token);
    }

    public async Task<ThreeCardPokerTableStoreResult> JoinAsync(string userId, string displayName, int expectedVersion,
        string key, DateTime nowUtc, CancellationToken token)
    {
        await RecoverAccountAsync(userId, token);
        var stateId = await ResolveStateIdAsync(userId, key, token);
        if (string.IsNullOrEmpty(stateId)) stateId = ThreeCardPokerTableIds.Hash($"three-card-poker-solo-state\n{userId}");
        var identity = new CommandIdentity("join", ThreeCardPokerTableIds.Hash($"{userId}\n{key}"), Version(expectedVersion));
        return await ExecuteRoutedAsync(stateId, userId, key, identity,
            (coordinator, state, balances) => coordinator.JoinAsync(state, balances, userId, displayName, expectedVersion, key, nowUtc), nowUtc, token);
    }

    internal async Task<ThreeCardPokerTableStoreResult?> TryReplayJoinAsync(string userId, string key, int expectedVersion, CancellationToken token)
    {
        await RecoverAccountAsync(userId, token);
        var identity = new CommandIdentity("join", ThreeCardPokerTableIds.Hash($"{userId}\n{key}"), Version(expectedVersion));
        var receipt = await CommandReceipt(userId, key).GetSnapshotAsync(token);
        if (receipt.Exists)
        {
            ValidateIdentity(receipt, identity);
            return await ResumeAsync(receipt.GetValue<string>("operationId"), token);
        }
        var legacy = await GuardDocument(userId, key).GetSnapshotAsync(token);
        if (!legacy.Exists) return null;
        var guard = ReadGuard(legacy);
        if (guard.Operation != identity.Operation || guard.Target != identity.Target || guard.Detail != identity.Detail)
            throw new ThreeCardPokerTableConflictException("This Idempotency-Key was already used for another Three Card Poker request.");
        return await GetSessionAsync(userId, DateTime.UtcNow, token);
    }

    public Task<ThreeCardPokerTableStoreResult> CancelAsync(string userId, string ticketId, int expectedVersion, string key,
        DateTime nowUtc, CancellationToken token) => ExecuteResolvedAsync(userId, key, new("cancel", ticketId, Version(expectedVersion)),
        (coordinator, state, balances) => coordinator.CancelAsync(state, balances, userId, ticketId, expectedVersion, key, nowUtc), nowUtc, token);

    public Task<ThreeCardPokerTableStoreResult> WagerAsync(string userId, string tableId, long wagerCents, long pairPlusCents,
        int expectedVersion, string key, DateTime nowUtc, CancellationToken token) => ExecuteResolvedAsync(userId, key,
        new("wager", tableId, $"{wagerCents}:{pairPlusCents}:{expectedVersion}"),
        (coordinator, state, balances) => coordinator.WagerAsync(state, balances, userId, tableId, wagerCents, pairPlusCents, expectedVersion, key, nowUtc), nowUtc, token);

    public Task<ThreeCardPokerTableStoreResult> ActionAsync(string userId, string tableId, string action, int expectedVersion,
        string key, DateTime nowUtc, CancellationToken token) => ExecuteResolvedAsync(userId, key,
        new("action", tableId, $"{action.Trim().ToLowerInvariant()}:{expectedVersion}"),
        (coordinator, state, balances) => coordinator.ActionAsync(state, balances, userId, tableId, action, expectedVersion, key, nowUtc), nowUtc, token);

    public Task<ThreeCardPokerTableStoreResult> SitOutAsync(string userId, string tableId, int expectedVersion, string key,
        DateTime nowUtc, CancellationToken token) => ExecuteResolvedAsync(userId, key, new("sit-out", tableId, Version(expectedVersion)),
        (coordinator, state, balances) => coordinator.SitOutAsync(state, balances, userId, tableId, expectedVersion, key, nowUtc), nowUtc, token);

    public Task<ThreeCardPokerTableStoreResult> LeaveAsync(string userId, string tableId, int expectedVersion, string key,
        DateTime nowUtc, CancellationToken token) => ExecuteResolvedAsync(userId, key, new("leave", tableId, Version(expectedVersion)),
        (coordinator, state, balances) => coordinator.LeaveAsync(state, balances, userId, tableId, expectedVersion, key, nowUtc), nowUtc, token);

    private async Task<ThreeCardPokerTableStoreResult> ExecuteResolvedAsync(string userId, string key, CommandIdentity identity,
        Func<ThreeCardPokerTableCoordinator, ThreeCardPokerTableLobbyState, IDictionary<string, long>, ValueTask<ThreeCardPokerTableCoordinatorResult>> command,
        DateTime nowUtc, CancellationToken token)
    {
        await RecoverAccountAsync(userId, token);
        var receipt = await CommandReceipt(userId, key).GetSnapshotAsync(token);
        if (receipt.Exists)
        {
            ValidateIdentity(receipt, identity);
            return await ResumeAsync(receipt.GetValue<string>("operationId"), token) ?? throw new InvalidOperationException("The command receipt is missing its reply.");
        }
        var stateId = await ResolveStateIdAsync(userId, key, token);
        if (string.IsNullOrEmpty(stateId)) throw new ThreeCardPokerTableNotFoundException("The Three Card Poker table session was not found.");
        return await ExecuteRoutedAsync(stateId, userId, key, identity, command, nowUtc, token);
    }

    private async Task<ThreeCardPokerTableStoreResult> ExecuteRoutedAsync(string stateId, string userId, string? key, CommandIdentity? identity,
        Func<ThreeCardPokerTableCoordinator, ThreeCardPokerTableLobbyState, IDictionary<string, long>, ValueTask<ThreeCardPokerTableCoordinatorResult>> command,
        DateTime nowUtc, CancellationToken token)
    {
        for (var attempt = 0; attempt < 4; attempt++)
        {
            try { return await ExecuteShardAsync(stateId, userId, key, identity, command, nowUtc, token) ?? throw new InvalidOperationException("The command did not produce a session reply."); }
            catch (ThreeCardPokerTableShardChangedException changed) { stateId = changed.StateId; }
        }
        throw new ThreeCardPokerTableConflictException("The Three Card Poker table session changed. Reconnect and retry the same request.");
    }

    public async Task SweepAsync(DateTime nowUtc, CancellationToken token)
    {
        var acquired = await RunTransactionAsync(async transaction =>
        {
            var lease = await transaction.GetSnapshotAsync(LeaseDocument(), token);
            if (lease.Exists && ReadString(lease, "owner") != leaseOwner && lease.TryGetValue<Timestamp>("leaseUntil", out var until) && until.ToDateTime() > nowUtc) return false;
            transaction.Set(LeaseDocument(), new Dictionary<string, object>
            { ["owner"] = leaseOwner, ["leaseUntil"] = Timestamp.FromDateTime(nowUtc.AddSeconds(2)), ["heartbeatAt"] = Timestamp.FromDateTime(nowUtc), ["schemaVersion"] = 2L }, SetOptions.MergeAll);
            return true;
        }, token);
        if (!acquired) return;
        var pending = await database.Collection("threeCardPokerTablePendingShards").Limit(MaximumSweepBatch).GetSnapshotAsync(token);
        foreach (var operation in pending.Documents)
        {
            try { await ResumeAsync(operation.GetValue<string>("operationId"), token); }
            catch (ThreeCardPokerTableInsufficientCreditsException) { }
        }
        var due = await StateCollection().WhereLessThanOrEqualTo("nextDeadlineAt", Timestamp.FromDateTime(nowUtc)).Limit(MaximumSweepBatch).GetSnapshotAsync(token);
        foreach (var state in due.Documents) await ExecuteShardAsync(state.Id, null, null, null, null, nowUtc, token);
    }

    public async Task<IReadOnlyList<ThreeCardPokerTableHistoryItemResponse>> GetHistoryAsync(string userId, int limit, CancellationToken token)
    {
        await RecoverAccountAsync(userId, token);
        var snapshots = await database.Collection("cardGameResults")
            .WhereEqualTo("userGameHash", ThreeCardPokerTableIds.Hash($"{userId}\nthree-card-poker\ncredit-table"))
            .OrderByDescending("completedAt").Limit(Math.Min(limit, 50)).GetSnapshotAsync(token);
        return snapshots.Documents.Select(snapshot => HistoryResponse(snapshot)).ToArray();
    }

    public async Task<ThreeCardPokerTableHistoryItemResponse> MarkHistorySeenAsync(string userId, string resultId, DateTime nowUtc, CancellationToken token)
    {
        await RecoverAccountAsync(userId, token);
        return await RunTransactionAsync(async transaction =>
        {
            var reference = CardGameResultDocument(resultId);
            var snapshot = await transaction.GetSnapshotAsync(reference, token);
            if (!snapshot.Exists || ReadString(snapshot, "userId") != userId || ReadString(snapshot, "game") != ThreeCardPokerModule.Descriptor.Id || ReadString(snapshot, "mode") != "credit-table")
                throw new ThreeCardPokerTableNotFoundException("The Three Card Poker table result was not found.");
            var alreadySeen = snapshot.ToDictionary().TryGetValue("seenAt", out var value) && value is Timestamp;
            var seen = alreadySeen ? ((Timestamp)value!).ToDateTime() : nowUtc;
            if (!alreadySeen)
                transaction.Update(reference, new Dictionary<string, object> { ["seenAt"] = Timestamp.FromDateTime(seen) });
            return HistoryResponse(snapshot, seen);
        }, token);
    }

    private static string Version(int version) => version.ToString(CultureInfo.InvariantCulture);
}
