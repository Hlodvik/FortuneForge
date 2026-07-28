using System.Security.Cryptography;
using System.Text;
using FortuneForge.Server.Payments.Models;
using Google.Cloud.Firestore;

namespace FortuneForge.Server.Payments.Storage;

internal sealed partial class FirestorePaymentStore
{
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
}
