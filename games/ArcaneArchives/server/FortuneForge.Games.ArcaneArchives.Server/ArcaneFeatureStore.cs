using System.Text.Json;
using FortuneForge.Games.Slots;
using Google.Cloud.Firestore;

namespace FortuneForge.Games.ArcaneArchives.Server;

public sealed record ArcaneFeatureAdmission(ArcaneTomeState? Tome, ArcanePotionState? Potions, bool TracksIngredientWagers);

public sealed record ArcaneFeatureOutcome(string EngineVersion, string ReelSetId,
    IReadOnlyList<IReadOnlyList<string>> Reels, ArcanePotionBottle? PotionApplied);

public sealed record ArcaneFeatureSettlement(ArcaneTomeProgress? Tome, ArcanePotionState? Potions);

public sealed class ArcaneFeatureStore(FirestoreDb database)
{
    private const string TomeField = "arcaneTomeJson";
    private const string TomeBeforeField = "arcaneTomeBeforeJson";
    private const string PotionsField = "arcanePotionsJson";
    private const string PotionsBeforeField = "arcanePotionsBeforeJson";
    private const string PotionWagerTrackedField = "arcanePotionWagerTracked";

    public Task<ArcaneTomeState> SelectTomePageAsync(
        string userId, string page, bool activate, DateTime nowUtc, CancellationToken cancellationToken) =>
        UpdateFeatureAsync(userId, nowUtc, TomeField, guard =>
        {
            if (HasActiveAdmission(guard, nowUtc))
                throw new ArcaneTomeConflictException("Finish the current Arcane spin before changing the Tome.");
            return ArcaneTome.SelectPage(ReadTome(guard), page, activate);
        }, cancellationToken);

    public Task<ArcanePotionState> ActivatePotionAsync(
        string userId, long potionId, DateTime nowUtc, CancellationToken cancellationToken) =>
        UpdateFeatureAsync(userId, nowUtc, PotionsField, guard =>
        {
            if (HasActiveAdmission(guard, nowUtc))
                throw new ArcanePotionConflictException("Finish the current Arcane spin before using a potion.");
            return ArcanePotions.Use(ReadPotions(guard), potionId);
        }, cancellationToken);

    public static ArcaneFeatureAdmission Admit(DocumentSnapshot guard, long wagerPoints, bool isFreeSpin)
    {
        var tome = ReadTome(guard);
        ArcaneTome.EnsureSpinAllowed(tome);
        var potions = ReadPotions(guard);
        if (ArcanePotions.LockedWagerForSpin(potions, isFreeSpin) is { } lockedWager && wagerPoints != lockedWager)
            throw new ArcanePotionWagerLockedException(lockedWager);
        return new(tome, potions, true);
    }

    public static ArcaneTomeState ReadTome(DocumentSnapshot guard) =>
        Text(guard, TomeField) is { } saved ? ParseTome(saved) : ArcaneTome.Empty;

    public static ArcanePotionState ReadPotions(DocumentSnapshot guard) =>
        Text(guard, PotionsField) is { } saved ? ParsePotions(saved) : ArcanePotions.Empty;

    public static void WriteAdmission(IDictionary<string, object> fields, ArcaneFeatureAdmission admission)
    {
        if (admission.Tome is not null)
            fields[TomeBeforeField] = JsonSerializer.Serialize(admission.Tome);
        if (admission.Potions is not null)
        {
            fields[PotionsBeforeField] = JsonSerializer.Serialize(admission.Potions);
            fields[PotionWagerTrackedField] = admission.TracksIngredientWagers;
        }
    }

    public static ArcaneFeatureSettlement CompleteSpin(ArcaneFeatureAdmission admission, ArcaneFeatureOutcome outcome,
        DocumentSnapshot guard, DocumentSnapshot admissionRecord, long wagerPoints, bool isFreeSpin)
    {
        ArgumentNullException.ThrowIfNull(admissionRecord);
        return EvaluateSpin(admission, outcome, guard, admissionRecord, wagerPoints, isFreeSpin);
    }

    internal static ArcaneFeatureSettlement PreviewSpin(ArcaneFeatureAdmission admission, ArcaneFeatureOutcome outcome,
        DocumentSnapshot guard, long wagerPoints, bool isFreeSpin) =>
        EvaluateSpin(admission, outcome, guard, null, wagerPoints, isFreeSpin);

    private static ArcaneFeatureSettlement EvaluateSpin(ArcaneFeatureAdmission admission, ArcaneFeatureOutcome outcome,
        DocumentSnapshot guard, DocumentSnapshot? admissionRecord, long wagerPoints, bool isFreeSpin)
    {
        var tome = ArcaneArchivesEngine.IsWandEngineVersion(outcome.EngineVersion)
            ? CompleteTome(admission, outcome, guard, admissionRecord, isFreeSpin)
            : null;
        var potions = ArcaneArchivesEngine.SupportsPotionEngineVersion(outcome.EngineVersion)
            ? CompletePotions(admission, outcome, guard, admissionRecord, wagerPoints, isFreeSpin)
            : null;
        return new(tome, potions);
    }

    public static void WriteSettlement(IDictionary<string, object> guard, ArcaneFeatureSettlement settlement)
    {
        if (settlement.Tome is not null) guard[TomeField] = JsonSerializer.Serialize(settlement.Tome.State);
        if (settlement.Potions is not null) guard[PotionsField] = JsonSerializer.Serialize(settlement.Potions);
    }

    private Task<T> UpdateFeatureAsync<T>(string userId, DateTime nowUtc, string field, Func<DocumentSnapshot, T> update,
        CancellationToken cancellationToken)
    {
        var guard = database.Collection(SlotSpinGuard.Collection).Document(SlotSpinGuard.DocumentId(userId, ArcaneArchivesEngine.GameId));
        return database.RunTransactionAsync(async transaction =>
        {
            var next = update(await transaction.GetSnapshotAsync(guard, cancellationToken));
            transaction.Set(guard, new Dictionary<string, object>
            {
                ["userId"] = userId,
                ["updatedAt"] = Timestamp.FromDateTime(nowUtc),
                [field] = JsonSerializer.Serialize(next)
            }, SetOptions.MergeAll);
            return next;
        }, cancellationToken: cancellationToken);
    }

    private static ArcaneTomeProgress CompleteTome(ArcaneFeatureAdmission admission, ArcaneFeatureOutcome outcome,
        DocumentSnapshot guard, DocumentSnapshot? admissionRecord, bool isFreeSpin)
    {
        var admitted = admission.Tome ?? throw new InvalidOperationException("Arcane spin admission has no Tome state.");
        ArcaneTome.ValidateState(admitted);
        var recorded = admissionRecord is null ? admitted : Text(admissionRecord, TomeBeforeField) is { } saved
            ? ParseTome(saved) : throw new InvalidOperationException("Arcane spin admission has no saved Tome state.");
        if (!ArcaneTome.SameState(admitted, recorded) ||
            !ArcaneTome.SameState(admitted, ReadTome(guard)) ||
            !ArcaneArchivesEngine.MatchesTomeState(outcome.ReelSetId, admitted))
            throw new InvalidOperationException("Arcane spin outcome differs from the admitted Tome state.");
        return ArcaneTome.CompleteSpin(admitted, outcome.Reels, isFreeSpin,
            requireIngredient: outcome.EngineVersion == ArcaneArchivesEngine.PreviousWandEngineVersion);
    }

    private static ArcanePotionState CompletePotions(ArcaneFeatureAdmission admission, ArcaneFeatureOutcome outcome,
        DocumentSnapshot guard, DocumentSnapshot? admissionRecord, long wagerPoints, bool isFreeSpin)
    {
        var before = admission.Potions ?? throw new InvalidOperationException("Arcane spin admission has no potion state.");
        var recorded = admissionRecord is null ? before : Text(admissionRecord, PotionsBeforeField) is { } saved
            ? ParsePotions(saved) : throw new InvalidOperationException("Arcane spin admission has no saved potion state.");
        var tome = admission.Tome ?? throw new InvalidOperationException("Arcane spin admission has no Tome state.");
        ArcanePotions.ValidateState(before);
        var tracked = admissionRecord is null ? admission.TracksIngredientWagers :
            admissionRecord.TryGetValue<bool>(PotionWagerTrackedField, out var storedTracking) && storedTracking;
        if (tracked != admission.TracksIngredientWagers ||
            tracked && ArcanePotions.LockedWagerForSpin(before, isFreeSpin) is { } lockedWager && wagerPoints != lockedWager ||
            !ArcanePotions.SameState(before, recorded) || !ArcanePotions.SameState(before, ReadPotions(guard)) ||
            outcome.PotionApplied != ArcanePotions.ForSpin(before, isFreeSpin))
            throw new InvalidOperationException("Arcane spin outcome differs from the admitted potion state.");
        return ArcanePotions.CompleteSpin(before, ArcaneTome.OrderedIngredientUnits(tome, outcome.Reels, isFreeSpin),
            isFreeSpin, tracked ? wagerPoints : null, allowRareRecipes: ArcaneArchivesEngine.SupportsRarePotions(outcome.EngineVersion));
    }

    private static bool HasActiveAdmission(DocumentSnapshot guard, DateTime nowUtc) =>
        Text(guard, SlotSpinGuard.ActiveAdmissionField) is { } active &&
        (active == Text(guard, SlotSpinGuard.PreparedAdmissionField) ||
         guard.TryGetValue<Timestamp>(SlotSpinGuard.LastSpinStartedField, out var startedAt) &&
         startedAt.ToDateTime().Add(SlotSpinGuard.ActiveAdmissionTimeout) > nowUtc);

    private static string? Text(DocumentSnapshot snapshot, string field) =>
        snapshot.Exists && snapshot.TryGetValue<string>(field, out var value) && !string.IsNullOrWhiteSpace(value)
            ? value : null;

    private static ArcaneTomeState ParseTome(string saved)
    {
        var state = JsonSerializer.Deserialize<ArcaneTomeState>(saved)
            ?? throw new InvalidOperationException("The saved Arcane Tome state is missing.");
        ArcaneTome.ValidateState(state);
        return state;
    }

    private static ArcanePotionState ParsePotions(string saved)
    {
        var state = JsonSerializer.Deserialize<ArcanePotionState>(saved)
            ?? throw new InvalidOperationException("The saved Arcane potion state is missing.");
        ArcanePotions.ValidateState(state);
        return state;
    }
}
