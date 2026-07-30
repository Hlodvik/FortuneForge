using System.Security.Cryptography;
using System.Text;
using FortuneForge.Server.Payments.Models;
using Google.Cloud.Firestore;

namespace FortuneForge.Server.Payments.Storage;

internal sealed partial class FirestorePaymentStore
{
    public Task<PaymentResult<StoredPaymentWithdrawal>> FailWithdrawalReservationAsync(
        string withdrawalId,
        string userId,
        DateTime updatedAtUtc,
        CancellationToken cancellationToken)
    {
        var withdrawalReference = WithdrawalDocument(withdrawalId);
        var balanceReference = BalanceDocument(userId);
        var refundLedgerReference = WithdrawalRefundLedgerDocument(withdrawalId);

        return database.RunTransactionAsync(
            async transaction =>
            {
                var snapshots = await Task.WhenAll(
                    transaction.GetSnapshotAsync(withdrawalReference, cancellationToken),
                    transaction.GetSnapshotAsync(balanceReference, cancellationToken),
                    transaction.GetSnapshotAsync(refundLedgerReference, cancellationToken));
                var withdrawal = ToStoredWithdrawal(snapshots[0]);
                if (withdrawal is null ||
                    !string.Equals(withdrawal.UserId, userId, StringComparison.Ordinal))
                {
                    return PaymentResult<StoredPaymentWithdrawal>.Failure(PaymentError.CheckoutNotFound);
                }

                if (!snapshots[1].Exists)
                {
                    return PaymentResult<StoredPaymentWithdrawal>.Failure(
                        PaymentError.AccountBalanceNotFound);
                }

                var updates = new Dictionary<string, object>
                {
                    ["status"] = "failed",
                    ["statusUpdatedAt"] = Timestamp.FromDateTime(updatedAtUtc),
                    ["notice"] = "Withdrawal request failed before the payment provider accepted it. Reserved Rand was returned."
                };
                var updated = withdrawal with
                {
                    Status = "failed",
                    StatusUpdatedAtUtc = updatedAtUtc,
                    Notice = "Withdrawal request failed before the payment provider accepted it. Reserved Rand was returned."
                };
                if (!snapshots[2].Exists)
                {
                    var balanceAfterWholeRand = checked(
                        ReadLong(snapshots[1], "available") + withdrawal.CreditsDebited);
                    var balanceAfter = BalanceWithFractionalCents(
                        snapshots[1],
                        balanceAfterWholeRand);
                    transaction.Update(balanceReference, new Dictionary<string, object>
                    {
                        ["available"] = balanceAfterWholeRand,
                        ["version"] = FieldValue.Increment(1L),
                        ["updatedAt"] = Timestamp.FromDateTime(updatedAtUtc)
                    });
                    transaction.Create(refundLedgerReference, new Dictionary<string, object>
                    {
                        ["transactionId"] = $"withdrawal-refund-{withdrawalId}",
                        ["userId"] = userId,
                        ["currencyId"] = SlotsCreditsCurrencyId,
                        ["amount"] = withdrawal.CreditsDebited,
                        ["balanceAfter"] = (double)balanceAfter,
                        ["type"] = "withdrawal-reservation-refund",
                        ["idempotencyKey"] = $"withdrawal-refund:{withdrawalId}",
                        ["withdrawalId"] = withdrawalId,
                        ["createdAt"] = Timestamp.FromDateTime(updatedAtUtc)
                    });
                }

                transaction.Update(withdrawalReference, updates);
                return PaymentResult<StoredPaymentWithdrawal>.Success(updated);
            },
            cancellationToken: cancellationToken);
    }

    public Task<PaymentResult<StoredPaymentWithdrawal>> MarkWithdrawalProviderSubmissionUncertainAsync(
        string withdrawalId,
        string userId,
        DateTime updatedAtUtc,
        CancellationToken cancellationToken)
    {
        var withdrawalReference = WithdrawalDocument(withdrawalId);
        return database.RunTransactionAsync(
            async transaction =>
            {
                var snapshot = await transaction.GetSnapshotAsync(
                    withdrawalReference,
                    cancellationToken);
                var withdrawal = ToStoredWithdrawal(snapshot);
                if (withdrawal is null ||
                    !string.Equals(withdrawal.UserId, userId, StringComparison.Ordinal))
                {
                    return PaymentResult<StoredPaymentWithdrawal>.Failure(PaymentError.CheckoutNotFound);
                }

                if (!string.IsNullOrWhiteSpace(withdrawal.ProviderWithdrawalId) ||
                    WithdrawalStatusProjection.IsTerminal(withdrawal.Status))
                {
                    return PaymentResult<StoredPaymentWithdrawal>.Success(withdrawal);
                }

                const string notice =
                    "Withdrawal request was submitted to the payment provider, but confirmation is pending. Reserved Rand remains held.";
                var updated = withdrawal with
                {
                    Status = "pending",
                    StatusUpdatedAtUtc = updatedAtUtc,
                    Notice = notice
                };

                transaction.Update(withdrawalReference, new Dictionary<string, object>
                {
                    ["status"] = updated.Status,
                    ["statusUpdatedAt"] = Timestamp.FromDateTime(updatedAtUtc),
                    ["notice"] = notice
                });
                return PaymentResult<StoredPaymentWithdrawal>.Success(updated);
            },
            cancellationToken: cancellationToken);
    }
}
