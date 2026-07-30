using System.Security.Cryptography;
using System.Text;
using FortuneForge.Server.Accounts.Models;
using FortuneForge.Server.Slots.Bonuses;
using FortuneForge.Server.Slots.Models;
using Google.Cloud.Firestore;

namespace FortuneForge.Server.Accounts.Storage;

public sealed partial class FirestoreAccountStore
{
    public Task<SlotSpinSettlement> RecordSlotSpinAsync(
        string userId,
        SpinResult result,
        long chargedWagerCents,
        bool isFreeSpin,
        string? activeFreeSpinFeatureMode,
        DateTime createdAtUtc,
        CancellationToken cancellationToken)
    {
        var resultReference = SlotSpinResultDocument(result.SpinId);
        var statisticsReference = StatisticsDocument(userId);
        var slotsCreditsReference = BalanceDocument(userId, SlotsCreditsCurrencyId);
        var freeGamesCurrencyId = GameCurrencyId(FreeGamesCurrencyId, result.GameId);
        var specialPointsCurrencyId = GameCurrencyId(SpecialPointsCurrencyId, result.GameId);
        var energyCurrencyId = GameCurrencyId(EnergyCurrencyId, result.GameId);
        var freeGamesReference = BalanceDocument(userId, freeGamesCurrencyId);
        var specialPointsReference = BalanceDocument(userId, specialPointsCurrencyId);
        var energyReference = BalanceDocument(userId, energyCurrencyId);
        var guardReference = SlotSpinGuardDocument(userId, result.GameId);
        var spinKey = result.SpinId.ToString("N");
        var wagerTransactionReference = BalanceTransactionDocument($"{spinKey}-wager");
        var payoutTransactionReference = BalanceTransactionDocument($"{spinKey}-payout");
        var freeGamesAwardTransactionReference = BalanceTransactionDocument(
            $"{spinKey}-free-games-award");
        var specialPointsAwardTransactionReference = BalanceTransactionDocument(
            $"{spinKey}-special-points-award");
        var energyAwardTransactionReference = BalanceTransactionDocument(
            $"{spinKey}-energy-award");
        var energyResetTransactionReference = BalanceTransactionDocument(
            $"{spinKey}-energy-multiplier-use");

        return database.RunTransactionAsync(
            async transaction =>
            {
                var existingResult = await transaction.GetSnapshotAsync(
                    resultReference,
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
                var guardSnapshot = await transaction.GetSnapshotAsync(
                    guardReference,
                    cancellationToken);
                var availableCents = ReadRandBalanceCents(slotsCreditsSnapshot);
                var currentFreeGames = ReadLong(freeGamesSnapshot, "available");
                var currentSpecialPoints = ReadLong(specialPointsSnapshot, "available");
                var currentEnergy = ReadLong(energySnapshot, "available");
                if (existingResult.Exists)
                {
                    var existingSealCollections = CreateSealCollections(
                        guardSnapshot,
                        result.PointValueInCents);
                    var existingFreeSpinWagerCents = ReadLong(guardSnapshot, "freeSpinWagerCents");
                    if (existingFreeSpinWagerCents <= 0)
                    {
                        existingFreeSpinWagerCents = checked(
                            ReadLong(guardSnapshot, "freeSpinWagerPoints") *
                            RandMoney.CentsPerRand);
                    }
                    return new SlotSpinSettlement(
                        RandMoney.CentsToRand(availableCents),
                        checked((int)currentFreeGames),
                        checked((int)currentSpecialPoints),
                        currentEnergy,
                        result.Payout,
                        false,
                        1m,
                        existingFreeSpinWagerCents > 0
                            ? RandMoney.CentsToPoints(
                                existingFreeSpinWagerCents,
                                result.PointValueInCents)
                            : null,
                        existingSealCollections,
                        ReadString(guardSnapshot, "freeSpinFeatureMode"));
                }

                if (availableCents < chargedWagerCents)
                {
                    throw new InsufficientSlotCreditsException(
                        RandMoney.CentsToRand(availableCents),
                        RandMoney.CentsToRand(chargedWagerCents));
                }

                var energyBonus = EnergyBonus.Settle(currentEnergy, result.EnergyAwarded, result.Payout);
                var sealSettlement = SettleSealCollections(
                    guardSnapshot,
                    result,
                    energyBonus.MultiplierApplied);
                var settledPayout = energyBonus.Payout;
                var payoutCents = RandMoney.PointsToCents(
                    settledPayout.TotalPoints,
                    result.PointValueInCents);
                var netCents = checked(payoutCents - chargedWagerCents);
                var isWin = settledPayout.TotalPoints > 0;
                var balanceAfterWagerCents = checked(availableCents - chargedWagerCents);
                var balanceAfterPayoutCents = checked(balanceAfterWagerCents + payoutCents);
                var wagerRand = RandMoney.CentsToRand(chargedWagerCents);
                var payoutRand = RandMoney.CentsToRand(payoutCents);
                var netRand = RandMoney.CentsToRand(netCents);
                var balanceAfterWagerRand = RandMoney.CentsToRand(balanceAfterWagerCents);
                var balanceAfterPayoutRand = RandMoney.CentsToRand(balanceAfterPayoutCents);
                var totalFreeSpinsAwarded = checked(
                    result.FreeSpinsAwarded + sealSettlement.FreeSpinsAwarded);
                var freeGamesRemaining = checked(
                    (isFreeSpin ? currentFreeGames : 0) + totalFreeSpinsAwarded);
                var specialPointsBalance = checked(currentSpecialPoints + result.SpecialPointsAwarded);
                var energyBalance = energyBonus.FinalEnergyBalance;
                var nextFreeSpinFeatureMode = freeGamesRemaining > 0
                    ? sealSettlement.FreeSpinFeatureMode ?? (isFreeSpin ? activeFreeSpinFeatureMode : null)
                    : null;
                var nextFreeSpinWagerPoints = freeGamesRemaining > 0
                    ? sealSettlement.FreeSpinWagerPoints > 0
                        ? sealSettlement.FreeSpinWagerPoints
                        : result.WagerPoints
                    : 0;
                var nextFreeSpinWagerCents = nextFreeSpinWagerPoints > 0
                    ? RandMoney.PointsToCents(
                        nextFreeSpinWagerPoints,
                        result.PointValueInCents)
                    : 0;

                transaction.Create(resultReference, new Dictionary<string, object>
                {
                    ["spinId"] = result.SpinId.ToString("N"),
                    ["userId"] = userId,
                    ["gameId"] = result.GameId,
                    ["reelSetId"] = result.ReelSetId,
                    ["symbolSetId"] = result.SymbolSetId,
                    ["paytableId"] = result.PaytableId,
                    ["reelStops"] = result.ReelStops.Select(static stop => (long)stop).ToArray(),
                    ["wageredSlotsCredits"] = (double)wagerRand,
                    ["payoutWagerPoints"] = result.WagerPoints,
                    ["wonSlotsCredits"] = (double)payoutRand,
                    ["netSlotsCredits"] = (double)netRand,
                    ["isFreeSpin"] = isFreeSpin,
                    ["freeSpinsAwarded"] = result.FreeSpinsAwarded,
                    ["specialPointsAwarded"] = result.SpecialPointsAwarded,
                    ["energyAwarded"] = result.EnergyAwarded,
                    ["energyMultiplierApplied"] = energyBonus.MultiplierApplied,
                    ["payoutMultiplier"] = (double)energyBonus.PayoutMultiplier,
                    ["monkeyPawCount"] = result.MonkeyPawCount,
                    ["moneyGrabPoints"] = result.MoneyGrabPoints,
                    ["bananaBonusPoints"] = result.BananaBonusPoints,
                    ["sealsAwarded"] = result.SealsAwarded.ToDictionary(
                        pair => pair.Key,
                        pair => (object)(long)pair.Value,
                        StringComparer.Ordinal),
                    ["sealFreeSpinsAwarded"] = sealSettlement.FreeSpinsAwarded,
                    ["sealFreeSpinFeatureMode"] = sealSettlement.FreeSpinFeatureMode ?? string.Empty,
                    ["specialBoostApplied"] = result.SpecialBoostApplied,
                    ["consecutiveFiveMisses"] = result.ConsecutiveFiveMisses,
                    ["fiveMatchPityTriggered"] = result.FiveMatchPityTriggered,
                    ["outcomeSchemaVersion"] = 4,
                    ["result"] = isWin ? "win" : "loss",
                    ["createdAt"] = Timestamp.FromDateTime(createdAtUtc)
                });
                transaction.Update(slotsCreditsReference, new Dictionary<string, object>
                {
                    ["available"] = balanceAfterPayoutCents / RandMoney.CentsPerRand,
                    [AvailableFractionalCentsField] = balanceAfterPayoutCents % RandMoney.CentsPerRand,
                    ["version"] = FieldValue.Increment(1),
                    ["updatedAt"] = Timestamp.FromDateTime(createdAtUtc)
                });
                if (chargedWagerCents > 0)
                {
                    transaction.Create(
                        wagerTransactionReference,
                        RandBalanceTransactionData(
                            wagerTransactionReference.Id,
                            userId,
                            -wagerRand,
                            balanceAfterWagerRand,
                            "slot-wager",
                            wagerTransactionReference.Id,
                            createdAtUtc));
                }
                if (payoutCents > 0)
                {
                    transaction.Create(
                        payoutTransactionReference,
                        RandBalanceTransactionData(
                            payoutTransactionReference.Id,
                            userId,
                            payoutRand,
                            balanceAfterPayoutRand,
                            "slot-payout",
                            payoutTransactionReference.Id,
                            createdAtUtc));
                }
                transaction.Set(statisticsReference, new Dictionary<string, object>
                {
                    ["userId"] = userId,
                    ["spinsPlayed"] = FieldValue.Increment(1),
                    ["wins"] = FieldValue.Increment(isWin ? 1 : 0),
                    ["losses"] = FieldValue.Increment(isWin ? 0 : 1),
                    ["creditsWagered"] = FieldValue.Increment((double)wagerRand),
                    ["creditsWon"] = FieldValue.Increment((double)payoutRand),
                    ["netCredits"] = FieldValue.Increment((double)netRand),
                    ["updatedAt"] = Timestamp.FromDateTime(createdAtUtc)
                }, SetOptions.MergeAll);

                var shouldWriteFreeGamesBalance =
                    totalFreeSpinsAwarded > 0 ||
                    (!isFreeSpin && currentFreeGames != freeGamesRemaining);
                if (shouldWriteFreeGamesBalance)
                {
                    if (freeGamesSnapshot.Exists)
                    {
                        transaction.Update(freeGamesReference, new Dictionary<string, object>
                        {
                            ["available"] = freeGamesRemaining,
                            ["version"] = FieldValue.Increment(1),
                            ["updatedAt"] = Timestamp.FromDateTime(createdAtUtc)
                        });
                    }
                    else
                    {
                        transaction.Create(
                            freeGamesReference,
                            BalanceData(userId, freeGamesCurrencyId, freeGamesRemaining, createdAtUtc));
                    }
                }

                if (totalFreeSpinsAwarded > 0)
                {
                    transaction.Create(
                        freeGamesAwardTransactionReference,
                        BalanceTransactionData(
                            freeGamesAwardTransactionReference.Id,
                            userId,
                            freeGamesCurrencyId,
                            totalFreeSpinsAwarded,
                            freeGamesRemaining,
                            "free-game-award",
                            freeGamesAwardTransactionReference.Id,
                            createdAtUtc));
                }

                if (totalFreeSpinsAwarded > 0 ||
                    sealSettlement.SealsChanged ||
                    (!isFreeSpin && currentFreeGames > 0) ||
                    (isFreeSpin && currentFreeGames == 0 && !string.IsNullOrWhiteSpace(activeFreeSpinFeatureMode)))
                {
                    transaction.Set(guardReference, new Dictionary<string, object>
                    {
                        ["userId"] = userId,
                        ["freeSpinWagerPoints"] = nextFreeSpinWagerPoints,
                        ["freeSpinWagerCents"] = nextFreeSpinWagerCents,
                        ["freeSpinFeatureMode"] = nextFreeSpinFeatureMode ?? string.Empty,
                        ["sealCounts"] = sealSettlement.SealCounts.ToDictionary(
                            pair => pair.Key,
                            pair => (object)pair.Value,
                            StringComparer.Ordinal),
                        ["sealWagerCents"] = sealSettlement.SealWagerCents.ToDictionary(
                            pair => pair.Key,
                            pair => (object)pair.Value,
                            StringComparer.Ordinal),
                        ["updatedAt"] = Timestamp.FromDateTime(createdAtUtc)
                    }, SetOptions.MergeAll);
                }

                if (result.SpecialPointsAwarded > 0)
                {
                    if (specialPointsSnapshot.Exists)
                    {
                        transaction.Update(specialPointsReference, new Dictionary<string, object>
                        {
                            ["available"] = specialPointsBalance,
                            ["version"] = FieldValue.Increment(1),
                            ["updatedAt"] = Timestamp.FromDateTime(createdAtUtc)
                        });
                    }
                    else
                    {
                        transaction.Create(
                            specialPointsReference,
                            BalanceData(
                                userId,
                                specialPointsCurrencyId,
                                specialPointsBalance,
                                createdAtUtc));
                    }
                    transaction.Create(
                        specialPointsAwardTransactionReference,
                        BalanceTransactionData(
                            specialPointsAwardTransactionReference.Id,
                            userId,
                            specialPointsCurrencyId,
                            result.SpecialPointsAwarded,
                            specialPointsBalance,
                            "special-point-award",
                            specialPointsAwardTransactionReference.Id,
                            createdAtUtc));
                }

                var shouldWriteEnergyBalance =
                    result.EnergyAwarded > 0 ||
                    energyBonus.MultiplierApplied ||
                    currentEnergy != energyBonus.FinalEnergyBalance;
                if (shouldWriteEnergyBalance)
                {
                    if (energySnapshot.Exists)
                    {
                        transaction.Update(energyReference, new Dictionary<string, object>
                        {
                            ["available"] = energyBalance,
                            ["version"] = FieldValue.Increment(1),
                            ["updatedAt"] = Timestamp.FromDateTime(createdAtUtc)
                        });
                    }
                    else
                    {
                        transaction.Create(
                            energyReference,
                            BalanceData(userId, energyCurrencyId, energyBalance, createdAtUtc));
                    }
                }

                if (energyBonus.EnergyAddedToMeter > 0)
                {
                    transaction.Create(
                        energyAwardTransactionReference,
                        BalanceTransactionData(
                            energyAwardTransactionReference.Id,
                            userId,
                            energyCurrencyId,
                            energyBonus.EnergyAddedToMeter,
                            energyBonus.MeterBalanceBeforeReset,
                            "energy-award",
                            energyAwardTransactionReference.Id,
                            createdAtUtc));
                }

                if (energyBonus.MultiplierApplied)
                {
                    transaction.Create(
                        energyResetTransactionReference,
                        BalanceTransactionData(
                            energyResetTransactionReference.Id,
                            userId,
                            energyCurrencyId,
                            -energyBonus.MeterBalanceBeforeReset,
                            energyBalance,
                            "energy-multiplier-use",
                            energyResetTransactionReference.Id,
                            createdAtUtc));
                }

                return new SlotSpinSettlement(
                    balanceAfterPayoutRand,
                    checked((int)freeGamesRemaining),
                    checked((int)specialPointsBalance),
                    energyBalance,
                    settledPayout,
                    energyBonus.MultiplierApplied,
                    energyBonus.PayoutMultiplier,
                    nextFreeSpinWagerPoints > 0 ? nextFreeSpinWagerPoints : null,
                    sealSettlement.Collections,
                    nextFreeSpinFeatureMode);
            },
            cancellationToken: cancellationToken);
    }
}
