using FortuneForge.Games.GodsOfOlympus;
using FortuneForge.Games.Slots;
using FortuneForge.Games.Slots.Runtime;
using FortuneForge.Server.Accounts.Models;
using FortuneForge.Server.GameWorkers;
using FortuneForge.Server.Slots.Bonuses;
using FortuneForge.Server.Slots.Games;
using FortuneForge.Server.Slots.Games.GoldFoundry;
using Google.Cloud.Firestore;

namespace FortuneForge.Server.Accounts.Storage;

public enum LegacySlotRecoveryResult
{
    NoLegacyAdmission,
    Active,
    Reconciled,
    ManualReviewRequired,
    Unsupported
}

public sealed partial class FirestoreAccountStore
{
    public async Task<LegacySlotRecoveryResult> ReconcileExpiredLegacySlotAsync(string userId, string gameId, DateTime nowUtc,
        CancellationToken cancellationToken)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(userId);
        ArgumentException.ThrowIfNullOrWhiteSpace(gameId);
        if (nowUtc.Kind != DateTimeKind.Utc) throw new ArgumentException("A UTC time is required.", nameof(nowUtc));
        if (gameId is SlotSpecialRoundProfiles.ArcaneArchivesGameId or SlotSpecialRoundProfiles.NeonNightsGameId or
            SlotSpecialRoundProfiles.JadeDragonGameId or SlotSpecialRoundProfiles.DragonHoardGameId or
            GoldFoundrySlotDefinitions.GameId ||
            PreparedSlotWorkerRegistry.Find(gameId) is not { } route || StandaloneSlotPackageRegistry.GetGame(gameId) is not { } game)
            return LegacySlotRecoveryResult.Unsupported;

        var guardReference = SlotSpinGuardDocument(userId, gameId);
        try
        {
            return await database.RunTransactionAsync(async transaction =>
            {
                var guard = await transaction.GetSnapshotAsync(guardReference, cancellationToken);
                if (ReadString(guard, route.LegacyOwnerField) is not null ||
                    ReadString(guard, SlotSpinGuard.ActiveAdmissionField) is not { } active)
                    return LegacySlotRecoveryResult.NoLegacyAdmission;
                if (ReadString(guard, "userId") != userId || !Guid.TryParseExact(active, "N", out var admissionId) ||
                    guard.ContainsField(SlotSpinGuard.PreparedAdmissionField) ||
                    !guard.TryGetValue<Timestamp>(SlotSpinGuard.LastSpinStartedField, out var started))
                    return LegacySlotRecoveryResult.ManualReviewRequired;
                if (nowUtc - started.ToDateTime() < SlotSpinGuard.ActiveAdmissionTimeout)
                    return LegacySlotRecoveryResult.Active;

                var admissionReference = SlotSpinAdmissionDocument(admissionId);
                var admission = await transaction.GetSnapshotAsync(admissionReference, cancellationToken);
                if (!TryReadClassicLegacyAdmission(admission, guard, userId, gameId, active, started, game.Wagering, out var terms))
                    return LegacySlotRecoveryResult.ManualReviewRequired;

                // Historically the wallet, visible result and unlock committed together. A visible result
                // against a still-open admission is inconsistent and must be reconciled outside this route.
                var results = await transaction.GetSnapshotAsync(database.Collection("slotSpinResults")
                    .WhereEqualTo("admissionId", active).Limit(1), cancellationToken);
                if (results.Documents.Count != 0) return LegacySlotRecoveryResult.ManualReviewRequired;

                var freeCurrency = GameCurrencyId(FreeGamesCurrencyId, gameId);
                var freeBalance = await transaction.GetSnapshotAsync(BalanceDocument(userId, freeCurrency), cancellationToken);
                var availableFreeSpins = terms.FreeSpinsBefore - (terms.IsFreeSpin ? 1 : 0);
                if (!LegacyBalanceMatches(freeBalance, userId, freeCurrency, availableFreeSpins))
                    return LegacySlotRecoveryResult.ManualReviewRequired;

                if (gameId == GodsOfOlympusEngine.GameId)
                {
                    var energyCurrency = GameCurrencyId(EnergyCurrencyId, gameId);
                    var energy = await transaction.GetSnapshotAsync(BalanceDocument(userId, energyCurrency), cancellationToken);
                    if (!admission.TryGetValue<long>("energyBalanceBefore", out var energyBefore) || energyBefore < 0 ||
                        !LegacyBalanceMatches(energy, userId, energyCurrency, energyBefore))
                        return LegacySlotRecoveryResult.ManualReviewRequired;
                }

                LegacySlotRefund? freeRefund = null;
                if (terms.FreeGameUseId is { } freeUse)
                {
                    freeRefund = await ReadLegacySlotRefundAsync(transaction, freeBalance, userId, freeCurrency, freeUse, active,
                        "free-game", 1, terms.FreeSpinsBefore, started, cancellationToken);
                    if (freeRefund is null) return LegacySlotRecoveryResult.ManualReviewRequired;
                }

                LegacySlotRefund? boostRefund = null;
                if (terms.SpecialPointUseId is { } boostUse)
                {
                    var specialCurrency = GameCurrencyId(SpecialPointsCurrencyId, gameId);
                    var specialBalance = await transaction.GetSnapshotAsync(BalanceDocument(userId, specialCurrency), cancellationToken);
                    boostRefund = await ReadLegacySlotRefundAsync(transaction, specialBalance, userId, specialCurrency, boostUse, active,
                        "special-boost", terms.BoostCost, terms.SpecialPointsBefore, started, cancellationToken);
                    if (boostRefund is null) return LegacySlotRecoveryResult.ManualReviewRequired;
                }

                if (freeRefund is not null) WriteLegacySlotRefund(transaction, freeRefund, userId, active, nowUtc);
                if (boostRefund is not null) WriteLegacySlotRefund(transaction, boostRefund, userId, active, nowUtc);
                var changes = new Dictionary<string, object>
                {
                    [SlotSpinGuard.ActiveAdmissionField] = string.Empty,
                    ["updatedAt"] = Timestamp.FromDateTime(nowUtc)
                };
                if (terms.IsFreeSpin && terms.FreeSpinsBefore == 1)
                {
                    changes["freeSpinWagerPoints"] = terms.EffectiveWager;
                    changes["freeSpinWagerCents"] = RandMoney.PointsToCents(terms.EffectiveWager, game.Wagering.PointValueInCents);
                    changes["freeSpinFeatureMode"] = terms.FeatureMode ?? string.Empty;
                }
                transaction.Update(guardReference, changes);
                transaction.Update(admissionReference, new Dictionary<string, object>
                {
                    ["status"] = "recovered",
                    ["recoveredAtUtc"] = Timestamp.FromDateTime(nowUtc),
                    ["recoveryKind"] = gameId == GodsOfOlympusEngine.GameId && !terms.IsFreeSpin
                        ? "expired-unkeyed-olympus-tumble" : "expired-unkeyed-classic",
                    ["freeGameRefundTransactionId"] = freeRefund?.Receipt.Id ?? string.Empty,
                    ["specialBoostRefundTransactionId"] = boostRefund?.Receipt.Id ?? string.Empty
                });
                return LegacySlotRecoveryResult.Reconciled;
            }, cancellationToken: cancellationToken);
        }
        catch (Exception exception) when (exception is ArgumentException or InvalidOperationException or OverflowException)
        {
            return LegacySlotRecoveryResult.ManualReviewRequired;
        }
    }

    private static bool TryReadClassicLegacyAdmission(DocumentSnapshot admission, DocumentSnapshot guard, string userId,
        string gameId, string active, Timestamp started, GameWageringDefinition wager, out LegacySlotAdmissionTerms terms)
    {
        terms = default;
        if (ReadString(admission, "status") != "admitted" || ReadString(admission, "admissionId") != active ||
            ReadString(admission, "userId") != userId || ReadString(admission, "gameId") != gameId ||
            !admission.TryGetValue<Timestamp>("startedAtUtc", out var admittedAt) || admittedAt != started ||
            admission.ContainsField("spinId") || admission.ContainsField("settledAtUtc") ||
            admission.ContainsField(PreparedOperationIdField) || admission.ContainsField(PreparedRoundJsonField) ||
            admission.ContainsField(PreparedOutcomeSha256Field) ||
            !admission.TryGetValue<long>("requestedWagerPoints", out var requested) || requested <= 0 ||
            !admission.TryGetValue<long>("effectiveWagerPoints", out var effective) || effective <= 0 ||
            wager.MaximumWagerPoints is { } maximum && (requested > maximum || effective > maximum) ||
            !admission.TryGetValue<long>("chargedWagerCents", out var charged) ||
            !admission.TryGetValue<bool>("isFreeSpin", out var isFreeSpin) ||
            !admission.TryGetValue<bool>("specialBoostApplied", out var usedBoost) ||
            !admission.TryGetValue<long>("freeSpinsBefore", out var freeBefore) || freeBefore is < 0 or > int.MaxValue ||
            !admission.TryGetValue<long>("specialPointsBefore", out var specialBefore) || specialBefore is < 0 or > int.MaxValue ||
            !admission.TryGetValue<long>("specialBoostCost", out var boostCost) || boostCost < 0 || boostCost > specialBefore ||
            !admission.TryGetValue<long>("pityMissesBefore", out var pity) || pity < 0 || ReadLong(guard, "consecutiveFiveMisses") != pity ||
            ReadString(guard, "freeSpinMathModelId") is not null)
            return false;

        var freeUse = ReadString(admission, "freeGameUseTransactionId");
        var boostUse = ReadString(admission, "specialPointUseTransactionId");
        var mode = ReadString(admission, "freeSpinFeatureMode");
        var mathModel = ReadString(admission, "mathModelId");
        var paidOlympus = gameId == GodsOfOlympusEngine.GameId && !isFreeSpin;
        if ((paidOlympus ? !OlympusTumbleRules.SupportsPaidEngineVersion(mathModel) || usedBoost : mathModel is not null) ||
            usedBoost != (boostCost > 0) || (usedBoost ? !IsLegacyUseId(boostUse, "special-boost") : boostUse is not null) ||
            (isFreeSpin ? charged != 0 || freeBefore < 1 || !IsLegacyUseId(freeUse, "free-game")
                : requested != effective || charged != RandMoney.PointsToCents(effective, wager.PointValueInCents) ||
                freeBefore != 0 || freeUse is not null || mode is not null) ||
            mode is not null && !SlotProfile.IsFeatureMode(mode))
            return false;

        var stillHasCarry = isFreeSpin && freeBefore > 1;
        if (ReadLong(guard, "freeSpinWagerPoints") != (stillHasCarry ? effective : 0) ||
            ReadLong(guard, "freeSpinWagerCents") != (stillHasCarry ? RandMoney.PointsToCents(effective, wager.PointValueInCents) : 0) ||
            ReadString(guard, "freeSpinFeatureMode") != (stillHasCarry ? mode : null))
            return false;

        // A last-spin admission cleared its historical lock. A substituted request is direct
        // evidence that the effective lock actually existed before this admission.
        if (isFreeSpin && freeBefore == 1 && requested == effective) return false;
        terms = new(isFreeSpin, effective, freeBefore, specialBefore, boostCost, mode, freeUse, boostUse);
        return true;
    }

    private static bool IsLegacyUseId(string? id, string kind) => id is not null && id.Length == 32 + kind.Length + 5 &&
        id.EndsWith($"-{kind}-use", StringComparison.Ordinal) && Guid.TryParseExact(id.AsSpan(0, 32), "N", out _);

    private static bool LegacyBalanceMatches(DocumentSnapshot balance, string userId, string currency, long amount) =>
        !balance.Exists ? amount == 0 : ReadString(balance, "userId") == userId && ReadString(balance, "currencyId") == currency &&
            balance.TryGetValue<long>("available", out var available) && available == amount;

    private async Task<LegacySlotRefund?> ReadLegacySlotRefundAsync(Transaction transaction, DocumentSnapshot balance,
        string userId, string currency, string useId, string admissionId, string kind, long amount, long restored,
        Timestamp started, CancellationToken cancellationToken)
    {
        if (!balance.Exists || !LegacyBalanceMatches(balance, userId, currency, restored - amount)) return null;
        var refund = BalanceTransactionDocument($"{admissionId}-{kind}-refund");
        var snapshots = await transaction.GetAllSnapshotsAsync([BalanceTransactionDocument(useId), refund], cancellationToken);
        var use = snapshots[0];
        if (snapshots[1].Exists || ReadString(use, "transactionId") != useId || ReadString(use, "idempotencyKey") != useId ||
            ReadString(use, "userId") != userId || ReadString(use, "currencyId") != currency ||
            ReadString(use, "type") != $"{kind}-use" || !use.TryGetValue<long>("amount", out var spent) || spent != -amount ||
            !use.TryGetValue<long>("balanceAfter", out var after) || after != restored - amount ||
            !use.TryGetValue<Timestamp>("createdAt", out var at) || at != started)
            return null;
        return new(balance.Reference, refund, currency, kind, amount, restored);
    }

    private static void WriteLegacySlotRefund(Transaction transaction, LegacySlotRefund refund, string userId, string admissionId,
        DateTime nowUtc)
    {
        transaction.Update(refund.Balance, new Dictionary<string, object>
        {
            ["available"] = refund.Restored,
            ["version"] = FieldValue.Increment(1),
            ["updatedAt"] = Timestamp.FromDateTime(nowUtc)
        });
        transaction.Create(refund.Receipt, BalanceTransactionData(refund.Receipt.Id, userId, refund.Currency, refund.Amount,
            refund.Restored, $"{refund.Kind}-refund", admissionId, nowUtc));
    }

    private readonly record struct LegacySlotAdmissionTerms(bool IsFreeSpin, long EffectiveWager, long FreeSpinsBefore,
        long SpecialPointsBefore, long BoostCost, string? FeatureMode, string? FreeGameUseId, string? SpecialPointUseId);

    private sealed record LegacySlotRefund(DocumentReference Balance, DocumentReference Receipt, string Currency, string Kind,
        long Amount, long Restored);
}
