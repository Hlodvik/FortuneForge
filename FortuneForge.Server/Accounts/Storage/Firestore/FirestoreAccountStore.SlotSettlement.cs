// Edited: 2026-11-18T18:01:37-06:00 | Computer: Vinnabok
// Edited: 2026-11-18T16:45:56-06:00 | Computer: Vinnabok
// Edited: 2026-11-18T16:30:05-06:00 | Computer: Vinnabok
// Edited: 2026-11-18T14:45:02-06:00 | Computer: Vinnabok
// Edited: 2026-11-18T16:31:27-06:00 | Computer: Vinnabok
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using FortuneForge.Games.Abstractions.Auditing;
using FortuneForge.Games.JadeDragon;
using FortuneForge.Games.NeonNights;
using FortuneForge.Games.Slots;
using FortuneForge.Server.Accounts.Models;
using FortuneForge.Server.Slots.Bonuses;
using FortuneForge.Server.Slots.Configuration;
using FortuneForge.Server.Slots.Games.GoldFoundry;
using FortuneForge.Server.Slots.Models;
using FortuneForge.Server.RoundAudit;
using Google.Cloud.Firestore;

namespace FortuneForge.Server.Accounts.Storage;

public sealed partial class FirestoreAccountStore
{
    internal const long SlotOutcomeSchemaVersion = 7;

    public Task<SlotSpinSettlement> RecordSlotSpinAsync(
        string userId,
        SpinResult result,
        SlotSpinAdmission admission,
        DateTime createdAtUtc,
        CancellationToken cancellationToken)
    {
        RejectRemovedSlotRuntime(result.GameId);
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
        var isJadeCascade = result.EngineAudit.EngineVersion == JadeDragonCascadeRules.Version;
        if (isJadeCascade || admission.MathModelId == JadeDragonCascadeRules.Version ||
            result.JadeDragonCascade is not null || result.PayoutPointValueInCents is not null)
        {
            if (!isJadeCascade || result.GameId != JadeDragonEngine.GameId ||
                admission.MathModelId != JadeDragonCascadeRules.Version ||
                result.IsFreeSpin != admission.IsFreeSpin || admission.SpecialBoostApplied ||
                admission.FreeSpinFeatureMode is not null ||
                result.JadeDragonCascade is null ||
                result.PointValueInCents != JadeDragonCascadeRules.WagerPointValueInCents ||
                result.PayoutPointValueInCents != JadeDragonCascadeRules.PayoutPointValueInCents ||
                result.ChargedWagerCents != admission.ChargedWagerCents ||
                admission.ChargedWagerCents != (admission.IsFreeSpin ? 0 :
                    RandMoney.PointsToCents(result.WagerPoints, result.PointValueInCents)))
                throw new InvalidOperationException("Jade Dragon's cascade does not match its admitted wager or engine.");
        }
        var isOlympusTumble = OlympusTumbleDemo.SupportsEngineVersion(result.EngineAudit.EngineVersion);
        if (isOlympusTumble || OlympusTumbleDemo.SupportsEngineVersion(admission.MathModelId))
        {
            if (result.GameId != SlotSpecialRoundProfiles.GodsOfOlympusGameId ||
                !OlympusTumbleDemo.SupportsPaidEngineVersion(result.EngineAudit.EngineVersion) ||
                result.EngineAudit.EngineVersion != admission.MathModelId ||
                admission.IsFreeSpin || result.IsFreeSpin || admission.SpecialBoostApplied ||
                admission.FreeSpinsRemaining != 0 || admission.FreeSpinFeatureMode is not null ||
                admission.ChargedWagerCents <= 0 ||
                admission.ChargedWagerCents != RandMoney.PointsToCents(result.WagerPoints, result.PointValueInCents))
                throw new InvalidOperationException("Olympus tumble outcome differs from its admitted wager or model.");
        }
        else if (!isJadeCascade && admission.MathModelId is not null &&
            (result.PaytableId != admission.MathModelId ||
             result.EngineAudit.BaseGame.Math.PaytableId != admission.MathModelId ||
             result.IsFreeSpin != admission.IsFreeSpin ||
             result.EngineAudit.IsDerivedFreeSpin != admission.IsFreeSpin))
            throw new InvalidOperationException("Midnight Vault's outcome differs from its admitted payout model or bonus entitlement.");
        var isMidnightGame = result.GameId == SlotSpecialRoundProfiles.DragonHoardGameId;
        var isGoldFoundry = result.GameId == GoldFoundrySlotDefinitions.GameId;
        var isNeon = result.GameId == SlotSpecialRoundProfiles.NeonNightsGameId;
        if (isGoldFoundry && result.ChargedWagerCents != admission.ChargedWagerCents)
            throw new InvalidOperationException("Gold Foundry's outcome differs from its admitted wager charge.");
        var isNeonDiscoPlay = isNeon && admission.IsNeonDiscoPlay;
        var isNeonCarPlay = isNeon && NeonCarRoundState.IsCarPlay(admission.IsFreeSpin, admission.FreeSpinFeatureMode);
        if (isNeon && result.NeonNitroWild?.RequiresChoice == true)
            throw new InvalidOperationException("A pending Nitro reel choice cannot settle a wager.");
        var isNeonNitroResolved = isNeon && result.NeonNitroWild is
            { SecondNitro: not null, ExpandedReel: not null, RequiresChoice: false };
        var neonCarSource = isNeonCarPlay
            ? Slots.Games.NeonNights.NeonNightsEngine.ToPackage(result, result.EngineAudit)
            : null;
        if (!isNeon && admission.IsNeonDiscoPlay)
            throw new InvalidOperationException("A Disco admission is only valid for Neon Nights.");
        if (isNeon &&
            (result.IsFreeSpin != admission.IsFreeSpin ||
             result.EngineAudit.FreeSpinFeatureMode != admission.FreeSpinFeatureMode ||
             (result.NeonDiscoCascade is not null) != admission.IsNeonDiscoPlay ||
             isNeonDiscoPlay && (result.EngineAudit.NeonDiscoCascade?.StartingMultiplier ??
                 NeonDiscoCascade.InitialMultiplier) != admission.NeonDiscoMultiplier ||
             admission.ChargedWagerCents !=
             (NeonPlayPolicy.ChargesWager(admission.IsFreeSpin, admission.FreeSpinFeatureMode)
                 ? RandMoney.PointsToCents(result.WagerPoints, result.PointValueInCents) : 0) ||
             admission.IsFreeSpin && result.FreeSpinsAwarded != 0 &&
             (result.FreeSpinsAwarded != NeonArcadeSlotDefinitions.SpecialPlaySpins ||
              !NeonArcadeSlotDefinitions.UsesCurrentDiscoMath(result.EngineAudit.EngineVersion))))
            throw new InvalidOperationException("Neon's bonus outcome differs from its accepted admission.");
        var ignoreLegacyEnergy = isNeon ||
            result.GameId == SlotSpecialRoundProfiles.RainbowRealmGameId && result.EngineAudit.BaseGame.Energy is null;
        if (isGoldFoundry)
            GoldFoundryCrucible.ValidatePaidOutcome(result, admission);
        if (isMidnightGame &&
            (admission.IsFreeSpin != (admission.BonusRound is not null) ||
             admission.BonusRound?.FeatureMode != admission.FreeSpinFeatureMode ||
             result.EngineAudit.FreeSpinFeatureMode != admission.FreeSpinFeatureMode))
            throw new InvalidOperationException("Midnight Vault's outcome differs from its admitted bonus mode.");
        var frozenSettlementField = isMidnightGame ? "midnightSettlementJson" :
            isGoldFoundry ? "goldFoundrySettlementJson" :
            isOlympusTumble ? "olympusTumbleSettlementJson" :
            isJadeCascade ? "jadeDragonCascadeSettlementJson" :
            isNeonDiscoPlay ? "neonDiscoSettlementJson" :
            isNeonNitroResolved ? "neonNitroSettlementJson" : "slotSettlementJson";
        var engineOutcomeSha256 = EngineOutcomeSha256(result);

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
        var freeGamesCurrencyId = GameCurrencyId(FreeGamesCurrencyId, result.GameId);
        var specialPointsCurrencyId = GameCurrencyId(SpecialPointsCurrencyId, result.GameId);
        var energyCurrencyId = GameCurrencyId(EnergyCurrencyId, result.GameId);
        var freeGamesReference = BalanceDocument(userId, freeGamesCurrencyId);
        var specialPointsReference = BalanceDocument(userId, specialPointsCurrencyId);
        var energyReference = BalanceDocument(userId, energyCurrencyId);
        var guardReference = SlotSpinGuardDocument(userId, result.GameId);
        var spinKey = result.SpinId.ToString("N");
        var freeGamesAwardTransactionReference = BalanceTransactionDocument(
            $"{spinKey}-free-games-award");
        var specialPointsAwardTransactionReference = BalanceTransactionDocument(
            $"{spinKey}-special-points-award");
        var energyAwardTransactionReference = BalanceTransactionDocument(
            $"{spinKey}-energy-award");
        var energyResetTransactionReference = BalanceTransactionDocument(
            isGoldFoundry ? $"{spinKey}-energy-crucible-pour" : $"{spinKey}-energy-multiplier-use");
        var payoutPointValueInCents = result.PayoutPointValueInCents ?? result.PointValueInCents;

        Task<WalletRoundSettlementReceipt> SettleWalletAsync(Transaction transaction, long payoutCents, bool requireExisting = false) =>
            FirestoreWalletRoundSettlementStore.StageAsync(database, transaction,
                new(result.GameId, userId, spinKey, engineOutcomeSha256, chargedWagerCents, payoutCents),
                new DateTimeOffset(createdAtUtc), cancellationToken, WalletRoundLedgerLayout.LegacySlot(result.SpinId), requireExisting);

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
                    NeonDiscoRoundState.ReadAdmittedPlay(field => admissionSnapshot.ContainsField(field) &&
                        admissionSnapshot.GetValue<bool>(field)) != admission.IsNeonDiscoPlay ||
                    isNeon && NeonDiscoRoundState.ReadAdmittedMultiplier(field =>
                        ReadNeonDiscoField(admissionSnapshot, field)) != admission.NeonDiscoMultiplier ||
                    admissionSnapshot.GetValue<bool>("specialBoostApplied") != result.SpecialBoostApplied ||
                    (ReadString(admissionSnapshot, "mathModelId") ?? string.Empty) != (admission.MathModelId ?? string.Empty) ||
                    (isMidnightGame && ReadString(admissionSnapshot, "freeSpinFeatureMode") != admission.FreeSpinFeatureMode) ||
                    (isOlympusTumble && (ReadLong(admissionSnapshot, "freeSpinsBefore") != 0 ||
                        ReadLong(admissionSnapshot, "energyBalanceBefore") != admission.EnergyBalance)) ||
                    (isJadeCascade &&
                        (ReadLong(admissionSnapshot, "freeSpinsBefore") !=
                            admission.FreeSpinsRemaining + (isFreeSpin ? 1L : 0L) ||
                         ReadLong(admissionSnapshot, "energyBalanceBefore") != admission.EnergyBalance ||
                         ReadString(admissionSnapshot, "freeSpinFeatureMode") is not null)) ||
                    admissionSnapshot.GetValue<long>("pityMissesBefore") != admission.PityMissesBefore)
                {
                    throw new InvalidOperationException("Slot admission receipt does not match the engine result.");
                }
                if (admissionSnapshot.ContainsField(PreparedOutcomeSha256Field) &&
                    ReadString(admissionSnapshot, PreparedOutcomeSha256Field) != engineOutcomeSha256)
                    throw new InvalidOperationException("The slot result differs from the prepared outcome.");
                using var preparedObservation = admission.PreparedRequestObservation is { } originalRequest
                    ? GameSecurityContext.Push(originalRequest) : null;
                var admissionStatus = admissionSnapshot.GetValue<string>("status");
                if (admissionStatus != "admitted" &&
                    !(admissionStatus == "settled" && existingResult.Exists &&
                        admissionSnapshot.GetValue<string>("spinId") == result.SpinId.ToString("N")))
                {
                    throw new InvalidOperationException("Slot admission has already been used.");
                }
                var pendingNitroJson = isNeon ? ReadString(admissionSnapshot, "pendingNeonNitroOutcomeJson") : null;
                if (pendingNitroJson is not null)
                {
                    if (!isNeonNitroResolved || result.NeonNitroWild?.ExpandedReel is not { } chosenReel ||
                        ReadString(admissionSnapshot, "pendingNeonNitroSpinId") != result.SpinId.ToString("N"))
                        throw new InvalidOperationException("The Nitro settlement has no matching pending choice.");
                    var pendingNitro = JsonSerializer.Deserialize<NeonSpinOutcome>(pendingNitroJson)
                        ?? throw new InvalidOperationException("The saved pending Nitro spin is invalid.");
                    var expectedNitro = FortuneForge.Games.NeonNights.NeonNightsEngine.ResolveNitroChoice(
                        pendingNitro, chosenReel);
                    var actualNitro = Slots.Games.NeonNights.NeonNightsEngine.ToPackage(result, result.EngineAudit);
                    if (JsonSerializer.Serialize(expectedNitro) != JsonSerializer.Serialize(actualNitro))
                        throw new InvalidOperationException("The Nitro choice differs from its frozen original spin.");
                }
                if (existingResult.Exists &&
                    (isMidnightGame || isOlympusTumble || isJadeCascade || isNeonDiscoPlay ||
                        ReadString(existingResult, "engineOutcomeSha256") is not null))
                {
                    if (admissionStatus != "settled" || ReadString(existingResult, "userId") != userId ||
                        ReadString(existingResult, "admissionId") != admission.AdmissionId.ToString("N") ||
                        ReadString(existingResult, "gameId") != result.GameId ||
                        ReadString(existingResult, "engineOutcomeSha256") != engineOutcomeSha256 ||
                        ReadString(existingResult, frozenSettlementField) is not { } frozenJson)
                        throw new InvalidOperationException("The slot retry differs from its original engine outcome or has no frozen settlement.");
                    var original = JsonSerializer.Deserialize<SlotSpinSettlement>(frozenJson)
                        ?? throw new InvalidOperationException("The original slot settlement is invalid.");
                    if (ReadString(existingResult, "walletRoundSettlementId") is { } walletId)
                    {
                        var originalPayoutCents = checked(RandMoney.PointsToCents(original.Payout.TotalPoints,
                            payoutPointValueInCents) + (original.CruciblePayoutCents ?? 0));
                        var receipt = await SettleWalletAsync(transaction, originalPayoutCents, requireExisting: true);
                        if (receipt.SettlementId != walletId || receipt.InsufficientFunds ||
                            RandMoney.CentsToRand(receipt.BalanceAfterCents) != original.SlotsCreditsBalance)
                            throw new InvalidOperationException("The original slot settlement differs from its wallet receipt.");
                    }
                    return original;
                }
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
                var currentFreeGames = ReadLong(freeGamesSnapshot, "available");
                var currentSpecialPoints = ReadLong(specialPointsSnapshot, "available");
                var currentEnergy = ignoreLegacyEnergy ? 0 : ReadLong(energySnapshot, "available");
                var neonDiscoBefore = isNeon ? ReadNeonDiscoRound(guardSnapshot) : NeonDiscoRoundState.Inactive;
                var neonCarBefore = isNeon ? ReadNeonCarRound(guardSnapshot) : NeonCarRoundState.Inactive;
                if (existingResult.Exists)
                {
                    var wallet = await transaction.GetSnapshotAsync(BalanceDocument(userId, SlotsCreditsCurrencyId), cancellationToken);
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
                        ReadRandBalance(wallet),
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
                        checked((int)ReadLong(existingResult, "sealFreeSpinsAwarded")))
                    {
                        NeonDiscoSpinsRemaining = neonDiscoBefore.SpinsRemaining,
                        NeonDiscoWagerPoints = neonDiscoBefore.LockedWagerPoints,
                        NeonDiscoMultiplier = isNeon ? neonDiscoBefore.Multiplier : null,
                        NeonDiscoRoundWinPoints = neonDiscoBefore.ReceiptWinPoints(),
                        NeonCarRespinOffer = neonCarBefore.OfferForSpin(result.SpinId),
                        NeonCarRoundWinPoints = isNeonCarPlay ? neonCarBefore.WinPointsForRound(
                            NeonCarRoundState.ReadRoundId(field => ReadString(existingResult, field))) : null
                    };
                }

                if (isNeon)
                    neonDiscoBefore.ValidateSettlement(field => ReadNeonDiscoField(admissionSnapshot, field),
                        admission.NeonDiscoMultiplier, admission.IsNeonDiscoPlay,
                        isFreeSpin || currentFreeGames != 0, result.WagerPoints);

                if (ReadString(guardSnapshot, SlotSpinGuard.ActiveAdmissionField) != admission.AdmissionId.ToString("N") ||
                    ReadLong(guardSnapshot, "consecutiveFiveMisses") != admission.PityMissesBefore)
                {
                    throw new InvalidOperationException("Slot spin guard no longer matches the admitted spin.");
                }

                SlotSpecialRoundProfiles.TryGet(result.GameId, out var specialRoundProfile);
                if (result.GameId is SlotSpecialRoundProfiles.RainbowRealmGameId)
                    specialRoundProfile = result.EngineAudit.FeatureProfile ?? specialRoundProfile;
                if (isGoldFoundry && currentEnergy != admission.EnergyBalance)
                    throw new InvalidOperationException("The crucible charge differs from the admitted spin.");
                if (isOlympusTumble && (currentFreeGames != 0 || currentEnergy != admission.EnergyBalance))
                    throw new InvalidOperationException("Olympus legacy balances changed during the admitted spin.");
                if (isJadeCascade && (currentFreeGames != admission.FreeSpinsRemaining ||
                    currentEnergy != admission.EnergyBalance))
                    throw new InvalidOperationException("Jade Dragon's balances changed during the admitted spin.");
                var crucibleWageredBeforeCents = isGoldFoundry ? ReadLong(guardSnapshot, "crucibleWageredCents") : 0;
                var crucible = isGoldFoundry
                    ? GoldFoundryCrucible.Settle(currentEnergy, result.EnergyAwarded,
                        crucibleWageredBeforeCents, chargedWagerCents)
                    : null;
                var energyBonus = isNeon ? new EnergyBonusSettlement(result.Payout, 0, 0, 0, false, 1m) : isOlympusTumble || isJadeCascade
                    ? new EnergyBonusSettlement(result.Payout, currentEnergy, currentEnergy, 0, false, 1m)
                    : crucible?.AsEnergySettlement(result.Payout) ?? EnergyBonus.Settle(
                        specialRoundProfile?.UsesEnergy != false ? currentEnergy : 0,
                        result.EnergyAwarded, result.Payout);
                var piratesGemCountsBefore = result.GameId == SlotSpecialRoundProfiles.PiratesFortuneGameId
                    ? ReadLongMap(guardSnapshot, "sealCounts") : null;
                var piratesGemWagerCentsBefore = piratesGemCountsBefore is not null
                    ? ReadSealWagerCents(guardSnapshot) : null;
                var isMidnight = result.GameId == SlotSpecialRoundProfiles.DragonHoardGameId;
                SealCollectionSettlement sealSettlement;
                if (isMidnight)
                {
                    var before = ReadMidnightRoundsField(admissionSnapshot, "midnightBonusRoundsBefore");
                    var afterConsumption = ReadMidnightRoundsField(admissionSnapshot, "midnightBonusRoundsAfter");
                    var selected = admissionSnapshot.GetValue<bool>("isFreeSpin")
                        ? ReadMidnightRound(admissionSnapshot.GetValue<Dictionary<string, object>>("midnightAdmittedRound")) : null;
                    var storedQueue = ReadMidnightBonusRounds(guardSnapshot, currentFreeGames, result.PointValueInCents);
                    if (!before.SequenceEqual(admission.BonusRoundsBeforeAdmission) || selected != admission.BonusRound ||
                        selected?.FeatureMode != admission.FreeSpinFeatureMode ||
                        result.EngineAudit.FreeSpinFeatureMode != selected?.FeatureMode ||
                        !storedQueue.SequenceEqual(afterConsumption))
                        throw new InvalidOperationException("The admitted Midnight Vault bonus round does not match its saved queue.");
                    sealSettlement = SettleMidnightBonusRounds(guardSnapshot, result, before, afterConsumption, selected);
                }
                else if (isNeon) sealSettlement = new(new Dictionary<string, long>(),
                    new Dictionary<string, long>(), 0, null, 0, [], false);
                else sealSettlement = SettleSealCollections(guardSnapshot, result, energyBonus.MultiplierApplied,
                    specialRoundProfile, freezeProgress: isOlympusTumble || isJadeCascade);
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
                var outcomeFindings = SlotRoundAudit.Check(result, settledPayout, isPaid: true);
                var outcomeVerified = !outcomeFindings.Any(finding => finding.Severity == "error");
                if ((isOlympusTumble || isNeonDiscoPlay || isNeonCarPlay || isJadeCascade) && !outcomeVerified)
                    throw new InvalidOperationException("The paid feature outcome could not be verified.");
                var payoutCents = checked(RandMoney.PointsToCents(
                    settledPayout.TotalPoints, payoutPointValueInCents) + (crucible?.PayoutCents ?? 0));
                var walletSettlement = await SettleWalletAsync(transaction, payoutCents);
                if (walletSettlement.WasAlreadySettled)
                    throw new InvalidOperationException("The wallet has a settlement for a slot spin with no result.");
                if (walletSettlement.InsufficientFunds)
                    throw new InsufficientSlotCreditsException(RandMoney.CentsToRand(walletSettlement.BalanceBeforeCents),
                        RandMoney.CentsToRand(chargedWagerCents));

                var netCents = checked(payoutCents - chargedWagerCents);
                var isWin = settledPayout.TotalPoints > 0 || crucible?.PayoutCents > 0;
                var wagerRand = RandMoney.CentsToRand(chargedWagerCents);
                var payoutRand = RandMoney.CentsToRand(payoutCents);
                var netRand = RandMoney.CentsToRand(netCents);
                var balanceAfterPayoutRand = RandMoney.CentsToRand(walletSettlement.BalanceAfterCents);
                var totalFreeSpinsAwarded = checked(
                    result.FreeSpinsAwarded + sealSettlement.FreeSpinsAwarded);
                var midnightProgress = sealSettlement.MidnightProgress;
                var neonProgress = isNeon ? NeonPlayPolicy.AdvanceAwardedPlays(
                    isFreeSpin ? currentFreeGames : 0, isFreeSpin, activeFreeSpinFeatureMode,
                    result.FreeSpinsAwarded, result.WagerPoints, result.NeonArcade) : null;
                var freeGamesRemaining = midnightProgress is null
                    ? neonProgress?.Remaining ?? checked((isFreeSpin ? currentFreeGames : 0) + totalFreeSpinsAwarded)
                    : midnightProgress.Rounds.Sum(round => (long)round.RemainingSpins);
                var specialPointsBalance = checked(currentSpecialPoints + result.SpecialPointsAwarded);
                var energyBalance = energyBonus.FinalEnergyBalance;
                var nextFreeSpinFeatureMode = freeGamesRemaining > 0
                    ? sealSettlement.FreeSpinFeatureMode ?? (isFreeSpin ? activeFreeSpinFeatureMode : null)
                    : null;
                if (neonProgress is not null) nextFreeSpinFeatureMode = neonProgress.FeatureMode;
                if (midnightProgress is not null) nextFreeSpinFeatureMode = midnightProgress.Rounds.FirstOrDefault()?.FeatureMode;
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
                var neonDiscoAfter = isNeon
                    ? neonDiscoBefore.Settle(isNeonDiscoPlay, result.WagerPoints, settledPayout.TotalPoints,
                        result.NeonDiscoCascade, result.NeonArcade)
                    : NeonDiscoRoundState.Inactive;
                var neonCarAfter = neonCarBefore.Advance(result.SpinId, neonCarSource, neonProgress?.FeatureMode,
                    settledPayout.TotalPoints);
                var settlement = new SlotSpinSettlement(
                    balanceAfterPayoutRand, midnightProgress is null ? checked((int)freeGamesRemaining) :
                        midnightProgress.Rounds.FirstOrDefault()?.RemainingSpins ?? 0,
                    checked((int)specialPointsBalance), energyBalance, settledPayout, energyBonus.MultiplierApplied,
                    energyBonus.PayoutMultiplier, nextFreeSpinWagerPoints > 0 ? nextFreeSpinWagerPoints : null,
                    sealSettlement.Collections, nextFreeSpinFeatureMode, sealSettlement.FreeSpinsAwarded)
                {
                    BonusRound = midnightProgress?.Rounds.FirstOrDefault(),
                    QueuedBonusRounds = midnightProgress?.Rounds.Skip(1).ToArray() ?? [],
                    AwardedBonusRounds = midnightProgress?.AwardedRounds ?? [],
                    CrucibleWageredCents = crucible?.WageredCents,
                    CruciblePourValueCents = crucible?.PourValueCents,
                    CruciblePoured = crucible?.Poured,
                    CruciblePayoutCents = crucible?.PayoutCents,
                    NeonDiscoSpinsRemaining = neonDiscoAfter.SpinsRemaining,
                    NeonDiscoWagerPoints = neonDiscoAfter.LockedWagerPoints,
                    NeonDiscoMultiplier = isNeon ? neonDiscoAfter.Multiplier : null,
                    NeonDiscoRoundWinPoints = neonDiscoAfter.ReceiptWinPoints(isNeonDiscoPlay),
                    NeonCarRespinOffer = neonCarAfter.AvailableOffer(),
                    NeonCarRoundWinPoints = isNeonCarPlay ? neonCarAfter.WinPoints : null
                };

                if (FortuneForge.Server.Activity.AcceptedGameActivityReader.SlotIds.TryGetValue(result.GameId, out var catalogueGameId))
                {
                    var payload = new { result, admissionId = admission.AdmissionId, admission.StartedAtUtc,
                        admission.RequestId, engineAudit = result.EngineAudit, rulesSha256,
                        settledPayout, isFreeSpin, isNeonDiscoPlay, activeFreeSpinFeatureMode,
                        neonDiscoSpinsBefore = neonDiscoBefore.SpinsRemaining,
                        neonDiscoSpinsRemaining = neonDiscoAfter.SpinsRemaining,
                        admittedMathModelId = admission.MathModelId,
                        midnightBonusEvidence = sealSettlement.MidnightEvidence,
                        energyBonus.PayoutMultiplier, piratesGemEvidence,
                        outcomeSchemaVersion = SlotOutcomeSchemaVersion };
                    object evidence = payload;
                    if (isNeon || crucible is not null)
                    {
                        var json = JsonSerializer.SerializeToNode(payload, FortuneForge.Games.Abstractions.Auditing.RoundAuditCapture.Json)!.AsObject();
                        if (isNeon)
                        {
                            json["neonDiscoMultiplierBefore"] = neonDiscoBefore.Multiplier;
                            json["neonDiscoWagerPointsBefore"] = neonDiscoBefore.LockedWagerPoints;
                            json["neonDiscoWagerPoints"] = neonDiscoAfter.LockedWagerPoints;
                            json["neonDiscoMultiplier"] = neonDiscoAfter.Multiplier;
                        }
                        if (crucible is not null)
                            json["crucible"] = JsonSerializer.SerializeToNode(
                                new { wageredBeforeCents = crucibleWageredBeforeCents, settlement = crucible },
                                FortuneForge.Games.Abstractions.Auditing.RoundAuditCapture.Json);
                        evidence = json;
                    }
                    FortuneForge.Server.RoundAudit.SinglePlayerRoundRecording.Write(transaction, database,
                        catalogueGameId, result.SpinId.ToString("N"), userId, chargedWagerCents, payoutCents,
                        admission.StartedAtUtc, createdAtUtc, "slotSpinResults", result.SpinId.ToString("N"),
                        evidence,
                        "slot-reels-paylines-and-features-v1", outcomeVerified,
                        SlotRoundAudit.AuditorVersion, outcomeFindings);
                }

                if (FortuneForge.Server.Activity.AcceptedGameActivityReader.SlotIds.TryGetValue(result.GameId, out var activityGameId))
                {
                    var participation = FortuneForge.Games.Abstractions.Analytics.AcceptedGameParticipationDocuments.Create(
                        userId, activityGameId, createdAtUtc);
                    transaction.Set(database.Collection("catalogPlayerActivity").Document(participation.DocOwnerKey)
                        .Collection("games").Document(participation.GameId), participation.Fields, SetOptions.MergeAll);
                }

                transaction.Update(admissionReference, new Dictionary<string, object>
                {
                    ["status"] = "settled",
                    ["spinId"] = result.SpinId.ToString("N"),
                    ["engineGeneratedAtUtc"] = Timestamp.FromDateTime(result.EngineAudit.GeneratedAtUtc),
                    ["settledAtUtc"] = Timestamp.FromDateTime(createdAtUtc),
                    ["pityMissesAfter"] = result.ConsecutiveFiveMisses,
                    ["rulesSha256"] = rulesSha256
                });

                var resultData = new Dictionary<string, object>
                {
                    ["spinId"] = result.SpinId.ToString("N"),
                    ["admissionId"] = admission.AdmissionId.ToString("N"),
                    ["requestId"] = admission.RequestId ?? string.Empty,
                    ["userId"] = userId,
                    ["gameId"] = result.GameId,
                    ["reelSetId"] = result.ReelSetId,
                    ["symbolSetId"] = result.SymbolSetId,
                    ["paytableId"] = result.PaytableId,
                    ["mathModelId"] = admission.MathModelId ?? string.Empty,
                    ["reelStops"] = result.ReelStops.Select(static stop => (long)stop).ToArray(),
                    ["visibleSymbolWindow"] = CreateVisibleSymbolWindowData(result.Reels),
                    ["payoutBreakdown"] = CreatePayoutBreakdownData(settledPayout),
                    ["wageredSlotsCredits"] = (double)wagerRand,
                    ["payoutWagerPoints"] = result.WagerPoints,
                    ["wonSlotsCredits"] = (double)payoutRand,
                    ["netSlotsCredits"] = (double)netRand,
                    ["isFreeSpin"] = isFreeSpin,
                    ["isNeonDiscoPlay"] = isNeonDiscoPlay,
                    ["neonDiscoSpinsRemaining"] = neonDiscoAfter.SpinsRemaining,
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
                };
                resultData["walletRoundSettlementId"] = walletSettlement.SettlementId;
                resultData["engineOutcomeSha256"] = engineOutcomeSha256;
                resultData[frozenSettlementField] = JsonSerializer.Serialize(settlement);
                if (neonCarSource is not null)
                    neonCarAfter.WriteSpinResult(resultData, neonCarSource);
                if (pendingNitroJson is not null)
                {
                    resultData["neonNitroChosenReel"] = (long)result.NeonNitroWild!.ExpandedReel!.Value;
                    resultData["neonNitroFinalOutcomeJson"] = JsonSerializer.Serialize(
                        Slots.Games.NeonNights.NeonNightsEngine.ToPackage(result, result.EngineAudit));
                }
                if (isJadeCascade)
                    resultData["payoutPointValueInCents"] = JadeDragonCascadeRules.PayoutPointValueInCents;
                if (crucible is not null)
                {
                    resultData["crucibleWageredBeforeCents"] = crucibleWageredBeforeCents;
                    resultData["crucibleWageredCents"] = crucible.WageredCents;
                    resultData["cruciblePayoutCents"] = crucible.PayoutCents;
                    resultData["cruciblePoured"] = crucible.Poured;
                }
                transaction.Create(resultReference, resultData);
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

                var guardData = new Dictionary<string, object>
                {
                    [SlotSpinGuard.ActiveAdmissionField] = string.Empty,
                    ["consecutiveFiveMisses"] = result.ConsecutiveFiveMisses,
                    ["updatedAt"] = Timestamp.FromDateTime(createdAtUtc)
                };
                if (isNeon)
                {
                    neonDiscoAfter.WriteGuard(guardData);
                    neonCarAfter.WriteGuard(guardData);
                    guardData["pendingNeonNitroChoiceJson"] = string.Empty;
                    guardData["pendingNeonNitroAdmissionId"] = string.Empty;
                }
                if (crucible is not null)
                    guardData["crucibleWageredCents"] = crucible.WageredCents;
                if (midnightProgress is not null || totalFreeSpinsAwarded > 0 ||
                    sealSettlement.SealsChanged ||
                    (!isFreeSpin && currentFreeGames > 0) ||
                    (isFreeSpin && currentFreeGames == 0 &&
                        (isNeon || isJadeCascade || !string.IsNullOrWhiteSpace(activeFreeSpinFeatureMode))))
                {
                    guardData["userId"] = userId;
                    guardData["freeSpinWagerPoints"] = nextFreeSpinWagerPoints;
                    guardData["freeSpinWagerCents"] = nextFreeSpinWagerCents;
                    guardData["freeSpinFeatureMode"] = nextFreeSpinFeatureMode ?? string.Empty;
                    guardData["freeSpinMathModelId"] = freeGamesRemaining > 0 ? admission.MathModelId ?? string.Empty : string.Empty;
                    guardData["sealCounts"] = sealSettlement.SealCounts.ToDictionary(
                        pair => pair.Key, pair => (object)pair.Value, StringComparer.Ordinal);
                    guardData["sealWagerCents"] = sealSettlement.SealWagerCents.ToDictionary(
                        pair => pair.Key, pair => (object)pair.Value, StringComparer.Ordinal);
                    if (midnightProgress is not null)
                        foreach (var pair in MidnightBonusGuardData(midnightProgress.Rounds, result.PointValueInCents)) guardData[pair.Key] = pair.Value;
                }
                if (ReadString(guardSnapshot, SlotSpinGuard.PreparedAdmissionField) == admission.AdmissionId.ToString("N"))
                    guardData[SlotSpinGuard.PreparedAdmissionField] = FieldValue.Delete;
                transaction.Set(guardReference, guardData, SetOptions.MergeAll);

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
                    energyBonus.MultiplierApplied || crucible?.Poured == true ||
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

                if (energyBonus.MultiplierApplied || crucible?.Poured == true)
                {
                    transaction.Create(
                        energyResetTransactionReference,
                        BalanceTransactionData(
                            energyResetTransactionReference.Id,
                            userId,
                            energyCurrencyId,
                            -energyBonus.MeterBalanceBeforeReset,
                            energyBalance,
                            isGoldFoundry ? "energy-crucible-pour" : "energy-multiplier-use",
                            energyResetTransactionReference.Id,
                            createdAtUtc));
                }

                return settlement;
            },
            cancellationToken: cancellationToken);
    }

    private static string EngineOutcomeSha256(SpinResult result)
    {
        var snapshot = new
        {
            result.SpinId, result.GameId, result.ReelSetId, result.SymbolSetId, result.PaytableId,
            result.WagerPoints, result.PointValueInCents, result.ReelStops, result.Reels, result.Payout,
            result.ConsecutiveFiveMisses, result.FiveMatchPityTriggered, result.FreeSpinsAwarded,
            result.SpecialPointsAwarded, result.EnergyAwarded, result.SpecialBoostApplied,
            result.MonkeyPawCount, result.MoneyGrabPoints, result.BananaBonusPoints,
            SealsAwarded = result.SealsAwarded.OrderBy(pair => pair.Key, StringComparer.Ordinal),
            EngineAudit = result.EngineAudit
        };
        var json = result.EngineAudit?.EngineVersion switch
        {
            var version when OlympusTumbleDemo.SupportsPaidEngineVersion(version) =>
                JsonSerializer.Serialize(new { snapshot, result.OlympusTumble }),
            _ when result.NeonDiscoCascade is not null =>
                JsonSerializer.Serialize(new { snapshot, result.NeonDiscoCascade }),
            _ when result.NeonNitroWild?.SecondNitro is not null =>
                JsonSerializer.Serialize(new { snapshot, result.NeonNitroWild }),
            JadeDragonCascadeRules.Version =>
                JsonSerializer.Serialize(new { snapshot, result.JadeDragonCascade, result.PayoutPointValueInCents }),
            _ => JsonSerializer.Serialize(snapshot)
        };
        return Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(json)));
    }

    internal static Dictionary<string, object> CreateVisibleSymbolWindowData(IReadOnlyList<IReadOnlyList<string>> reels) =>
        SlotSpinResultFields.VisibleSymbolWindow(reels);

    internal static Dictionary<string, object> CreatePayoutBreakdownData(SpinPayout payout) =>
        SlotSpinResultFields.PayoutBreakdown(payout);
}
