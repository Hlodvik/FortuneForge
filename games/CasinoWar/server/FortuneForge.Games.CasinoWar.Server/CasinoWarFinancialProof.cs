using FortuneForge.Games.Abstractions.Auditing;
using FortuneForge.Games.Abstractions.Wallet;
using FortuneForge.Games.CasinoWar;
using FortuneForge.Games.Firestore;
using Google.Cloud.Firestore;

namespace FortuneForge.Server.Cards.CasinoWar;

internal sealed record CasinoWarFinancialSnapshot(RoundAuditLedgerEntry[] Entries, RoundAuditWalletOperation[] Operations)
{
    internal static CasinoWarFinancialSnapshot Empty => new([], []);
}

internal static class CasinoWarFinancialProof
{
    internal const string Contract = "casino-war-protected-wallet-evidence-v1";

    internal static CasinoWarFinancialSnapshot Confirmed(CasinoWarPreparedAction action, GameWalletSettlementReceipt receipt)
    {
        if (!GameWalletReceiptVerifier.MatchesSettlement(receipt, CasinoWarModule.GameId, action.AccountId, action.WalletRequest()) ||
            receipt.InsufficientFunds || receipt.InsufficientLegacyFreeGames || receipt.LegacyFreeGamesCountMismatch)
            throw new GameWalletUnavailableException();
        var entries = new List<RoundAuditLedgerEntry>();
        if (receipt.WagerTransactionId is { } wager)
            entries.Add(new("balanceTransactions", wager, action.AccountId,
                action.Kind == "opening" ? "casino-war-opening-wager" : "casino-war-war-wager", action.RoundId,
                -action.WagerCents, RoundAuditFinancialProof.SignedWallet, action.WalletOperationId, receipt.SettlementId));
        if (receipt.PayoutTransactionId is { } payout)
            entries.Add(new("balanceTransactions", payout, action.AccountId,
                action.Kind == "opening" ? "casino-war-opening-payout" : "casino-war-primary-payout", action.RoundId,
                action.PayoutCents, RoundAuditFinancialProof.SignedWallet, action.WalletOperationId, receipt.SettlementId));
        return new(entries.ToArray(), [new(CasinoWarFirestoreStore.Commands, action.Id, action.Digest,
            entries.Select(entry => entry.DocumentId).ToArray(),
            new Dictionary<string, GameWalletSettlementReceipt> { [action.AccountId] = receipt })]);
    }

    internal static async Task<CasinoWarFinancialSnapshot> ReadLegacyOpeningAsync(Transaction transaction, FirestoreDb database,
        CasinoWarSavedRound round, CancellationToken token)
    {
        var opening = round with { Decision = null, DecisionKey = null };
        var stored = opening.Restore();
        var expected = new (string Suffix, string Kind, long Amount)[]
        {
            ("opening-wager", "casino-war-opening-wager", -checked(round.PrimaryStakeCents + round.TieStakeCents)),
            ("tie-payout", "casino-war-tie-payout", CasinoWarMoney.ToCents(stored.TieSettlement?.TotalReturn ?? 0m)),
            ("opening-primary-payout", "casino-war-primary-payout", CasinoWarMoney.ToCents(stored.Round.Settlement?.TotalReturn ?? 0m)),
        };
        var entries = new List<RoundAuditLedgerEntry>();
        foreach (var term in expected.Where(term => term.Amount != 0))
        {
            var id = $"casino-war-{round.RoundId}-{term.Suffix}";
            var saved = await transaction.GetSnapshotAsync(database.Collection("balanceTransactions").Document(id), token);
            if (!saved.Exists)
            {
                entries.Add(new("balanceTransactions", id, round.UserId, term.Kind, round.RoundId, term.Amount,
                    RoundAuditFinancialProof.LegacyHostUnavailable, EvidenceAvailable: false));
                continue;
            }
            var entry = FirestoreWalletEvidence.Legacy(saved, round.RoundId);
            if (entry.UserId != round.UserId || entry.Kind != term.Kind || entry.AmountCents != term.Amount)
                throw new GameWalletUnavailableException();
            entries.Add(entry);
        }
        return new(entries.ToArray(), []);
    }

    internal static RoundAuditCapture Protect(RoundAuditCapture capture, CasinoWarPreparedAction action, GameWalletSettlementReceipt receipt)
    {
        var prior = action.PriorFinancialEvidence ?? CasinoWarFinancialSnapshot.Empty;
        var current = Confirmed(action, receipt);
        return RoundAuditFinancialProof.Attach(capture, CasinoWarRoundCodec.BackendOwner, Contract,
            prior.Entries.Concat(current.Entries).ToArray(), prior.Operations.Concat(current.Operations).ToArray());
    }
}
