using System.Security.Cryptography;
using System.Text;
using FortuneForge.Server.Accounts.Models;
using FortuneForge.Server.Slots.Bonuses;
using FortuneForge.Server.Slots.Models;
using Google.Cloud.Firestore;

namespace FortuneForge.Server.Accounts.Storage;

public sealed partial class FirestoreAccountStore
{
    private static Dictionary<string, object> UserData(
        StoredAccount account,
        DateTime createdAtUtc) => new()
    {
        ["userId"] = account.Account.UserId,
        ["playerName"] = account.Account.PlayerName,
        ["normalizedPlayerName"] = account.NormalizedPlayerName,
        ["email"] = account.Account.Email,
        ["passwordHash"] = account.PasswordHash,
        ["status"] = account.Status,
        ["deactivated"] = account.Deactivated,
        ["authProvider"] = "firebase-email",
        ["firebaseUid"] = account.Account.UserId,
        ["emailVerified"] = account.Status == "active",
        ["role"] = account.Account.Role,
        ["accountSchemaVersion"] = AccountSchemaVersion,
        ["createdAt"] = Timestamp.FromDateTime(createdAtUtc),
        ["updatedAt"] = Timestamp.FromDateTime(createdAtUtc)
    };

    private static Dictionary<string, object> BalanceData(
        string userId,
        string currencyId,
        long available,
        DateTime createdAtUtc) => new()
    {
        ["userId"] = userId,
        ["currencyId"] = currencyId,
        ["available"] = available,
        ["reserved"] = 0L,
        ["version"] = 1L,
        ["createdAt"] = Timestamp.FromDateTime(createdAtUtc),
        ["updatedAt"] = Timestamp.FromDateTime(createdAtUtc)
    };

    private static string GameCurrencyId(string currencyId, string gameId) =>
        $"{currencyId}:{gameId.Replace('/', '_')}";

    private static Dictionary<string, object> CurrencyData(
        string currencyId,
        string name,
        long precision,
        DateTime createdAtUtc) => new()
    {
        ["currencyId"] = currencyId,
        ["name"] = name,
        ["precision"] = precision,
        ["active"] = true,
        ["updatedAt"] = Timestamp.FromDateTime(createdAtUtc)
    };

    private static Dictionary<string, object> BalanceTransactionData(
        string transactionId,
        string userId,
        string currencyId,
        long amount,
        long balanceAfter,
        string type,
        string idempotencyKey,
        DateTime createdAtUtc) => new()
    {
        ["transactionId"] = transactionId,
        ["userId"] = userId,
        ["currencyId"] = currencyId,
        ["amount"] = amount,
        ["balanceAfter"] = balanceAfter,
        ["type"] = type,
        ["idempotencyKey"] = idempotencyKey,
        ["createdAt"] = Timestamp.FromDateTime(createdAtUtc)
    };

    private static Dictionary<string, object> StatisticsData(
        string userId,
        DateTime createdAtUtc) => new()
    {
        ["userId"] = userId,
        ["spinsPlayed"] = 0L,
        ["wins"] = 0L,
        ["losses"] = 0L,
        ["creditsWagered"] = 0L,
        ["creditsWon"] = 0L,
        ["netCredits"] = 0L,
        ["createdAt"] = Timestamp.FromDateTime(createdAtUtc),
        ["updatedAt"] = Timestamp.FromDateTime(createdAtUtc)
    };

    private static Dictionary<string, object> KeyData(string userId, DateTime createdAtUtc) => new()
    {
        ["userId"] = userId,
        ["createdAt"] = Timestamp.FromDateTime(createdAtUtc)
    };
}
