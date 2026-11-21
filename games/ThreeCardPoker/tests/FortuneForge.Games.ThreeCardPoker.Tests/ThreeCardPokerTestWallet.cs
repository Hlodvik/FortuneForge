using System.Text.Json;
using FortuneForge.Games.Abstractions.Wallet;
using Google.Cloud.Firestore;
using Xunit;

namespace FortuneForge.Games.ThreeCardPoker.Tests;

// This test wallet alone owns the fixture account ledger; the game sees only its RPC interfaces.
internal sealed class ThreeCardPokerTestWallet(FirestoreDb database) : IGameWalletClient, IGameWalletAccountReader
{
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);
    private int calls;
    private int accountReads;
    public int Calls => calls;
    public int AccountReads => accountReads;
    public bool LoseNextReply { get; set; }
    public long? ReportedBalanceCents { get; set; }
    public string? PlayerName { get; set; } = "Trusted Alice";

    public async Task<GameWalletAccountState> GetStateAsync(string accountId, CancellationToken token)
    {
        Interlocked.Increment(ref accountReads);
        var snapshot = await Balance(accountId).GetSnapshotAsync(token);
        return new(0, ReportedBalanceCents ?? ReadBalance(snapshot), PlayerName);
    }

    public async Task<GameWalletSettlementReceipt> SettleAsync(string accountId, GameWalletSettlementRequest request, CancellationToken token)
    {
        Interlocked.Increment(ref calls);
        var id = GameWalletSettlementIdentity.CreateId(ThreeCardPokerModule.Descriptor.Id, accountId, request.OperationId);
        var reference = database.Collection("testWalletSettlements").Document(id);
        var receipt = await database.RunTransactionAsync(async transaction =>
        {
            var saved = await transaction.GetSnapshotAsync(reference, token);
            if (saved.Exists)
            {
                Assert.Equal(JsonSerializer.Serialize(request, Json), saved.GetValue<string>("requestJson"));
                return JsonSerializer.Deserialize<GameWalletSettlementReceipt>(saved.GetValue<string>("json"), Json)! with { WasAlreadySettled = true };
            }
            var balance = await transaction.GetSnapshotAsync(Balance(accountId), token);
            var before = ReadBalance(balance);
            var declined = before < GameWalletBalanceRequirement.RequiredCents(request.WagerCents, request.MinimumBalanceBeforeCents);
            var afterWager = declined ? before : checked(before - request.WagerCents);
            var after = declined ? before : checked(afterWager + request.PayoutCents);
            var result = new GameWalletSettlementReceipt(id, !declined && request.WagerCents > 0 ? id + "-wager" : null,
                !declined && request.PayoutCents > 0 ? id + "-payout" : null, before, afterWager, after, DateTimeOffset.UtcNow, false, declined);
            transaction.Create(reference, new Dictionary<string, object> { ["requestJson"] = JsonSerializer.Serialize(request, Json), ["json"] = JsonSerializer.Serialize(result, Json) });
            if (!declined)
            {
                transaction.Set(Balance(accountId), new Dictionary<string, object>
                { ["available"] = after / 100, ["availableFractionalCents"] = after % 100 }, SetOptions.MergeAll);
                if (result.WagerTransactionId is { } wagerId)
                    transaction.Create(database.Collection("balanceTransactions").Document(wagerId), Ledger(accountId, -request.WagerCents, afterWager, "wallet-wager"));
                if (result.PayoutTransactionId is { } payoutId)
                    transaction.Create(database.Collection("balanceTransactions").Document(payoutId), Ledger(accountId, request.PayoutCents, after, "wallet-payout"));
            }
            return result;
        }, cancellationToken: token);
        if (LoseNextReply)
        {
            LoseNextReply = false;
            throw new GameWalletUnavailableException();
        }
        return receipt;
    }

    internal async Task SetBalanceAsync(string user, long cents) => await Balance(user).SetAsync(new Dictionary<string, object>
    { ["available"] = cents / 100, ["availableFractionalCents"] = cents % 100 });
    private DocumentReference Balance(string user) => database.Collection("userBalances").Document(user + "_slotsCredits");
    private static long ReadBalance(DocumentSnapshot snapshot) => snapshot.Exists ? snapshot.GetValue<long>("available") * 100 + snapshot.GetValue<long>("availableFractionalCents") : 0;
    private static Dictionary<string, object> Ledger(string user, long amount, long balance, string type) => new()
    { ["userId"] = user, ["amount"] = amount / 100d, ["balanceAfter"] = balance / 100d, ["type"] = type, ["currencyId"] = "slotsCredits" };
}

public sealed class ThreeCardPokerEmulatorFactAttribute : FactAttribute
{
    public ThreeCardPokerEmulatorFactAttribute()
    {
        if (!ThreeCardPokerEmulator.IsConfigured) Skip = "A localhost Firestore emulator is required for Three Card Poker persistence checks.";
    }
}
public sealed class ThreeCardPokerEmulatorTheoryAttribute : TheoryAttribute
{
    public ThreeCardPokerEmulatorTheoryAttribute()
    {
        if (!ThreeCardPokerEmulator.IsConfigured) Skip = "A localhost Firestore emulator is required for Three Card Poker persistence checks.";
    }
}
internal static class ThreeCardPokerEmulator
{
    internal static bool IsConfigured
    {
        get
        {
            var endpoint = Environment.GetEnvironmentVariable("FIRESTORE_EMULATOR_HOST");
            if (string.IsNullOrWhiteSpace(endpoint)) return false;
            if (!endpoint.StartsWith("127.0.0.1:", StringComparison.Ordinal) && !endpoint.StartsWith("localhost:", StringComparison.Ordinal))
                throw new InvalidOperationException("Three Card Poker tests require a localhost emulator.");
            return true;
        }
    }
}
