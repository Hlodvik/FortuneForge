using System.Text.Json;
using FortuneForge.Games.Abstractions.Wallet;
using FortuneForge.Games.Hosting;
using FortuneForge.Server.Accounts.Models;
using FortuneForge.Server.Accounts.Storage;
using Google.Cloud.Firestore;
using static FortuneForge.Server.Accounts.Storage.FirestoreWalletDocuments;

namespace FortuneForge.Server.Accounts.Wallet;

internal sealed class GameWalletBatchFirestoreStore(FirestoreDb database)
{
    internal const string ReceiptCollection = "walletBatchSettlements";

    private sealed record AccountDocuments(GameWalletBatchEffect Effect, DocumentReference Balance,
        DocumentReference Wager, DocumentReference Payout);

    internal Task<GameWalletBatchReceipt> SettleAsync(string gameId, string actorAccountId, GameWalletBatchRequest request,
        string bodySha256, DateTimeOffset settledAtUtc, CancellationToken cancellationToken, bool requireExisting = false)
    {
        if (!GameWalletBatchVerifier.IsValidAccountId(actorAccountId) || !GameWalletBatchVerifier.IsValidRequest(request) ||
            !GameWalletBatchAuthentication.AllowsEffects(gameId, actorAccountId, request.Effects) ||
            bodySha256.Length != 64 || !bodySha256.All(char.IsAsciiHexDigit) ||
            settledAtUtc == default || settledAtUtc.Offset != TimeSpan.Zero)
            throw new ArgumentException("The wallet batch identity or terms are invalid.");

        var batchId = GameWalletBatchIdentity.CreateId(gameId, request.OperationId);
        var receiptDocument = database.Collection(ReceiptCollection).Document(batchId);
        var identity = new Dictionary<string, object>
        {
            ["batchId"] = batchId,
            ["gameId"] = gameId,
            ["actorAccountId"] = actorAccountId,
            ["operationId"] = request.OperationId,
            ["outcomeSha256"] = request.OutcomeSha256.ToLowerInvariant(),
            ["requestBodySha256"] = bodySha256.ToLowerInvariant(),
            ["schemaVersion"] = 1L,
        };
        var accounts = request.Effects.Select(effect => Documents(batchId, effect)).ToArray();
        var reads = new List<DocumentReference> { receiptDocument };
        foreach (var account in accounts)
            reads.AddRange([account.Balance, account.Wager, account.Payout]);
        var storedTime = settledAtUtc.AddTicks(-(settledAtUtc.Ticks % TimeSpan.TicksPerMicrosecond));

        return database.RunTransactionAsync(async transaction =>
        {
            var snapshots = (await transaction.GetAllSnapshotsAsync(reads, cancellationToken))
                .ToDictionary(snapshot => snapshot.Reference);
            var archived = snapshots[receiptDocument];
            if (archived.Exists)
            {
                var fields = archived.ToDictionary();
                if (!Matches(fields, identity)) throw new GameWalletConflictException();
                if (fields.GetValueOrDefault("receiptJson") is not string receiptJson ||
                    JsonSerializer.Deserialize<GameWalletBatchReceipt>(receiptJson, GameWalletAuthentication.WireJson) is not { WasAlreadySettled: false } original ||
                    !GameWalletBatchVerifier.MatchesSettlement(original, gameId, actorAccountId, request))
                    throw Corrupt();
                for (var index = 0; index < accounts.Length; index++)
                {
                    var account = accounts[index];
                    var entry = original.Accounts[index];
                    CheckLeg(snapshots[account.Wager], gameId, request.OperationId, original, entry, -account.Effect.WagerCents, wager: true);
                    CheckLeg(snapshots[account.Payout], gameId, request.OperationId, original, entry, account.Effect.PayoutCents, wager: false);
                }
                return original with { WasAlreadySettled = true };
            }

            if (requireExisting || accounts.Any(account => snapshots[account.Wager].Exists || snapshots[account.Payout].Exists))
                throw Corrupt();
            var before = accounts.Select(account => ReadSlotsCreditCents(snapshots[account.Balance],
                account.Effect.AccountId, allowMissingFraction: true)).ToArray();
            var insufficient = accounts.Select((account, index) => before[index] <
                GameWalletBalanceRequirement.RequiredCents(account.Effect.WagerCents, account.Effect.MinimumBalanceBeforeCents)).ToArray();
            var declined = insufficient.Any(value => value);
            var entries = accounts.Select((account, index) => new GameWalletBatchAccountReceipt(account.Effect.AccountId, before[index],
                declined ? before[index] : checked(before[index] - account.Effect.WagerCents + account.Effect.PayoutCents), insufficient[index],
                !declined && account.Effect.WagerCents > 0 ? account.Wager.Id : null,
                !declined && account.Effect.PayoutCents > 0 ? account.Payout.Id : null)).ToArray();
            var receipt = new GameWalletBatchReceipt(batchId, actorAccountId, entries, storedTime, false, declined);

            for (var index = 0; index < accounts.Length; index++)
            {
                var account = accounts[index];
                var entry = entries[index];
                if (entry.BalanceBeforeCents != entry.BalanceAfterCents)
                    transaction.Update(account.Balance, SlotsCreditUpdate(entry.BalanceAfterCents, Timestamp.FromDateTime(storedTime.UtcDateTime)));
                if (entry.WagerTransactionId is { } wager)
                    transaction.Create(account.Wager, LegData(wager, gameId, request.OperationId, receipt, entry, -account.Effect.WagerCents, wager: true));
                if (entry.PayoutTransactionId is { } payout)
                    transaction.Create(account.Payout, LegData(payout, gameId, request.OperationId, receipt, entry, account.Effect.PayoutCents, wager: false));
            }
            var completed = new Dictionary<string, object>(identity)
            {
                ["receiptJson"] = JsonSerializer.Serialize(receipt, GameWalletAuthentication.WireJson),
            };
            transaction.Create(receiptDocument, completed);
            return receipt;
        }, cancellationToken: cancellationToken);
    }

    private AccountDocuments Documents(string batchId, GameWalletBatchEffect effect)
    {
        var prefix = GameWalletBatchIdentity.AccountLedgerPrefix(batchId, effect.AccountId);
        return new(effect, Balance(database, effect.AccountId, SlotsCreditsCurrencyId),
            FirestoreWalletDocuments.Transaction(database, $"{prefix}-wager"),
            FirestoreWalletDocuments.Transaction(database, $"{prefix}-payout"));
    }

    private static void CheckLeg(DocumentSnapshot saved, string gameId, string operationId, GameWalletBatchReceipt receipt,
        GameWalletBatchAccountReceipt entry, long cents, bool wager)
    {
        var expectedId = wager ? entry.WagerTransactionId : entry.PayoutTransactionId;
        if (expectedId is null ? saved.Exists : !saved.Exists ||
            !Matches(saved.ToDictionary(), LegData(expectedId, gameId, operationId, receipt, entry, cents, wager)))
            throw Corrupt();
    }

    private static Dictionary<string, object> LegData(string id, string gameId, string operationId,
        GameWalletBatchReceipt receipt, GameWalletBatchAccountReceipt entry, long cents, bool wager)
    {
        var fields = RandTransactionIdentity(id, entry.AccountId, cents, wager ? "game-wager" : "game-payout", id);
        fields.Add("amountCents", cents);
        fields.Add("balanceAfter", (double)RandMoney.CentsToRand(entry.BalanceAfterCents));
        fields.Add("balanceAfterCents", entry.BalanceAfterCents);
        fields.Add("createdAt", Timestamp.FromDateTime(receipt.SettledAtUtc.UtcDateTime));
        fields.Add("gameId", gameId);
        fields.Add("operationId", operationId);
        fields.Add("walletBatchId", receipt.BatchId);
        fields.Add("schemaVersion", 1L);
        return fields;
    }

    private static bool Matches(IReadOnlyDictionary<string, object> stored, IReadOnlyDictionary<string, object> expected) =>
        expected.All(field => stored.TryGetValue(field.Key, out var value) && Equals(value, field.Value));

    private static InvalidOperationException Corrupt() => new("The wallet batch and its ledger records do not agree.");
}
