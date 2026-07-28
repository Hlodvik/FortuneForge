using System.Security.Cryptography;
using System.Text;
using FortuneForge.Server.Payments.Models;
using Google.Cloud.Firestore;

namespace FortuneForge.Server.Payments.Storage;

internal sealed partial class FirestorePaymentStore
{
    private DocumentReference CheckoutDocument(string checkoutId) =>
        database.Collection("slotCreditPurchases").Document(checkoutId);

    private DocumentReference WithdrawalDocument(string withdrawalId) =>
        database.Collection("slotCreditWithdrawals").Document(withdrawalId);

    private DocumentReference BalanceDocument(string userId) =>
        database.Collection("userBalances").Document($"{userId}_{SlotsCreditsCurrencyId}");

    private DocumentReference UserDocument(string userId) =>
        database.Collection("users").Document(userId);

    private DocumentReference SettlementLedgerDocument(string checkoutId) =>
        database.Collection("balanceTransactions").Document($"payment-{checkoutId}");

    private DocumentReference WithdrawalLedgerDocument(string withdrawalId) =>
        database.Collection("balanceTransactions").Document($"withdrawal-{withdrawalId}");

    private DocumentReference WithdrawalRefundLedgerDocument(string withdrawalId) =>
        database.Collection("balanceTransactions").Document($"withdrawal-refund-{withdrawalId}");

    private DocumentReference IdempotencyDocument(string userId, string idempotencyKey) =>
        database.Collection("paymentIdempotencyKeys").Document(
            HashKey($"{userId}\u001f{idempotencyKey}"));

    private DocumentReference WithdrawalIdempotencyDocument(string userId, string idempotencyKey) =>
        database.Collection("withdrawalIdempotencyKeys").Document(
            HashKey($"{userId}\u001f{idempotencyKey}"));

    private DocumentReference WithdrawalProviderKeyDocument(
        string providerId,
        string providerWithdrawalId) =>
        database.Collection("paymentWithdrawalProviderKeys").Document(
            HashKey($"{providerId}\u001f{providerWithdrawalId}"));

    private DocumentReference CheckoutProviderKeyDocument(
        string providerId,
        string providerCheckoutId) =>
        database.Collection("paymentCheckoutProviderKeys").Document(
            HashKey($"{providerId}\u001f{providerCheckoutId}"));

    private DocumentReference ProviderEventDocument(string providerId, string eventId) =>
        database.Collection("paymentProviderEvents").Document(
            HashKey($"{providerId}\u001f{eventId}"));

    private DocumentReference InvoiceKeyDocument(string invoiceId) =>
        database.Collection("paymentInvoiceKeys").Document(HashKey(invoiceId));
}
