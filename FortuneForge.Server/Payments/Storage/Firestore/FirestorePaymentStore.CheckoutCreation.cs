using System.Security.Cryptography;
using System.Text;
using FortuneForge.Server.Payments.Models;
using Google.Cloud.Firestore;

namespace FortuneForge.Server.Payments.Storage;

internal sealed partial class FirestorePaymentStore
{
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
}
