// Edited: 2026-11-18T18:04:50-06:00 | Computer: Vinnabok
// Edited: 2026-11-18T16:45:56-06:00 | Computer: Vinnabok
// Edited: 2026-11-18T16:30:13-06:00 | Computer: Vinnabok
using System.Text.Json;
using FortuneForge.Games.ArcaneArchives;
using FortuneForge.Server.Accounts.Models;
using FortuneForge.Server.Slots.Models;
using Google.Cloud.Firestore;

namespace FortuneForge.Server.Accounts.Storage;

public sealed partial class FirestoreAccountStore
{
    private const string ArcaneTomeField = "arcaneTomeJson";
    private const string ArcaneTomeBeforeField = "arcaneTomeBeforeJson";

    public Task<ArcaneTomeState> SelectArcaneTomePageAsync(
        string userId, string page, bool activate, DateTime nowUtc, CancellationToken cancellationToken)
    {
        var guardReference = SlotSpinGuardDocument(userId, ArcaneArchivesEngine.GameId);
        return database.RunTransactionAsync(async transaction =>
        {
            var guard = await transaction.GetSnapshotAsync(guardReference, cancellationToken);
            if (HasActiveSlotSpinAdmission(guard, nowUtc))
                throw new ArcaneTomeConflictException("Finish the current Arcane spin before changing the Tome.");
            var next = ArcaneTome.SelectPage(ReadArcaneTome(guard), page, activate);
            transaction.Set(guardReference, new Dictionary<string, object>
            {
                ["userId"] = userId,
                ["updatedAt"] = Timestamp.FromDateTime(nowUtc),
                [ArcaneTomeField] = JsonSerializer.Serialize(next)
            }, SetOptions.MergeAll);
            return next;
        }, cancellationToken: cancellationToken);
    }

    private static ArcaneTomeProgress SettleArcaneTome(
        SlotSpinAdmission admission, SpinResult result, DocumentSnapshot guard, DocumentSnapshot admissionRecord)
    {
        var admitted = admission.ArcaneTomeBefore
            ?? throw new InvalidOperationException("Arcane spin admission has no Tome state.");
        ArcaneTome.ValidateState(admitted);
        var recorded = ReadString(admissionRecord, ArcaneTomeBeforeField) is { } saved
            ? ParseArcaneTome(saved)
            : throw new InvalidOperationException("Arcane spin admission has no saved Tome state.");
        if (!ArcaneTome.SameState(admitted, recorded) ||
            !ArcaneTome.SameState(admitted, ReadArcaneTome(guard)) ||
            !ArcaneArchivesEngine.MatchesTomeState(result.ReelSetId, admitted))
            throw new InvalidOperationException("Arcane spin outcome differs from the admitted Tome state.");
        return ArcaneTome.CompleteSpin(admitted, result.Reels, admission.IsFreeSpin,
            requireIngredient: result.EngineAudit?.EngineVersion == ArcaneArchivesEngine.PreviousWandEngineVersion);
    }

    private static ArcaneTomeState ReadArcaneTome(DocumentSnapshot snapshot) =>
        ReadString(snapshot, ArcaneTomeField) is { } saved ? ParseArcaneTome(saved) : ArcaneTome.Empty;

    private static ArcaneTomeState ParseArcaneTome(string saved)
    {
        var state = JsonSerializer.Deserialize<ArcaneTomeState>(saved)
            ?? throw new InvalidOperationException("The saved Arcane Tome state is missing.");
        ArcaneTome.ValidateState(state);
        return state;
    }
}
