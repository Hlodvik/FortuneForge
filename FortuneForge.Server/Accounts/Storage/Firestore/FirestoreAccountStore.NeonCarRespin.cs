using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using FortuneForge.Games.NeonNights;
using FortuneForge.Server.Accounts.Models;
using FortuneForge.Server.RoundAudit;
using FortuneForge.Server.Slots.Bonuses;
using FortuneForge.Server.Slots.Configuration;
using FortuneForge.Server.Slots.Reels;
using Google.Cloud.Firestore;

namespace FortuneForge.Server.Accounts.Storage;

public sealed partial class FirestoreAccountStore
{
    private static NeonCarRespinOffer? ReadNeonCarRespinOffer(DocumentSnapshot guard)
    {
        if (ReadString(guard, "activeAdmissionId") is not null ||
            ReadString(guard, "neonCarRespinRoundId") is null ||
            ReadString(guard, "neonCarRespinSourceSpinId") is not { } sourceId ||
            ReadString(guard, "neonCarRespinOfferJson") is not { } offerJson)
            return null;

        var offer = JsonSerializer.Deserialize<NeonCarRespinOffer>(offerJson);
        if (offer is null || offer.EligibleReels.Count == 0 ||
            offer.SpinId.ToString("N") != sourceId ||
            offer.RoundWinPoints != ReadLong(guard, "neonCarRoundWinPoints"))
            throw new InvalidOperationException("The saved Car respin offer is invalid.");
        return offer;
    }

    public Task<NeonCarRespinSettlement> RespinNeonCarReelAsync(
        string userId, Guid spinId, int reelIndex, CancellationToken cancellationToken) =>
        ResolveNeonCarRespinAsync(userId, spinId, reelIndex, null, cancellationToken);

    public Task<NeonCarRespinSettlement> FinalizeNeonCarRespinNitroChoiceAsync(
        string userId, Guid spinId, int reelIndex, CancellationToken cancellationToken) =>
        ResolveNeonCarRespinAsync(userId, spinId, null, reelIndex, cancellationToken);

    private Task<NeonCarRespinSettlement> ResolveNeonCarRespinAsync(
        string userId, Guid spinId, int? initialReelIndex, int? selectedNitroReel, CancellationToken cancellationToken)
    {
        if (spinId == Guid.Empty || initialReelIndex is < 0 or >= 5 || selectedNitroReel is < 0 or >= 5 ||
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
            var roundId = ReadString(source, "neonCarRespinRoundId");
            var sourceJson = ReadString(source, "neonCarRespinSourceJson");
            if (ReadString(source, "userId") != userId ||
                ReadString(source, "gameId") != SlotSpecialRoundProfiles.NeonNightsGameId ||
                roundId is null || sourceJson is null)
                throw new NeonCarRespinConflictException("This Car Play is no longer available for a reel respin.");

            // Previously issued offers used a receipt keyed to the whole Car round.
            var perSpinReceiptId = ReadString(source, "neonCarRespinReceiptId");
            var receiptId = perSpinReceiptId ?? roundId;
            var receiptReference = database.Collection(NeonCarRespinReceipt.CollectionName).Document(receiptId);
            var snapshots = await transaction.GetAllSnapshotsAsync(
                [receiptReference, guardReference, walletReference, freeGamesReference], cancellationToken);
            var receipt = snapshots[0];
            var guard = snapshots[1];
            var wallet = snapshots[2];
            var freeGames = snapshots[3];
            var balanceCents = ReadRandBalanceCents(wallet);
            var pendingNitroOffer = ReadPendingNeonNitroChoice(guard);

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
                    roundWinPoints: ReadString(guard, "neonCarRespinRoundId") == roundId
                        ? ReadLong(guard, "neonCarRoundWinPoints") : null);
            }

            if (pendingNitroOffer is { SourceKind: "car-respin" })
            {
                if (pendingNitroOffer.SpinId != spinId ||
                    ReadString(guard, "pendingNeonNitroCarRespinJson") is not { } pendingJson)
                    throw new NeonCarRespinConflictException("Finish the pending Nitro choice first.");
                var pendingOutcome = JsonSerializer.Deserialize<NeonCarRespinOutcome>(pendingJson)
                    ?? throw new InvalidOperationException("The saved Car respin Nitro outcome is invalid.");
                if (initialReelIndex is { } retryInitial)
                {
                    if (pendingNitroOffer.ReelIndex != retryInitial)
                        throw new NeonCarRespinConflictException("Finish the pending Nitro choice first.");
                    return ToSettlement(pendingOutcome, balanceCents, alreadyApplied: false,
                        pendingNitroOffer, ReadLong(guard, "neonCarRoundWinPoints"));
                }
            }
            else if (selectedNitroReel is not null || pendingNitroOffer is not null)
                throw new NeonCarRespinConflictException("The pending Nitro choice is no longer available.");

            if (ReadString(guard, "neonCarRespinRoundId") != roundId ||
                ReadString(guard, "neonCarRespinSourceSpinId") != sourceSpinId ||
                ReadString(guard, "activeAdmissionId") is not null ||
                ReadNeonCarRespinOffer(guard) is not { } offer ||
                !(initialReelIndex is { } selectedInitial && offer.EligibleReels.Contains(selectedInitial) ||
                    pendingNitroOffer?.ReelIndex is { } pendingInitial && offer.EligibleReels.Contains(pendingInitial)))
                throw new NeonCarRespinConflictException("Choose an available reel from the latest Car Play.");

            var original = JsonSerializer.Deserialize<NeonSpinOutcome>(sourceJson)
                ?? throw new InvalidOperationException("The saved Car spin source is invalid.");
            var eligibleReels = perSpinReceiptId is null
                ? NeonCarRespin.EligibleReels(original) : NeonCarRespin.OfferReels(original);
            if (original.SpinId != spinId || original.GameId != SlotSpecialRoundProfiles.NeonNightsGameId ||
                original.EngineAudit.FreeSpinFeatureMode != NeonArcadeBonus.PaidCarFeatureMode ||
                !eligibleReels.Contains(initialReelIndex ?? pendingNitroOffer!.ReelIndex!.Value))
                throw new InvalidOperationException("The saved Car spin source does not match its offer.");

            var outcome = selectedNitroReel is { } target
                ? NeonCarRespin.ResolveChoice(original,
                    JsonSerializer.Deserialize<NeonCarRespinOutcome>(ReadString(guard, "pendingNeonNitroCarRespinJson")!)!, target)
                : new NeonCarRespin(new CryptoRandomIndexSource()).Resolve(original, initialReelIndex!.Value);
            var findings = NeonCarRespin.Check(original, outcome);
            if (findings.Count != 0)
                throw new InvalidOperationException("The Car reel respin could not be replayed.");

            if (outcome.NeonNitroWild?.RequiresChoice == true)
            {
                var pendingOffer = CreateNeonNitroChoiceOffer(spinId, "car-respin", original.WagerPoints,
                    checked((int)ReadLong(freeGames, "available")), outcome.NeonNitroWild) with
                {
                    ReelIndex = outcome.ReelIndex,
                    RoundWinPoints = ReadLong(guard, "neonCarRoundWinPoints")
                };
                transaction.Update(guardReference, new Dictionary<string, object>
                {
                    ["pendingNeonNitroChoiceJson"] = JsonSerializer.Serialize(pendingOffer),
                    ["pendingNeonNitroCarRespinJson"] = JsonSerializer.Serialize(outcome),
                    ["updatedAt"] = Timestamp.FromDateTime(settledAtUtc)
                });
                return ToSettlement(outcome, balanceCents, alreadyApplied: false,
                    pendingOffer, ReadLong(guard, "neonCarRoundWinPoints"));
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
            transaction.Update(guardReference, new Dictionary<string, object>
            {
                ["neonCarRespinSourceSpinId"] = string.Empty,
                ["neonCarRespinOfferJson"] = string.Empty,
                ["neonCarRoundWinPoints"] = checked(ReadLong(guard, "neonCarRoundWinPoints") + outcome.ImprovementPoints),
                ["pendingNeonNitroChoiceJson"] = string.Empty,
                ["pendingNeonNitroCarRespinJson"] = string.Empty,
                ["updatedAt"] = Timestamp.FromDateTime(settledAtUtc)
            });
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
                    NeonCarRespinReceipt.CollectionName, receiptReference.Id,
                    new { originalSpinId = spinId, sourceSha256, outcome, improvementCents },
                    NeonCarRespin.RuleVersion, true, NeonCarRespin.RuleVersion);

            return ToSettlement(outcome, newBalanceCents, alreadyApplied: false,
                roundWinPoints: checked(ReadLong(guard, "neonCarRoundWinPoints") + outcome.ImprovementPoints));
        }, cancellationToken: cancellationToken);
    }

    public Task DismissNeonCarRespinAsync(string userId, Guid spinId, CancellationToken cancellationToken)
    {
        var guardReference = SlotSpinGuardDocument(userId, SlotSpecialRoundProfiles.NeonNightsGameId);
        return database.RunTransactionAsync(async transaction =>
        {
            var guard = await transaction.GetSnapshotAsync(guardReference, cancellationToken);
            if (ReadString(guard, "neonCarRespinSourceSpinId") != spinId.ToString("N") ||
                ReadString(guard, "activeAdmissionId") is not null ||
                ReadPendingNeonNitroChoice(guard) is not null)
                return;
            transaction.Update(guardReference, new Dictionary<string, object>
            {
                ["neonCarRespinSourceSpinId"] = string.Empty,
                ["neonCarRespinOfferJson"] = string.Empty,
                ["updatedAt"] = Timestamp.FromDateTime(DateTime.UtcNow)
            });
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
