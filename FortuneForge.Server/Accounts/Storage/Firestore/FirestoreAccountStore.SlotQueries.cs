using System.Security.Cryptography;
using System.Text;
using FortuneForge.Server.Accounts.Models;
using FortuneForge.Server.Slots.Bonuses;
using FortuneForge.Server.Slots.Models;
using Google.Cloud.Firestore;

namespace FortuneForge.Server.Accounts.Storage;

public sealed partial class FirestoreAccountStore
{
    public async Task<SlotStateResponse> GetSlotStateAsync(
        string userId,
        string gameId,
        decimal pointValueInCents,
        CancellationToken cancellationToken)
    {
        var guardReference = SlotSpinGuardDocument(userId, gameId);
        var specialPointsCurrencyId = GameCurrencyId(SpecialPointsCurrencyId, gameId);
        var energyCurrencyId = GameCurrencyId(EnergyCurrencyId, gameId);
        var snapshots = await Task.WhenAll(
            guardReference.GetSnapshotAsync(cancellationToken),
            BalanceDocument(userId, specialPointsCurrencyId).GetSnapshotAsync(cancellationToken),
            BalanceDocument(userId, energyCurrencyId).GetSnapshotAsync(cancellationToken));
        var guardSnapshot = snapshots[0];
        return new SlotStateResponse(
            0,
            null,
            checked((int)ReadLong(snapshots[1], "available")),
            ReadLong(snapshots[2], "available"),
            string.Equals(gameId, LegacyWukongGameId, StringComparison.Ordinal)
                ? CreateSealCollections(guardSnapshot, pointValueInCents)
                : [],
            null);
    }

    public async Task<IReadOnlyList<SlotSpinHistoryItem>> GetSlotSpinHistoryAsync(
        string userId,
        int limit,
        CancellationToken cancellationToken)
    {
        var snapshots = await database
            .Collection("slotSpinResults")
            .WhereEqualTo("userId", userId)
            .GetSnapshotAsync(cancellationToken);

        return snapshots.Documents
            .Select(snapshot => new SlotSpinHistoryItem(
                snapshot.GetValue<string>("spinId"),
                snapshot.GetValue<string>("gameId"),
                ReadDecimal(snapshot, "wageredSlotsCredits"),
                ReadDecimal(snapshot, "wonSlotsCredits"),
                ReadDecimal(snapshot, "netSlotsCredits"),
                snapshot.GetValue<string>("result"),
                snapshot.GetValue<Timestamp>("createdAt").ToDateTime()))
            .OrderByDescending(spin => spin.CreatedAtUtc)
            .Take(Math.Clamp(limit, 1, 100))
            .ToArray();
    }
}
