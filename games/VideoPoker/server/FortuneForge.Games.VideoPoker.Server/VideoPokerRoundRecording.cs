using FortuneForge.Games.Abstractions.Analytics;
using FortuneForge.Games.Abstractions.Auditing;
using FortuneForge.Games.Abstractions.Wallet;
using FortuneForge.Games.Firestore;
using Google.Cloud.Firestore;

namespace FortuneForge.Server.Cards.VideoPoker;

internal static class VideoPokerRoundRecording
{
    internal static void Write(Transaction transaction, FirestoreDb database, VideoPokerPreparedOperation operation,
        DateTime completedAtUtc, VideoPokerFinancialSnapshot previous, GameWalletSettlementReceipt receipt)
    {
        using var scope = operation.Observation is { } observed ? GameSecurityContext.Push(observed) : null;
        var security = GameSecurityEventDocuments.Capture(VideoPokerBackend.GameId, operation.RoundId,
            operation.Kind == "deal" ? 0 : 1, operation.OccurredAtUtc.UtcDateTime,
            operation.Round, [operation.UserId], operation.Kind == "deal" ? "deal" : "round-settled");
        FirestoreGameAudit.WriteObservation(transaction, database, security);
        FirestoreGameAudit.WriteAcceptedParticipation(transaction, database, VideoPokerBackend.GameId, operation.UserId, completedAtUtc);
        if (operation.Kind != "draw") return;

        var record = SinglePlayerRoundRecord.Create(VideoPokerBackend.GameId, operation.RoundId, operation.UserId,
            VideoPokerMoney.WagerCents(operation.Round.CoinsWagered, operation.Round.HandCount), operation.PayoutCents,
            operation.StartedAtUtc.UtcDateTime, completedAtUtc, "videoPokerRounds", operation.RoundId,
            operation.Round, "video-poker-deal-holds-and-draw-v1", true);
        transaction.Create(database.Collection(GameAnalyticsOutbox.CollectionName).Document(record.Settlement.Id), record.Settlement.Fields);
        FirestoreGameAudit.WriteCompletion(transaction, database, VideoPokerFirestoreStore.ProtectCompletion(record.Audit, operation, previous, receipt));
    }
}
