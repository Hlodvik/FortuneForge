namespace FortuneForge.Server.Cards.Blackjack.Table;

internal sealed class InMemoryBlackjackTableStore(
    Func<IReadOnlyList<string>>? deckFactory = null,
    Func<ulong>? seedFactory = null) : IBlackjackTableStore
{
    private readonly object gate = new();
    private readonly BlackjackTableLobbyState state = new();
    private readonly Dictionary<string, long> balances = new(StringComparer.Ordinal);
    private readonly Dictionary<string, BlackjackTableLedgerEntry> ledger = new(StringComparer.Ordinal);
    private readonly Dictionary<string, BlackjackTableRevenueEntry> revenue = new(StringComparer.Ordinal);
    private readonly Dictionary<string, (BlackjackTableResultEntry Result, DateTime? SeenAtUtc)> history = new(StringComparer.Ordinal);
    private readonly BlackjackTableCoordinator coordinator = new(deckFactory, seedFactory);

    internal void SetBalance(string userId, long cents)
    {
        lock (gate) balances[userId] = cents;
    }

    internal long Balance(string userId)
    {
        lock (gate) return balances.GetValueOrDefault(userId);
    }

    internal IReadOnlyList<BlackjackTableLedgerEntry> Ledger
    {
        get { lock (gate) return ledger.Values.ToArray(); }
    }

    internal IReadOnlyList<BlackjackTableRevenueEntry> Revenue
    {
        get { lock (gate) return revenue.Values.ToArray(); }
    }

    internal BlackjackTableState TableForTest(string tableId)
    {
        lock (gate) return state.Tables[tableId];
    }

    internal BlackjackTableLobbyState StateForTest => state;

    public Task<BlackjackTableStoreResult> GetSessionAsync(
        string userId,
        DateTime nowUtc,
        CancellationToken cancellationToken) =>
        Execute(() => coordinator.Get(state, balances, userId, nowUtc), cancellationToken);

    public Task<BlackjackTableStoreResult> JoinAsync(
        string userId,
        string displayName,
        int expectedVersion,
        string idempotencyKey,
        DateTime nowUtc,
        CancellationToken cancellationToken) =>
        Execute(() => coordinator.Join(
            state, balances, userId, displayName, expectedVersion, idempotencyKey, nowUtc), cancellationToken);

    public Task<BlackjackTableStoreResult> CancelAsync(
        string userId,
        string ticketId,
        int expectedVersion,
        string idempotencyKey,
        DateTime nowUtc,
        CancellationToken cancellationToken) =>
        Execute(() => coordinator.Cancel(
            state, balances, userId, ticketId, expectedVersion, idempotencyKey, nowUtc), cancellationToken);

    public Task<BlackjackTableStoreResult> WagerAsync(
        string userId,
        string tableId,
        long wagerCents,
        int expectedVersion,
        string idempotencyKey,
        DateTime nowUtc,
        CancellationToken cancellationToken) =>
        Execute(() => coordinator.Wager(
            state, balances, userId, tableId, wagerCents, expectedVersion, idempotencyKey, nowUtc), cancellationToken);

    public Task<BlackjackTableStoreResult> ActionAsync(
        string userId,
        string tableId,
        string action,
        int expectedVersion,
        string idempotencyKey,
        DateTime nowUtc,
        CancellationToken cancellationToken) =>
        Execute(() => coordinator.Action(
            state, balances, userId, tableId, action, expectedVersion, idempotencyKey, nowUtc), cancellationToken);

    public Task<BlackjackTableStoreResult> LeaveAsync(
        string userId,
        string tableId,
        int expectedVersion,
        string idempotencyKey,
        DateTime nowUtc,
        CancellationToken cancellationToken) =>
        Execute(() => coordinator.Leave(
            state, balances, userId, tableId, expectedVersion, idempotencyKey, nowUtc), cancellationToken);

    public Task SweepAsync(DateTime nowUtc, CancellationToken cancellationToken)
    {
        lock (gate)
        {
            cancellationToken.ThrowIfCancellationRequested();
            Apply(coordinator.Sweep(state, balances, nowUtc));
            return Task.CompletedTask;
        }
    }

    public Task<IReadOnlyList<BlackjackTableHistoryItemResponse>> GetHistoryAsync(
        string userId,
        int limit,
        CancellationToken cancellationToken)
    {
        lock (gate)
        {
            cancellationToken.ThrowIfCancellationRequested();
            return Task.FromResult<IReadOnlyList<BlackjackTableHistoryItemResponse>>(history.Values
                .Where(value => value.Result.UserId == userId)
                .OrderByDescending(value => value.Result.CompletedAtUtc)
                .Take(limit)
                .Select(value => ToHistory(value.Result, value.SeenAtUtc))
                .ToArray());
        }
    }

    public Task<BlackjackTableHistoryItemResponse> MarkHistorySeenAsync(
        string userId,
        string resultId,
        DateTime nowUtc,
        CancellationToken cancellationToken)
    {
        lock (gate)
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (!history.TryGetValue(resultId, out var value) || value.Result.UserId != userId)
                throw new BlackjackTableNotFoundException("The Blackjack table result was not found.");
            value.SeenAtUtc ??= nowUtc;
            history[resultId] = value;
            return Task.FromResult(ToHistory(value.Result, value.SeenAtUtc));
        }
    }

    private Task<BlackjackTableStoreResult> Execute(
        Func<BlackjackTableCoordinatorResult> command,
        CancellationToken cancellationToken)
    {
        lock (gate)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var result = command();
            Apply(result.Journal);
            return Task.FromResult(result.Store);
        }
    }

    private void Apply(BlackjackTableJournal journal)
    {
        foreach (var entry in journal.Ledger)
        {
            if (!ledger.TryAdd(entry.Id, entry))
                throw new InvalidOperationException("A Blackjack table ledger entry was emitted more than once.");
        }
        foreach (var entry in journal.Revenue)
        {
            if (!revenue.TryAdd(entry.RoundId, entry))
                throw new InvalidOperationException("Blackjack table round revenue was recognized more than once.");
        }
        foreach (var result in journal.Results)
        {
            if (!history.TryAdd(result.ResultId, new(result, null)))
                throw new InvalidOperationException("A Blackjack table result was emitted more than once.");
        }
    }

    private static BlackjackTableHistoryItemResponse ToHistory(
        BlackjackTableResultEntry result,
        DateTime? seenAtUtc) => new(
            result.ResultId,
            "blackjack",
            "credit-table",
            result.TableId,
            result.TableId,
            result.RoundNumber,
            BlackjackMoney.ToRand(result.WagerCents),
            BlackjackMoney.ToRand(result.PayoutCents),
            BlackjackMoney.ToRand(checked(result.PayoutCents - result.WagerCents)),
            "completed",
            "paid",
            result.CompletedAtUtc,
            seenAtUtc is not null,
            seenAtUtc);
}
