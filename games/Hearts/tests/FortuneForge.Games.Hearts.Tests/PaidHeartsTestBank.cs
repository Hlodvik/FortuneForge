using System.Text.Json;
using FortuneForge.Games.Abstractions.ManagedPlayers;
using FortuneForge.Games.Abstractions.Wallet;
using FortuneForge.Server.Games.Hearts;
using Google.Cloud.Firestore;

namespace FortuneForge.Server.Tests.Games.Hearts;

internal static class PaidHeartsTestStore
{
    internal static FirestorePaidHeartsStore Create(FirestoreDb db, IManagedPlayerRosterClient roster, PaidHeartsOptions? options = null,
        PaidHeartsTestBank? bank = null, Func<bool>? admits = null)
    {
        bank ??= new(db);
        return new(db, bank, bank, roster, options, admits: admits);
    }
}

/// <summary>A durable remote bank double: game writes never touch these balance or receipt documents.</summary>
internal sealed class PaidHeartsTestBank(FirestoreDb db) : IGameWalletBatchClient, IGameWalletAccountReader
{
    internal bool LoseNextReply { get; set; }
    internal bool FailSettlements { get; set; }
    internal int Calls { get; private set; }
    internal bool FailProfileReads { get; set; }
    public async Task<GameWalletAccountState> GetStateAsync(string accountId, CancellationToken token)
    {
        if (FailProfileReads) throw new GameWalletUnavailableException();
        return new(0, Cents(await Balance(accountId).GetSnapshotAsync(token)), $"Display {accountId}");
    }
    private DocumentReference Balance(string accountId) => db.Collection("userBalances").Document(accountId + "_slotsCredits");
    public async Task<GameWalletBatchReceipt> SettleBatchAsync(string actor, GameWalletBatchRequest request, CancellationToken token)
    {
        if (FailSettlements) throw new GameWalletUnavailableException();
        Calls++;
        var reference = db.Collection("testHeartsRemoteWalletBatches").Document(request.OperationId);
        var requestJson = JsonSerializer.Serialize(request, PaidHeartsMutation.Json);
        var receipt = await db.RunTransactionAsync(async transaction =>
        {
            var prior = await transaction.GetSnapshotAsync(reference, token);
            if (prior.Exists)
            {
                if (prior.GetValue<string>("requestJson") != requestJson) throw new GameWalletConflictException();
                return JsonSerializer.Deserialize<GameWalletBatchReceipt>(prior.GetValue<string>("receiptJson"), PaidHeartsMutation.Json)!
                    with { WasAlreadySettled = true };
            }
            var snapshots = await transaction.GetAllSnapshotsAsync(request.Effects.Select(value => Balance(value.AccountId)), token);
            var declined = request.Effects.Select((value, index) => Cents(snapshots[index]) <
                GameWalletBalanceRequirement.RequiredCents(value.WagerCents, value.MinimumBalanceBeforeCents)).Any(value => value);
            var batchId = GameWalletBatchIdentity.CreateId("hearts", request.OperationId);
            var accounts = request.Effects.Select((value, index) =>
            {
                var before = Cents(snapshots[index]); var after = declined ? before : checked(before - value.WagerCents + value.PayoutCents);
                if (!declined && after != before) transaction.Set(Balance(value.AccountId), new Dictionary<string, object>
                { ["available"] = after / 100, ["availableFractionalCents"] = after % 100 }, SetOptions.MergeAll);
                var prefix = GameWalletBatchIdentity.AccountLedgerPrefix(batchId, value.AccountId);
                return new GameWalletBatchAccountReceipt(value.AccountId, before, after,
                    before < GameWalletBalanceRequirement.RequiredCents(value.WagerCents, value.MinimumBalanceBeforeCents),
                    !declined && value.WagerCents > 0 ? prefix + "-wager" : null, !declined && value.PayoutCents > 0 ? prefix + "-payout" : null);
            }).ToArray();
            var completed = new GameWalletBatchReceipt(batchId, actor, accounts, DateTimeOffset.UtcNow, false, declined);
            transaction.Create(reference, new Dictionary<string, object>
            { ["requestJson"] = requestJson, ["receiptJson"] = JsonSerializer.Serialize(completed, PaidHeartsMutation.Json) });
            return completed;
        }, cancellationToken: token);
        if (LoseNextReply) { LoseNextReply = false; throw new GameWalletUnavailableException(); }
        return receipt;
    }
    private static long Cents(DocumentSnapshot snapshot) => snapshot.Exists
        ? checked(snapshot.GetValue<long>("available") * 100 + snapshot.GetValue<long>("availableFractionalCents")) : 0;
}
