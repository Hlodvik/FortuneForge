using System.Security.Cryptography;
using System.Text;
using FortuneForge.Server.Payments.Models;
using Google.Cloud.Firestore;

namespace FortuneForge.Server.Payments.Storage;

internal sealed partial class FirestorePaymentStore
{
    private static StoredPaymentWithdrawal? ToStoredWithdrawal(DocumentSnapshot snapshot)
    {
        if (!snapshot.Exists)
        {
            return null;
        }

        var marketCode = snapshot.GetValue<string>("market");
        var catalogMarket = PaymentCatalog.Markets.FirstOrDefault(candidate =>
            string.Equals(candidate.Code, marketCode, StringComparison.Ordinal));
        if (catalogMarket is null)
        {
            return null;
        }

        var market = catalogMarket with
        {
            DisplayName = snapshot.GetValue<string>("marketName"),
            Currency = snapshot.GetValue<string>("currency"),
            Locale = snapshot.GetValue<string>("locale")
        };

        return new StoredPaymentWithdrawal(
            snapshot.GetValue<string>("withdrawalId"),
            ReadString(snapshot, "providerWithdrawalId"),
            ReadString(snapshot, "providerPathwayKey"),
            snapshot.GetValue<string>("userId"),
            snapshot.GetValue<string>("idempotencyKey"),
            snapshot.GetValue<string>("providerId"),
            snapshot.GetValue<bool>("isMock"),
            market,
            ReadLong(snapshot, "amount"),
            ReadLong(snapshot, "amountMinor"),
            ReadLong(snapshot, "creditsDebited"),
            snapshot.GetValue<string>("status"),
            snapshot.GetValue<Timestamp>("statusUpdatedAt").ToDateTime(),
            snapshot.GetValue<Timestamp>("createdAt").ToDateTime(),
            ReadTimestamp(snapshot, "completedAt"),
            new PaymentCustomerDetails(
                ReadString(snapshot, "customerFirstName"),
                ReadString(snapshot, "customerLastName"),
                ReadString(snapshot, "customerEmail"),
                ReadString(snapshot, "customerReference", ReadString(snapshot, "beneficiaryReference")),
                ReadString(snapshot, "beneficiaryReference")),
            new WithdrawalBankDetails(
                ReadString(snapshot, "accountHolder"),
                ReadString(snapshot, "bankName"),
                ReadString(snapshot, "bankAccountNumber"),
                ReadString(snapshot, "bankBranchCode"),
                ReadString(snapshot, "bankAccountType")),
            snapshot.GetValue<string>("notice"));
    }

    private static DateTime? ReadTimestamp(DocumentSnapshot snapshot, string field) =>
        snapshot.TryGetValue<Timestamp>(field, out var timestamp)
            ? timestamp.ToDateTime()
            : null;

    private static long ReadLong(DocumentSnapshot snapshot, string field) =>
        snapshot.TryGetValue<long>(field, out var value) ? value : 0;

    private static decimal ReadDecimal(DocumentSnapshot snapshot, string field) =>
        snapshot.TryGetValue<long>(field, out var longValue)
            ? longValue
            : snapshot.TryGetValue<double>(field, out var doubleValue)
                ? (decimal)doubleValue
                : 0;

    private static decimal BalanceWithFractionalCents(
        DocumentSnapshot snapshot,
        long wholeRand) =>
        wholeRand + ReadLong(snapshot, "availableFractionalCents") / 100m;

    private static string ReadString(DocumentSnapshot snapshot, string field) =>
        snapshot.TryGetValue<string>(field, out var value) ? value : string.Empty;

    private static string ReadString(DocumentSnapshot snapshot, string field, string fallback) =>
        snapshot.TryGetValue<string>(field, out var value) ? value : fallback;

    private static string HashKey(string value) => Convert.ToHexStringLower(
        SHA256.HashData(Encoding.UTF8.GetBytes(value)));
}
