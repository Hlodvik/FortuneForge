using System.Security.Cryptography;
using System.Text;
using FortuneForge.Server.Payments.Models;
using Google.Cloud.Firestore;

namespace FortuneForge.Server.Payments.Storage;

internal sealed partial class FirestorePaymentStore
{
    public Task<PaymentResult<StoredPaymentWithdrawal>> CreateWithdrawalReservationAsync(
        StoredPaymentWithdrawal withdrawal,
        CancellationToken cancellationToken)
    {
        var withdrawalReference = WithdrawalDocument(withdrawal.WithdrawalId);
        var idempotencyReference = WithdrawalIdempotencyDocument(
            withdrawal.UserId,
            withdrawal.IdempotencyKey);
        var userReference = UserDocument(withdrawal.UserId);
        var balanceReference = BalanceDocument(withdrawal.UserId);
        var ledgerReference = WithdrawalLedgerDocument(withdrawal.WithdrawalId);

        return database.RunTransactionAsync(
            async transaction =>
            {
                var initialSnapshots = await Task.WhenAll(
                    transaction.GetSnapshotAsync(userReference, cancellationToken),
                    transaction.GetSnapshotAsync(balanceReference, cancellationToken),
                    transaction.GetSnapshotAsync(idempotencyReference, cancellationToken));
                var userSnapshot = initialSnapshots[0];
                var balanceSnapshot = initialSnapshots[1];
                var idempotencySnapshot = initialSnapshots[2];
                if (!userSnapshot.Exists)
                {
                    return PaymentResult<StoredPaymentWithdrawal>.Failure(PaymentError.AccountNotFound);
                }

                if (!balanceSnapshot.Exists)
                {
                    return PaymentResult<StoredPaymentWithdrawal>.Failure(
                        PaymentError.AccountBalanceNotFound);
                }

                if (idempotencySnapshot.Exists)
                {
                    var existingWithdrawalId = idempotencySnapshot.GetValue<string>("withdrawalId");
                    var existingSnapshot = await transaction.GetSnapshotAsync(
                        WithdrawalDocument(existingWithdrawalId),
                        cancellationToken);
                    var existing = ToStoredWithdrawal(existingSnapshot);
                    return existing is not null && Matches(existing, withdrawal)
                        ? PaymentResult<StoredPaymentWithdrawal>.Success(existing)
                        : PaymentResult<StoredPaymentWithdrawal>.Failure(PaymentError.IdempotencyConflict);
                }

                var withdrawalSnapshot = await transaction.GetSnapshotAsync(
                    withdrawalReference,
                    cancellationToken);
                if (withdrawalSnapshot.Exists)
                {
                    return PaymentResult<StoredPaymentWithdrawal>.Failure(PaymentError.InvoiceConflict);
                }

                var availableBefore = ReadLong(balanceSnapshot, "available");
                if (availableBefore < withdrawal.CreditsDebited)
                {
                    return PaymentResult<StoredPaymentWithdrawal>.Failure(PaymentError.InsufficientCredits);
                }

                var availableAfter = checked(availableBefore - withdrawal.CreditsDebited);
                transaction.Create(withdrawalReference, WithdrawalData(withdrawal));
                transaction.Create(idempotencyReference, new Dictionary<string, object>
                {
                    ["userId"] = withdrawal.UserId,
                    ["userReference"] = userReference,
                    ["withdrawalId"] = withdrawal.WithdrawalId,
                    ["createdAt"] = Timestamp.FromDateTime(withdrawal.CreatedAtUtc)
                });
                transaction.Update(balanceReference, new Dictionary<string, object>
                {
                    ["available"] = availableAfter,
                    ["version"] = FieldValue.Increment(1L),
                    ["updatedAt"] = Timestamp.FromDateTime(withdrawal.CreatedAtUtc)
                });
                transaction.Create(ledgerReference, new Dictionary<string, object>
                {
                    ["transactionId"] = $"withdrawal-{withdrawal.WithdrawalId}",
                    ["userId"] = withdrawal.UserId,
                    ["currencyId"] = SlotsCreditsCurrencyId,
                    ["amount"] = -withdrawal.CreditsDebited,
                    ["balanceAfter"] = availableAfter,
                    ["type"] = "withdrawal-reservation",
                    ["idempotencyKey"] = $"withdrawal-reservation:{withdrawal.WithdrawalId}",
                    ["withdrawalId"] = withdrawal.WithdrawalId,
                    ["createdAt"] = Timestamp.FromDateTime(withdrawal.CreatedAtUtc)
                });

                return PaymentResult<StoredPaymentWithdrawal>.Success(withdrawal);
            },
            cancellationToken: cancellationToken);
    }
}
