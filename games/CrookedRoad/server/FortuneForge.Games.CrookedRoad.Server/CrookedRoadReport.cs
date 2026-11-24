using FortuneForge.Games.Abstractions.Analytics;
using FortuneForge.Games.Abstractions.Auditing;
using FortuneForge.Games.Abstractions.Wallet;
using Google.Cloud.Firestore;

namespace FortuneForge.Games.CrookedRoad.Server;

internal static class CrookedRoadReport
{
    internal static void Write(Transaction transaction, FirestoreDb database, CrookedRoadPreparedRound round,
        GameWalletSettlementReceipt wallet, DateTime completed)
    {
        CrookedRoadEvidence.Validate(round);
        var payload = new { prepared = round, walletSettlementId = wallet.SettlementId, wallet };
        SinglePlayerRoundRecord report;
        using (round.Observation is { } observation ? GameSecurityContext.Push(observation) : null)
            report = SinglePlayerRoundRecord.Create(CrookedRoadPaidRouteContract.AuditGameId, round.SpinId.ToString("N"),
                round.AccountId, round.WagerCents, round.PayoutCents, round.StartedAtUtc, completed,
                CrookedRoadRoundStore.RawCollectionName, round.AdmissionId.ToString("N"), payload,
                CrookedRoadRules.EngineVersion, true, CrookedRoadRules.EngineVersion);
        transaction.Create(database.Collection(GameAnalyticsOutbox.CollectionName).Document(report.Settlement.Id), report.Settlement.Fields);
        var evidence = RoundAuditDocuments.Create(report.Audit);
        transaction.Create(database.Collection(RoundAuditDocuments.Summaries).Document(report.Audit.Summary.Id), evidence.Summary);
        transaction.Create(database.Collection(RoundAuditDocuments.Evidence).Document(report.Audit.Summary.Id), evidence.Evidence);
        var journal = GameSecurityEventDocuments.Capture(CrookedRoadPaidRouteContract.AuditGameId, round.SpinId.ToString("N"),
            1, completed, payload, [round.AccountId], "round-settled");
        WriteJournal(transaction, database, journal, round.SpinId.ToString("N"));
    }

    internal static void WriteJournal(Transaction transaction, FirestoreDb database, GameSecurityEventDocument journal, string instance)
    {
        transaction.Create(database.Collection(GameSecurityEventDocuments.CollectionName).Document(journal.Id), journal.Fields);
        transaction.Set(database.Collection(GameSecurityEventDocuments.ManifestCollectionName)
            .Document(GameSecurityEventDocuments.ManifestId(CrookedRoadPaidRouteContract.AuditGameId, instance)), new Dictionary<string, object>
        {
            ["gameId"] = CrookedRoadPaidRouteContract.AuditGameId, ["instanceId"] = instance,
            ["eventIds"] = FieldValue.ArrayUnion(journal.Id), ["schemaVersion"] = 1L
        }, SetOptions.MergeAll);
    }
}
