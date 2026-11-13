using System.Text.Json;
using FortuneForge.Games.NeonNights;
using FortuneForge.Server.Accounts.Models;
using FortuneForge.Server.Slots.Bonuses;
using FortuneForge.Server.Slots.Configuration;
using FortuneForge.Server.Slots.Games.NeonNights;
using Google.Cloud.Firestore;

namespace FortuneForge.Server.Accounts.Storage;

public sealed partial class FirestoreAccountStore
{
    private static NeonNitroChoiceOffer CreateNeonNitroChoiceOffer(Guid spinId, string sourceKind,
        long wagerPoints, int freeSpinsRemaining, NeonNitroWildOutcome nitro)
    {
        if (!nitro.RequiresChoice || nitro.SecondNitro is null || nitro.ExpandedReel is not null)
            throw new InvalidOperationException("The Nitro outcome has no unresolved reel choice.");
        return new(spinId, sourceKind, wagerPoints, freeSpinsRemaining,
            nitro.InitialReels, [0, 1, 2, 3, 4], nitro);
    }

    private static NeonNitroChoiceOffer? ReadPendingNeonNitroChoice(DocumentSnapshot guard)
    {
        if (ReadString(guard, "pendingNeonNitroChoiceJson") is not { } json) return null;
        var offer = JsonSerializer.Deserialize<NeonNitroChoiceOffer>(json);
        if (offer?.NeonNitroWild.RequiresChoice != true || offer.EligibleReels.Count != 5)
            throw new InvalidOperationException("The saved Nitro reel choice is invalid.");
        return offer;
    }

    public async Task<NeonNitroChoiceFinalization> FinalizeNeonNitroChoiceAsync(
        string userId, Guid spinId, int reelIndex, CancellationToken cancellationToken)
    {
        if (spinId == Guid.Empty || reelIndex is < 0 or >= 5)
            throw new NeonNitroChoiceConflictException("Choose one of the five reels for Nitro Wild.");

        var resultSnapshot = await SlotSpinResultDocument(spinId).GetSnapshotAsync(cancellationToken);
        DocumentSnapshot admissionSnapshot;
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
            if (ReadPendingNeonNitroChoice(guard) is not { SourceKind: "spin" } offer ||
                offer.SpinId != spinId || !offer.EligibleReels.Contains(reelIndex) ||
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
        var pending = JsonSerializer.Deserialize<NeonSpinOutcome>(pendingJson)
            ?? throw new InvalidOperationException("The saved Nitro outcome is invalid.");
        if (admission.AdmissionId != Guid.ParseExact(admissionSnapshot.Id, "N") ||
            pending.SpinId != spinId || pending.NeonNitroWild?.RequiresChoice != true ||
            admission.FreeSpinFeatureMode != NeonArcadeBonus.PaidCarFeatureMode)
            throw new InvalidOperationException("The saved Nitro spin differs from its admission.");

        var finalOutcome = FortuneForge.Games.NeonNights.NeonNightsEngine.ResolveNitroChoice(pending, reelIndex);
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
            pending.PointValueInCents, cancellationToken);
        return new(result, settlement with
        {
            SlotsCreditsBalance = ReadRandBalance(currentWallet),
            FreeSpinsRemaining = currentState.FreeSpinsRemaining,
            FreeSpinWagerPoints = currentState.FreeSpinWagerPoints,
            FreeSpinFeatureMode = currentState.FreeSpinFeatureMode,
            NeonDiscoSpinsRemaining = currentState.NeonDiscoSpinsRemaining,
            NeonDiscoMultiplier = currentState.NeonDiscoMultiplier,
            NeonDiscoRoundWinPoints = currentState.NeonDiscoRoundWinPoints,
            NeonCarRespinOffer = currentState.NeonCarRespinOffer,
            NeonCarRoundWinPoints = currentState.NeonCarRoundWinPoints
        });
    }
}
