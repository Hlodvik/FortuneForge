using FortuneForge.Games.Abstractions.Analytics;
using FortuneForge.Games.Abstractions.Auditing;
using FortuneForge.Games.Abstractions.Wallet;
using FortuneForge.Games.CasinoWar;
using FortuneForge.Games.Firestore;
using Google.Cloud.Firestore;

namespace FortuneForge.Server.Cards.CasinoWar;

internal static class CasinoWarRoundReporting
{
    internal static void Write(Transaction transaction, FirestoreDb database, CasinoWarPreparedAction prepared,
        GameWalletSettlementReceipt receipt, DateTimeOffset now)
    {
        using var observation = prepared.Observation is { } original ? GameSecurityContext.Push(original) : null;
        var stored = prepared.After.Restore();
        var game = CasinoWarModule.Descriptor.Id;
        if (stored.Round.Phase == CasinoWarRoundPhase.Completed)
        {
            var primary = stored.Round.Settlement!;
            var wager = checked(CasinoWarMoney.ToCents(primary.TotalWagered) + prepared.After.TieStakeCents);
            var payout = checked(CasinoWarMoney.ToCents(primary.TotalReturn) +
                CasinoWarMoney.ToCents(stored.TieSettlement?.TotalReturn ?? 0m));
            var record = SinglePlayerRoundRecord.Create(game, stored.RoundId, stored.UserId, wager, payout,
                prepared.After.CreatedAt.UtcDateTime, now.UtcDateTime, CasinoWarRoundCodec.Rounds, stored.RoundId,
                stored, "casino-war-cards-and-decision-v1", settlementVerified: true);
            transaction.Create(database.Collection(GameAnalyticsOutbox.CollectionName).Document(record.Settlement.Id), record.Settlement.Fields);
            FirestoreGameAudit.WriteCompletion(transaction, database, CasinoWarFinancialProof.Protect(record.Audit, prepared, receipt));
            FirestoreGameJournal.Write(transaction, database, game, stored.RoundId, 1, now.UtcDateTime,
                stored, [stored.UserId], "round-settled");
        }
        else
            FirestoreGameJournal.Write(transaction, database, game, stored.RoundId, 0, now.UtcDateTime,
                stored, [stored.UserId], "opening");
        FirestoreGameAudit.WriteAcceptedParticipation(transaction, database, game, stored.UserId, now.UtcDateTime);
    }
}
