using FortuneForge.Games.Abstractions.Wallet;

namespace FortuneForge.Games.CrookedRoad.Tests.Support;

/// <summary>Test-only wallet transport. Never registered by a product game module.</summary>
public sealed class CrookedRoadTestWallet(long initialBalance = 1_000_000) : IGameWalletClient, IGameWalletAccountReader
{
    private readonly object gate = new();
    private readonly Dictionary<string, long> balances = new(StringComparer.Ordinal);
    private readonly Dictionary<string, (GameWalletSettlementRequest Request, GameWalletSettlementReceipt Receipt)> settlements = new(StringComparer.Ordinal);
    public bool FailAfterNextSettlement { get; set; }
    public int UniqueSettlements { get { lock (gate) return settlements.Count; } }
    public GameWalletSettlementRequest? LastRequest { get; private set; }

    public Task<GameWalletAccountState> GetStateAsync(string accountId, CancellationToken cancellationToken)
    {
        lock (gate) return Task.FromResult(new GameWalletAccountState(0, Balance(accountId), "Local paid-protocol test"));
    }

    public Task<GameWalletSettlementReceipt> SettleAsync(string accountId, GameWalletSettlementRequest request, CancellationToken cancellationToken)
    {
        lock (gate)
        {
            if (request.SpendLegacyFreeGame || request.ExpectedLegacyFreeGamesRemaining is not null ||
                request.Phase != GameWalletSettlementPhase.Direct || request.WagerCents < 0 || request.PayoutCents < 0)
                throw new GameWalletConflictException();
            var id = GameWalletSettlementIdentity.CreateId(CrookedRoadRules.GameId, accountId, request.OperationId);
            LastRequest = request;
            if (settlements.TryGetValue(id, out var saved))
            {
                if (saved.Request != request) throw new GameWalletConflictException();
                return Task.FromResult(saved.Receipt with { WasAlreadySettled = true });
            }
            var before = Balance(accountId);
            var decline = before < request.WagerCents;
            var afterWager = decline ? before : checked(before - request.WagerCents);
            var after = decline ? before : checked(afterWager + request.PayoutCents);
            var transactionIds = GameWalletTransactionIds.Create(request.OperationId, id, GameWalletTransactionIdLayout.StandardSettlement);
            var receipt = new GameWalletSettlementReceipt(id,
                !decline && request.WagerCents > 0 ? transactionIds.WagerId : null,
                !decline && request.PayoutCents > 0 ? transactionIds.PayoutId : null,
                before, afterWager, after, DateTimeOffset.UtcNow, false, decline, LegacyFreeGamesRemaining: null);
            balances[accountId] = after;
            settlements[id] = (request, receipt);
            if (FailAfterNextSettlement)
            {
                FailAfterNextSettlement = false;
                throw new GameWalletUnavailableException();
            }
            return Task.FromResult(receipt);
        }
    }
    private long Balance(string account) => balances.TryGetValue(account, out var balance) ? balance : initialBalance;
}
