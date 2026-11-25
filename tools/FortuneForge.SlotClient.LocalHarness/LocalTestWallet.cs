using FortuneForge.Games.Abstractions.Wallet;

namespace FortuneForge.SlotClient.LocalHarness;

/// <summary>Test infrastructure only. All terms come from production game admission and settlement.</summary>
internal sealed class LocalTestWallet(string gameId, string account) : IGameWalletClient, IGameWalletAccountReader
{
    private readonly object gate = new();
    private readonly Dictionary<string, (GameWalletSettlementRequest Request, GameWalletSettlementReceipt Receipt)> ledger = new(StringComparer.Ordinal);
    private long balance = 1_000_000;
    internal object Snapshot() { lock (gate) return new { testFundsOnly = true, balanceCents = balance, settlements = ledger.Values.Select(value => new { value.Request, value.Receipt }).ToArray() }; }
    public Task<GameWalletAccountState> GetStateAsync(string accountId, CancellationToken token)
    {
        if (accountId != account) throw new GameWalletConflictException();
        lock (gate) return Task.FromResult(new GameWalletAccountState(0, balance, "Isolated paid slot test"));
    }
    public Task<GameWalletSettlementReceipt> SettleAsync(string accountId, GameWalletSettlementRequest request, CancellationToken token)
    {
        if (accountId != account || request.SpendLegacyFreeGame || request.ExpectedLegacyFreeGamesRemaining is not (null or 0) ||
            request.Phase != GameWalletSettlementPhase.Direct || request.WagerCents < 0 || request.PayoutCents < 0) throw new GameWalletConflictException();
        lock (gate)
        {
            var id = GameWalletSettlementIdentity.CreateId(gameId, account, request.OperationId);
            if (ledger.TryGetValue(id, out var saved))
            {
                if (saved.Request != request) throw new GameWalletConflictException();
                return Task.FromResult(saved.Receipt with { WasAlreadySettled = true });
            }
            var before = balance;
            var declined = before < GameWalletBalanceRequirement.RequiredCents(request.WagerCents, request.MinimumBalanceBeforeCents);
            var afterWager = declined ? before : checked(before - request.WagerCents);
            var after = declined ? before : checked(afterWager + request.PayoutCents);
            var transactions = GameWalletTransactionIds.Create(request.OperationId, id, GameWalletTransactionIdLayout.LegacyOperation);
            var receipt = new GameWalletSettlementReceipt(id, !declined && request.WagerCents > 0 ? transactions.WagerId : null,
                !declined && request.PayoutCents > 0 ? transactions.PayoutId : null, before, afterWager, after, DateTimeOffset.UtcNow,
                false, declined, LegacyFreeGamesRemaining: 0);
            ledger[id] = (request, receipt); balance = after;
            return Task.FromResult(receipt);
        }
    }
}
