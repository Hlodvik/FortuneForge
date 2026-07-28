using System.Security.Cryptography;
using System.Text;
using FortuneForge.Server.Accounts.Models;
using FortuneForge.Server.Slots.Bonuses;
using FortuneForge.Server.Slots.Models;
using Google.Cloud.Firestore;

namespace FortuneForge.Server.Accounts.Storage;

public sealed partial class FirestoreAccountStore
{
    public Task<SlotSpinAdmission> BeginSlotSpinAsync(
        string userId,
        string gameId,
        long wagerPoints,
        bool useFreeSpin,
        bool useSpecialBoost,
        int specialBoostCost,
        DateTime startedAtUtc,
        TimeSpan cooldown,
        CancellationToken cancellationToken)
    {
        var guardReference = SlotSpinGuardDocument(userId, gameId);
        var slotsCreditsReference = BalanceDocument(userId, SlotsCreditsCurrencyId);
        var freeGamesCurrencyId = GameCurrencyId(FreeGamesCurrencyId, gameId);
        var specialPointsCurrencyId = GameCurrencyId(SpecialPointsCurrencyId, gameId);
        var energyCurrencyId = GameCurrencyId(EnergyCurrencyId, gameId);
        var freeGamesReference = BalanceDocument(userId, freeGamesCurrencyId);
        var specialPointsReference = BalanceDocument(userId, specialPointsCurrencyId);
        var energyReference = BalanceDocument(userId, energyCurrencyId);
        var freeGameUseTransactionReference = BalanceTransactionDocument(
            $"{Guid.NewGuid():N}-free-game-use");
        var specialPointUseTransactionReference = BalanceTransactionDocument(
            $"{Guid.NewGuid():N}-special-boost-use");

        return database.RunTransactionAsync(
            async transaction =>
            {
                var snapshots = await Task.WhenAll(
                    transaction.GetSnapshotAsync(guardReference, cancellationToken),
                    transaction.GetSnapshotAsync(slotsCreditsReference, cancellationToken),
                    transaction.GetSnapshotAsync(freeGamesReference, cancellationToken),
                    transaction.GetSnapshotAsync(specialPointsReference, cancellationToken),
                    transaction.GetSnapshotAsync(energyReference, cancellationToken));
                var guardSnapshot = snapshots[0];
                var slotsCreditsSnapshot = snapshots[1];
                var freeGamesSnapshot = snapshots[2];
                var freeGamesAvailable = ReadLong(freeGamesSnapshot, "available");
                var specialPointsSnapshot = snapshots[3];
                var specialPointsAvailable = ReadLong(specialPointsSnapshot, "available");
                var energySnapshot = snapshots[4];
                var energyBalance = ReadLong(energySnapshot, "available");
                if (guardSnapshot.Exists &&
                    guardSnapshot.TryGetValue<Timestamp>("lastSpinStartedAt", out var lastStartedAt))
                {
                    var nextAllowedAtUtc = lastStartedAt.ToDateTime().Add(cooldown);
                    if (nextAllowedAtUtc > startedAtUtc)
                    {
                        return new SlotSpinAdmission(
                            wagerPoints,
                            0,
                            false,
                            checked((int)freeGamesAvailable),
                            false,
                            checked((int)specialPointsAvailable),
                            nextAllowedAtUtc - startedAtUtc,
                            energyBalance,
                            null);
                    }
                }

                var availableCredits = ReadLong(slotsCreditsSnapshot, "available");
                var storedFreeSpinWagerPoints = ReadLong(guardSnapshot, "freeSpinWagerPoints");
                var storedFreeSpinFeatureMode = ReadString(guardSnapshot, "freeSpinFeatureMode");
                if (useFreeSpin && freeGamesAvailable <= 0)
                {
                    throw new NoFreeSpinsException();
                }
                if (useSpecialBoost && specialBoostCost <= 0)
                {
                    throw new ArgumentOutOfRangeException(
                        nameof(specialBoostCost),
                        "A special boost must have a positive point cost.");
                }
                if (useSpecialBoost && specialPointsAvailable < specialBoostCost)
                {
                    throw new InsufficientSpecialPointsException(
                        specialPointsAvailable,
                        specialBoostCost);
                }

                var effectiveWagerPoints = useFreeSpin
                    ? storedFreeSpinWagerPoints > 0 ? storedFreeSpinWagerPoints : wagerPoints
                    : wagerPoints;
                var activeFreeSpinFeatureMode = useFreeSpin ? storedFreeSpinFeatureMode : null;
                var chargedWagerPoints = useFreeSpin ? 0 : wagerPoints;
                if (!useFreeSpin && availableCredits < wagerPoints)
                {
                    throw new InsufficientSlotCreditsException(availableCredits, wagerPoints);
                }

                var remainingAfterAdmission = useFreeSpin
                    ? freeGamesAvailable - 1
                    : freeGamesAvailable;
                var specialPointsAfterAdmission = useSpecialBoost
                    ? specialPointsAvailable - specialBoostCost
                    : specialPointsAvailable;

                if (useFreeSpin)
                {
                    transaction.Update(freeGamesReference, new Dictionary<string, object>
                    {
                        ["available"] = remainingAfterAdmission,
                        ["version"] = FieldValue.Increment(1),
                        ["updatedAt"] = Timestamp.FromDateTime(startedAtUtc)
                    });
                    transaction.Create(
                        freeGameUseTransactionReference,
                        BalanceTransactionData(
                            freeGameUseTransactionReference.Id,
                            userId,
                            freeGamesCurrencyId,
                            -1,
                            remainingAfterAdmission,
                            "free-game-use",
                            freeGameUseTransactionReference.Id,
                            startedAtUtc));
                }

                if (useSpecialBoost)
                {
                    if (specialPointsSnapshot.Exists)
                    {
                        transaction.Update(specialPointsReference, new Dictionary<string, object>
                        {
                            ["available"] = specialPointsAfterAdmission,
                            ["version"] = FieldValue.Increment(1),
                            ["updatedAt"] = Timestamp.FromDateTime(startedAtUtc)
                        });
                    }
                    else
                    {
                        transaction.Create(
                            specialPointsReference,
                            BalanceData(
                                userId,
                                specialPointsCurrencyId,
                                specialPointsAfterAdmission,
                                startedAtUtc));
                    }
                    transaction.Create(
                        specialPointUseTransactionReference,
                        BalanceTransactionData(
                            specialPointUseTransactionReference.Id,
                            userId,
                            specialPointsCurrencyId,
                            -specialBoostCost,
                            specialPointsAfterAdmission,
                            "special-boost-use",
                            specialPointUseTransactionReference.Id,
                            startedAtUtc));
                }

                transaction.Set(guardReference, new Dictionary<string, object>
                {
                    ["userId"] = userId,
                    ["lastSpinStartedAt"] = Timestamp.FromDateTime(startedAtUtc),
                    ["freeSpinWagerPoints"] = remainingAfterAdmission > 0
                        ? effectiveWagerPoints
                        : 0,
                    ["freeSpinFeatureMode"] = remainingAfterAdmission > 0
                        ? activeFreeSpinFeatureMode ?? string.Empty
                        : string.Empty,
                    ["updatedAt"] = Timestamp.FromDateTime(startedAtUtc)
                }, SetOptions.MergeAll);
                return new SlotSpinAdmission(
                    effectiveWagerPoints,
                    chargedWagerPoints,
                    useFreeSpin,
                    checked((int)remainingAfterAdmission),
                    useSpecialBoost,
                    checked((int)specialPointsAfterAdmission),
                    null,
                    energyBalance,
                    activeFreeSpinFeatureMode);
            },
            cancellationToken: cancellationToken);
    }
}
