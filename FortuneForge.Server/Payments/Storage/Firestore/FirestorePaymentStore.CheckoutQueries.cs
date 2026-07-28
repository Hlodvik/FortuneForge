using System.Security.Cryptography;
using System.Text;
using FortuneForge.Server.Payments.Models;
using Google.Cloud.Firestore;

namespace FortuneForge.Server.Payments.Storage;

internal sealed partial class FirestorePaymentStore
{
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
}
