using FortuneForge.Games.Abstractions.Analytics;
using FortuneForge.Games.Abstractions.Auditing;
using Google.Cloud.Firestore;

namespace FortuneForge.Games.Slots.Persistence;

public sealed record ClassicSlotPaidReport(Dictionary<string, object> Result, Dictionary<string, object> Statistics,
    SinglePlayerRoundRecord Round, GameSecurityEventDocument Journal,
    IReadOnlyDictionary<string, Dictionary<string, object>> GameplayWitnesses);

/// <summary>Writes the platform projections of a package's settled classic slot round.</summary>
public static class ClassicSlotReportWriter
{
    public const string ResultCollection = "slotSpinResults";

    public static ClassicSlotPaidReport Prepare(Dictionary<string, object> result, SinglePlayerRoundRecord round,
        GameSecurityEventDocument journal, IReadOnlyDictionary<string, Dictionary<string, object>> gameplayWitnesses)
    {
        var summary = round.Audit.Summary;
        var participant = Account(summary);
        var win = participant.PayoutCents > 0;
        var statistics = new Dictionary<string, object>
        {
            ["userId"] = participant.ActorId,
            ["spinsPlayed"] = FieldValue.Increment(1),
            ["wins"] = FieldValue.Increment(win ? 1 : 0),
            ["losses"] = FieldValue.Increment(win ? 0 : 1),
            ["creditsWagered"] = FieldValue.Increment((double)(participant.WagerCents / 100m)),
            ["creditsWon"] = FieldValue.Increment((double)(participant.PayoutCents / 100m)),
            ["netCredits"] = FieldValue.Increment((double)(participant.NetCents / 100m)),
            ["updatedAt"] = Timestamp.FromDateTime(summary.CompletedAtUtc)
        };
        return new(result, statistics, round, journal, gameplayWitnesses);
    }

    public static void Write(Transaction transaction, FirestoreDb database, ClassicSlotPaidReport report)
    {
        var summary = report.Round.Audit.Summary;
        var account = Account(summary).ActorId;
        transaction.Create(database.Collection(ResultCollection).Document(summary.RoundId), report.Result);
        transaction.Set(database.Collection("userSlotStatistics").Document(account), report.Statistics, SetOptions.MergeAll);
        transaction.Create(database.Collection(GameAnalyticsOutbox.CollectionName).Document(report.Round.Settlement.Id),
            report.Round.Settlement.Fields);

        var audit = RoundAuditDocuments.Create(report.Round.Audit);
        transaction.Create(database.Collection(RoundAuditDocuments.Summaries).Document(summary.Id), audit.Summary);
        transaction.Create(database.Collection(RoundAuditDocuments.Evidence).Document(summary.Id), audit.Evidence);
        transaction.Set(database.Collection(GameSecurityEventDocuments.CollectionName).Document(report.Journal.Id), report.Journal.Fields);
        transaction.Set(database.Collection(GameSecurityEventDocuments.ManifestCollectionName)
            .Document(GameSecurityEventDocuments.ManifestId(summary.GameId, summary.RoundId)), new Dictionary<string, object>
            {
                ["gameId"] = summary.GameId, ["instanceId"] = summary.RoundId,
                ["eventIds"] = FieldValue.ArrayUnion(report.Journal.Id), ["schemaVersion"] = 1L
            }, SetOptions.MergeAll);

        var participation = AcceptedGameParticipationDocuments.Create(account, summary.GameId, summary.CompletedAtUtc);
        transaction.Set(database.Collection("catalogPlayerActivity").Document(participation.DocOwnerKey)
            .Collection("games").Document(participation.GameId), participation.Fields, SetOptions.MergeAll);

        foreach (var (id, fields) in report.GameplayWitnesses)
            transaction.Create(database.Collection("balanceTransactions").Document(id), fields);
    }

    private static RoundAuditParticipant Account(RoundAuditSummary summary) =>
        summary.Participants is [{ IsAccountBacked: true } account] ? account :
        throw new InvalidOperationException("A paid slot report must identify one account-backed participant.");
}
