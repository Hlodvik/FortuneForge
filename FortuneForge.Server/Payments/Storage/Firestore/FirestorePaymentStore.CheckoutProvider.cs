using System.Security.Cryptography;
using System.Text;
using FortuneForge.Server.Payments.Models;
using Google.Cloud.Firestore;

namespace FortuneForge.Server.Payments.Storage;

internal sealed partial class FirestorePaymentStore
{
    public Task<PaymentResult<StoredPaymentCheckout>> UpdateCheckoutProviderAsync(
        string checkoutId,
        string userId,
        string providerCheckoutId,
        string status,
        BankTransferInstructions? bankTransfer,
        DateTime updatedAtUtc,
        CancellationToken cancellationToken)
    {
        var checkoutReference = CheckoutDocument(checkoutId);
        return database.RunTransactionAsync(
            async transaction =>
            {
                var checkoutSnapshot = await transaction.GetSnapshotAsync(
                    checkoutReference,
                    cancellationToken);
                var checkout = ToStored(checkoutSnapshot);
                if (checkout is null ||
                    !string.Equals(checkout.UserId, userId, StringComparison.Ordinal))
                {
                    return PaymentResult<StoredPaymentCheckout>.Failure(PaymentError.CheckoutNotFound);
                }

                if (string.IsNullOrWhiteSpace(providerCheckoutId))
                {
                    return PaymentResult<StoredPaymentCheckout>.Failure(
                        PaymentError.InvalidStatusTransition);
                }

                if (!string.IsNullOrWhiteSpace(checkout.ProviderCheckoutId) &&
                    !string.Equals(
                        checkout.ProviderCheckoutId,
                        providerCheckoutId,
                        StringComparison.OrdinalIgnoreCase))
                {
                    return PaymentResult<StoredPaymentCheckout>.Failure(
                        PaymentError.InvalidStatusTransition);
                }

                if (!string.Equals(checkout.Status, status, StringComparison.Ordinal) &&
                    !CanTransition(checkout.Status, status))
                {
                    return PaymentResult<StoredPaymentCheckout>.Failure(
                        PaymentError.InvalidStatusTransition);
                }

                var providerKeyReference = CheckoutProviderKeyDocument(
                    checkout.ProviderId,
                    providerCheckoutId);
                var providerKeySnapshot = await transaction.GetSnapshotAsync(
                    providerKeyReference,
                    cancellationToken);
                if (providerKeySnapshot.Exists &&
                    (!string.Equals(
                        providerKeySnapshot.GetValue<string>("checkoutId"),
                        checkout.CheckoutId,
                        StringComparison.Ordinal) ||
                    !string.Equals(
                        providerKeySnapshot.GetValue<string>("userId"),
                        checkout.UserId,
                        StringComparison.Ordinal)))
                {
                    return PaymentResult<StoredPaymentCheckout>.Failure(
                        PaymentError.InvalidStatusTransition);
                }

                DocumentSnapshot? balanceSnapshot = null;
                DocumentSnapshot? ledgerSnapshot = null;
                var balanceReference = BalanceDocument(userId);
                var ledgerReference = SettlementLedgerDocument(checkoutId);
                if (status == "completed")
                {
                    balanceSnapshot = await transaction.GetSnapshotAsync(
                        balanceReference,
                        cancellationToken);
                    ledgerSnapshot = await transaction.GetSnapshotAsync(
                        ledgerReference,
                        cancellationToken);
                    if (!balanceSnapshot.Exists)
                    {
                        return PaymentResult<StoredPaymentCheckout>.Failure(
                            PaymentError.AccountBalanceNotFound);
                    }
                }

                var checkoutUpdates = new Dictionary<string, object>
                {
                    ["providerCheckoutId"] = providerCheckoutId,
                    ["status"] = status,
                    ["statusUpdatedAt"] = Timestamp.FromDateTime(updatedAtUtc),
                    ["providerSubmissionStatus"] = "bound",
                    ["providerSubmissionLeaseId"] = FieldValue.Delete,
                    ["providerSubmissionLeaseUntil"] = FieldValue.Delete,
                    ["nextProviderSubmissionAt"] = FieldValue.Delete
                };
                var updated = checkout with
                {
                    ProviderCheckoutId = providerCheckoutId,
                    Status = status,
                    StatusUpdatedAtUtc = updatedAtUtc,
                    ProviderSubmissionStatus = "bound",
                    ProviderSubmissionLeaseId = null,
                    ProviderSubmissionLeaseUntilUtc = null,
                    NextProviderSubmissionAtUtc = null
                };

                if (bankTransfer is not null)
                {
                    checkoutUpdates["bankName"] = bankTransfer.BankName;
                    checkoutUpdates["bankAccountName"] = bankTransfer.AccountName;
                    checkoutUpdates["bankAccountNumber"] = bankTransfer.AccountNumber;
                    checkoutUpdates["bankBranchCode"] = bankTransfer.BranchCode;
                    checkoutUpdates["bankReference"] = bankTransfer.Reference;
                    checkoutUpdates["bankInstructions"] = bankTransfer.Instructions;
                    updated = updated with { BankTransfer = bankTransfer };
                }

                if (status == "processing")
                {
                    checkoutUpdates["processingAt"] = Timestamp.FromDateTime(updatedAtUtc);
                    updated = updated with { ProcessingAtUtc = updatedAtUtc };
                }
                else if (status == "completed")
                {
                    var balanceBefore = ReadLong(balanceSnapshot!, "available");
                    var balanceAfterWholeRand = ledgerSnapshot!.Exists
                        ? balanceBefore
                        : checked(balanceBefore + checkout.Credits);
                    var balanceAfter = BalanceWithFractionalCents(
                        balanceSnapshot!,
                        balanceAfterWholeRand);
                    checkoutUpdates["completedAt"] = Timestamp.FromDateTime(updatedAtUtc);
                    checkoutUpdates["creditedBalance"] = balanceAfter;
                    updated = updated with
                    {
                        CompletedAtUtc = updatedAtUtc,
                        CreditedBalance = balanceAfter
                    };

                    if (!ledgerSnapshot.Exists)
                    {
                        transaction.Update(balanceReference, new Dictionary<string, object>
                        {
                            ["available"] = balanceAfterWholeRand,
                            ["version"] = FieldValue.Increment(1L),
                            ["updatedAt"] = Timestamp.FromDateTime(updatedAtUtc)
                        });
                        transaction.Create(ledgerReference, new Dictionary<string, object>
                        {
                            ["transactionId"] = $"payment-{checkoutId}",
                            ["userId"] = userId,
                            ["currencyId"] = SlotsCreditsCurrencyId,
                            ["amount"] = checkout.Credits,
                            ["balanceAfter"] = (double)balanceAfter,
                            ["type"] = "credit-purchase",
                            ["idempotencyKey"] = $"payment-settlement:{checkoutId}",
                            ["invoiceId"] = checkout.InvoiceId,
                            ["providerCheckoutId"] = providerCheckoutId,
                            ["createdAt"] = Timestamp.FromDateTime(updatedAtUtc)
                        });
                    }
                }

                transaction.Update(checkoutReference, checkoutUpdates);
                if (!providerKeySnapshot.Exists)
                {
                    transaction.Create(providerKeyReference, new Dictionary<string, object>
                    {
                        ["providerId"] = checkout.ProviderId,
                        ["providerCheckoutId"] = providerCheckoutId,
                        ["checkoutId"] = checkout.CheckoutId,
                        ["invoiceId"] = checkout.InvoiceId,
                        ["userId"] = checkout.UserId,
                        ["userReference"] = UserDocument(checkout.UserId),
                        ["createdAt"] = Timestamp.FromDateTime(updatedAtUtc)
                    });
                }

                return PaymentResult<StoredPaymentCheckout>.Success(updated);
            },
            cancellationToken: cancellationToken);
    }
}
