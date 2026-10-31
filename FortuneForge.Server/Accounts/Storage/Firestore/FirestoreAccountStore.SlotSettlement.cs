using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using FortuneForge.Server.Accounts.Models;
using FortuneForge.Server.Slots.Bonuses;
using FortuneForge.Server.Slots.Models;
using FortuneForge.Server.RoundAudit;
using Google.Cloud.Firestore;

namespace FortuneForge.Server.Accounts.Storage;

public sealed partial class FirestoreAccountStore
{
    internal const long SlotOutcomeSchemaVersion = 6;

    public Task<SlotSpinSettlement> RecordSlotSpinAsync(
        string userId,
        SpinResult result,
        SlotSpinAdmission admission,
        DateTime createdAtUtc,
        CancellationToken cancellationToken)
    {
        if (admission.AdmissionId == Guid.Empty || result.EngineAudit is null ||
            result.WagerPoints != admission.WagerPoints ||
            result.SpecialBoostApplied != admission.SpecialBoostApplied ||
            result.EngineAudit.PityMissesBefore != admission.PityMissesBefore ||
            result.EngineAudit.EnergyBalanceBefore != admission.EnergyBalance ||
            result.EngineAudit.GeneratedAtUtc < admission.StartedAtUtc ||
            createdAtUtc < result.EngineAudit.GeneratedAtUtc)
        {
            throw new InvalidOperationException("Slot outcome has no matching admission or engine audit.");
        }

        var chargedWagerCents = admission.ChargedWagerCents;
        var isFreeSpin = admission.IsFreeSpin;
        var activeFreeSpinFeatureMode = admission.FreeSpinFeatureMode;
        var admissionReference = SlotSpinAdmissionDocument(admission.AdmissionId);
        var rulesSha256 = Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(
            JsonSerializer.Serialize(new { result.EngineAudit.BaseGame, result.EngineAudit.EffectiveGame,
                result.EngineAudit.ReelSet, result.EngineAudit.SymbolSet, result.EngineAudit.Paytable,
                result.EngineAudit.FeatureProfile }))));
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
                var admissionSnapshot = await transaction.GetSnapshotAsync(
                    admissionReference,
                    cancellationToken);
                if (!admissionSnapshot.Exists ||
                    admissionSnapshot.GetValue<string>("userId") != userId ||
                    admissionSnapshot.GetValue<string>("gameId") != result.GameId ||
                    admissionSnapshot.GetValue<long>("effectiveWagerPoints") != result.WagerPoints ||
                    admissionSnapshot.GetValue<long>("chargedWagerCents") != chargedWagerCents ||
                    admissionSnapshot.GetValue<bool>("isFreeSpin") != isFreeSpin ||
                    admissionSnapshot.GetValue<bool>("specialBoostApplied") != result.SpecialBoostApplied ||
                    admissionSnapshot.GetValue<long>("pityMissesBefore") != admission.PityMissesBefore)
                {
                    throw new InvalidOperationException("Slot admission receipt does not match the engine result.");
                }
                var admissionStatus = admissionSnapshot.GetValue<string>("status");
                if (admissionStatus != "admitted" &&
                    !(admissionStatus == "settled" && existingResult.Exists &&
                        admissionSnapshot.GetValue<string>("spinId") == result.SpinId.ToString("N")))
                {
                    throw new InvalidOperationException("Slot admission has already been used.");
                }
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
                        result.PointValueInCents,
                        result.GameId);
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
                        ReadString(guardSnapshot, "freeSpinFeatureMode"),
                        checked((int)ReadLong(existingResult, "sealFreeSpinsAwarded")));
                }

                if (ReadString(guardSnapshot, "activeAdmissionId") != admission.AdmissionId.ToString("N") ||
                    ReadLong(guardSnapshot, "consecutiveFiveMisses") != admission.PityMissesBefore)
                {
                    throw new InvalidOperationException("Slot spin guard no longer matches the admitted spin.");
                }

                if (availableCents < chargedWagerCents)
                {
                    throw new InsufficientSlotCreditsException(
                        RandMoney.CentsToRand(availableCents),
                        RandMoney.CentsToRand(chargedWagerCents));
                }

                SlotSpecialRoundProfiles.TryGet(result.GameId, out var specialRoundProfile);
                var energyBonus = EnergyBonus.Settle(
                    specialRoundProfile?.UsesEnergy != false ? currentEnergy : 0,
                    result.EnergyAwarded,
                    result.Payout);
                var piratesGemCountsBefore = result.GameId == SlotSpecialRoundProfiles.PiratesFortuneGameId
                    ? ReadLongMap(guardSnapshot, "sealCounts") : null;
                var piratesGemWagerCentsBefore = piratesGemCountsBefore is not null
                    ? ReadSealWagerCents(guardSnapshot) : null;
                var sealSettlement = SettleSealCollections(
                    guardSnapshot,
                    result,
                    energyBonus.MultiplierApplied);
                PiratesGemSettlementEvidence? piratesGemEvidence = piratesGemCountsBefore is null
                    ? null
                    : new(
                        PiratesGemBonus.RuleVersion,
                        piratesGemCountsBefore,
                        piratesGemWagerCentsBefore!,
                        new Dictionary<string, long>(sealSettlement.SealCounts, StringComparer.Ordinal),
                        new Dictionary<string, long>(sealSettlement.SealWagerCents, StringComparer.Ordinal),
                        sealSettlement.FreeSpinsAwarded,
                        sealSettlement.FreeSpinFeatureMode,
                        sealSettlement.FreeSpinWagerPoints);
                var settledPayout = energyBonus.Payout;
                var outcomeFindings = SlotRoundAudit.Check(result, settledPayout);
                var outcomeVerified = !outcomeFindings.Any(finding => finding.Severity == "error");
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

                if (FortuneForge.Server.Activity.AcceptedGameActivityReader.SlotIds.TryGetValue(result.GameId, out var catalogueGameId))
                    FortuneForge.Server.RoundAudit.SinglePlayerRoundRecording.Write(transaction, database,
                        catalogueGameId, result.SpinId.ToString("N"), userId, chargedWagerCents, payoutCents,
                        admission.StartedAtUtc, createdAtUtc, "slotSpinResults", result.SpinId.ToString("N"),
                        new { result, admissionId = admission.AdmissionId, admission.StartedAtUtc,
                            admission.RequestId, engineAudit = result.EngineAudit, rulesSha256,
                            settledPayout, isFreeSpin, activeFreeSpinFeatureMode,
                            energyBonus.PayoutMultiplier, piratesGemEvidence,
                            outcomeSchemaVersion = SlotOutcomeSchemaVersion },
                        "slot-reels-paylines-and-features-v1", outcomeVerified,
                        SlotRoundAudit.AuditorVersion, outcomeFindings);

                transaction.Update(admissionReference, new Dictionary<string, object>
                {
                    ["status"] = "settled",
                    ["spinId"] = result.SpinId.ToString("N"),
                    ["engineGeneratedAtUtc"] = Timestamp.FromDateTime(result.EngineAudit.GeneratedAtUtc),
                    ["settledAtUtc"] = Timestamp.FromDateTime(createdAtUtc),
                    ["pityMissesAfter"] = result.ConsecutiveFiveMisses,
                    ["rulesSha256"] = rulesSha256
                });

                transaction.Create(resultReference, new Dictionary<string, object>
                {
                    ["spinId"] = result.SpinId.ToString("N"),
                    ["admissionId"] = admission.AdmissionId.ToString("N"),
                    ["requestId"] = admission.RequestId ?? string.Empty,
                    ["userId"] = userId,
                    ["gameId"] = result.GameId,
                    ["reelSetId"] = result.ReelSetId,
                    ["symbolSetId"] = result.SymbolSetId,
                    ["paytableId"] = result.PaytableId,
                    ["reelStops"] = result.ReelStops.Select(static stop => (long)stop).ToArray(),
                    ["visibleSymbolWindow"] = CreateVisibleSymbolWindowData(result.Reels),
                    ["payoutBreakdown"] = CreatePayoutBreakdownData(settledPayout),
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
                    ["pityMissesBefore"] = admission.PityMissesBefore,
                    ["outcomeSchemaVersion"] = SlotOutcomeSchemaVersion,
                    ["engineVersion"] = result.EngineAudit.EngineVersion,
                    ["rulesSha256"] = rulesSha256,
                    ["admittedAtUtc"] = Timestamp.FromDateTime(admission.StartedAtUtc),
                    ["engineGeneratedAtUtc"] = Timestamp.FromDateTime(result.EngineAudit.GeneratedAtUtc),
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
                        ["activeAdmissionId"] = string.Empty,
                        ["consecutiveFiveMisses"] = result.ConsecutiveFiveMisses,
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
                else
                {
                    transaction.Set(guardReference, new Dictionary<string, object>
                    {
                        ["activeAdmissionId"] = string.Empty,
                        ["consecutiveFiveMisses"] = result.ConsecutiveFiveMisses,
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
                    nextFreeSpinFeatureMode,
                    sealSettlement.FreeSpinsAwarded);
            },
            cancellationToken: cancellationToken);
    }

    internal static Dictionary<string, object> CreateVisibleSymbolWindowData(
        IReadOnlyList<IReadOnlyList<string>> reels) => new()
    {
        ["reels"] = reels
            .Select((symbols, reel) => (object)new Dictionary<string, object>
            {
                ["reel"] = (long)reel,
                ["symbols"] = symbols
                    .Select((symbolId, row) => (object)new Dictionary<string, object>
                    {
                        ["row"] = (long)row,
                        ["symbolId"] = symbolId
                    })
                    .ToArray()
            })
            .ToArray()
    };

    internal static Dictionary<string, object> CreatePayoutBreakdownData(SpinPayout payout) => new()
    {
        ["totalPoints"] = payout.TotalPoints,
        ["paylines"] = payout.Paylines
            .Select(static payline => (object)new Dictionary<string, object>
            {
                ["paylineId"] = (long)payline.PaylineId,
                ["amountPoints"] = payline.AmountPoints,
                ["matches"] = payline.Matches
                    .Select(static match => (object)new Dictionary<string, object>
                    {
                        ["paylineId"] = (long)match.Match.PaylineId,
                        ["symbolId"] = match.Match.SymbolId,
                        ["length"] = (long)match.Match.MatchLength,
                        ["multiplier"] = match.Multiplier,
                        ["amountPoints"] = match.AmountPoints,
                        ["positions"] = CreateGridPositionData(match.Match.Positions),
                        ["wildPositions"] = CreateGridPositionData(match.Match.WildPositions)
                    })
                    .ToArray()
            })
            .ToArray()
    };

    private static object[] CreateGridPositionData(IReadOnlyList<GridPosition> positions) =>
        positions
            .Select(static position => (object)new Dictionary<string, object>
            {
                ["reel"] = (long)position.Reel,
                ["row"] = (long)position.Row
            })
            .ToArray();
}
