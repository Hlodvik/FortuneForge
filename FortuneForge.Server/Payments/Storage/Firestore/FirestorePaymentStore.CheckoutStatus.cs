using System.Security.Cryptography;
using System.Text;
using FortuneForge.Server.Payments.Models;
using Google.Cloud.Firestore;

namespace FortuneForge.Server.Payments.Storage;

internal sealed partial class FirestorePaymentStore
{
    public Task<PaymentResult<StoredPaymentCheckout>> UpdateStatusAsync(
        string checkoutId,
        string userId,
        string status,
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

                if (string.Equals(checkout.Status, status, StringComparison.Ordinal))
                {
                    return PaymentResult<StoredPaymentCheckout>.Success(checkout);
                }

                if (!CanTransition(checkout.Status, status))
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
                    ["status"] = status,
                    ["statusUpdatedAt"] = Timestamp.FromDateTime(updatedAtUtc)
                };
                var updated = checkout with
                {
                    Status = status,
                    StatusUpdatedAtUtc = updatedAtUtc
                };

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
                            ["createdAt"] = Timestamp.FromDateTime(updatedAtUtc)
                        });
                    }
                }

                transaction.Update(checkoutReference, checkoutUpdates);
                return PaymentResult<StoredPaymentCheckout>.Success(updated);
            },
            cancellationToken: cancellationToken);
    }
}
