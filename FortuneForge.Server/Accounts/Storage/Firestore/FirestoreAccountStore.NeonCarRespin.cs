using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using FortuneForge.Games.NeonNights;
using FortuneForge.Games.NeonNights.Server;
using FortuneForge.Server.Accounts.Models;
using FortuneForge.Server.RoundAudit;
using FortuneForge.Server.Slots.Bonuses;
using FortuneForge.Server.Slots.Configuration;
using FortuneForge.Server.Slots.Reels;
using Google.Cloud.Firestore;

namespace FortuneForge.Server.Accounts.Storage;

public sealed partial class FirestoreAccountStore
{
    private static NeonCarRoundState ReadNeonCarRound(DocumentSnapshot guard) =>
        NeonCarRoundState.ReadGuard(field => ReadString(guard, field), field => ReadLong(guard, field));

    public Task<NeonCarRespinSettlement> RespinNeonCarReelAsync(
        string userId, Guid spinId, int reelIndex, CancellationToken cancellationToken) =>
        ResolveNeonCarRespinAsync(userId, spinId, reelIndex, null, cancellationToken);

    public Task<NeonCarRespinSettlement> FinalizeNeonCarRespinNitroChoiceAsync(
        string userId, Guid spinId, int reelIndex, CancellationToken cancellationToken) =>
        ResolveNeonCarRespinAsync(userId, spinId, null, reelIndex, cancellationToken);

    private Task<NeonCarRespinSettlement> ResolveNeonCarRespinAsync(
        string userId, Guid spinId, int? initialReelIndex, int? selectedNitroReel, CancellationToken cancellationToken)
    {
        if (spinId == Guid.Empty || initialReelIndex is { } initial && !NeonNitroChoice.IsChoiceReel(initial) ||
            selectedNitroReel is { } selected && !NeonNitroChoice.IsChoiceReel(selected) ||
            (initialReelIndex is null) == (selectedNitroReel is null))
            throw new NeonCarRespinConflictException("Choose an available reel from the latest Car Play.");

        var sourceReference = SlotSpinResultDocument(spinId);
        var sourceSpinId = sourceReference.Id;
        var guardReference = SlotSpinGuardDocument(userId, SlotSpecialRoundProfiles.NeonNightsGameId);
        var walletReference = BalanceDocument(userId, SlotsCreditsCurrencyId);
        var freeGamesReference = BalanceDocument(userId,
            GameCurrencyId(FreeGamesCurrencyId, SlotSpecialRoundProfiles.NeonNightsGameId));
        var statisticsReference = StatisticsDocument(userId);
        var settledAtUtc = DateTime.UtcNow;
        return database.RunTransactionAsync(async transaction =>
        {
            var source = await transaction.GetSnapshotAsync(sourceReference, cancellationToken);
            if (ReadString(source, "userId") != userId ||
                ReadString(source, "gameId") != SlotSpecialRoundProfiles.NeonNightsGameId ||
                NeonCarRoundState.ReadFrozenSource(field => ReadString(source, field)) is not { } frozen)
                throw new NeonCarRespinConflictException("This Car Play is no longer available for a reel respin.");

            var (roundId, sourceJson, receiptId, usesLegacyRoundReceipt) = frozen;
            var receiptReference = database.Collection(NeonCarRespinReport.ReceiptCollection).Document(receiptId);
            var snapshots = await transaction.GetAllSnapshotsAsync(
                [receiptReference, guardReference, walletReference, freeGamesReference], cancellationToken);
            var receipt = snapshots[0];
            var guard = snapshots[1];
            var wallet = snapshots[2];
            var freeGames = snapshots[3];
            var balanceCents = ReadRandBalanceCents(wallet);
            var pendingNitroOffer = ReadPendingNeonNitroChoice(guard);
            var carRound = ReadNeonCarRound(guard);

            if (receipt.Exists)
            {
                if (ReadString(receipt, "sourceSpinId") != sourceSpinId ||
                    initialReelIndex is { } requestedInitial && ReadLong(receipt, "reelIndex") != requestedInitial ||
                    selectedNitroReel is { } requestedTarget &&
                        (!receipt.ContainsField("neonNitroChosenReel") ||
                         ReadLong(receipt, "neonNitroChosenReel") != requestedTarget) ||
                    ReadString(receipt, "userId") != userId ||
                    ReadString(receipt, "outcomeJson") is not { } savedOutcomeJson)
                    throw new NeonCarRespinConflictException("This Car Play has already used its reel respin.");
                var savedOutcome = JsonSerializer.Deserialize<NeonCarRespinOutcome>(savedOutcomeJson)
                    ?? throw new InvalidOperationException("The saved Car respin result is invalid.");
                return ToSettlement(savedOutcome, balanceCents, alreadyApplied: true,
                    roundWinPoints: carRound.WinPointsForRound(roundId));
            }

            if (ReadString(guard, NeonCarRespinStore.WorkerOwnerField) is not null)
                throw new NeonCarRespinConflictException("Resume the current Car Play through the Neon Nights worker.");
            var original = JsonSerializer.Deserialize<NeonSpinOutcome>(sourceJson)
                ?? throw new InvalidOperationException("The saved Car spin source is invalid.");
            NeonCarRespinOutcome? pendingOutcome = null;
            if (pendingNitroOffer is { SourceKind: NeonNitroChoice.CarRespinSource })
            {
                if (pendingNitroOffer.SpinId != spinId ||
                    ReadString(guard, "pendingNeonNitroCarRespinJson") is not { } pendingJson)
                    throw new NeonCarRespinConflictException("Finish the pending Nitro choice first.");
                pendingOutcome = NeonNitroChoice.ReadSavedCarRespin(pendingJson, original, pendingNitroOffer);
                if (initialReelIndex is { } retryInitial)
                {
                    if (pendingNitroOffer.ReelIndex != retryInitial)
                        throw new NeonCarRespinConflictException("Finish the pending Nitro choice first.");
                    return ToSettlement(pendingOutcome, balanceCents, alreadyApplied: false,
                        pendingNitroOffer, carRound.WinPoints);
                }
                if (selectedNitroReel is { } targetReel &&
                    !pendingNitroOffer.Allows(spinId, NeonNitroChoice.CarRespinSource, targetReel))
                    throw new NeonCarRespinConflictException("The pending Nitro choice is no longer available.");
            }
            else if (selectedNitroReel is not null || pendingNitroOffer is not null)
                throw new NeonCarRespinConflictException("The pending Nitro choice is no longer available.");

            if ((initialReelIndex ?? pendingNitroOffer?.ReelIndex) is not { } offeredReel ||
                !carRound.CanRespin(roundId, spinId, offeredReel))
                throw new NeonCarRespinConflictException("Choose an available reel from the latest Car Play.");

            if (!NeonCarRespin.MatchesOfferedReel(original, spinId, offeredReel, usesLegacyRoundReceipt))
                throw new InvalidOperationException("The saved Car spin source does not match its offer.");

            var outcome = selectedNitroReel is { } target
                ? NeonCarRespin.ResolveChoice(original, pendingOutcome!, target)
                : new NeonCarRespin(new CryptoRandomIndexSource()).Resolve(original, offeredReel);
            var findings = NeonCarRespin.Check(original, outcome);
            if (findings.Count != 0)
                throw new InvalidOperationException("The Car reel respin could not be replayed.");

            if (outcome.NeonNitroWild?.RequiresChoice == true)
            {
                var pendingOffer = NeonNitroChoice.CreateCarRespinOffer(original, outcome,
                    checked((int)ReadLong(freeGames, "available")), carRound.WinPoints);
                transaction.Update(guardReference, new Dictionary<string, object>
                {
                    ["pendingNeonNitroChoiceJson"] = JsonSerializer.Serialize(pendingOffer),
                    ["pendingNeonNitroCarRespinJson"] = JsonSerializer.Serialize(outcome),
                    ["updatedAt"] = Timestamp.FromDateTime(settledAtUtc)
                });
                return ToSettlement(outcome, balanceCents, alreadyApplied: false,
                    pendingOffer, carRound.WinPoints);
            }

            var improvementCents = RandMoney.PointsToCents(outcome.ImprovementPoints, original.PointValueInCents);
            var newBalanceCents = checked(balanceCents + improvementCents);
            var outcomeJson = JsonSerializer.Serialize(outcome);
            var sourceSha256 = Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(sourceJson)));
            transaction.Create(receiptReference, new Dictionary<string, object>
            {
                ["userId"] = userId,
                ["roundId"] = roundId,
                ["sourceSpinId"] = sourceSpinId,
                ["reelIndex"] = (long)outcome.ReelIndex,
                ["outcomeJson"] = outcomeJson,
                ["sourceSha256"] = sourceSha256,
                ["improvementPoints"] = outcome.ImprovementPoints,
                ["improvementCents"] = improvementCents,
                ["neonNitroChosenReel"] = selectedNitroReel is { } chosenTarget ? (long)chosenTarget : -1L,
                ["createdAt"] = Timestamp.FromDateTime(settledAtUtc)
            });
            var completedRound = carRound.CompleteRespin(outcome.ImprovementPoints);
            var guardData = new Dictionary<string, object>
            {
                ["pendingNeonNitroChoiceJson"] = string.Empty,
                ["pendingNeonNitroCarRespinJson"] = string.Empty,
                ["updatedAt"] = Timestamp.FromDateTime(settledAtUtc)
            };
            completedRound.WriteRespinGuard(guardData);
            transaction.Update(guardReference, guardData);
            if (improvementCents > 0)
            {
                transaction.Update(walletReference, new Dictionary<string, object>
                {
                    ["available"] = newBalanceCents / RandMoney.CentsPerRand,
                    [AvailableFractionalCentsField] = newBalanceCents % RandMoney.CentsPerRand,
                    ["version"] = FieldValue.Increment(1),
                    ["updatedAt"] = Timestamp.FromDateTime(settledAtUtc)
                });
                var payoutReference = BalanceTransactionDocument($"{receiptId}-car-respin-payout");
                transaction.Create(payoutReference, RandBalanceTransactionData(
                    payoutReference.Id, userId, RandMoney.CentsToRand(improvementCents),
                    RandMoney.CentsToRand(newBalanceCents), "slot-car-respin-payout",
                    payoutReference.Id, settledAtUtc));
                transaction.Set(statisticsReference, new Dictionary<string, object>
                {
                    ["userId"] = userId,
                    ["creditsWon"] = FieldValue.Increment((double)RandMoney.CentsToRand(improvementCents)),
                    ["netCredits"] = FieldValue.Increment((double)RandMoney.CentsToRand(improvementCents)),
                    ["updatedAt"] = Timestamp.FromDateTime(settledAtUtc)
                }, SetOptions.MergeAll);
            }

            if (FortuneForge.Server.Activity.AcceptedGameActivityReader.SlotIds.TryGetValue(
                SlotSpecialRoundProfiles.NeonNightsGameId, out var catalogueGameId))
                SinglePlayerRoundRecording.Write(transaction, database, catalogueGameId,
                    $"{receiptId}-car-respin", userId, 0, improvementCents,
                    source.GetValue<Timestamp>("createdAt").ToDateTime(), settledAtUtc,
                    NeonCarRespinReport.ReceiptCollection, receiptReference.Id,
                    new { originalSpinId = spinId, sourceSha256, outcome, improvementCents },
                    NeonCarRespin.RuleVersion, true, NeonCarRespin.RuleVersion);

            return ToSettlement(outcome, newBalanceCents, alreadyApplied: false,
                roundWinPoints: completedRound.WinPoints);
        }, cancellationToken: cancellationToken);
    }

    public Task DismissNeonCarRespinAsync(string userId, Guid spinId, CancellationToken cancellationToken)
    {
        var guardReference = SlotSpinGuardDocument(userId, SlotSpecialRoundProfiles.NeonNightsGameId);
        return database.RunTransactionAsync(async transaction =>
        {
            var guard = await transaction.GetSnapshotAsync(guardReference, cancellationToken);
            if (ReadString(guard, NeonCarRespinStore.WorkerOwnerField) is not null)
                throw new NeonCarRespinConflictException("Resume the current Car Play through the Neon Nights worker.");
            var carRound = ReadNeonCarRound(guard);
            if (!carRound.CanDismiss(spinId, () => ReadPendingNeonNitroChoice(guard) is not null))
                return;
            var guardData = new Dictionary<string, object>
            {
                ["updatedAt"] = Timestamp.FromDateTime(DateTime.UtcNow)
            };
            carRound.ClearOffer().WriteOffer(guardData);
            transaction.Update(guardReference, guardData);
        }, cancellationToken: cancellationToken);
    }

    private static NeonCarRespinSettlement ToSettlement(NeonCarRespinOutcome outcome,
        long balanceCents, bool alreadyApplied, NeonNitroChoiceOffer? pendingNitroChoice = null,
        long? roundWinPoints = null) =>
        new(outcome.OriginalSpinId, outcome.ReelIndex, outcome.StopIndex, outcome.InitialReels,
            outcome.Reels, outcome.Payout, outcome.OriginalPayoutPoints, outcome.ImprovementPoints,
            RandMoney.CentsToRand(balanceCents), alreadyApplied)
        {
            NeonNitroWild = outcome.NeonNitroWild,
            PendingNeonNitroChoice = pendingNitroChoice,
            RoundWinPoints = roundWinPoints
        };
}
