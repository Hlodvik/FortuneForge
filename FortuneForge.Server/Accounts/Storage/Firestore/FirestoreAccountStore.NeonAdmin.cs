using FortuneForge.Server.Accounts.Models;
using FortuneForge.Server.Slots.Bonuses;
using FortuneForge.Server.Slots.Configuration;
using FortuneForge.Server.Slots.Models;
using Google.Cloud.Firestore;

namespace FortuneForge.Server.Accounts.Storage;

public sealed partial class FirestoreAccountStore
{
    public Task<bool> GrantNeonAdminSpecialPlaysAsync(
        string userId,
        long wagerPoints,
        decimal pointValueInCents,
        bool arcadeRows,
        CancellationToken cancellationToken)
    {
        NeonArcadeSlotDefinitions.ValidateCarryWager(wagerPoints);
        var gameId = SlotSpecialRoundProfiles.NeonNightsGameId;
        var guardReference = SlotSpinGuardDocument(userId, gameId);
        var currencyId = GameCurrencyId(FreeGamesCurrencyId, gameId);
        var balanceReference = BalanceDocument(userId, currencyId);
        var receiptReference = BalanceTransactionDocument($"{Guid.NewGuid():N}-admin-neon-special-plays");
        var grantedAt = DateTime.UtcNow;

        return database.RunTransactionAsync(async transaction =>
        {
            var snapshots = await transaction.GetAllSnapshotsAsync(
                [guardReference, balanceReference], cancellationToken);
            var guard = snapshots[0];
            var balance = snapshots[1];
            if (ReadLong(balance, "available") != 0 ||
                !string.IsNullOrEmpty(ReadString(guard, "activeAdmissionId")))
                return false;

            var remaining = NeonArcadeSlotDefinitions.SpecialPlaySpins;
            if (balance.Exists)
                transaction.Update(balanceReference, new Dictionary<string, object>
                {
                    ["available"] = remaining,
                    ["version"] = FieldValue.Increment(1),
                    ["updatedAt"] = Timestamp.FromDateTime(grantedAt)
                });
            else
                transaction.Create(balanceReference,
                    BalanceData(userId, currencyId, remaining, grantedAt));

            transaction.Set(guardReference, new Dictionary<string, object>
            {
                ["userId"] = userId,
                ["freeSpinWagerPoints"] = wagerPoints,
                ["freeSpinWagerCents"] = RandMoney.PointsToCents(wagerPoints, pointValueInCents),
                ["freeSpinFeatureMode"] = arcadeRows ? NeonArcadeBonus.PaidArcadeFeatureMode : string.Empty,
                ["freeSpinMathModelId"] = string.Empty,
                ["updatedAt"] = Timestamp.FromDateTime(grantedAt)
            }, SetOptions.MergeAll);
            transaction.Create(receiptReference, BalanceTransactionData(
                receiptReference.Id, userId, currencyId, remaining, remaining,
                "admin-neon-special-plays", receiptReference.Id, grantedAt));
            return true;
        }, cancellationToken: cancellationToken);
    }
}
