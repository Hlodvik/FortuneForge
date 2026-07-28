using System.Security.Cryptography;
using System.Text;
using FortuneForge.Server.Payments.Models;
using Google.Cloud.Firestore;

namespace FortuneForge.Server.Payments.Storage;

internal sealed class FirestorePaymentStore(FirestoreDb database) : IPaymentStore
{
    private const string SlotsCreditsCurrencyId = "slotsCredits";

    public Task<PaymentResult<StoredPaymentCheckout>> CreateAsync(
        StoredPaymentCheckout checkout,
        CancellationToken cancellationToken)
    {
        var checkoutReference = CheckoutDocument(checkout.CheckoutId);
        var idempotencyReference = IdempotencyDocument(checkout.UserId, checkout.IdempotencyKey);
        var invoiceReference = InvoiceKeyDocument(checkout.InvoiceId);
        var userReference = UserDocument(checkout.UserId);

        return database.RunTransactionAsync(
            async transaction =>
            {
                var initialSnapshots = await Task.WhenAll(
                    transaction.GetSnapshotAsync(userReference, cancellationToken),
                    transaction.GetSnapshotAsync(idempotencyReference, cancellationToken));
                var userSnapshot = initialSnapshots[0];
                var idempotencySnapshot = initialSnapshots[1];
                if (!userSnapshot.Exists)
                {
                    return PaymentResult<StoredPaymentCheckout>.Failure(PaymentError.AccountNotFound);
                }

                if (idempotencySnapshot.Exists)
                {
                    var existingCheckoutId = idempotencySnapshot.GetValue<string>("checkoutId");
                    var existingSnapshot = await transaction.GetSnapshotAsync(
                        CheckoutDocument(existingCheckoutId),
                        cancellationToken);
                    var existing = ToStored(existingSnapshot);
                    return existing is not null && Matches(existing, checkout)
                        ? PaymentResult<StoredPaymentCheckout>.Success(existing)
                        : PaymentResult<StoredPaymentCheckout>.Failure(PaymentError.IdempotencyConflict);
                }

                var invoiceSnapshot = await transaction.GetSnapshotAsync(
                    invoiceReference,
                    cancellationToken);
                if (invoiceSnapshot.Exists)
                {
                    return PaymentResult<StoredPaymentCheckout>.Failure(PaymentError.InvoiceConflict);
                }

                transaction.Create(checkoutReference, CheckoutData(checkout));
                transaction.Create(idempotencyReference, new Dictionary<string, object>
                {
                    ["userId"] = checkout.UserId,
                    ["userReference"] = userReference,
                    ["checkoutId"] = checkout.CheckoutId,
                    ["createdAt"] = Timestamp.FromDateTime(checkout.CreatedAtUtc)
                });
                transaction.Create(invoiceReference, new Dictionary<string, object>
                {
                    ["userId"] = checkout.UserId,
                    ["userReference"] = userReference,
                    ["checkoutId"] = checkout.CheckoutId,
                    ["invoiceId"] = checkout.InvoiceId,
                    ["createdAt"] = Timestamp.FromDateTime(checkout.CreatedAtUtc)
                });
                return PaymentResult<StoredPaymentCheckout>.Success(checkout);
            },
            cancellationToken: cancellationToken);
    }

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
                    var balanceAfter = ledgerSnapshot!.Exists
                        ? balanceBefore
                        : checked(balanceBefore + checkout.Credits);
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
                            ["available"] = balanceAfter,
                            ["version"] = FieldValue.Increment(1L),
                            ["updatedAt"] = Timestamp.FromDateTime(updatedAtUtc)
                        });
                        transaction.Create(ledgerReference, new Dictionary<string, object>
                        {
                            ["transactionId"] = $"payment-{checkoutId}",
                            ["userId"] = userId,
                            ["currencyId"] = SlotsCreditsCurrencyId,
                            ["amount"] = checkout.Credits,
                            ["balanceAfter"] = balanceAfter,
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

    public Task<PaymentCheckoutProviderSubmissionLease> TryBeginCheckoutProviderSubmissionAsync(
        string checkoutId,
        string userId,
        DateTime nowUtc,
        TimeSpan leaseDuration,
        CancellationToken cancellationToken)
    {
        var checkoutReference = CheckoutDocument(checkoutId);
        return database.RunTransactionAsync(
            async transaction =>
            {
                var snapshot = await transaction.GetSnapshotAsync(
                    checkoutReference,
                    cancellationToken);
                var checkout = ToStored(snapshot);
                if (checkout is null ||
                    !string.Equals(checkout.UserId, userId, StringComparison.Ordinal))
                {
                    return new PaymentCheckoutProviderSubmissionLease(
                        PaymentCheckoutProviderSubmissionLeaseState.NotFound,
                        null,
                        null);
                }

                if (!string.IsNullOrWhiteSpace(checkout.ProviderCheckoutId))
                {
                    return new PaymentCheckoutProviderSubmissionLease(
                        PaymentCheckoutProviderSubmissionLeaseState.AlreadyBound,
                        checkout,
                        null);
                }

                if (checkout.Status is "completed" or "failed" or "expired")
                {
                    return new PaymentCheckoutProviderSubmissionLease(
                        PaymentCheckoutProviderSubmissionLeaseState.Terminal,
                        checkout,
                        null);
                }

                if (checkout.NextProviderSubmissionAtUtc is { } nextRetryAtUtc &&
                    nextRetryAtUtc > nowUtc)
                {
                    return new PaymentCheckoutProviderSubmissionLease(
                        PaymentCheckoutProviderSubmissionLeaseState.NotDue,
                        checkout,
                        null);
                }

                if (checkout.ProviderSubmissionLeaseUntilUtc is { } leaseUntilUtc &&
                    leaseUntilUtc > nowUtc &&
                    !string.IsNullOrWhiteSpace(checkout.ProviderSubmissionLeaseId))
                {
                    return new PaymentCheckoutProviderSubmissionLease(
                        PaymentCheckoutProviderSubmissionLeaseState.NotDue,
                        checkout,
                        null);
                }

                var leaseId = Guid.NewGuid().ToString("N");
                var leaseUntil = nowUtc.Add(leaseDuration);
                var attempt = Math.Max(0, checkout.ProviderSubmissionAttempt) + 1;
                var updated = checkout with
                {
                    ProviderSubmissionStatus = "submitting",
                    ProviderSubmissionLeaseId = leaseId,
                    ProviderSubmissionLeaseUntilUtc = leaseUntil,
                    LastProviderSubmissionAtUtc = nowUtc,
                    ProviderSubmissionAttempt = attempt
                };
                transaction.Update(checkoutReference, new Dictionary<string, object>
                {
                    ["providerSubmissionStatus"] = "submitting",
                    ["providerSubmissionLeaseId"] = leaseId,
                    ["providerSubmissionLeaseUntil"] = Timestamp.FromDateTime(leaseUntil),
                    ["lastProviderSubmissionAt"] = Timestamp.FromDateTime(nowUtc),
                    ["providerSubmissionAttempt"] = attempt
                });

                return new PaymentCheckoutProviderSubmissionLease(
                    PaymentCheckoutProviderSubmissionLeaseState.Acquired,
                    updated,
                    leaseId);
            },
            cancellationToken: cancellationToken);
    }

    public Task<PaymentResult<StoredPaymentCheckout>> MarkCheckoutProviderSubmissionUncertainAsync(
        string checkoutId,
        string userId,
        string leaseId,
        DateTime updatedAtUtc,
        DateTime nextRetryAtUtc,
        int? providerStatusCode,
        CancellationToken cancellationToken)
    {
        var checkoutReference = CheckoutDocument(checkoutId);
        return database.RunTransactionAsync(
            async transaction =>
            {
                var snapshot = await transaction.GetSnapshotAsync(
                    checkoutReference,
                    cancellationToken);
                var checkout = ToStored(snapshot);
                if (checkout is null ||
                    !string.Equals(checkout.UserId, userId, StringComparison.Ordinal))
                {
                    return PaymentResult<StoredPaymentCheckout>.Failure(PaymentError.CheckoutNotFound);
                }

                if (!string.IsNullOrWhiteSpace(checkout.ProviderCheckoutId) ||
                    checkout.Status is "completed" or "failed" or "expired")
                {
                    return PaymentResult<StoredPaymentCheckout>.Success(checkout);
                }

                if (!string.Equals(
                    checkout.ProviderSubmissionLeaseId,
                    leaseId,
                    StringComparison.Ordinal))
                {
                    return PaymentResult<StoredPaymentCheckout>.Success(checkout);
                }

                var notice = "Payment invoice was submitted to the payment provider, but confirmation is pending. The same invoice will be retried automatically.";
                var updated = checkout with
                {
                    Status = "received",
                    StatusUpdatedAtUtc = updatedAtUtc,
                    ProviderSubmissionStatus = "uncertain",
                    ProviderSubmissionLeaseId = null,
                    ProviderSubmissionLeaseUntilUtc = null,
                    NextProviderSubmissionAtUtc = nextRetryAtUtc,
                    LastProviderSubmissionStatusCode = providerStatusCode,
                    Notice = notice
                };
                transaction.Update(checkoutReference, new Dictionary<string, object>
                {
                    ["status"] = "received",
                    ["statusUpdatedAt"] = Timestamp.FromDateTime(updatedAtUtc),
                    ["providerSubmissionStatus"] = "uncertain",
                    ["providerSubmissionLeaseId"] = FieldValue.Delete,
                    ["providerSubmissionLeaseUntil"] = FieldValue.Delete,
                    ["nextProviderSubmissionAt"] = Timestamp.FromDateTime(nextRetryAtUtc),
                    ["lastProviderSubmissionStatusCode"] = providerStatusCode ?? 0,
                    ["notice"] = notice
                });
                return PaymentResult<StoredPaymentCheckout>.Success(updated);
            },
            cancellationToken: cancellationToken);
    }

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
                    ["notice"] = "Withdrawal request failed before the payment provider accepted it. Reserved credits were returned."
                };
                var updated = withdrawal with
                {
                    Status = "failed",
                    StatusUpdatedAtUtc = updatedAtUtc,
                    Notice = "Withdrawal request failed before the payment provider accepted it. Reserved credits were returned."
                };
                if (!snapshots[2].Exists)
                {
                    var balanceAfter = checked(ReadLong(snapshots[1], "available") + withdrawal.CreditsDebited);
                    transaction.Update(balanceReference, new Dictionary<string, object>
                    {
                        ["available"] = balanceAfter,
                        ["version"] = FieldValue.Increment(1L),
                        ["updatedAt"] = Timestamp.FromDateTime(updatedAtUtc)
                    });
                    transaction.Create(refundLedgerReference, new Dictionary<string, object>
                    {
                        ["transactionId"] = $"withdrawal-refund-{withdrawalId}",
                        ["userId"] = userId,
                        ["currencyId"] = SlotsCreditsCurrencyId,
                        ["amount"] = withdrawal.CreditsDebited,
                        ["balanceAfter"] = balanceAfter,
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
                    "Withdrawal request was submitted to the payment provider, but confirmation is pending. Reserved credits remain held.";
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
                        var balanceAfter = checked(
                            ReadLong(balanceSnapshot, "available") + withdrawal.CreditsDebited);
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
                }

                transaction.Update(withdrawalReference, updates);
                return PaymentResult<StoredPaymentWithdrawal>.Success(updated);
            },
            cancellationToken: cancellationToken);
    }

    public async Task<StoredPaymentCheckout?> FindByCheckoutIdAsync(
        string checkoutId,
        string userId,
        CancellationToken cancellationToken)
    {
        var checkout = ToStored(
            await CheckoutDocument(checkoutId).GetSnapshotAsync(cancellationToken));
        checkout ??= await FindByProviderCheckoutIdForAdminAsync(
            string.Empty,
            checkoutId,
            cancellationToken);
        return checkout is not null && string.Equals(checkout.UserId, userId, StringComparison.Ordinal)
            ? checkout
            : null;
    }

    public async Task<StoredPaymentCheckout?> FindByCheckoutIdForAdminAsync(
        string checkoutId,
        CancellationToken cancellationToken)
    {
        var checkout = ToStored(await CheckoutDocument(checkoutId).GetSnapshotAsync(cancellationToken));
        return checkout ?? await FindByProviderCheckoutIdForAdminAsync(
            string.Empty,
            checkoutId,
            cancellationToken);
    }

    public async Task<StoredPaymentCheckout?> FindByProviderCheckoutIdForAdminAsync(
        string providerId,
        string providerCheckoutId,
        CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(providerCheckoutId))
        {
            return null;
        }

        if (string.IsNullOrWhiteSpace(providerId))
        {
            var snapshot = await database
                .Collection("paymentCheckoutProviderKeys")
                .WhereEqualTo("providerCheckoutId", providerCheckoutId)
                .Limit(1)
                .GetSnapshotAsync(cancellationToken);
            var key = snapshot.Documents.FirstOrDefault();
            return key is null
                ? null
                : ToStored(await CheckoutDocument(
                    key.GetValue<string>("checkoutId")).GetSnapshotAsync(cancellationToken));
        }

        var keySnapshot = await CheckoutProviderKeyDocument(
            providerId,
            providerCheckoutId).GetSnapshotAsync(cancellationToken);
        return !keySnapshot.Exists
            ? null
            : ToStored(await CheckoutDocument(
                keySnapshot.GetValue<string>("checkoutId")).GetSnapshotAsync(cancellationToken));
    }

    public async Task<StoredPaymentCheckout?> FindByInvoiceIdAsync(
        string invoiceId,
        string userId,
        CancellationToken cancellationToken)
    {
        var keySnapshot = await InvoiceKeyDocument(invoiceId).GetSnapshotAsync(cancellationToken);
        if (!keySnapshot.Exists ||
            !string.Equals(keySnapshot.GetValue<string>("userId"), userId, StringComparison.Ordinal))
        {
            return null;
        }

        return await FindByCheckoutIdAsync(
            keySnapshot.GetValue<string>("checkoutId"),
            userId,
            cancellationToken);
    }

    public async Task<StoredPaymentCheckout?> FindByInvoiceIdForAdminAsync(
        string invoiceId,
        CancellationToken cancellationToken)
    {
        var keySnapshot = await InvoiceKeyDocument(invoiceId).GetSnapshotAsync(cancellationToken);
        if (!keySnapshot.Exists)
        {
            return null;
        }

        return ToStored(await CheckoutDocument(
            keySnapshot.GetValue<string>("checkoutId")).GetSnapshotAsync(cancellationToken));
    }

    public async Task<IReadOnlyList<StoredPaymentCheckout>> ListAsync(
        string userId,
        int limit,
        CancellationToken cancellationToken)
    {
        var snapshot = await database
            .Collection("slotCreditPurchases")
            .WhereEqualTo("userId", userId)
            .GetSnapshotAsync(cancellationToken);
        return snapshot.Documents
            .Select(ToStored)
            .OfType<StoredPaymentCheckout>()
            .OrderByDescending(checkout => checkout.CreatedAtUtc)
            .Take(limit)
            .ToArray();
    }

    public async Task<IReadOnlyList<StoredPaymentCheckout>> ListPendingAsync(
        string providerId,
        int limit,
        CancellationToken cancellationToken)
    {
        var snapshot = await database
            .Collection("slotCreditPurchases")
            .WhereIn("status", new[] { "received", "processing" })
            .GetSnapshotAsync(cancellationToken);
        return snapshot.Documents
            .Select(ToStored)
            .OfType<StoredPaymentCheckout>()
            .Where(checkout => string.Equals(
                checkout.ProviderId,
                providerId,
                StringComparison.Ordinal))
            .OrderBy(checkout => checkout.StatusUpdatedAtUtc)
            .Take(limit)
            .ToArray();
    }

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
                    var balanceAfter = ledgerSnapshot!.Exists
                        ? balanceBefore
                        : checked(balanceBefore + checkout.Credits);
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
                            ["available"] = balanceAfter,
                            ["version"] = FieldValue.Increment(1L),
                            ["updatedAt"] = Timestamp.FromDateTime(updatedAtUtc)
                        });
                        transaction.Create(ledgerReference, new Dictionary<string, object>
                        {
                            ["transactionId"] = $"payment-{checkoutId}",
                            ["userId"] = userId,
                            ["currencyId"] = SlotsCreditsCurrencyId,
                            ["amount"] = checkout.Credits,
                            ["balanceAfter"] = balanceAfter,
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

    public Task<PaymentProviderEventProcessingLease> BeginProviderEventProcessingAsync(
        string providerId,
        string eventId,
        string eventType,
        DateTime occurredAtUtc,
        DateTime receivedAtUtc,
        CancellationToken cancellationToken)
    {
        var eventReference = ProviderEventDocument(providerId, eventId);
        return database.RunTransactionAsync(
            async transaction =>
            {
                var snapshot = await transaction.GetSnapshotAsync(
                    eventReference,
                    cancellationToken);
                if (snapshot.Exists)
                {
                    if (!string.Equals(
                        ReadString(snapshot, "eventType"),
                        eventType,
                        StringComparison.Ordinal))
                    {
                        return new PaymentProviderEventProcessingLease(
                            PaymentProviderEventProcessingState.Conflict,
                            IsRetry: true);
                    }

                    var status = ReadString(snapshot, "status", "applied");
                    if (string.Equals(status, "applied", StringComparison.Ordinal))
                    {
                        return new PaymentProviderEventProcessingLease(
                            PaymentProviderEventProcessingState.Applied,
                            IsRetry: true);
                    }

                    transaction.Update(eventReference, new Dictionary<string, object>
                    {
                        ["status"] = "processing",
                        ["receivedAt"] = Timestamp.FromDateTime(receivedAtUtc),
                        ["lastReceivedAt"] = Timestamp.FromDateTime(receivedAtUtc),
                        ["processingStartedAt"] = Timestamp.FromDateTime(receivedAtUtc),
                        ["attempts"] = FieldValue.Increment(1L)
                    });
                    return new PaymentProviderEventProcessingLease(
                        PaymentProviderEventProcessingState.Processing,
                        IsRetry: true);
                }

                transaction.Create(eventReference, new Dictionary<string, object>
                {
                    ["providerId"] = providerId,
                    ["eventId"] = eventId,
                    ["eventType"] = eventType,
                    ["status"] = "processing",
                    ["occurredAt"] = Timestamp.FromDateTime(occurredAtUtc),
                    ["firstReceivedAt"] = Timestamp.FromDateTime(receivedAtUtc),
                    ["receivedAt"] = Timestamp.FromDateTime(receivedAtUtc),
                    ["lastReceivedAt"] = Timestamp.FromDateTime(receivedAtUtc),
                    ["processingStartedAt"] = Timestamp.FromDateTime(receivedAtUtc),
                    ["attempts"] = 1L,
                    ["expiresAt"] = Timestamp.FromDateTime(receivedAtUtc.AddDays(90))
                });
                return new PaymentProviderEventProcessingLease(
                    PaymentProviderEventProcessingState.Processing,
                    IsRetry: false);
            },
            cancellationToken: cancellationToken);
    }

    public Task MarkProviderEventAppliedAsync(
        string providerId,
        string eventId,
        DateTime appliedAtUtc,
        CancellationToken cancellationToken)
    {
        var eventReference = ProviderEventDocument(providerId, eventId);
        return database.RunTransactionAsync(
            async transaction =>
            {
                var snapshot = await transaction.GetSnapshotAsync(
                    eventReference,
                    cancellationToken);
                if (!snapshot.Exists)
                {
                    return;
                }

                if (string.Equals(ReadString(snapshot, "status", "applied"), "applied", StringComparison.Ordinal))
                {
                    return;
                }

                transaction.Update(eventReference, new Dictionary<string, object>
                {
                    ["status"] = "applied",
                    ["appliedAt"] = Timestamp.FromDateTime(appliedAtUtc)
                });
            },
            cancellationToken: cancellationToken);
    }

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

    private static bool CanTransition(string current, string next) => current switch
    {
        "received" => next is "processing" or "completed" or "failed" or "expired",
        "processing" => next is "completed" or "failed" or "expired",
        _ => false
    };

    private static bool Matches(StoredPaymentCheckout existing, StoredPaymentCheckout proposed) =>
        string.Equals(existing.Market.Code, proposed.Market.Code, StringComparison.Ordinal) &&
        string.Equals(existing.Market.Currency, proposed.Market.Currency, StringComparison.Ordinal) &&
        string.Equals(existing.PaymentMethod.Id, proposed.PaymentMethod.Id, StringComparison.Ordinal) &&
        existing.Amount == proposed.Amount &&
        existing.Credits == proposed.Credits &&
        string.Equals(existing.Customer.Email, proposed.Customer.Email, StringComparison.OrdinalIgnoreCase) &&
        string.Equals(existing.Customer.FirstName, proposed.Customer.FirstName, StringComparison.Ordinal) &&
        string.Equals(existing.Customer.LastName, proposed.Customer.LastName, StringComparison.Ordinal) &&
        string.Equals(existing.PayerBank.AccountHolder, proposed.PayerBank.AccountHolder, StringComparison.Ordinal) &&
        string.Equals(existing.PayerBank.BankName, proposed.PayerBank.BankName, StringComparison.Ordinal) &&
        string.Equals(existing.PayerBank.AccountNumber, proposed.PayerBank.AccountNumber, StringComparison.Ordinal) &&
        string.Equals(existing.PayerBank.BranchCode, proposed.PayerBank.BranchCode, StringComparison.Ordinal) &&
        string.Equals(existing.PayerBank.AccountType, proposed.PayerBank.AccountType, StringComparison.Ordinal);

    private static bool Matches(StoredPaymentWithdrawal existing, StoredPaymentWithdrawal proposed) =>
        string.Equals(existing.Market.Code, proposed.Market.Code, StringComparison.Ordinal) &&
        string.Equals(existing.Market.Currency, proposed.Market.Currency, StringComparison.Ordinal) &&
        existing.Amount == proposed.Amount &&
        existing.CreditsDebited == proposed.CreditsDebited &&
        string.Equals(existing.Customer.Email, proposed.Customer.Email, StringComparison.OrdinalIgnoreCase) &&
        string.Equals(existing.Customer.FirstName, proposed.Customer.FirstName, StringComparison.Ordinal) &&
        string.Equals(existing.Customer.LastName, proposed.Customer.LastName, StringComparison.Ordinal) &&
        string.Equals(existing.Bank.AccountHolder, proposed.Bank.AccountHolder, StringComparison.Ordinal) &&
        string.Equals(existing.Bank.BankName, proposed.Bank.BankName, StringComparison.Ordinal) &&
        string.Equals(existing.Bank.AccountNumber, proposed.Bank.AccountNumber, StringComparison.Ordinal) &&
        string.Equals(existing.Bank.BranchCode, proposed.Bank.BranchCode, StringComparison.Ordinal) &&
        string.Equals(existing.Bank.AccountType, proposed.Bank.AccountType, StringComparison.Ordinal);

    private static string NormalizeWithdrawalStatus(string? status, string fallback) =>
        status?.Trim().ToLowerInvariant() switch
        {
            "pending" => "pending",
            "processing" => "processing",
            "completed" => "completed",
            "rejected" => "failed",
            "reversed" => "failed",
            "failed" => "failed",
            _ => fallback
        };

    private Dictionary<string, object> CheckoutData(StoredPaymentCheckout checkout)
    {
        var data = new Dictionary<string, object>
        {
            ["checkoutId"] = checkout.CheckoutId,
            ["providerCheckoutId"] = checkout.ProviderCheckoutId,
            ["providerPathwayKey"] = checkout.ProviderPathwayKey ?? string.Empty,
            ["invoiceId"] = checkout.InvoiceId,
            ["userId"] = checkout.UserId,
            ["userReference"] = database.Collection("users").Document(checkout.UserId),
            ["idempotencyKey"] = checkout.IdempotencyKey,
            ["providerId"] = checkout.ProviderId,
            ["isMock"] = checkout.IsMock,
            ["market"] = checkout.Market.Code,
            ["marketName"] = checkout.Market.DisplayName,
            ["currency"] = checkout.Market.Currency,
            ["locale"] = checkout.Market.Locale,
            ["paymentMethodId"] = checkout.PaymentMethod.Id,
            ["paymentMethodName"] = checkout.PaymentMethod.DisplayName,
            ["paymentMethodType"] = checkout.PaymentMethod.Type,
            ["amount"] = checkout.Amount,
            ["amountMinor"] = checkout.AmountMinor,
            ["credits"] = checkout.Credits,
            ["customerFirstName"] = checkout.Customer.FirstName,
            ["customerLastName"] = checkout.Customer.LastName,
            ["customerEmail"] = checkout.Customer.Email,
            ["customerReference"] = checkout.Customer.CustomerReference,
            ["beneficiaryReference"] = checkout.Customer.BeneficiaryReference,
            ["payerAccountHolder"] = checkout.PayerBank.AccountHolder,
            ["payerBankName"] = checkout.PayerBank.BankName,
            ["payerAccountNumber"] = checkout.PayerBank.AccountNumber,
            ["payerBranchCode"] = checkout.PayerBank.BranchCode,
            ["payerAccountType"] = checkout.PayerBank.AccountType,
            ["status"] = checkout.Status,
            ["statusUpdatedAt"] = Timestamp.FromDateTime(checkout.StatusUpdatedAtUtc),
            ["createdAt"] = Timestamp.FromDateTime(checkout.CreatedAtUtc),
            ["expiresAt"] = Timestamp.FromDateTime(checkout.ExpiresAtUtc),
            ["providerSubmissionStatus"] = checkout.ProviderSubmissionStatus,
            ["providerSubmissionAttempt"] = checkout.ProviderSubmissionAttempt,
            ["notice"] = checkout.Notice
        };
        if (!string.IsNullOrWhiteSpace(checkout.ProviderSubmissionLeaseId))
        {
            data["providerSubmissionLeaseId"] = checkout.ProviderSubmissionLeaseId;
        }

        if (checkout.ProviderSubmissionLeaseUntilUtc is { } leaseUntilUtc)
        {
            data["providerSubmissionLeaseUntil"] = Timestamp.FromDateTime(leaseUntilUtc);
        }

        if (checkout.NextProviderSubmissionAtUtc is { } nextSubmissionAtUtc)
        {
            data["nextProviderSubmissionAt"] = Timestamp.FromDateTime(nextSubmissionAtUtc);
        }

        if (checkout.LastProviderSubmissionAtUtc is { } lastSubmissionAtUtc)
        {
            data["lastProviderSubmissionAt"] = Timestamp.FromDateTime(lastSubmissionAtUtc);
        }

        if (checkout.LastProviderSubmissionStatusCode is { } statusCode)
        {
            data["lastProviderSubmissionStatusCode"] = statusCode;
        }

        if (checkout.BankTransfer is not null)
        {
            data["bankName"] = checkout.BankTransfer.BankName;
            data["bankAccountName"] = checkout.BankTransfer.AccountName;
            data["bankAccountNumber"] = checkout.BankTransfer.AccountNumber;
            data["bankBranchCode"] = checkout.BankTransfer.BranchCode;
            data["bankReference"] = checkout.BankTransfer.Reference;
            data["bankInstructions"] = checkout.BankTransfer.Instructions;
        }

        return data;
    }

    private Dictionary<string, object> WithdrawalData(StoredPaymentWithdrawal withdrawal) => new()
    {
        ["withdrawalId"] = withdrawal.WithdrawalId,
        ["providerWithdrawalId"] = withdrawal.ProviderWithdrawalId,
        ["providerPathwayKey"] = withdrawal.ProviderPathwayKey ?? string.Empty,
        ["userId"] = withdrawal.UserId,
        ["userReference"] = database.Collection("users").Document(withdrawal.UserId),
        ["idempotencyKey"] = withdrawal.IdempotencyKey,
        ["providerId"] = withdrawal.ProviderId,
        ["isMock"] = withdrawal.IsMock,
        ["market"] = withdrawal.Market.Code,
        ["marketName"] = withdrawal.Market.DisplayName,
        ["currency"] = withdrawal.Market.Currency,
        ["locale"] = withdrawal.Market.Locale,
        ["amount"] = withdrawal.Amount,
        ["amountMinor"] = withdrawal.AmountMinor,
        ["creditsDebited"] = withdrawal.CreditsDebited,
        ["status"] = withdrawal.Status,
        ["statusUpdatedAt"] = Timestamp.FromDateTime(withdrawal.StatusUpdatedAtUtc),
        ["createdAt"] = Timestamp.FromDateTime(withdrawal.CreatedAtUtc),
        ["customerFirstName"] = withdrawal.Customer.FirstName,
        ["customerLastName"] = withdrawal.Customer.LastName,
        ["customerEmail"] = withdrawal.Customer.Email,
        ["customerReference"] = withdrawal.Customer.CustomerReference,
        ["beneficiaryReference"] = withdrawal.Customer.BeneficiaryReference,
        ["accountHolder"] = withdrawal.Bank.AccountHolder,
        ["bankName"] = withdrawal.Bank.BankName,
        ["bankAccountNumber"] = withdrawal.Bank.AccountNumber,
        ["bankBranchCode"] = withdrawal.Bank.BranchCode,
        ["bankAccountType"] = withdrawal.Bank.AccountType,
        ["notice"] = withdrawal.Notice
    };

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

    private static string ReadString(DocumentSnapshot snapshot, string field) =>
        snapshot.TryGetValue<string>(field, out var value) ? value : string.Empty;

    private static string ReadString(DocumentSnapshot snapshot, string field, string fallback) =>
        snapshot.TryGetValue<string>(field, out var value) ? value : fallback;

    private static string HashKey(string value) => Convert.ToHexStringLower(
        SHA256.HashData(Encoding.UTF8.GetBytes(value)));
}
