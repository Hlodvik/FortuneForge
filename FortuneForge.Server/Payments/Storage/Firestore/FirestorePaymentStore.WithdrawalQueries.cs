using FortuneForge.Server.Payments.Models;
using Google.Cloud.Firestore;

namespace FortuneForge.Server.Payments.Storage;

internal sealed partial class FirestorePaymentStore
{
    public async Task<IReadOnlyList<StoredPaymentWithdrawal>> ListWithdrawalsAsync(
        string userId,
        int limit,
        CancellationToken cancellationToken)
    {
        var snapshot = await database.Collection("slotCreditWithdrawals")
            .WhereEqualTo("userId", userId)
            .OrderByDescending("createdAt")
            .Limit(limit)
            .GetSnapshotAsync(cancellationToken);

        return snapshot.Documents
            .Select(ToStoredWithdrawal)
            .OfType<StoredPaymentWithdrawal>()
            .ToArray();
    }
}
