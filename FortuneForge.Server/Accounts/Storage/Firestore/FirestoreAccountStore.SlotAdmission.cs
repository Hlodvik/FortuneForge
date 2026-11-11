using System.Security.Cryptography;
using System.Text;
using FortuneForge.Server.Accounts.Models;
using FortuneForge.Server.Slots.Bonuses;
using FortuneForge.Server.Slots.Configuration;
using FortuneForge.Server.Slots.Games.GoldFoundry;
using FortuneForge.Server.Slots.Models;
using FortuneForge.Games.Abstractions.Auditing;
using Google.Cloud.Firestore;

namespace FortuneForge.Server.Accounts.Storage;

public sealed partial class FirestoreAccountStore
{
    public Task<SlotSpinAdmission> BeginSlotSpinAsync(
        string userId,
        string gameId,
        long wagerPoints,
        decimal pointValueInCents,
        bool useFreeSpin,
        bool useSpecialBoost,
        int specialBoostCost,
        DateTime startedAtUtc,
        TimeSpan cooldown,
        CancellationToken cancellationToken)
    {
        if (gameId == GoldFoundrySlotDefinitions.GameId)
            GoldFoundrySlotDefinitions.ValidateSpinRequest(useFreeSpin);

        var admissionId = Guid.NewGuid();
        var isOlympusTumble = gameId == SlotSpecialRoundProfiles.GodsOfOlympusGameId && !useFreeSpin;
        if (isOlympusTumble && useSpecialBoost)
            throw new ArgumentException("Olympus tumble spins do not use the classic special-point boost.", nameof(useSpecialBoost));
        var admissionReference = SlotSpinAdmissionDocument(admissionId);
        var requestObservation = GameSecurityContext.Current;
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
                var pityMissesBefore = checked((int)ReadLong(guardSnapshot, "consecutiveFiveMisses"));
                if (pityMissesBefore < 0)
                    throw new InvalidOperationException("Slot pity state is invalid.");
                var slotsCreditsSnapshot = snapshots[1];
                var freeGamesSnapshot = snapshots[2];
                var freeGamesAvailable = ReadLong(freeGamesSnapshot, "available");
                var specialPointsSnapshot = snapshots[3];
                var specialPointsAvailable = ReadLong(specialPointsSnapshot, "available");
                var energySnapshot = snapshots[4];
                var isNeon = gameId == SlotSpecialRoundProfiles.NeonNightsGameId;
                var neonDiscoSpinsBefore = isNeon ? ReadNeonDiscoSpinsRemaining(guardSnapshot) : 0;
                var neonDiscoMultiplierBefore = neonDiscoSpinsBefore > 0
                    ? ReadNeonDiscoMultiplier(guardSnapshot) : NeonDiscoCascade.InitialMultiplier;
                var energyBalance = isNeon || gameId == SlotSpecialRoundProfiles.RainbowRealmGameId
                    ? 0 : ReadLong(energySnapshot, "available");
                var isMidnight = gameId == SlotSpecialRoundProfiles.DragonHoardGameId;
                IReadOnlyList<MidnightVaultBonusRound> roundsBefore = isMidnight
                    ? ReadMidnightBonusRounds(guardSnapshot, freeGamesAvailable, pointValueInCents) : [];
                if (guardSnapshot.Exists &&
                    guardSnapshot.TryGetValue<Timestamp>("lastSpinStartedAt", out var lastStartedAt))
                {
                    var nextAllowedAtUtc = lastStartedAt.ToDateTime().Add(cooldown);
                    var pendingAdmission = ReadString(guardSnapshot, "activeAdmissionId");
                    if (!string.IsNullOrEmpty(pendingAdmission))
                        nextAllowedAtUtc = lastStartedAt.ToDateTime().AddSeconds(30);
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
                    if (isMidnight && !string.IsNullOrEmpty(pendingAdmission))
                    {
                        if (!Guid.TryParseExact(pendingAdmission, "N", out var expiredId))
                            throw new InvalidOperationException("The interrupted Midnight Vault admission is invalid.");
                        var expiredReference = SlotSpinAdmissionDocument(expiredId);
                        var expired = await transaction.GetSnapshotAsync(expiredReference, cancellationToken);
                        if (!expired.Exists || ReadString(expired, "status") != "admitted" ||
                            ReadString(expired, "userId") != userId || ReadString(expired, "gameId") != gameId ||
                            !expired.ContainsField("midnightBonusRoundsBefore") ||
                            expired.GetValue<Timestamp>("startedAtUtc") != lastStartedAt)
                            throw new InvalidOperationException("The interrupted Midnight Vault admission cannot be restored safely.");
                        var restored = ReadMidnightRoundsField(expired, "midnightBonusRoundsBefore");
                        var consumed = ReadMidnightRoundsField(expired, "midnightBonusRoundsAfter");
                        var wasFree = expired.GetValue<bool>("isFreeSpin");
                        var restoredCount = restored.Sum(round => (long)round.RemainingSpins);
                        if (!roundsBefore.SequenceEqual(consumed) ||
                            restoredCount - freeGamesAvailable != (wasFree ? 1 : 0))
                            throw new InvalidOperationException("The interrupted Midnight Vault queue changed before recovery.");
                        var hadBoost = expired.GetValue<bool>("specialBoostApplied");
                        var refundBoost = hadBoost ? ReadLong(expired, "specialBoostCost") : 0;
                        if (refundBoost < 0 || hadBoost && refundBoost == 0 ||
                            refundBoost > ReadLong(expired, "specialPointsBefore") ||
                            refundBoost > 0 && !specialPointsSnapshot.Exists)
                            throw new InvalidOperationException("The interrupted Midnight Vault boost receipt is invalid.");
                        if (wasFree)
                        {
                            transaction.Update(freeGamesReference, new Dictionary<string, object>
                            { ["available"] = restoredCount, ["version"] = FieldValue.Increment(1), ["updatedAt"] = Timestamp.FromDateTime(startedAtUtc) });
                            var refund = BalanceTransactionDocument($"{expiredId:N}-free-game-refund");
                            transaction.Create(refund, BalanceTransactionData(refund.Id, userId, freeGamesCurrencyId,
                                1, restoredCount, "free-game-refund", expiredId.ToString("N"), startedAtUtc));
                        }
                        if (refundBoost > 0)
                        {
                            specialPointsAvailable = checked(specialPointsAvailable + refundBoost);
                            transaction.Update(specialPointsReference, new Dictionary<string, object>
                            { ["available"] = specialPointsAvailable, ["version"] = FieldValue.Increment(1), ["updatedAt"] = Timestamp.FromDateTime(startedAtUtc) });
                            var refund = BalanceTransactionDocument($"{expiredId:N}-special-boost-refund");
                            transaction.Create(refund, BalanceTransactionData(refund.Id, userId, specialPointsCurrencyId,
                                refundBoost, specialPointsAvailable, "special-boost-refund", expiredId.ToString("N"), startedAtUtc));
                        }
                        transaction.Update(expiredReference, new Dictionary<string, object>
                        { ["status"] = "recovered", ["recoveredAtUtc"] = Timestamp.FromDateTime(startedAtUtc) });
                        roundsBefore = restored;
                        freeGamesAvailable = restoredCount;
                    }
                }

                var availableCents = ReadRandBalanceCents(slotsCreditsSnapshot);
                var storedFreeSpinWagerCents = ReadLong(guardSnapshot, "freeSpinWagerCents");
                if (!isNeon && storedFreeSpinWagerCents <= 0)
                {
                    storedFreeSpinWagerCents = checked(
                        ReadLong(guardSnapshot, "freeSpinWagerPoints") * RandMoney.CentsPerRand);
                }
                var storedFreeSpinFeatureMode = ReadString(guardSnapshot, "freeSpinFeatureMode");
                if (useFreeSpin && freeGamesAvailable <= 0)
                {
                    throw new NoFreeSpinsException();
                }
                if (isOlympusTumble && freeGamesAvailable > 0)
                    throw new SlotFreeSpinsRequiredException();
                if (isNeon && !useFreeSpin && freeGamesAvailable > 0)
                    throw new SlotFreeSpinsRequiredException();
                var isNeonDiscoPlay = isNeon && !useFreeSpin && neonDiscoSpinsBefore > 0;
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

                var effectiveWagerPoints = wagerPoints;
                if (useFreeSpin && isNeon)
                    effectiveWagerPoints = ReadNeonCarryWagerPoints(guardSnapshot, pointValueInCents);
                else if (useFreeSpin && storedFreeSpinWagerCents > 0)
                    effectiveWagerPoints = RandMoney.CentsToPoints(storedFreeSpinWagerCents, pointValueInCents);
                var admittedRound = isMidnight && useFreeSpin ? roundsBefore.FirstOrDefault() : null;
                string? mathModelId = isOlympusTumble ? OlympusTumbleDemo.EngineVersion : null;
                if (isMidnight)
                {
                    if (!useFreeSpin && roundsBefore.Count > 0)
                        throw new ArgumentException("Finish the awarded Vault Breach spins before placing another cash bet.");
                    // Validate the actual locked server wager before consuming either
                    // a free spin or special points. The cash menu remains even.
                    if (useFreeSpin)
                    {
                        if (admittedRound is null) throw new NoFreeSpinsException();
                        effectiveWagerPoints = admittedRound.WagerPoints;
                        MidnightVaultBonusPolicy.ValidateDerivedWager(effectiveWagerPoints);
                    }
                    else
                        MidnightVaultBonusPolicy.ValidateCashWager(effectiveWagerPoints);
                    mathModelId = useFreeSpin
                        ? admittedRound!.MathModelId
                        : MidnightVaultSlotDefinitions.PaytableId;
                }
                var activeFreeSpinFeatureMode = useFreeSpin
                    ? isMidnight ? admittedRound!.FeatureMode : storedFreeSpinFeatureMode : null;
                var roundsAfter = isMidnight && useFreeSpin ? MidnightVaultBonusRounds.Consume(roundsBefore) : roundsBefore;
                var chargedWagerCents = isNeon
                    ? NeonPlayPolicy.ChargesWager(useFreeSpin, activeFreeSpinFeatureMode)
                        ? RandMoney.PointsToCents(effectiveWagerPoints, pointValueInCents) : 0
                    : !useFreeSpin
                    ? RandMoney.PointsToCents(effectiveWagerPoints, pointValueInCents)
                    : 0;
                if (availableCents < chargedWagerCents)
                {
                    throw new InsufficientSlotCreditsException(
                        RandMoney.CentsToRand(availableCents),
                        RandMoney.CentsToRand(chargedWagerCents));
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

                var guardData = new Dictionary<string, object>
                {
                    ["userId"] = userId,
                    ["lastSpinStartedAt"] = Timestamp.FromDateTime(startedAtUtc),
                    ["activeAdmissionId"] = admissionId.ToString("N"),
                    ["freeSpinWagerPoints"] = remainingAfterAdmission > 0
                        ? effectiveWagerPoints
                        : 0,
                    ["freeSpinWagerCents"] = remainingAfterAdmission > 0
                        ? RandMoney.PointsToCents(effectiveWagerPoints, pointValueInCents)
                        : 0,
                    ["freeSpinFeatureMode"] = remainingAfterAdmission > 0
                        ? activeFreeSpinFeatureMode ?? string.Empty
                        : string.Empty,
                    ["freeSpinMathModelId"] = remainingAfterAdmission > 0 ? mathModelId ?? string.Empty : string.Empty,
                    ["updatedAt"] = Timestamp.FromDateTime(startedAtUtc)
                };
                if (isMidnight)
                    foreach (var pair in MidnightBonusGuardData(roundsAfter, pointValueInCents)) guardData[pair.Key] = pair.Value;
                if (isNeon)
                {
                    guardData["neonDiscoSpinsRemaining"] = neonDiscoSpinsBefore;
                    guardData["neonDiscoMultiplier"] = neonDiscoMultiplierBefore;
                }
                transaction.Set(guardReference, guardData, SetOptions.MergeAll);
                var admissionData = new Dictionary<string, object>
                {
                    ["admissionId"] = admissionId.ToString("N"),
                    ["userId"] = userId,
                    ["gameId"] = gameId,
                    ["mathModelId"] = mathModelId ?? string.Empty,
                    ["requestedWagerPoints"] = wagerPoints,
                    ["effectiveWagerPoints"] = effectiveWagerPoints,
                    ["chargedWagerCents"] = chargedWagerCents,
                    ["isFreeSpin"] = useFreeSpin,
                    ["isNeonDiscoPlay"] = isNeonDiscoPlay,
                    ["neonDiscoSpinsBefore"] = neonDiscoSpinsBefore,
                    ["specialBoostApplied"] = useSpecialBoost,
                    ["specialBoostCost"] = useSpecialBoost ? specialBoostCost : 0,
                    ["freeGameUseTransactionId"] = useFreeSpin
                        ? freeGameUseTransactionReference.Id : string.Empty,
                    ["specialPointUseTransactionId"] = useSpecialBoost
                        ? specialPointUseTransactionReference.Id : string.Empty,
                    ["freeSpinFeatureMode"] = activeFreeSpinFeatureMode ?? string.Empty,
                    ["freeSpinsBefore"] = freeGamesAvailable,
                    ["specialPointsBefore"] = specialPointsAvailable,
                    ["energyBalanceBefore"] = energyBalance,
                    ["pityMissesBefore"] = pityMissesBefore,
                    ["slotsCreditsBeforeCents"] = availableCents,
                    ["requestId"] = requestObservation?.RequestId ?? string.Empty,
                    ["requestObservedAtUtc"] = requestObservation is null
                        ? null! : Timestamp.FromDateTime(requestObservation.ReceivedAtUtc),
                    ["ipAddress"] = requestObservation?.IpAddress ?? string.Empty,
                    ["sessionId"] = requestObservation?.SessionId ?? string.Empty,
                    ["status"] = "admitted",
                    ["auditObserved"] = false,
                    ["auditNextAttemptAtUtc"] = Timestamp.FromDateTime(startedAtUtc.AddMinutes(2)),
                    ["startedAtUtc"] = Timestamp.FromDateTime(startedAtUtc)
                };
                if (isNeon)
                    admissionData["neonDiscoMultiplierBefore"] = neonDiscoMultiplierBefore;
                if (isMidnight)
                {
                    admissionData["midnightBonusRoundsBefore"] = MidnightRoundsData(roundsBefore);
                    admissionData["midnightBonusRoundsAfter"] = MidnightRoundsData(roundsAfter);
                    admissionData["midnightAdmittedRound"] = admittedRound is null ? null! : MidnightRoundData(admittedRound);
                }
                transaction.Create(admissionReference, admissionData);
                return new SlotSpinAdmission(
                    effectiveWagerPoints,
                    chargedWagerCents,
                    useFreeSpin,
                    isMidnight ? roundsAfter.FirstOrDefault()?.RemainingSpins ?? 0 : checked((int)remainingAfterAdmission),
                    useSpecialBoost,
                    checked((int)specialPointsAfterAdmission),
                    null,
                    energyBalance,
                    activeFreeSpinFeatureMode)
                {
                    AdmissionId = admissionId,
                    StartedAtUtc = startedAtUtc,
                    RequestId = requestObservation?.RequestId,
                    PityMissesBefore = pityMissesBefore,
                    IsNeonDiscoPlay = isNeonDiscoPlay,
                    NeonDiscoMultiplier = neonDiscoMultiplierBefore,
                    MathModelId = mathModelId,
                    BonusRound = admittedRound,
                    BonusRoundsBeforeAdmission = roundsBefore
                };
            },
            cancellationToken: cancellationToken);
    }
}
