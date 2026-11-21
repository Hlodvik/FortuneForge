using FortuneForge.Server.Accounts.Models;
using FortuneForge.Games.NeonNights.Server;
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
        bool discoBall,
        CancellationToken cancellationToken)
    {
        if (arcadeRows && discoBall)
            throw new ArgumentException("A Neon preview can start one feature at a time.");
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
            if (ReadString(guard, NeonCarRespinStore.WorkerOwnerField) is not null ||
                ReadLong(balance, "available") != 0 ||
                NeonDiscoRoundState.ReadSpinsRemaining(field => ReadNeonDiscoField(guard, field)) != 0 ||
                ReadPendingNeonNitroChoice(guard) is not null ||
                !string.IsNullOrEmpty(ReadString(guard, "activeAdmissionId")))
                return false;

            if (discoBall)
            {
                var guardData = new Dictionary<string, object>
                {
                    ["userId"] = userId,
                    ["updatedAt"] = Timestamp.FromDateTime(grantedAt)
                };
                NeonDiscoRoundState.Grant(wagerPoints).WriteGuard(guardData);
                transaction.Set(guardReference, guardData, SetOptions.MergeAll);
                return true;
            }

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

            var featureMode = arcadeRows ? NeonArcadeBonus.PaidCarFeatureMode : string.Empty;
            var grantGuardData = new Dictionary<string, object>
            {
                ["userId"] = userId,
                ["freeSpinWagerPoints"] = wagerPoints,
                ["freeSpinWagerCents"] = RandMoney.PointsToCents(wagerPoints, pointValueInCents),
                ["freeSpinFeatureMode"] = featureMode,
                ["freeSpinMathModelId"] = string.Empty,
                ["updatedAt"] = Timestamp.FromDateTime(grantedAt)
            };
            NeonCarRoundState.Start(receiptReference.Id, featureMode).WriteGuard(grantGuardData);
            transaction.Set(guardReference, grantGuardData, SetOptions.MergeAll);
            transaction.Create(receiptReference, BalanceTransactionData(
                receiptReference.Id, userId, currencyId, remaining, remaining,
                "admin-neon-special-plays", receiptReference.Id, grantedAt));
            return true;
        }, cancellationToken: cancellationToken);
    }
}
