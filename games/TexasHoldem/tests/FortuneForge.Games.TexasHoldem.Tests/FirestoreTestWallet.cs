using System.Text.Json;
using FortuneForge.Games.Abstractions.Wallet;
using FortuneForge.Server.Cards.TexasHoldem.Credit;
using Google.Cloud.Firestore;

namespace FortuneForge.Games.TexasHoldem.Tests;

/// <summary>A durable fake remote bank for the migrated historical store integration suite.</summary>
internal sealed class FirestoreTestWallet(FirestoreDb database) : IGameWalletBatchClient, IGameWalletAccountReader
{
    private DocumentReference Balance(string account) => database.Collection("userBalances").Document($"{account}_slotsCredits");
    public async Task<GameWalletAccountState> GetStateAsync(string accountId, CancellationToken token) =>
        new(0, Cents(await Balance(accountId).GetSnapshotAsync(token)), $"Display {accountId}");

    public Task<GameWalletBatchReceipt> SettleBatchAsync(string actorAccountId, GameWalletBatchRequest request, CancellationToken token)
    {
        var receiptDocument = database.Collection("testRemoteWalletBatches").Document(request.OperationId);
        var requestJson = JsonSerializer.Serialize(request, CreditHoldemMutation.Json);
        return database.RunTransactionAsync(async transaction =>
        {
            var prior = await transaction.GetSnapshotAsync(receiptDocument, token);
            if (prior.Exists)
            {
                if (prior.GetValue<string>("requestJson") != requestJson) throw new GameWalletConflictException();
                return JsonSerializer.Deserialize<GameWalletBatchReceipt>(prior.GetValue<string>("receiptJson"), CreditHoldemMutation.Json)!
                    with { WasAlreadySettled = true };
            }
            var snapshots = await transaction.GetAllSnapshotsAsync(request.Effects.Select(effect => Balance(effect.AccountId)), token);
            var declined = request.Effects.Select((effect, index) => Cents(snapshots[index]) <
                GameWalletBalanceRequirement.RequiredCents(effect.WagerCents, effect.MinimumBalanceBeforeCents)).Any(value => value);
            var batchId = GameWalletBatchIdentity.CreateId(CreditHoldemBackend.GameId, request.OperationId);
            var entries = request.Effects.Select((effect, index) =>
            {
                var before = Cents(snapshots[index]);
                var after = declined ? before : checked(before - effect.WagerCents + effect.PayoutCents);
                if (!declined && after != before)
                    transaction.Set(Balance(effect.AccountId), new Dictionary<string, object>
                    {
                        ["available"] = after / 100, ["availableFractionalCents"] = after % 100,
                        ["version"] = FieldValue.Increment(1), ["updatedAt"] = Timestamp.GetCurrentTimestamp()
                    }, SetOptions.MergeAll);
                var prefix = GameWalletBatchIdentity.AccountLedgerPrefix(batchId, effect.AccountId);
                return new GameWalletBatchAccountReceipt(effect.AccountId, before, after,
                    before < GameWalletBalanceRequirement.RequiredCents(effect.WagerCents, effect.MinimumBalanceBeforeCents),
                    !declined && effect.WagerCents > 0 ? $"{prefix}-wager" : null,
                    !declined && effect.PayoutCents > 0 ? $"{prefix}-payout" : null);
            }).ToArray();
            var receipt = new GameWalletBatchReceipt(batchId, actorAccountId, entries, DateTimeOffset.UtcNow, false, declined);
            transaction.Create(receiptDocument, new Dictionary<string, object>
            {
                ["requestJson"] = requestJson, ["receiptJson"] = JsonSerializer.Serialize(receipt, CreditHoldemMutation.Json)
            });
            return receipt;
        }, cancellationToken: token);
    }

    private static long Cents(DocumentSnapshot snapshot) => snapshot.Exists ?
        checked(snapshot.GetValue<long>("available") * 100 + snapshot.GetValue<long>("availableFractionalCents")) : 0;
}
