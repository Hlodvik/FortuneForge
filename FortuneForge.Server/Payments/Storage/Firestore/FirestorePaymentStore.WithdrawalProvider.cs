using System.Security.Cryptography;
using System.Text;
using FortuneForge.Server.Payments.Models;
using Google.Cloud.Firestore;

namespace FortuneForge.Server.Payments.Storage;

internal sealed partial class FirestorePaymentStore
{
    public Task<PaymentResult<StoredPaymentWithdrawal>> UpdateWithdrawalProviderAsync(
        string withdrawalId,
        string userId,
        string providerWithdrawalId,
        string status,
        DateTime updatedAtUtc,
        CancellationToken cancellationToken)
    {
        var withdrawalReference = WithdrawalDocument(withdrawalId);
        return database.RunTransactionAsync(
            async transaction =>
            {
                var snapshot = await transaction.GetSnapshotAsync(withdrawalReference, cancellationToken);
                var withdrawal = ToStoredWithdrawal(snapshot);
                if (withdrawal is null ||
                    !string.Equals(withdrawal.UserId, userId, StringComparison.Ordinal))
                {
                    return PaymentResult<StoredPaymentWithdrawal>.Failure(PaymentError.CheckoutNotFound);
                }

                var normalizedStatus = WithdrawalStatusProjection.NormalizeProviderStatus(status);
                if (string.IsNullOrWhiteSpace(providerWithdrawalId) ||
                    normalizedStatus is null ||
                    !WithdrawalStatusProjection.CanApply(withdrawal.Status, normalizedStatus))
                {
                    return PaymentResult<StoredPaymentWithdrawal>.Failure(
                        PaymentError.InvalidStatusTransition);
                }

                var providerKeyReference = WithdrawalProviderKeyDocument(
                    withdrawal.ProviderId,
                    providerWithdrawalId);
                var providerKeySnapshot = await transaction.GetSnapshotAsync(
                    providerKeyReference,
                    cancellationToken);
                if (providerKeySnapshot.Exists &&
                    (!string.Equals(
                        providerKeySnapshot.GetValue<string>("withdrawalId"),
                        withdrawal.WithdrawalId,
                        StringComparison.Ordinal) ||
                    !string.Equals(
                        providerKeySnapshot.GetValue<string>("userId"),
                        withdrawal.UserId,
                        StringComparison.Ordinal)))
                {
                    return PaymentResult<StoredPaymentWithdrawal>.Failure(
                        PaymentError.InvalidStatusTransition);
                }

                DocumentSnapshot? balanceSnapshot = null;
                DocumentSnapshot? refundLedgerSnapshot = null;
                var refundLedgerReference = WithdrawalRefundLedgerDocument(withdrawal.WithdrawalId);
                var balanceReference = BalanceDocument(withdrawal.UserId);
                if (WithdrawalStatusProjection.IsNegativeTerminal(normalizedStatus))
                {
                    var refundSnapshots = await Task.WhenAll(
                        transaction.GetSnapshotAsync(balanceReference, cancellationToken),
                        transaction.GetSnapshotAsync(refundLedgerReference, cancellationToken));
                    balanceSnapshot = refundSnapshots[0];
                    refundLedgerSnapshot = refundSnapshots[1];
                    if (!balanceSnapshot.Exists)
                    {
                        return PaymentResult<StoredPaymentWithdrawal>.Failure(
                            PaymentError.AccountBalanceNotFound);
                    }
                }

                var updates = new Dictionary<string, object>
                {
                    ["providerWithdrawalId"] = providerWithdrawalId,
                    ["status"] = normalizedStatus,
                    ["statusUpdatedAt"] = Timestamp.FromDateTime(updatedAtUtc),
                    ["notice"] = WithdrawalStatusProjection.NoticeFor(normalizedStatus)
                };
                var updated = withdrawal with
                {
                    ProviderWithdrawalId = providerWithdrawalId,
                    Status = normalizedStatus,
                    StatusUpdatedAtUtc = updatedAtUtc,
                    Notice = WithdrawalStatusProjection.NoticeFor(normalizedStatus)
                };
                if (normalizedStatus == "completed")
                {
                    updates["completedAt"] = Timestamp.FromDateTime(updatedAtUtc);
                    updated = updated with { CompletedAtUtc = updatedAtUtc };
                }
                else if (WithdrawalStatusProjection.IsNegativeTerminal(normalizedStatus) &&
                    refundLedgerSnapshot is not null &&
                    !refundLedgerSnapshot.Exists)
                {
                    var balanceAfter = checked(
                        ReadLong(balanceSnapshot!, "available") + withdrawal.CreditsDebited);
                    transaction.Update(balanceReference, new Dictionary<string, object>
                    {
                        ["available"] = balanceAfter,
                        ["version"] = FieldValue.Increment(1L),
                        ["updatedAt"] = Timestamp.FromDateTime(updatedAtUtc)
                    });
                    transaction.Create(refundLedgerReference, new Dictionary<string, object>
                    {
                        ["transactionId"] = $"withdrawal-refund-{withdrawal.WithdrawalId}",
                        ["userId"] = withdrawal.UserId,
                        ["currencyId"] = SlotsCreditsCurrencyId,
                        ["amount"] = withdrawal.CreditsDebited,
                        ["balanceAfter"] = balanceAfter,
                        ["type"] = "withdrawal-reservation-refund",
                        ["idempotencyKey"] = $"withdrawal-refund:{withdrawal.WithdrawalId}",
                        ["withdrawalId"] = withdrawal.WithdrawalId,
                        ["providerWithdrawalId"] = providerWithdrawalId,
                        ["createdAt"] = Timestamp.FromDateTime(updatedAtUtc)
                    });
                }

                transaction.Update(withdrawalReference, updates);
                if (!providerKeySnapshot.Exists)
                {
                    transaction.Create(providerKeyReference, new Dictionary<string, object>
                    {
                        ["providerId"] = withdrawal.ProviderId,
                        ["providerWithdrawalId"] = providerWithdrawalId,
                        ["withdrawalId"] = withdrawal.WithdrawalId,
                        ["userId"] = withdrawal.UserId,
                        ["userReference"] = UserDocument(withdrawal.UserId),
                        ["createdAt"] = Timestamp.FromDateTime(updatedAtUtc)
                    });
                }

                return PaymentResult<StoredPaymentWithdrawal>.Success(updated);
            },
            cancellationToken: cancellationToken);
    }
}
