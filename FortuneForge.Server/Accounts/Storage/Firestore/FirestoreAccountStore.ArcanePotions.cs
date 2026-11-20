// Edited: 2026-11-18T17:59:35-06:00 | Computer: Vinnabok
using System.Text.Json;
using FortuneForge.Games.ArcaneArchives;
using FortuneForge.Server.Accounts.Models;
using FortuneForge.Server.Slots.Models;
using Google.Cloud.Firestore;

namespace FortuneForge.Server.Accounts.Storage;

public sealed partial class FirestoreAccountStore
{
    private const string ArcanePotionsField = "arcanePotionsJson";
    private const string ArcanePotionsBeforeField = "arcanePotionsBeforeJson";
    private const string ArcanePotionWagerTrackedField = "arcanePotionWagerTracked";

    public Task<ArcanePotionState> ActivateArcanePotionAsync(
        string userId, long potionId, DateTime nowUtc, CancellationToken cancellationToken)
    {
        var guardReference = SlotSpinGuardDocument(userId, ArcaneArchivesEngine.GameId);
        return database.RunTransactionAsync(async transaction =>
        {
            var guard = await transaction.GetSnapshotAsync(guardReference, cancellationToken);
            if (HasActiveSlotSpinAdmission(guard, nowUtc))
                throw new ArcanePotionConflictException("Finish the current Arcane spin before using a potion.");

            var next = ArcanePotions.Use(ReadArcanePotions(guard), potionId);
            transaction.Set(guardReference, new Dictionary<string, object>
            {
                ["userId"] = userId,
                ["updatedAt"] = Timestamp.FromDateTime(nowUtc),
                [ArcanePotionsField] = JsonSerializer.Serialize(next)
            }, SetOptions.MergeAll);
            return next;
        }, cancellationToken: cancellationToken);
    }

    private static ArcanePotionState SettleArcanePotions(
        SlotSpinAdmission admission, SpinResult result, DocumentSnapshot guard, DocumentSnapshot admissionRecord)
    {
        var before = admission.ArcanePotionsBefore
            ?? throw new InvalidOperationException("Arcane spin admission has no potion state.");
        var recorded = ReadString(admissionRecord, ArcanePotionsBeforeField) is { } saved
            ? ParseArcanePotions(saved)
            : throw new InvalidOperationException("Arcane spin admission has no saved potion state.");
        var tome = admission.ArcaneTomeBefore
            ?? throw new InvalidOperationException("Arcane spin admission has no Tome state.");
        ArcanePotions.ValidateState(before);
        var tracked = admissionRecord.TryGetValue<bool>(ArcanePotionWagerTrackedField, out var storedTracking) && storedTracking;
        if (tracked != admission.ArcanePotionWagerTracked ||
            tracked && ArcanePotions.LockedWagerForSpin(before, admission.IsFreeSpin) is { } lockedWager &&
                admission.WagerPoints != lockedWager ||
            !ArcanePotions.SameState(before, recorded) ||
            !ArcanePotions.SameState(before, ReadArcanePotions(guard)) ||
            result.EngineAudit?.ArcanePotionApplied != ArcanePotions.ForSpin(before, admission.IsFreeSpin))
            throw new InvalidOperationException("Arcane spin outcome differs from the admitted potion state.");

        return ArcanePotions.CompleteSpin(before,
            ArcaneTome.OrderedIngredientUnits(tome, result.Reels, admission.IsFreeSpin), admission.IsFreeSpin,
            tracked ? admission.WagerPoints : null,
            allowRareRecipes: ArcaneArchivesEngine.SupportsRarePotions(result.EngineAudit?.EngineVersion));
    }

    private static ArcanePotionState ReadArcanePotions(DocumentSnapshot snapshot) =>
        ReadString(snapshot, ArcanePotionsField) is { } saved ? ParseArcanePotions(saved) : ArcanePotions.Empty;

    private static ArcanePotionState ParseArcanePotions(string saved)
    {
        var state = JsonSerializer.Deserialize<ArcanePotionState>(saved)
            ?? throw new InvalidOperationException("The saved Arcane potion state is missing.");
        ArcanePotions.ValidateState(state);
        return state;
    }
}
