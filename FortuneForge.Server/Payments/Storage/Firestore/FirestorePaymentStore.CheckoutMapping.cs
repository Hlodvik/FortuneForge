using System.Security.Cryptography;
using System.Text;
using FortuneForge.Server.Payments.Models;
using Google.Cloud.Firestore;

namespace FortuneForge.Server.Payments.Storage;

internal sealed partial class FirestorePaymentStore
{
    private static StoredPaymentCheckout? ToStored(DocumentSnapshot snapshot)
    {
        if (!snapshot.Exists)
        {
            return null;
        }

        var marketCode = ReadString(snapshot, "market");
        if (string.IsNullOrWhiteSpace(marketCode))
        {
            return null;
        }

        var catalogMarket = PaymentCatalog.Markets.FirstOrDefault(candidate =>
            string.Equals(candidate.Code, marketCode, StringComparison.Ordinal));
        if (catalogMarket is null)
        {
            return null;
        }

        var methodId = ReadString(snapshot, "paymentMethodId");
        if (string.IsNullOrWhiteSpace(methodId))
        {
            return null;
        }

        var catalogMethod = catalogMarket.PaymentMethods.FirstOrDefault(candidate =>
            string.Equals(candidate.Id, methodId, StringComparison.Ordinal));
        if (catalogMethod is null)
        {
            return null;
        }

        if (!snapshot.TryGetValue<Timestamp>("statusUpdatedAt", out var statusUpdatedAt) ||
            !snapshot.TryGetValue<Timestamp>("createdAt", out var createdAt) ||
            !snapshot.TryGetValue<Timestamp>("expiresAt", out var expiresAt))
        {
            return null;
        }

        var market = catalogMarket with
        {
            DisplayName = ReadString(snapshot, "marketName", catalogMarket.DisplayName),
            Currency = ReadString(snapshot, "currency", catalogMarket.Currency),
            Locale = ReadString(snapshot, "locale", catalogMarket.Locale)
        };
        var method = catalogMethod with
        {
            DisplayName = ReadString(snapshot, "paymentMethodName", catalogMethod.DisplayName),
            Type = ReadString(snapshot, "paymentMethodType", catalogMethod.Type)
        };
        BankTransferInstructions? bankTransfer = null;
        if (snapshot.TryGetValue<string>("bankName", out var bankName))
        {
            bankTransfer = new BankTransferInstructions(
                bankName,
                snapshot.GetValue<string>("bankAccountName"),
                snapshot.GetValue<string>("bankAccountNumber"),
                snapshot.GetValue<string>("bankBranchCode"),
                snapshot.GetValue<string>("bankReference"),
                snapshot.GetValue<string>("bankInstructions"));
        }

        return new StoredPaymentCheckout(
            ReadString(snapshot, "checkoutId"),
            ReadString(snapshot, "providerCheckoutId", ReadString(snapshot, "checkoutId")),
            ReadString(snapshot, "providerPathwayKey"),
            ReadString(snapshot, "invoiceId"),
            ReadString(snapshot, "userId"),
            ReadString(snapshot, "idempotencyKey"),
            ReadString(snapshot, "providerId"),
            snapshot.TryGetValue<bool>("isMock", out var isMock) && isMock,
            market,
            method,
            ReadLong(snapshot, "amount"),
            ReadLong(snapshot, "amountMinor"),
            ReadLong(snapshot, "credits"),
            ReadString(snapshot, "status", "received"),
            statusUpdatedAt.ToDateTime(),
            createdAt.ToDateTime(),
            expiresAt.ToDateTime(),
            ReadTimestamp(snapshot, "processingAt"),
            ReadTimestamp(snapshot, "completedAt"),
            snapshot.TryGetValue<long>("creditedBalance", out var creditedBalance)
                ? creditedBalance
                : null,
            new PaymentCustomerDetails(
                ReadString(snapshot, "customerFirstName"),
                ReadString(snapshot, "customerLastName"),
                ReadString(snapshot, "customerEmail"),
                ReadString(snapshot, "customerReference", ReadString(snapshot, "beneficiaryReference")),
                ReadString(snapshot, "beneficiaryReference")),
            new PaymentBankDetails(
                ReadString(snapshot, "payerAccountHolder"),
                ReadString(snapshot, "payerBankName"),
                ReadString(snapshot, "payerAccountNumber"),
                ReadString(snapshot, "payerBranchCode"),
                ReadString(snapshot, "payerAccountType")),
            bankTransfer,
            ReadString(snapshot, "notice"),
            ReadString(snapshot, "providerSubmissionStatus", "idle"),
            ReadString(snapshot, "providerSubmissionLeaseId"),
            ReadTimestamp(snapshot, "providerSubmissionLeaseUntil"),
            ReadTimestamp(snapshot, "nextProviderSubmissionAt"),
            ReadTimestamp(snapshot, "lastProviderSubmissionAt"),
            (int)ReadLong(snapshot, "providerSubmissionAttempt"),
            snapshot.TryGetValue<long>("lastProviderSubmissionStatusCode", out var statusCode)
                ? (int)statusCode
                : null);
    }
}
