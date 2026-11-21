using FortuneForge.Games.Abstractions.Analytics;
using FortuneForge.Games.Abstractions.Auditing;
using FortuneForge.Games.Abstractions.Wallet;
using FortuneForge.Games.Firestore;
using Google.Cloud.Firestore;

namespace FortuneForge.Server.Cards.HouseHoldem;

internal static class HouseHoldemRoundReporting
{
    internal static void Write(Transaction transaction, FirestoreDb database, HouseHoldemPreparedCommand prepared,
        GameWalletSettlementReceipt receipt, DateTimeOffset now, HouseHoldemFinancialSnapshot? financialEvidence)
    {
        using var observed = prepared.Observation is { } observation ? GameSecurityContext.Push(observation) : null;
        var stored = prepared.After;
        var payload = new { round = stored.Round, deck = stored.Deck, steps = stored.Steps,
            walletSettlementId = receipt.SettlementId, outcomeSha256 = prepared.Digest };
        FirestoreGameJournal.Write(transaction, database, HouseHoldemBackend.GameId, stored.RoundId, stored.Steps.Length + 1,
            now.UtcDateTime, payload, [stored.UserId], stored.Round.Settlement is null ? prepared.Command : "round-settled");
        FirestoreGameAudit.WriteAcceptedParticipation(transaction, database, HouseHoldemBackend.GameId, stored.UserId, now.UtcDateTime);
        if (stored.Round.Settlement is not { } settlement) return;

        var record = SinglePlayerRoundRecord.Create(HouseHoldemBackend.GameId, stored.RoundId, stored.UserId,
            stored.Round.TotalWagerCents, stored.Round.PayoutCents, stored.CreatedAt.UtcDateTime, now.UtcDateTime,
            HouseHoldemBackend.RoundsCollection, stored.RoundId, payload, "house-holdem-cards-and-actions-v1",
            settlementVerified: true, outcomeAuditorVersion: "house-holdem-deck-action-replay-v1");
        transaction.Create(database.Collection(GameAnalyticsOutbox.CollectionName).Document(record.Settlement.Id), record.Settlement.Fields);
        FirestoreGameAudit.WriteCompletion(transaction, database, HouseHoldemFinancialProof.Protect(record.Audit, prepared, receipt,
            financialEvidence ?? throw new InvalidOperationException("The House Hold'em completion is missing frozen financial evidence.")));
        var resultId = $"house-holdem-{stored.RoundId}";
        transaction.Create(database.Collection("cardGameResults").Document(resultId), new Dictionary<string, object>
        {
            ["resultId"] = resultId, ["game"] = HouseHoldemBackend.GameId, ["mode"] = "credit",
            ["matchId"] = stored.RoundId, ["userId"] = stored.UserId,
            ["wagerCents"] = stored.Round.TotalWagerCents, ["payoutCents"] = stored.Round.PayoutCents,
            ["netCents"] = checked(stored.Round.PayoutCents - stored.Round.TotalWagerCents),
            ["outcome"] = settlement.Outcome, ["playerHandRank"] = settlement.PlayerHandRank,
            ["dealerHandRank"] = settlement.DealerHandRank, ["settlementStatus"] = "paid", ["claimStatus"] = "completed",
            ["startedAt"] = Timestamp.FromDateTime(stored.CreatedAt.UtcDateTime),
            ["completedAt"] = Timestamp.FromDateTime(now.UtcDateTime), ["schemaVersion"] = 1L
        });
    }
}
