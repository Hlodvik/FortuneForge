using System.Text.Json;
using FortuneForge.Games.Abstractions.Wallet;

namespace FortuneForge.Games.PharaohsAscent.LocalHarness;

/// <summary>Isolated test funds; game decisions and settlement terms come from the production round service.</summary>
internal sealed class LocalTestWallet : IGameWalletClient, IGameWalletAccountReader
{
    private const long InitialBalanceCents = 1_000_000;
    private readonly string account;
    private readonly object gate = new();
    private readonly Dictionary<string, (GameWalletSettlementRequest Request, GameWalletSettlementReceipt Receipt)> settlements = new(StringComparer.Ordinal);
    private long balance = InitialBalanceCents;

    internal LocalTestWallet(string account, string? snapshotPath = null)
    {
        this.account = account;
        if (snapshotPath is not null) Restore(snapshotPath);
    }

    private sealed record WalletSnapshot(bool TestFundsOnly, string AccountId, long InitialBalanceCents,
        long BalanceCents, SavedSettlement[] Settlements);
    private sealed record SavedSettlement(string Key, GameWalletSettlementRequest Request, GameWalletSettlementReceipt Receipt);

    private void Restore(string path)
    {
        var snapshot = JsonSerializer.Deserialize<WalletSnapshot>(File.ReadAllText(path), new JsonSerializerOptions(JsonSerializerDefaults.Web))
            ?? throw InvalidSnapshot();
        if (!snapshot.TestFundsOnly || snapshot.AccountId != account || snapshot.InitialBalanceCents != InitialBalanceCents ||
            snapshot.BalanceCents < 0 || snapshot.Settlements is null)
            throw InvalidSnapshot();
        var restoredBalance = InitialBalanceCents;
        foreach (var saved in snapshot.Settlements)
        {
            if (saved?.Request is not { } request || saved.Receipt is not { } original ||
                string.IsNullOrWhiteSpace(request.OperationId) || string.IsNullOrWhiteSpace(request.OutcomeSha256) ||
                request.SpendLegacyFreeGame || request.ExpectedLegacyFreeGamesRemaining is not (null or 0) ||
                request.Phase != GameWalletSettlementPhase.Direct || request.WagerCents < 0 || request.PayoutCents < 0)
                throw InvalidSnapshot();
            // JSON round-tripped by PowerShell can change the offset while retaining the same instant.
            var receipt = original with { SettledAtUtc = original.SettledAtUtc.ToUniversalTime() };
            var id = GameWalletSettlementIdentity.CreateId(PharaohsAscentEngine.GameId, account, request.OperationId);
            var transactions = GameWalletTransactionIds.Create(request.OperationId, id, GameWalletTransactionIdLayout.LegacyOperation);
            if (saved.Key != id || receipt.BalanceBeforeCents != restoredBalance || receipt.WasAlreadySettled ||
                receipt.InsufficientLegacyFreeGames || receipt.LegacyFreeGamesCountMismatch || receipt.LegacyFreeGamesRemaining != 0 ||
                !GameWalletReceiptVerifier.MatchesSettlement(receipt, PharaohsAscentEngine.GameId, account, request) ||
                receipt.WagerTransactionId != (!receipt.InsufficientFunds && request.WagerCents > 0 ? transactions.WagerId : null) ||
                receipt.PayoutTransactionId != (!receipt.InsufficientFunds && request.PayoutCents > 0 ? transactions.PayoutId : null) ||
                !settlements.TryAdd(id, (request, receipt)))
                throw InvalidSnapshot();
            restoredBalance = receipt.BalanceAfterCents;
        }
        if (restoredBalance != snapshot.BalanceCents) throw InvalidSnapshot();
        balance = restoredBalance;
    }

    private static InvalidOperationException InvalidSnapshot() =>
        new("The isolated local wallet snapshot does not match its test account, settlement ledger, or balance.");
    internal bool LoseNextReply { get; set; }
    internal int Calls { get; private set; }
    internal int UniqueSettlements { get { lock (gate) return settlements.Count; } }
    internal long BalanceCents { get { lock (gate) return balance; } }
    internal IReadOnlyList<GameWalletSettlementRequest> Requests
    { get { lock (gate) return settlements.Values.Select(value => value.Request).ToArray(); } }

    internal object Snapshot()
    {
        lock (gate) return new
        {
            testFundsOnly = true,
            accountId = account,
            initialBalanceCents = InitialBalanceCents,
            balanceCents = balance,
            settlements = settlements.Select(pair => new { pair.Key, pair.Value.Request, pair.Value.Receipt }).ToArray(),
        };
    }

    public Task<GameWalletAccountState> GetStateAsync(string accountId, CancellationToken cancellationToken)
    {
        if (accountId != account) throw new GameWalletConflictException();
        lock (gate) return Task.FromResult(new GameWalletAccountState(0, balance, "Local Pharaoh test"));
    }

    public Task<GameWalletSettlementReceipt> SettleAsync(string accountId, GameWalletSettlementRequest request,
        CancellationToken cancellationToken)
    {
        if (accountId != account || request.SpendLegacyFreeGame || request.ExpectedLegacyFreeGamesRemaining is not (null or 0) ||
            request.Phase != GameWalletSettlementPhase.Direct || request.WagerCents < 0 || request.PayoutCents < 0)
            throw new GameWalletConflictException();
        lock (gate)
        {
            Calls++;
            var id = GameWalletSettlementIdentity.CreateId(PharaohsAscentEngine.GameId, account, request.OperationId);
            if (settlements.TryGetValue(id, out var saved))
            {
                if (saved.Request != request) throw new GameWalletConflictException();
                return Task.FromResult(saved.Receipt with { WasAlreadySettled = true });
            }

            var before = balance;
            var decline = before < GameWalletBalanceRequirement.RequiredCents(request.WagerCents, request.MinimumBalanceBeforeCents);
            var afterWager = decline ? before : checked(before - request.WagerCents);
            var after = decline ? before : checked(afterWager + request.PayoutCents);
            var transactions = GameWalletTransactionIds.Create(request.OperationId, id, GameWalletTransactionIdLayout.LegacyOperation);
            var receipt = new GameWalletSettlementReceipt(id,
                !decline && request.WagerCents > 0 ? transactions.WagerId : null,
                !decline && request.PayoutCents > 0 ? transactions.PayoutId : null,
                before, afterWager, after, DateTimeOffset.UtcNow, false, decline, LegacyFreeGamesRemaining: 0);
            settlements[id] = (request, receipt);
            balance = after;
            if (LoseNextReply)
            {
                LoseNextReply = false;
                throw new GameWalletUnavailableException();
            }
            return Task.FromResult(receipt);
        }
    }
}
