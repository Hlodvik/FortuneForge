using FortuneForge.Server.Accounts.Models;
using Google.Cloud.Firestore;

namespace FortuneForge.Server.Accounts.Storage;

internal static class FirestoreWalletDocuments
{
    internal const string SlotsCreditsCurrencyId = "slotsCredits";
    internal const string AvailableFractionalCentsField = "availableFractionalCents";

    internal static DocumentReference Balance(FirestoreDb database, string userId, string currencyId) =>
        database.Collection("userBalances").Document($"{userId}_{currencyId}");

    internal static DocumentReference Transaction(FirestoreDb database, string transactionId) =>
        database.Collection("balanceTransactions").Document(transactionId);

    internal static string LegacyFreeGameCurrencyId(string gameId) => $"freeGames:{FirestoreAccountStore.StorageGameId(gameId)}";

    internal static async Task<long> ReadLegacyFreeGamesAsync(FirestoreDb database, string userId, string gameId,
        CancellationToken cancellationToken)
    {
        var currency = LegacyFreeGameCurrencyId(gameId);
        var balance = await Balance(database, userId, currency).GetSnapshotAsync(cancellationToken);
        return ReadLegacyFreeGames(balance, userId, currency);
    }

    internal static long ReadLegacyFreeGames(DocumentSnapshot snapshot, string userId, string currency)
    {
        if (!snapshot.Exists) return 0;
        if (!snapshot.TryGetValue<long>("available", out var available) || available < 0 ||
            (snapshot.ContainsField("userId") && snapshot.GetValue<string>("userId") != userId) ||
            (snapshot.ContainsField("currencyId") && snapshot.GetValue<string>("currencyId") != currency))
            throw new InvalidOperationException("The account's legacy free-game balance is invalid.");
        return available;
    }

    internal static long ReadLegacyRandBalanceCents(DocumentSnapshot snapshot) => RandMoney.CombineCents(
        snapshot.Exists && snapshot.TryGetValue<long>("available", out var available) ? available : 0,
        snapshot.Exists && snapshot.TryGetValue<long>(AvailableFractionalCentsField, out var fractional) ? fractional : 0);

    internal static async Task<long> ReadSlotsCreditCentsAsync(FirestoreDb database, string userId, bool useLegacyLedger,
        CancellationToken cancellationToken)
    {
        var snapshot = await Balance(database, userId, SlotsCreditsCurrencyId).GetSnapshotAsync(cancellationToken);
        var cents = useLegacyLedger
            ? ReadLegacyRandBalanceCents(snapshot)
            : ReadSlotsCreditCents(snapshot, userId, allowMissingFraction: true);
        if (cents < 0) throw new InvalidOperationException("The account's slots credit balance is invalid.");
        return cents;
    }

    internal static long ReadSlotsCreditCents(DocumentSnapshot snapshot, string userId, bool allowMissingFraction = false)
    {
        if (!snapshot.Exists ||
            !snapshot.TryGetValue<string>("userId", out var storedUserId) || storedUserId != userId ||
            !snapshot.TryGetValue<string>("currencyId", out var currencyId) || currencyId != SlotsCreditsCurrencyId ||
            !snapshot.TryGetValue<long>("available", out var available) || available < 0 ||
            (!snapshot.TryGetValue<long>(AvailableFractionalCentsField, out var fractional) &&
                (!allowMissingFraction || snapshot.ContainsField(AvailableFractionalCentsField))) ||
            fractional is < 0 or >= RandMoney.CentsPerRand ||
            !snapshot.TryGetValue<long>("reserved", out var reserved) || reserved < 0 ||
            !snapshot.TryGetValue<long>("version", out var version) || version < 1)
        {
            throw new InvalidOperationException("The account's slots credit balance is missing or invalid.");
        }

        return RandMoney.CombineCents(available, fractional);
    }

    internal static Dictionary<string, object> SlotsCreditUpdate(long cents, Timestamp updatedAt) => new()
    {
        ["available"] = cents / RandMoney.CentsPerRand,
        [AvailableFractionalCentsField] = cents % RandMoney.CentsPerRand,
        ["version"] = FieldValue.Increment(1),
        ["updatedAt"] = updatedAt,
    };

    internal static Dictionary<string, object> RandTransactionIdentity(
        string transactionId, string userId, long amountCents, string type, string idempotencyKey) => new()
    {
        ["transactionId"] = transactionId,
        ["userId"] = userId,
        ["currencyId"] = SlotsCreditsCurrencyId,
        ["amount"] = (double)RandMoney.CentsToRand(amountCents),
        ["type"] = type,
        ["idempotencyKey"] = idempotencyKey,
    };
}
