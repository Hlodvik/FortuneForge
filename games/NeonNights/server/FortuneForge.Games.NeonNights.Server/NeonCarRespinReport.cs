using System.Text.Json;
using FortuneForge.Games.Abstractions.Analytics;
using FortuneForge.Games.Abstractions.Auditing;
using FortuneForge.Games.Abstractions.Wallet;
using Google.Cloud.Firestore;

namespace FortuneForge.Games.NeonNights.Server;

public static class NeonCarRespinReport
{
    public const string ReceiptCollection = "neonCarRespinReceipts";

    internal static void Write(Transaction transaction, FirestoreDb database, DocumentReference reference,
        NeonCarRespinStore.CarSource source, NeonCarRespinStore.FrozenRespin frozen, GameWalletSettlementReceipt wallet)
    {
        var completed = wallet.SettledAtUtc.UtcDateTime;
        var timestamp = Timestamp.FromDateTime(completed);
        var frozenSha = NeonCarRespinStore.Hash(frozen);
        transaction.Create(reference, new Dictionary<string, object>
        {
            ["userId"] = frozen.AccountId,
            ["roundId"] = source.RoundId,
            ["sourceSpinId"] = frozen.SpinId.ToString("N"),
            ["reelIndex"] = (long)frozen.Outcome.ReelIndex,
            ["outcomeJson"] = JsonSerializer.Serialize(frozen.Outcome),
            ["sourceSha256"] = frozen.SourceSha256,
            ["improvementPoints"] = frozen.Outcome.ImprovementPoints,
            ["improvementCents"] = frozen.PayoutCents,
            ["neonNitroChosenReel"] = (long?)frozen.ChosenNitroReel ?? -1L,
            ["createdAt"] = timestamp,
            ["walletRoundSettlementId"] = wallet.SettlementId,
            ["outcomeSha256"] = frozenSha,
            ["operationId"] = frozen.Id
        });

        if (frozen.PayoutCents > 0)
        {
            var win = (double)(frozen.PayoutCents / 100m);
            transaction.Set(database.Collection("userSlotStatistics").Document(frozen.AccountId), new Dictionary<string, object>
            {
                ["userId"] = frozen.AccountId,
                ["creditsWon"] = FieldValue.Increment(win),
                ["netCredits"] = FieldValue.Increment(win),
                ["updatedAt"] = timestamp
            }, SetOptions.MergeAll);
        }

        var gameId = NeonCarRespinEndpoints.AuditGameId;
        var roundId = $"{source.ReceiptId}-car-respin";
        var payload = new
        {
            originalSpinId = frozen.SpinId,
            sourceSha256 = frozen.SourceSha256,
            outcome = frozen.Outcome,
            improvementCents = frozen.PayoutCents,
            chosenNitroReel = frozen.ChosenNitroReel,
            initialRequestId = frozen.Observation?.RequestId,
            choiceRequestId = frozen.ChoiceObservation?.RequestId,
            outcomeSha256 = frozenSha,
            operationId = frozen.Id,
            walletSettlementId = wallet.SettlementId
        };
        SinglePlayerRoundRecord report;
        using ((frozen.Observation ?? frozen.ChoiceObservation) is { } original ? GameSecurityContext.Push(original) : null)
            report = SinglePlayerRoundRecord.Create(gameId, roundId, frozen.AccountId, 0, frozen.PayoutCents,
                source.CreatedAtUtc, completed, ReceiptCollection, reference.Id, payload, NeonCarRespin.RuleVersion,
                settlementVerified: true, outcomeAuditorVersion: NeonCarRespin.RuleVersion);
        transaction.Create(database.Collection(GameAnalyticsOutbox.CollectionName).Document(report.Settlement.Id), report.Settlement.Fields);
        var evidence = RoundAuditDocuments.Create(report.Audit);
        var auditId = report.Audit.Summary.Id;
        transaction.Create(database.Collection(RoundAuditDocuments.Summaries).Document(auditId), evidence.Summary);
        transaction.Create(database.Collection(RoundAuditDocuments.Evidence).Document(auditId), evidence.Evidence);

        GameSecurityEventDocument journal;
        using ((frozen.ChoiceObservation ?? frozen.Observation) is { } selected ? GameSecurityContext.Push(selected) : null)
            journal = GameSecurityEventDocuments.Capture(gameId, roundId, 1, completed, payload, [frozen.AccountId], "round-settled");
        transaction.Set(database.Collection(GameSecurityEventDocuments.CollectionName).Document(journal.Id), journal.Fields);
        transaction.Set(database.Collection(GameSecurityEventDocuments.ManifestCollectionName)
            .Document(GameSecurityEventDocuments.ManifestId(gameId, roundId)), new Dictionary<string, object>
            {
                ["gameId"] = gameId, ["instanceId"] = roundId,
                ["eventIds"] = FieldValue.ArrayUnion(journal.Id), ["schemaVersion"] = 1L
            }, SetOptions.MergeAll);
    }
}
