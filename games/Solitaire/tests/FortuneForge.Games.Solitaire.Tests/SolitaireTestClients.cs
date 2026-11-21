using System.Text.Json;
using FortuneForge.Games.Abstractions.Wallet;

namespace FortuneForge.Games.Solitaire.Tests;

internal sealed class SolitaireTestWallet : IGameWalletBatchClient, IGameWalletAccountReader
{
    private readonly Lock sync = new();
    private readonly Dictionary<string, long> balances = [];
    private readonly Dictionary<string, (GameWalletBatchRequest Request, GameWalletBatchReceipt Receipt)> settled = [];
    private readonly List<string> reads = [];
    internal bool LoseNextResponse { get; set; }
    internal bool Unavailable { get; set; }
    internal bool IncludePlayerName { get; set; } = true;
    internal int SettlementCount { get { lock (sync) return settled.Count; } }
    internal IReadOnlyList<GameWalletBatchRequest> Requests { get { lock (sync) return settled.Values.Select(item => item.Request).ToArray(); } }
    internal IReadOnlyList<string> ReadAccounts { get { lock (sync) return reads.ToArray(); } }
    internal void Fund(string account, long cents) { lock (sync) balances[account] = cents; }
    internal long Balance(string account) { lock (sync) return balances.GetValueOrDefault(account, 10_000); }

    public Task<GameWalletAccountState> GetStateAsync(string accountId, CancellationToken cancellationToken)
    {
        lock (sync)
        {
            reads.Add(accountId);
            return Task.FromResult(new GameWalletAccountState(0, Balance(accountId),
                IncludePlayerName ? accountId == "alice" ? "Alice" : "Bob" : null));
        }
    }

    public Task<GameWalletBatchReceipt> SettleBatchAsync(string actorAccountId, GameWalletBatchRequest request, CancellationToken cancellationToken)
    {
        lock (sync)
        {
            if (Unavailable) throw new GameWalletUnavailableException();
            if (settled.TryGetValue(request.OperationId, out var old))
            {
                if (JsonSerializer.Serialize(request) != JsonSerializer.Serialize(old.Request)) throw new GameWalletConflictException();
                return Task.FromResult(old.Receipt with { WasAlreadySettled = true });
            }
            if (!GameWalletBatchVerifier.IsValidRequest(request)) throw new ArgumentException("Invalid test wallet request.");
            var declined = request.Effects.Any(effect => Balance(effect.AccountId) <
                GameWalletBalanceRequirement.RequiredCents(effect.WagerCents, effect.MinimumBalanceBeforeCents));
            var batchId = GameWalletBatchIdentity.CreateId(SolitaireModule.GameId, request.OperationId);
            var entries = request.Effects.Select(effect =>
            {
                var before = Balance(effect.AccountId);
                var after = declined ? before : checked(before - effect.WagerCents + effect.PayoutCents);
                balances[effect.AccountId] = after;
                var prefix = GameWalletBatchIdentity.AccountLedgerPrefix(batchId, effect.AccountId);
                return new GameWalletBatchAccountReceipt(effect.AccountId, before, after,
                    before < GameWalletBalanceRequirement.RequiredCents(effect.WagerCents, effect.MinimumBalanceBeforeCents),
                    !declined && effect.WagerCents > 0 ? prefix + "-wager" : null,
                    !declined && effect.PayoutCents > 0 ? prefix + "-payout" : null);
            }).ToArray();
            var receipt = new GameWalletBatchReceipt(batchId, actorAccountId, entries,
                new DateTimeOffset(CompetitiveSolitaireBackendTests.Now.AddSeconds(settled.Count + 1)), false, declined);
            settled.Add(request.OperationId, (request, receipt));
            if (LoseNextResponse) { LoseNextResponse = false; throw new GameWalletUnavailableException(); }
            return Task.FromResult(receipt);
        }
    }
}

internal sealed class SolitaireTestRoster : IManagedPlayerRosterClient
{
    private int ordinal;
    internal bool Unavailable { get; set; }
    internal bool OwnsProfiles { get; set; } = true;

    public Task<IReadOnlyList<ManagedPlayerRosterProfile>> ReserveAsync(string assignmentId, int count,
        IReadOnlyCollection<string> excludedProfileIds, DateTime nowUtc, CancellationToken cancellationToken)
    {
        if (Unavailable) throw new ManagedPlayerRosterUnavailableException();
        return Task.FromResult<IReadOnlyList<ManagedPlayerRosterProfile>>(Enumerable.Range(0, count)
            .Select(_ => new ManagedPlayerRosterProfile("managed-" + Interlocked.Increment(ref ordinal), "Managed player", ManagedPlayerSkillLevels.Poor))
            .ToArray());
    }
    public Task HeartbeatAsync(string assignmentId, IReadOnlyCollection<string> profileIds, DateTime nowUtc, CancellationToken cancellationToken)
    {
        if (Unavailable) throw new ManagedPlayerRosterUnavailableException();
        return Task.CompletedTask;
    }
    public Task<bool> TryRenewAsync(string assignmentId, IReadOnlyCollection<string> profileIds, DateTime nowUtc, CancellationToken cancellationToken)
    {
        if (Unavailable) throw new ManagedPlayerRosterUnavailableException();
        return Task.FromResult(OwnsProfiles);
    }
    public Task ReleaseAsync(string assignmentId, IReadOnlyCollection<string> profileIds, DateTime nowUtc, CancellationToken cancellationToken)
    {
        if (Unavailable) throw new ManagedPlayerRosterUnavailableException();
        return Task.CompletedTask;
    }
}
