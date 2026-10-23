using System.Text.Json;
using FortuneForge.Games.Cards;

namespace FortuneForge.Games.AndarBahar.Server;

/// <summary>Durable development credits only. This store has no Firestore or production wallet dependency.</summary>
public sealed class PracticeAndarBaharStore : IAndarBaharStore
{
    private readonly string statePath;
    private readonly long startingBalanceCents;
    public PracticeAndarBaharStore(string statePath, long startingBalanceCents = 1_000_000)
    {
        if (startingBalanceCents < 0) throw new ArgumentOutOfRangeException(nameof(startingBalanceCents));
        this.statePath = Path.GetFullPath(statePath);
        this.startingBalanceCents = startingBalanceCents;
    }
    public Task<long> BalanceAsync(string userId, CancellationToken cancellationToken) =>
        WithState(state => (Balance(state, userId), false), cancellationToken);
    public Task<AndarBaharStoreResult> StartAsync(string userId, string key, AndarBaharSide side, long stakeCents,
        IReadOnlyList<PlayingCard> shuffledDeck, DateTimeOffset nowUtc, CancellationToken cancellationToken)
    {
        AndarBaharMoney.Validate(side, stakeCents);
        return WithState(state =>
        {
            var id = AndarBaharService.LookupKey(userId, key);
            if (state.Rounds.TryGetValue(id, out var previous))
            {
                var prior = ReadVerified(state, id, previous);
                if (prior.UserId != userId || prior.Side != side || prior.StakeCents != stakeCents) throw new AndarBaharRoundConflictException();
                return (new AndarBaharStoreResult(prior, Balance(state, userId)), false);
            }
            var balance = Balance(state, userId);
            if (balance < stakeCents) throw new AndarBaharInsufficientCreditsException(balance, stakeCents);
            var round = AndarBaharRules.Deal(shuffledDeck);
            var returned = AndarBaharRules.Settle(side, stakeCents, round).TotalReturnCents;
            var after = checked(balance - stakeCents);
            var final = checked(after + returned);
            var stored = new AndarBaharStoredRound(id, userId, key, side, stakeCents, round, after, final, nowUtc);
            state.Rounds.Add(id, AndarBaharPersistence.Encode(stored));
            state.Ledger.Add($"{id}-wager", new(userId, -stakeCents, after, key));
            if (returned > 0) state.Ledger.Add($"{id}-payout", new(userId, returned, final, $"{key}-payout"));
            state.SettledEvents.Add(id, new(userId, key));
            state.Balances[userId] = final;
            state.Latest[userId] = id;
            return (new AndarBaharStoreResult(stored, final), true);
        }, cancellationToken);
    }
    public Task<AndarBaharStoreResult?> GetAsync(string userId, string roundId, CancellationToken cancellationToken) =>
        WithState<AndarBaharStoreResult?>(state =>
        {
            if (!state.Rounds.TryGetValue(roundId, out var persisted) || persisted.UserId != userId) return (null, false);
            return (new(ReadVerified(state, roundId, persisted), Balance(state, userId)), false);
        }, cancellationToken);
    public Task<AndarBaharStoreResult?> LatestAsync(string userId, CancellationToken cancellationToken) =>
        WithState<AndarBaharStoreResult?>(state =>
        {
            if (!state.Latest.TryGetValue(userId, out var id)) return (null, false);
            if (!state.Rounds.TryGetValue(id, out var persisted) || persisted.UserId != userId) throw new InvalidOperationException("The practice recovery record is corrupt.");
            return (new(ReadVerified(state, id, persisted), Balance(state, userId)), false);
        }, cancellationToken);

    private static AndarBaharStoredRound ReadVerified(PracticeState state, string id, PersistedAndarBaharRound persisted)
    {
        var stored = AndarBaharPersistence.Decode(persisted);
        var returned = AndarBaharRules.Settle(stored.Side, stored.StakeCents, stored.Round).TotalReturnCents;
        if (id != stored.RoundId || !state.Ledger.TryGetValue($"{id}-wager", out var wager) ||
            wager != new PracticeLedger(stored.UserId, -stored.StakeCents, stored.BalanceAfterWagerCents, stored.IdempotencyKey) ||
            !state.SettledEvents.TryGetValue(id, out var settled) || settled != new PracticeEvent(stored.UserId, stored.IdempotencyKey) ||
            state.Ledger.ContainsKey($"{id}-payout") != (returned > 0) || (returned > 0 && state.Ledger[$"{id}-payout"] != new PracticeLedger(stored.UserId, returned, stored.BalanceAfterSettlementCents, $"{stored.IdempotencyKey}-payout")))
            throw new InvalidOperationException("The practice wallet ledger is corrupt.");
        return stored;
    }
    private static long Balance(PracticeState state, string user)
    {
        var balance = state.Balances.GetValueOrDefault(user, state.StartingBalanceCents);
        return balance >= 0 ? balance : throw new InvalidOperationException("The practice balance is corrupt.");
    }
    private async Task<T> WithState<T>(Func<PracticeState, (T Value, bool Save)> operation, CancellationToken token)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(statePath)!);
        // An exclusive file handle serializes independent host processes as well as threads. A crashed process releases it.
        await using var held = await AcquireLock(token);
        PracticeState state;
        try
        {
            state = File.Exists(statePath)
                ? JsonSerializer.Deserialize<PracticeState>(await File.ReadAllTextAsync(statePath, token)) ?? throw new InvalidOperationException("The practice state is missing.")
                : new PracticeState { StartingBalanceCents = startingBalanceCents };
        }
        catch (JsonException exception) { throw new InvalidOperationException("The practice state is corrupt; it was preserved for recovery.", exception); }
        if (state.SchemaVersion != 1 || state.StartingBalanceCents < 0 || state.Rounds is null || state.Balances is null || state.Latest is null || state.Ledger is null || state.SettledEvents is null)
            throw new InvalidOperationException("The practice state is corrupt; it was preserved for recovery.");
        token.ThrowIfCancellationRequested();
        var (result, save) = operation(state);
        if (save)
        {
            // Same-directory rename replaces the complete snapshot atomically. Never return a result before flushing it.
            var temporary = $"{statePath}.{Guid.NewGuid():N}.tmp";
            try
            {
                await using (var stream = new FileStream(temporary, FileMode.CreateNew, FileAccess.Write, FileShare.None, 4096, FileOptions.WriteThrough))
                {
                    JsonSerializer.Serialize(stream, state);
                    stream.Flush(flushToDisk: true);
                }
                File.Move(temporary, statePath, overwrite: true);
            }
            finally { if (File.Exists(temporary)) File.Delete(temporary); }
        }
        return result;
    }
    private async Task<FileStream> AcquireLock(CancellationToken token)
    {
        var deadline = Environment.TickCount64 + 10_000;
        while (true)
        {
            token.ThrowIfCancellationRequested();
            try { return new FileStream($"{statePath}.lock", FileMode.OpenOrCreate, FileAccess.ReadWrite, FileShare.None); }
            catch (IOException) when (Environment.TickCount64 < deadline) { await Task.Delay(20, token); }
        }
    }
    private sealed class PracticeState
    {
        public int SchemaVersion { get; set; } = 1;
        public long StartingBalanceCents { get; set; }
        public Dictionary<string, PersistedAndarBaharRound> Rounds { get; set; } = [];
        public Dictionary<string, long> Balances { get; set; } = [];
        public Dictionary<string, string> Latest { get; set; } = [];
        public Dictionary<string, PracticeLedger> Ledger { get; set; } = [];
        public Dictionary<string, PracticeEvent> SettledEvents { get; set; } = [];
    }
    private sealed record PracticeLedger(string UserId, long AmountCents, long BalanceAfterCents, string Key);
    private sealed record PracticeEvent(string UserId, string Key);
}
