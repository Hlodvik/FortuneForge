using System.Text.Json;
using FortuneForge.Games.Abstractions.Analytics;
using FortuneForge.Games.Abstractions.Auditing;
using FortuneForge.Games.Abstractions.Wallet;
using FortuneForge.Games.Slots;
using Google.Cloud.Firestore;

namespace FortuneForge.Games.NeonNights.Server;

internal static class NeonPrimarySpinReport
{
    private const string EvidenceKind = "slot-reels-paylines-and-features-v1";
    private const long OutcomeSchemaVersion = 7;

    internal static void Write(Transaction transaction, FirestoreDb database, NeonPrimaryPreparedRound round,
        NeonPrimaryFeatures next, NeonSpinReply reply, GameWalletSettlementReceipt wallet, DateTime completedAt)
    {
        var outcome = round.Outcome;
        var audit = outcome.EngineAudit;
        const string catalogId = NeonPrimaryRouteContract.AuditGameId;
        const string resultCollection = NeonPrimaryRoundStore.SpinResultCollection;
        var spin = outcome.SpinId.ToString("N");
        var rulesHash = GameSecurityEventDocuments.Hash(JsonSerializer.Serialize(
            new { audit.BaseGame, audit.EffectiveGame, audit.ReelSet, audit.SymbolSet, audit.Paytable },
            NeonPrimaryRoundStore.Json));
        var evidence = new
        {
            result = reply, admissionId = round.AdmissionId, round.StartedAtUtc, RequestId = round.Observation?.RequestId,
            engineAudit = audit, rulesSha256 = rulesHash, settledPayout = outcome.Payout, isFreeSpin = outcome.IsFreeSpin,
            isNeonDiscoPlay = round.IsDisco, activeFreeSpinFeatureMode = round.Before.Awarded.FeatureMode,
            neonDiscoSpinsBefore = round.Before.Disco.SpinsRemaining, neonDiscoSpinsRemaining = next.Disco.SpinsRemaining,
            neonDiscoMultiplierBefore = round.Before.Disco.Multiplier, neonDiscoWagerPointsBefore = round.Before.Disco.LockedWagerPoints,
            neonDiscoWagerPoints = next.Disco.LockedWagerPoints, neonDiscoMultiplier = next.Disco.Multiplier,
            admittedMathModelId = (string?)null, payoutMultiplier = 1m, outcomeSchemaVersion = OutcomeSchemaVersion,
            engineOutcomeSha256 = round.OutcomeSha256, walletSettlementId = wallet.SettlementId,
            neonNitroReservation = round.Reservation is { } hold ? new
            {
                outcomeSha256 = round.ReservationSha256,
                admittedOutcome = round.ReservedOutcome,
                settlementId = hold.SettlementId, hold.WagerTransactionId, hold.LegacyFreeGameTransactionId,
                hold.BalanceBeforeCents, hold.BalanceAfterWagerCents, hold.SettledAtUtc,
                payoutSettlementId = wallet.SettlementId, wallet.PayoutTransactionId
            } : null,
            neonAwardedPlays = new { nativeBefore = round.Before.Native, nativeAfter = next.Native,
                legacyBefore = round.Before.Legacy, legacyAfter = next.Legacy, legacyUnitSpent = round.SpendsLegacy }
        };
        using var request = round.Observation is { } observation ? GameSecurityContext.Push(observation) : null;
        var report = SinglePlayerRoundRecord.Create(catalogId, spin, round.AccountId, round.WagerCents, round.PayoutCents,
            round.StartedAtUtc, completedAt, resultCollection, spin, evidence, EvidenceKind, settlementVerified: true,
            outcomeAuditorVersion: "slot-outcome-auditor-v1");
        var docs = RoundAuditDocuments.Create(report.Audit);
        var journal = GameSecurityEventDocuments.Capture(catalogId, spin, 1, completedAt, evidence, [round.AccountId], "round-settled");
        var auditId = report.Audit.Summary.Id;
        transaction.Create(database.Collection(RoundAuditDocuments.Summaries).Document(auditId), docs.Summary);
        transaction.Create(database.Collection(RoundAuditDocuments.Evidence).Document(auditId), docs.Evidence);
        transaction.Create(database.Collection(GameAnalyticsOutbox.CollectionName).Document(report.Settlement.Id), report.Settlement.Fields);
        transaction.Set(database.Collection(GameSecurityEventDocuments.CollectionName).Document(journal.Id), journal.Fields);
        transaction.Set(database.Collection(GameSecurityEventDocuments.ManifestCollectionName)
            .Document(GameSecurityEventDocuments.ManifestId(catalogId, spin)),
            new Dictionary<string, object>
            {
                ["gameId"] = catalogId, ["instanceId"] = spin, ["eventIds"] = FieldValue.ArrayUnion(journal.Id), ["schemaVersion"] = 1L
            }, SetOptions.MergeAll);
        var participation = AcceptedGameParticipationDocuments.Create(round.AccountId, catalogId, completedAt);
        transaction.Set(database.Collection("catalogPlayerActivity").Document(participation.DocOwnerKey)
            .Collection("games").Document(participation.GameId), participation.Fields, SetOptions.MergeAll);

        var createdAt = Timestamp.FromDateTime(completedAt);
        var wager = (double)(round.WagerCents / 100m);
        var payout = (double)(round.PayoutCents / 100m);
        var net = (double)((round.PayoutCents - round.WagerCents) / 100m);
        var win = round.PayoutCents > 0;
        var result = new Dictionary<string, object>
        {
            ["spinId"] = spin, ["admissionId"] = round.AdmissionId.ToString("N"), ["requestId"] = round.Observation?.RequestId ?? string.Empty,
            ["userId"] = round.AccountId, ["gameId"] = outcome.GameId, ["reelSetId"] = outcome.ReelSetId,
            ["symbolSetId"] = outcome.SymbolSetId, ["paytableId"] = outcome.PaytableId, ["mathModelId"] = string.Empty,
            ["reelStops"] = outcome.ReelStops.Select(stop => (long)stop).ToArray(),
            ["visibleSymbolWindow"] = SlotSpinResultFields.VisibleSymbolWindow(outcome.Reels),
            ["payoutBreakdown"] = SlotSpinResultFields.PayoutBreakdown(outcome.Payout),
            ["wageredSlotsCredits"] = wager, ["payoutWagerPoints"] = outcome.WagerPoints, ["wonSlotsCredits"] = payout,
            ["netSlotsCredits"] = net, ["isFreeSpin"] = outcome.IsFreeSpin, ["isNeonDiscoPlay"] = round.IsDisco,
            ["neonDiscoSpinsRemaining"] = next.Disco.SpinsRemaining, ["freeSpinsAwarded"] = outcome.FreeSpinsAwarded,
            ["specialPointsAwarded"] = outcome.SpecialPointsAwarded, ["energyAwarded"] = outcome.EnergyAwarded,
            ["energyMultiplierApplied"] = false, ["payoutMultiplier"] = 1d, ["monkeyPawCount"] = 0L,
            ["moneyGrabPoints"] = 0L, ["bananaBonusPoints"] = outcome.BananaBonusPoints,
            ["sealsAwarded"] = new Dictionary<string, object>(), ["sealFreeSpinsAwarded"] = 0L,
            ["sealFreeSpinFeatureMode"] = string.Empty, ["specialBoostApplied"] = outcome.SpecialBoostApplied,
            ["consecutiveFiveMisses"] = outcome.ConsecutiveFiveMisses, ["pityMissesBefore"] = round.Before.Pity,
            ["fiveMatchPityTriggered"] = outcome.FiveMatchPityTriggered, ["outcomeSchemaVersion"] = OutcomeSchemaVersion,
            ["engineVersion"] = audit.EngineVersion, ["rulesSha256"] = rulesHash,
            ["admittedAtUtc"] = Timestamp.FromDateTime(round.StartedAtUtc), ["engineGeneratedAtUtc"] = Timestamp.FromDateTime(audit.GeneratedAtUtc),
            ["result"] = win ? "win" : "loss", ["createdAt"] = createdAt,
            ["walletRoundSettlementId"] = wallet.SettlementId, ["engineOutcomeSha256"] = round.OutcomeSha256,
            [NeonCarRespinStore.WorkerOwnerField] = NeonCarRespinStore.WorkerOwner
        };
        if (NeonCarRoundState.IsCarPlay(outcome.IsFreeSpin, round.Before.Awarded.FeatureMode))
            next.Car.WriteSpinResult(result, outcome);
        if (round.ChosenReel is { } chosen && round.Reservation is { } holdReceipt)
        {
            result["neonNitroChosenReel"] = chosen;
            result["walletReservationSettlementId"] = holdReceipt.SettlementId;
        }
        transaction.Create(database.Collection(resultCollection).Document(spin), result);
        transaction.Set(database.Collection("userSlotStatistics").Document(round.AccountId), new Dictionary<string, object>
        {
            ["userId"] = round.AccountId, ["spinsPlayed"] = FieldValue.Increment(1), ["wins"] = FieldValue.Increment(win ? 1 : 0),
            ["losses"] = FieldValue.Increment(win ? 0 : 1), ["creditsWagered"] = FieldValue.Increment(wager),
            ["creditsWon"] = FieldValue.Increment(payout), ["netCredits"] = FieldValue.Increment(net), ["updatedAt"] = createdAt
        }, SetOptions.MergeAll);
    }
}
