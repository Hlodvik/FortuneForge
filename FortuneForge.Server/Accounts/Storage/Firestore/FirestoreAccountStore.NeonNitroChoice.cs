using System.Text.Json;
using FortuneForge.Games.NeonNights;
using FortuneForge.Games.NeonNights.Server;
using FortuneForge.Server.Accounts.Models;
using FortuneForge.Server.Slots.Bonuses;
using FortuneForge.Server.Slots.Configuration;
using Google.Cloud.Firestore;

namespace FortuneForge.Server.Accounts.Storage;

public sealed partial class FirestoreAccountStore
{
    private static NeonNitroChoiceOffer? ReadPendingNeonNitroChoice(DocumentSnapshot guard) =>
        NeonNitroChoice.ReadOffer(ReadString(guard, "pendingNeonNitroChoiceJson"));

    public async Task<NeonNitroChoiceFinalization> FinalizeNeonNitroChoiceAsync(
        string userId, Guid spinId, int reelIndex, CancellationToken cancellationToken)
    {
        if (spinId == Guid.Empty || !NeonNitroChoice.IsChoiceReel(reelIndex))
            throw new NeonNitroChoiceConflictException("Choose one of the five reels for Nitro Wild.");

        var resultSnapshot = await SlotSpinResultDocument(spinId).GetSnapshotAsync(cancellationToken);
        DocumentSnapshot admissionSnapshot;
        NeonNitroChoiceOffer? offer = null;
        if (resultSnapshot.Exists)
        {
            if (ReadString(resultSnapshot, "userId") != userId ||
                ReadString(resultSnapshot, "gameId") != SlotSpecialRoundProfiles.NeonNightsGameId ||
                ReadLong(resultSnapshot, "neonNitroChosenReel") != reelIndex ||
                ReadString(resultSnapshot, "neonNitroFinalOutcomeJson") is null ||
                !Guid.TryParseExact(ReadString(resultSnapshot, "admissionId"), "N", out var settledAdmissionId))
                throw new NeonNitroChoiceConflictException("This Nitro choice has already been settled differently.");
            admissionSnapshot = await SlotSpinAdmissionDocument(settledAdmissionId).GetSnapshotAsync(cancellationToken);
        }
        else
        {
            var guard = await SlotSpinGuardDocument(userId, SlotSpecialRoundProfiles.NeonNightsGameId)
                .GetSnapshotAsync(cancellationToken);
            if (ReadString(guard, NeonCarRespinStore.WorkerOwnerField) is not null)
                throw new NeonNitroChoiceConflictException("Resume the current Nitro choice through the Neon Nights worker.");
            offer = ReadPendingNeonNitroChoice(guard);
            if (offer?.Allows(spinId, NeonNitroChoice.SpinSource, reelIndex) != true ||
                !Guid.TryParseExact(ReadString(guard, "pendingNeonNitroAdmissionId"), "N", out var pendingAdmissionId) ||
                ReadString(guard, "activeAdmissionId") != pendingAdmissionId.ToString("N"))
                throw new NeonNitroChoiceConflictException("The pending Nitro choice is no longer available.");
            admissionSnapshot = await SlotSpinAdmissionDocument(pendingAdmissionId).GetSnapshotAsync(cancellationToken);
        }

        if (ReadString(admissionSnapshot, "userId") != userId ||
            ReadString(admissionSnapshot, "gameId") != SlotSpecialRoundProfiles.NeonNightsGameId ||
            ReadString(admissionSnapshot, "pendingNeonNitroSpinId") != spinId.ToString("N") ||
            ReadString(admissionSnapshot, "pendingNeonNitroOutcomeJson") is not { } pendingJson ||
            ReadString(admissionSnapshot, "pendingNeonNitroAdmissionJson") is not { } admissionJson)
            throw new NeonNitroChoiceConflictException("The pending Nitro spin cannot be restored.");
        var admission = JsonSerializer.Deserialize<SlotSpinAdmission>(admissionJson)
            ?? throw new InvalidOperationException("The saved Nitro admission is invalid.");
        if (admission.AdmissionId != Guid.ParseExact(admissionSnapshot.Id, "N") ||
            !NeonCarRoundState.IsCarPlay(admission.IsFreeSpin, admission.FreeSpinFeatureMode))
            throw new InvalidOperationException("The saved Nitro spin differs from its admission.");

        var finalOutcome = NeonNitroChoice.ResolveSavedSpin(pendingJson, spinId, admission.WagerPoints, reelIndex, offer);
        var result = FortuneForge.Server.Slots.Games.NeonNights.NeonNightsEngine.MapOutcome(finalOutcome) with
        { ChargedWagerCents = admission.ChargedWagerCents };
        SlotSpinSettlement settlement;
        try
        {
            settlement = await RecordSlotSpinAsync(userId, result, admission, DateTime.UtcNow,
                CancellationToken.None);
        }
        catch (InvalidOperationException exception) when (exception.Message.Contains("slot retry differs", StringComparison.Ordinal))
        {
            throw new NeonNitroChoiceConflictException("This Nitro choice has already been settled differently.");
        }
        var currentWallet = await BalanceDocument(userId, SlotsCreditsCurrencyId)
            .GetSnapshotAsync(cancellationToken);
        var currentState = await GetSlotStateAsync(userId, SlotSpecialRoundProfiles.NeonNightsGameId,
            finalOutcome.PointValueInCents, cancellationToken);
        return new(result, settlement with
        {
            SlotsCreditsBalance = ReadRandBalance(currentWallet),
            FreeSpinsRemaining = currentState.FreeSpinsRemaining,
            FreeSpinWagerPoints = currentState.FreeSpinWagerPoints,
            FreeSpinFeatureMode = currentState.FreeSpinFeatureMode,
            NeonDiscoSpinsRemaining = currentState.NeonDiscoSpinsRemaining,
            NeonDiscoWagerPoints = currentState.NeonDiscoWagerPoints,
            NeonDiscoMultiplier = currentState.NeonDiscoMultiplier,
            NeonDiscoRoundWinPoints = currentState.NeonDiscoRoundWinPoints,
            NeonCarRespinOffer = currentState.NeonCarRespinOffer,
            NeonCarRoundWinPoints = currentState.NeonCarRoundWinPoints
        });
    }
}
