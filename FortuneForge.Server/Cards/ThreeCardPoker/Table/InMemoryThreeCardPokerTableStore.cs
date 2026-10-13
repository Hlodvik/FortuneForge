using FortuneForge.Server.Bots;
using System.Text.Json;

namespace FortuneForge.Server.Cards.ThreeCardPoker.Table;

internal sealed class InMemoryThreeCardPokerTableStore(
    Func<IReadOnlyList<string>>? deckFactory = null,
    Func<ulong>? seedFactory = null,
    IManagedTablePopulationDirector? managedPopulationDirector = null) : IThreeCardPokerTableStore
{
    private readonly object gate = new();
    private ThreeCardPokerTableLobbyState state = new();
    private readonly Dictionary<string, long> balances = new(StringComparer.Ordinal);
    private readonly Dictionary<string, ThreeCardPokerTableLedgerEntry> ledger = new(StringComparer.Ordinal);
    private readonly Dictionary<string, ThreeCardPokerTableRevenueEntry> revenue = new(StringComparer.Ordinal);
    private readonly Dictionary<string, (ThreeCardPokerTableResultEntry Result, DateTime? SeenAtUtc)> history = new(StringComparer.Ordinal);
    private readonly ThreeCardPokerTableCoordinator coordinator = new(
        deckFactory,
        seedFactory,
        managedPopulationDirector: managedPopulationDirector);

    internal void SetBalance(string userId, long cents)
    {
        lock (gate) balances[userId] = cents;
    }

    internal long Balance(string userId)
    {
        lock (gate) return balances.GetValueOrDefault(userId);
    }

    internal IReadOnlyList<ThreeCardPokerTableLedgerEntry> Ledger
    {
        get { lock (gate) return ledger.Values.ToArray(); }
    }

    internal IReadOnlyList<ThreeCardPokerTableRevenueEntry> Revenue
    {
        get { lock (gate) return revenue.Values.ToArray(); }
    }

    internal ThreeCardPokerTableState TableForTest(string tableId)
    {
        lock (gate) return state.Tables[tableId];
    }

    internal ThreeCardPokerTableLobbyState StateForTest => state;

    public Task<ThreeCardPokerTableStoreResult> GetSessionAsync(
        string userId,
        DateTime nowUtc,
        CancellationToken cancellationToken) =>
        Execute(() => coordinator.Get(state, balances, userId, nowUtc), cancellationToken);

    public Task<ThreeCardPokerTableStoreResult> JoinAsync(
        string userId,
        string displayName,
        int expectedVersion,
        string idempotencyKey,
        DateTime nowUtc,
        CancellationToken cancellationToken) =>
        Execute(() => coordinator.Join(
            state, balances, userId, displayName, expectedVersion, idempotencyKey, nowUtc), cancellationToken);

    public Task<ThreeCardPokerTableStoreResult> CancelAsync(
        string userId,
        string ticketId,
        int expectedVersion,
        string idempotencyKey,
        DateTime nowUtc,
        CancellationToken cancellationToken) =>
        Execute(() => coordinator.Cancel(
            state, balances, userId, ticketId, expectedVersion, idempotencyKey, nowUtc), cancellationToken);

    public Task<ThreeCardPokerTableStoreResult> WagerAsync(
        string userId,
        string tableId,
        long wagerCents,
        long pairPlusCents,
        int expectedVersion,
        string idempotencyKey,
        DateTime nowUtc,
        CancellationToken cancellationToken) =>
        Execute(() => coordinator.Wager(
            state, balances, userId, tableId, wagerCents, pairPlusCents, expectedVersion, idempotencyKey, nowUtc), cancellationToken);

    public Task<ThreeCardPokerTableStoreResult> SitOutAsync(
        string userId,
        string tableId,
        int expectedVersion,
        string idempotencyKey,
        DateTime nowUtc,
        CancellationToken cancellationToken) =>
        Execute(() => coordinator.SitOut(
            state, balances, userId, tableId, expectedVersion, idempotencyKey, nowUtc), cancellationToken);

    public Task<ThreeCardPokerTableStoreResult> ActionAsync(
        string userId,
        string tableId,
        string action,
        int expectedVersion,
        string idempotencyKey,
        DateTime nowUtc,
        CancellationToken cancellationToken) =>
        Execute(() => coordinator.Action(
            state, balances, userId, tableId, action, expectedVersion, idempotencyKey, nowUtc), cancellationToken);

    public Task<ThreeCardPokerTableStoreResult> LeaveAsync(
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

    public Task<IReadOnlyList<ThreeCardPokerTableHistoryItemResponse>> GetHistoryAsync(
        string userId,
        int limit,
        CancellationToken cancellationToken)
    {
        lock (gate)
        {
            cancellationToken.ThrowIfCancellationRequested();
            return Task.FromResult<IReadOnlyList<ThreeCardPokerTableHistoryItemResponse>>(history.Values
                .Where(value => value.Result.UserId == userId)
                .OrderByDescending(value => value.Result.CompletedAtUtc)
                .Take(limit)
                .Select(value => ToHistory(value.Result, value.SeenAtUtc))
                .ToArray());
        }
    }

    public Task<ThreeCardPokerTableHistoryItemResponse> MarkHistorySeenAsync(
        string userId,
        string resultId,
        DateTime nowUtc,
        CancellationToken cancellationToken)
    {
        lock (gate)
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (!history.TryGetValue(resultId, out var value) || value.Result.UserId != userId)
                throw new ThreeCardPokerTableNotFoundException("The Three Card Poker table result was not found.");
            value.SeenAtUtc ??= nowUtc;
            history[resultId] = value;
            return Task.FromResult(ToHistory(value.Result, value.SeenAtUtc));
        }
    }

    private Task<ThreeCardPokerTableStoreResult> Execute(
        Func<ThreeCardPokerTableCoordinatorResult> command,
        CancellationToken cancellationToken)
    {
        lock (gate)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var priorState = JsonSerializer.Serialize(state);
            var priorBalances = new Dictionary<string, long>(balances, StringComparer.Ordinal);
            try
            {
                var result = command();
                Apply(result.Journal);
                return Task.FromResult(result.Store);
            }
            catch
            {
                state = JsonSerializer.Deserialize<ThreeCardPokerTableLobbyState>(priorState)!;
                balances.Clear();
                foreach (var (userId, cents) in priorBalances) balances[userId] = cents;
                throw;
            }
        }
    }

    private void Apply(ThreeCardPokerTableJournal journal)
    {
        foreach (var entry in journal.Ledger)
        {
            if (!ledger.TryAdd(entry.Id, entry))
                throw new InvalidOperationException("A Three Card Poker table ledger entry was emitted more than once.");
        }
        foreach (var entry in journal.Revenue)
        {
            if (!revenue.TryAdd(entry.RoundId, entry))
                throw new InvalidOperationException("Three Card Poker table round revenue was recognized more than once.");
        }
        foreach (var result in journal.Results)
        {
            if (!history.TryAdd(result.ResultId, new(result, null)))
                throw new InvalidOperationException("A Three Card Poker table result was emitted more than once.");
        }
    }

    private static ThreeCardPokerTableHistoryItemResponse ToHistory(
        ThreeCardPokerTableResultEntry result,
        DateTime? seenAtUtc) => new(
            result.ResultId,
            "three-card-poker",
            "credit-table",
            result.TableId,
            result.TableId,
            result.RoundNumber,
            ThreeCardPokerMoney.ToRand(result.WagerCents),
            ThreeCardPokerMoney.ToRand(result.PayoutCents),
            ThreeCardPokerMoney.ToRand(checked(result.PayoutCents - result.WagerCents)),
            "completed",
            "paid",
            result.CompletedAtUtc,
            seenAtUtc is not null,
            seenAtUtc);
}
