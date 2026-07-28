using System.Security.Cryptography;
using System.Text;
using FortuneForge.Server.Accounts.Models;
using FortuneForge.Server.Slots.Bonuses;
using FortuneForge.Server.Slots.Models;
using Google.Cloud.Firestore;

namespace FortuneForge.Server.Accounts.Storage;

public sealed partial class FirestoreAccountStore
{
    private DocumentReference UserDocument(string userId) =>
        database.Collection("users").Document(userId);

    private DocumentReference BalanceDocument(string userId, string currencyId) =>
        database.Collection("userBalances").Document($"{userId}_{currencyId}");

    private DocumentReference CurrencyDocument(string currencyId) =>
        database.Collection("currencies").Document(currencyId);

    private DocumentReference BalanceTransactionDocument(string transactionId) =>
        database.Collection("balanceTransactions").Document(transactionId);

    private DocumentReference StatisticsDocument(string userId) =>
        database.Collection("userSlotStatistics").Document(userId);

    private DocumentReference SlotSpinResultDocument(Guid spinId) =>
        database.Collection("slotSpinResults").Document(spinId.ToString("N"));

    private DocumentReference SlotSpinGuardDocument(string userId, string gameId) =>
        database.Collection("slotSpinGuards").Document($"{userId}_{CreateLookupKey(gameId)}");

    private DocumentReference EmailKeyDocument(string email) =>
        database.Collection("accountEmailKeys").Document(CreateLookupKey(email));

    private DocumentReference PlayerNameKeyDocument(string normalizedPlayerName) =>
        database.Collection("accountPlayerNameKeys").Document(CreateLookupKey(normalizedPlayerName));

    private static string CreateLookupKey(string value)
    {
        var digest = SHA256.HashData(Encoding.UTF8.GetBytes(value));
        return Convert.ToHexStringLower(digest);
    }
}
