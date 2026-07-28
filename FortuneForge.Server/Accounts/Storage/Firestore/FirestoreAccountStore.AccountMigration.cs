using System.Security.Cryptography;
using System.Text;
using FortuneForge.Server.Accounts.Models;
using FortuneForge.Server.Slots.Bonuses;
using FortuneForge.Server.Slots.Models;
using Google.Cloud.Firestore;

namespace FortuneForge.Server.Accounts.Storage;

public sealed partial class FirestoreAccountStore
{
    private Task<StoredAccount?> EnsureAccountSchemaAsync(
        string userId,
        CancellationToken cancellationToken)
    {
        var userReference = UserDocument(userId);
        var legacyLoadedMoneyReference = BalanceDocument(userId, LegacyLoadedMoneyCurrencyId);
        var slotsCreditsReference = BalanceDocument(userId, SlotsCreditsCurrencyId);
        var freeGamesReference = BalanceDocument(userId, FreeGamesCurrencyId);
        var specialPointsReference = BalanceDocument(userId, SpecialPointsCurrencyId);
        var energyReference = BalanceDocument(userId, EnergyCurrencyId);
        var scopedWukongFreeGamesCurrencyId = GameCurrencyId(FreeGamesCurrencyId, LegacyWukongGameId);
        var scopedWukongSpecialPointsCurrencyId = GameCurrencyId(SpecialPointsCurrencyId, LegacyWukongGameId);
        var scopedWukongEnergyCurrencyId = GameCurrencyId(EnergyCurrencyId, LegacyWukongGameId);
        var scopedWukongFreeGamesReference = BalanceDocument(userId, scopedWukongFreeGamesCurrencyId);
        var scopedWukongSpecialPointsReference = BalanceDocument(userId, scopedWukongSpecialPointsCurrencyId);
        var scopedWukongEnergyReference = BalanceDocument(userId, scopedWukongEnergyCurrencyId);
        var statisticsReference = StatisticsDocument(userId);
        var migrationTransactionReference = BalanceTransactionDocument(
            $"{userId}-wallet-v1-migration");

        return database.RunTransactionAsync(
            async transaction =>
            {
                var userSnapshot = await transaction.GetSnapshotAsync(
                    userReference,
                    cancellationToken);
                if (!userSnapshot.Exists)
                {
                    return null;
                }

                var legacyLoadedMoneySnapshot = await transaction.GetSnapshotAsync(
                    legacyLoadedMoneyReference,
                    cancellationToken);
                var slotsCreditsSnapshot = await transaction.GetSnapshotAsync(
                    slotsCreditsReference,
                    cancellationToken);
                var freeGamesSnapshot = await transaction.GetSnapshotAsync(
                    freeGamesReference,
                    cancellationToken);
                var specialPointsSnapshot = await transaction.GetSnapshotAsync(
                    specialPointsReference,
                    cancellationToken);
                var energySnapshot = await transaction.GetSnapshotAsync(
                    energyReference,
                    cancellationToken);
                var scopedWukongFreeGamesSnapshot = await transaction.GetSnapshotAsync(
                    scopedWukongFreeGamesReference,
                    cancellationToken);
                var scopedWukongSpecialPointsSnapshot = await transaction.GetSnapshotAsync(
                    scopedWukongSpecialPointsReference,
                    cancellationToken);
                var scopedWukongEnergySnapshot = await transaction.GetSnapshotAsync(
                    scopedWukongEnergyReference,
                    cancellationToken);
                var statisticsSnapshot = await transaction.GetSnapshotAsync(
                    statisticsReference,
                    cancellationToken);
                var migrationTransactionSnapshot = await transaction.GetSnapshotAsync(
                    migrationTransactionReference,
                    cancellationToken);

                var migratedAtUtc = DateTime.UtcNow;
                var accountSchemaVersion = ReadLong(userSnapshot, "accountSchemaVersion");
                var needsAccountMigration =
                    accountSchemaVersion < AccountSchemaVersion ||
                    !userSnapshot.TryGetValue<bool>("deactivated", out _) ||
                    legacyLoadedMoneySnapshot.Exists ||
                    !slotsCreditsSnapshot.Exists ||
                    !freeGamesSnapshot.Exists ||
                    !specialPointsSnapshot.Exists ||
                    !energySnapshot.Exists ||
                    !scopedWukongFreeGamesSnapshot.Exists ||
                    !scopedWukongSpecialPointsSnapshot.Exists ||
                    !scopedWukongEnergySnapshot.Exists;
                var slotsCredits = slotsCreditsSnapshot.Exists
                    ? ReadLong(slotsCreditsSnapshot, "available")
                    : ReadLong(userSnapshot, "balancePoints", LegacySlotsCreditsFallback);
                var freeGames = freeGamesSnapshot.Exists
                    ? ReadLong(freeGamesSnapshot, "available")
                    : 0;
                var specialPoints = specialPointsSnapshot.Exists
                    ? ReadLong(specialPointsSnapshot, "available")
                    : 0;
                var energy = energySnapshot.Exists
                    ? ReadLong(energySnapshot, "available")
                    : 0;

                if (legacyLoadedMoneySnapshot.Exists)
                {
                    transaction.Delete(legacyLoadedMoneyReference);
                }

                if (!slotsCreditsSnapshot.Exists)
                {
                    transaction.Create(
                        slotsCreditsReference,
                        BalanceData(userId, SlotsCreditsCurrencyId, slotsCredits, migratedAtUtc));
                }

                if (!freeGamesSnapshot.Exists)
                {
                    transaction.Create(
                        freeGamesReference,
                        BalanceData(userId, FreeGamesCurrencyId, freeGames, migratedAtUtc));
                }

                if (!specialPointsSnapshot.Exists)
                {
                    transaction.Create(
                        specialPointsReference,
                        BalanceData(userId, SpecialPointsCurrencyId, 0, migratedAtUtc));
                }

                if (!energySnapshot.Exists)
                {
                    transaction.Create(
                        energyReference,
                        BalanceData(userId, EnergyCurrencyId, 0, migratedAtUtc));
                }

                if (!scopedWukongFreeGamesSnapshot.Exists)
                {
                    transaction.Create(
                        scopedWukongFreeGamesReference,
                        BalanceData(
                            userId,
                            scopedWukongFreeGamesCurrencyId,
                            freeGames,
                            migratedAtUtc));
                }

                if (!scopedWukongSpecialPointsSnapshot.Exists)
                {
                    transaction.Create(
                        scopedWukongSpecialPointsReference,
                        BalanceData(
                            userId,
                            scopedWukongSpecialPointsCurrencyId,
                            specialPoints,
                            migratedAtUtc));
                }

                if (!scopedWukongEnergySnapshot.Exists)
                {
                    transaction.Create(
                        scopedWukongEnergyReference,
                        BalanceData(
                            userId,
                            scopedWukongEnergyCurrencyId,
                            energy,
                            migratedAtUtc));
                }

                if (!statisticsSnapshot.Exists)
                {
                    transaction.Create(statisticsReference, StatisticsData(userId, migratedAtUtc));
                }

                if (!slotsCreditsSnapshot.Exists && !migrationTransactionSnapshot.Exists)
                {
                    transaction.Create(
                        migrationTransactionReference,
                        BalanceTransactionData(
                            migrationTransactionReference.Id,
                            userId,
                            SlotsCreditsCurrencyId,
                            slotsCredits,
                            slotsCredits,
                            "wallet-v1-migration",
                            migrationTransactionReference.Id,
                            migratedAtUtc));
                }

                if (needsAccountMigration)
                {
                    transaction.Set(userReference, new Dictionary<string, object>
                    {
                        ["lifetimeLoadedMoney"] = FieldValue.Delete,
                        ["walletSchemaVersion"] = FieldValue.Delete,
                        ["deactivated"] = userSnapshot.TryGetValue<bool>(
                            "deactivated",
                            out var deactivated) && deactivated,
                        ["accountSchemaVersion"] = AccountSchemaVersion,
                        ["updatedAt"] = Timestamp.FromDateTime(migratedAtUtc)
                    }, SetOptions.MergeAll);
                }

                return ToStoredAccount(
                    userSnapshot,
                    slotsCredits,
                    freeGames,
                    statisticsSnapshot.Exists
                        ? ToSlotStatistics(statisticsSnapshot)
                        : EmptySlotStatistics());
            },
            cancellationToken: cancellationToken);
    }
}
