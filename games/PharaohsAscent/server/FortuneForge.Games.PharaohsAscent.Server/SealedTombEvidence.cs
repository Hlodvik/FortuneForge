using System.Text.Json;
using FortuneForge.Games.Abstractions.Analytics;
using FortuneForge.Games.Abstractions.Auditing;
using FortuneForge.Games.Abstractions.Wallet;
using Google.Cloud.Firestore;

namespace FortuneForge.Games.PharaohsAscent.Server;

/// <summary>Replays non-reel expedition choices and proves their final wallet credit.</summary>
public static class SealedTombEvidence
{
    public const string AuditorVersion = "pharaoh-sealed-tomb-auditor-v1";
    public const string EvidenceKind = "pharaoh-sealed-tomb-action-v1";
    public const string ResultCollection = "pharaohSealedTombActions";

    public static bool IsNative(RoundAuditSummary summary) =>
        summary.GameId == PharaohsAscentPaidRouteContract.AuditGameId && summary.AuditorVersion == AuditorVersion && summary.EvidenceKind == EvidenceKind;

    public static IReadOnlyList<RoundAuditFinding> Inspect(RoundAuditSummary summary, JsonElement data)
    {
        try
        {
            var round = data.GetProperty("prepared").Deserialize<PharaohsAscentPreparedRound>(RoundAuditCapture.Json) ?? throw Invalid();
            var wallet = data.GetProperty("wallet").Deserialize<GameWalletSettlementReceipt>(RoundAuditCapture.Json) ?? throw Invalid();
            PharaohsAscentRoundPolicy.Validate(round);
            ValidateWallet(round, wallet);
            if (!IsNative(summary) || round.TombEvidence is not { PayoutPoints: > 0 } tomb ||
                summary.RoundId != tomb.EventId.ToString("N") ||
                summary.Participants is not [{ IsAccountBacked: true } account] || account.ActorId != round.AccountId ||
                account.WagerCents != 0 || account.PayoutCents != round.PayoutCents || account.NetCents != round.PayoutCents ||
                summary.StartedAtUtc.Ticks / TimeSpan.TicksPerMicrosecond != round.StartedAtUtc.Ticks / TimeSpan.TicksPerMicrosecond ||
                summary.CompletedAtUtc < tomb.GeneratedAtUtc || data.GetProperty("walletSettlementId").GetString() != wallet.SettlementId)
                throw Invalid();
            return [];
        }
        catch (Exception exception) when (exception is JsonException or ArgumentException or InvalidOperationException or
            OverflowException or KeyNotFoundException or NullReferenceException or GameWalletUnavailableException)
        {
            return [new("pharaoh-sealed-tomb-proof-mismatch", "error", "The Sealed Tomb choices, funded stake or wallet settlement do not replay.")];
        }
    }

    internal static void Write(Transaction transaction, FirestoreDb database, PharaohsAscentPreparedRound round,
        GameWalletSettlementReceipt wallet, DateTime completed)
    {
        PharaohsAscentRoundPolicy.Validate(round);
        ValidateWallet(round, wallet);
        var tomb = round.TombEvidence ?? throw Invalid();
        var id = tomb.EventId.ToString("N");
        var payload = new { prepared = round, walletSettlementId = wallet.SettlementId, wallet };
        transaction.Create(database.Collection(ResultCollection).Document(id), new Dictionary<string, object>
        {
            ["userId"] = round.AccountId, ["operationId"] = round.Request.OperationId,
            ["action"] = round.Request.TombAction!.Action, ["payoutPoints"] = tomb.PayoutPoints,
            ["payoutCents"] = round.PayoutCents, ["walletSettlementId"] = wallet.SettlementId,
            ["createdAt"] = Timestamp.FromDateTime(completed),
        });
        var journal = GameSecurityEventDocuments.Capture(PharaohsAscentPaidRouteContract.AuditGameId, id, 1, completed,
            payload, [round.AccountId], "sealed-tomb-action");
        transaction.Create(database.Collection(GameSecurityEventDocuments.CollectionName).Document(journal.Id), journal.Fields);
        transaction.Set(database.Collection(GameSecurityEventDocuments.ManifestCollectionName)
            .Document(GameSecurityEventDocuments.ManifestId(PharaohsAscentPaidRouteContract.AuditGameId, id)), new Dictionary<string, object>
        {
            ["gameId"] = PharaohsAscentPaidRouteContract.AuditGameId, ["instanceId"] = id,
            ["eventIds"] = FieldValue.ArrayUnion(journal.Id), ["schemaVersion"] = 1L,
        }, SetOptions.MergeAll);
        if (tomb.PayoutPoints == 0) return;

        var report = Capture(round, wallet, completed);
        transaction.Create(database.Collection(GameAnalyticsOutbox.CollectionName).Document(report.Settlement.Id), report.Settlement.Fields);
        var audit = RoundAuditDocuments.Create(report.Audit);
        transaction.Create(database.Collection(RoundAuditDocuments.Summaries).Document(report.Audit.Summary.Id), audit.Summary);
        transaction.Create(database.Collection(RoundAuditDocuments.Evidence).Document(report.Audit.Summary.Id), audit.Evidence);
        transaction.Set(database.Collection("userSlotStatistics").Document(round.AccountId), new Dictionary<string, object>
        {
            ["userId"] = round.AccountId, ["creditsWon"] = FieldValue.Increment((double)(round.PayoutCents / 100m)),
            ["netCredits"] = FieldValue.Increment((double)(round.PayoutCents / 100m)),
            ["sealedTombsCompleted"] = FieldValue.Increment(1), ["updatedAt"] = Timestamp.FromDateTime(completed),
        }, SetOptions.MergeAll);
    }

    internal static SinglePlayerRoundRecord Capture(PharaohsAscentPreparedRound round, GameWalletSettlementReceipt wallet, DateTime completed)
    {
        var tomb = round.TombEvidence ?? throw Invalid();
        PharaohsAscentRoundPolicy.Validate(round);
        ValidateWallet(round, wallet);
        using var observation = round.Observation is { } observed ? GameSecurityContext.Push(observed) : null;
        return SinglePlayerRoundRecord.Create(PharaohsAscentPaidRouteContract.AuditGameId, tomb.EventId.ToString("N"),
            round.AccountId, 0, round.PayoutCents, round.StartedAtUtc, completed, ResultCollection, tomb.EventId.ToString("N"),
            new { prepared = round, walletSettlementId = wallet.SettlementId, wallet }, EvidenceKind, true, AuditorVersion);
    }

    private static void ValidateWallet(PharaohsAscentPreparedRound round, GameWalletSettlementReceipt wallet)
    {
        var tomb = round.TombEvidence ?? throw Invalid();
        var id = tomb.EventId.ToString("N");
        if (wallet.SettlementId != GameWalletSettlementIdentity.CreateId(PharaohsAscentEngine.GameId, round.AccountId, id) ||
            wallet.WagerTransactionId is not null || wallet.PayoutTransactionId != (round.PayoutCents > 0 ? id + "-payout" : null) ||
            wallet.LegacyFreeGameTransactionId is not null || wallet.LegacyFreeGamesRemaining != round.Before.LegacyRemaining ||
            wallet.InsufficientFunds || wallet.InsufficientLegacyFreeGames || wallet.LegacyFreeGamesCountMismatch ||
            wallet.BalanceBeforeCents < 0 || wallet.BalanceAfterWagerCents != wallet.BalanceBeforeCents ||
            wallet.BalanceAfterCents != checked(wallet.BalanceBeforeCents + round.PayoutCents)) throw Invalid();
    }
    private static InvalidOperationException Invalid() => new("The Sealed Tomb settlement proof is invalid.");
}
