using System.Security.Cryptography;
using System.Text;
using FortuneForge.Server.Payments.Models;
using Google.Cloud.Firestore;

namespace FortuneForge.Server.Payments.Storage;

internal sealed partial class FirestorePaymentStore
{
    public Task<PaymentResult<StoredPaymentWithdrawal>> ProjectWithdrawalProviderStatusAsync(
        string providerId,
        string providerWithdrawalId,
        string status,
        DateTime updatedAtUtc,
        CancellationToken cancellationToken)
    {
        var normalizedStatus = WithdrawalStatusProjection.NormalizeProviderStatus(status);
        if (string.IsNullOrWhiteSpace(providerId) ||
            string.IsNullOrWhiteSpace(providerWithdrawalId) ||
            normalizedStatus is null)
        {
            return Task.FromResult(
                PaymentResult<StoredPaymentWithdrawal>.Failure(
                    PaymentError.InvalidStatusTransition));
        }

        var providerKeyReference = WithdrawalProviderKeyDocument(providerId, providerWithdrawalId);
        return database.RunTransactionAsync(
            async transaction =>
            {
                var providerKeySnapshot = await transaction.GetSnapshotAsync(
                    providerKeyReference,
                    cancellationToken);
                if (!providerKeySnapshot.Exists)
                {
                    return PaymentResult<StoredPaymentWithdrawal>.Failure(PaymentError.CheckoutNotFound);
                }

                var withdrawalId = providerKeySnapshot.GetValue<string>("withdrawalId");
                var userId = providerKeySnapshot.GetValue<string>("userId");
                var withdrawalReference = WithdrawalDocument(withdrawalId);
                var withdrawalSnapshot = await transaction.GetSnapshotAsync(
                    withdrawalReference,
                    cancellationToken);
                var withdrawal = ToStoredWithdrawal(withdrawalSnapshot);
                if (withdrawal is null ||
                    !string.Equals(withdrawal.UserId, userId, StringComparison.Ordinal) ||
                    !string.Equals(withdrawal.ProviderId, providerId, StringComparison.Ordinal) ||
                    !string.Equals(
                        withdrawal.ProviderWithdrawalId,
                        providerWithdrawalId,
                        StringComparison.OrdinalIgnoreCase))
                {
                    return PaymentResult<StoredPaymentWithdrawal>.Failure(
                        PaymentError.InvalidStatusTransition);
                }

                var isSameStatus = string.Equals(
                    withdrawal.Status,
                    normalizedStatus,
                    StringComparison.Ordinal);
                if (isSameStatus &&
                    !WithdrawalStatusProjection.IsNegativeTerminal(normalizedStatus))
                {
                    return PaymentResult<StoredPaymentWithdrawal>.Success(withdrawal);
                }

                if (!isSameStatus &&
                    !WithdrawalStatusProjection.CanApply(withdrawal.Status, normalizedStatus))
                {
                    return PaymentResult<StoredPaymentWithdrawal>.Failure(
                        PaymentError.InvalidStatusTransition);
                }

                var updates = new Dictionary<string, object>
                {
                    ["status"] = normalizedStatus,
                    ["statusUpdatedAt"] = Timestamp.FromDateTime(updatedAtUtc),
                    ["notice"] = WithdrawalStatusProjection.NoticeFor(normalizedStatus)
                };
                var updated = withdrawal with
                {
                    Status = normalizedStatus,
                    StatusUpdatedAtUtc = updatedAtUtc,
                    Notice = WithdrawalStatusProjection.NoticeFor(normalizedStatus)
                };

                if (normalizedStatus == "completed")
                {
                    updates["completedAt"] = Timestamp.FromDateTime(updatedAtUtc);
                    updated = updated with { CompletedAtUtc = updatedAtUtc };
                }
                else if (WithdrawalStatusProjection.IsNegativeTerminal(normalizedStatus))
                {
                    var balanceReference = BalanceDocument(withdrawal.UserId);
                    var refundLedgerReference = WithdrawalRefundLedgerDocument(withdrawal.WithdrawalId);
                    var snapshots = await Task.WhenAll(
                        transaction.GetSnapshotAsync(balanceReference, cancellationToken),
                        transaction.GetSnapshotAsync(refundLedgerReference, cancellationToken));
                    var balanceSnapshot = snapshots[0];
                    var refundLedgerSnapshot = snapshots[1];
                    if (!balanceSnapshot.Exists)
                    {
                        return PaymentResult<StoredPaymentWithdrawal>.Failure(
                            PaymentError.AccountBalanceNotFound);
                    }

                    if (!refundLedgerSnapshot.Exists)
                    {
                        var balanceAfterWholeRand = checked(
                            ReadLong(balanceSnapshot, "available") + withdrawal.CreditsDebited);
                        var balanceAfter = BalanceWithFractionalCents(
                            balanceSnapshot,
                            balanceAfterWholeRand);
                        transaction.Update(balanceReference, new Dictionary<string, object>
                        {
                            ["available"] = balanceAfterWholeRand,
                            ["version"] = FieldValue.Increment(1L),
                            ["updatedAt"] = Timestamp.FromDateTime(updatedAtUtc)
                        });
                        transaction.Create(refundLedgerReference, new Dictionary<string, object>
                        {
                            ["transactionId"] = $"withdrawal-refund-{withdrawal.WithdrawalId}",
                            ["userId"] = withdrawal.UserId,
                            ["currencyId"] = SlotsCreditsCurrencyId,
                            ["amount"] = withdrawal.CreditsDebited,
                            ["balanceAfter"] = (double)balanceAfter,
                            ["type"] = "withdrawal-reservation-refund",
                            ["idempotencyKey"] = $"withdrawal-refund:{withdrawal.WithdrawalId}",
                            ["withdrawalId"] = withdrawal.WithdrawalId,
                            ["providerWithdrawalId"] = providerWithdrawalId,
                            ["createdAt"] = Timestamp.FromDateTime(updatedAtUtc)
                        });
                    }
                }

                transaction.Update(withdrawalReference, updates);
                return PaymentResult<StoredPaymentWithdrawal>.Success(updated);
            },
            cancellationToken: cancellationToken);
    }
}
