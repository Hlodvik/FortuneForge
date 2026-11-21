using System.Text.Json;
using FortuneForge.Games.Abstractions.Auditing;
using FortuneForge.Games.Abstractions.Wallet;
using FortuneForge.Games.Firestore;
using Google.Cloud.Firestore;

namespace FortuneForge.Server.Cards.Solitaire;

internal sealed record SolitaireFinancialSnapshot(RoundAuditLedgerEntry[] Entries, RoundAuditWalletOperation[] Operations)
{
    internal static SolitaireFinancialSnapshot Empty => new([], []);
}

internal sealed record SolitaireCompletionReference(string EvidenceId, string? EvidenceSha256);

internal sealed partial class FirestoreCompetitiveSolitaireStore
{
    internal const string FinancialProofContract = "solitaire-protected-wallet-evidence-v1";

    private async Task<SolitaireFinancialSnapshot> ReadEntryFinancialProofAsync(Transaction transaction,
        SolitaireMatch match, IReadOnlyList<SolitairePlayerState> players, CancellationToken token)
    {
        // Managed seats retain an empty ticket placeholder in the legacy match schema.
        var tickets = await Task.WhenAll(match.TicketIds.Where(id => !string.IsNullOrWhiteSpace(id)).Distinct(StringComparer.Ordinal)
            .Select(id => transaction.GetSnapshotAsync(TicketDocument(id), token)));
        var entries = new List<RoundAuditLedgerEntry>();
        var operations = new List<RoundAuditWalletOperation>();
        foreach (var player in players.Where(player => player.IsAccountBacked))
        {
            var ticket = tickets.SingleOrDefault(snapshot => ReadString(snapshot, "userId") == player.UserId);
            var operationId = ticket is null ? "" : ReadString(ticket, "buyInWalletOperationId");
            if (ticket is not null && ReadString(ticket, "backendOwner") == BackendOwner && operationId.Length > 0)
            {
                var saved = ReadFinancial(await transaction.GetSnapshotAsync(FinancialOperation(operationId), token));
                if (saved.Plan.Operation != "join" || saved.Plan.AccountId != player.UserId || saved.Plan.Target != ticket.Id ||
                    saved.Plan.WagerCents != match.BuyInCents || saved.Plan.PayoutCents != 0 ||
                    saved.Wallet is not { BatchDeclined: false } || saved.Stage != "complete" ||
                    ReadString(ticket, "buyInWalletBatchId") != saved.Wallet.BatchId)
                    throw new GameWalletUnavailableException();
                var proof = ConfirmedFinancialProof(saved, "solitaire-buyin", match.MatchId);
                entries.AddRange(proof.Entries);
                operations.AddRange(proof.Operations);
            }
            else
            {
                var legacyId = (ticket?.Id ?? match.MatchId) + "-buyin";
                var legacy = await transaction.GetSnapshotAsync(database.Collection("balanceTransactions").Document(legacyId), token);
                if (legacy.Exists)
                {
                    var entry = FirestoreWalletEvidence.Legacy(legacy, match.MatchId);
                    if (entry.UserId != player.UserId || entry.Kind != "solitaire-buyin" || entry.AmountCents != -match.BuyInCents)
                        throw new GameWalletUnavailableException();
                    entries.Add(entry);
                }
                else
                    entries.Add(new("balanceTransactions", legacyId, player.UserId, "solitaire-buyin", match.MatchId,
                        -match.BuyInCents, RoundAuditFinancialProof.LegacyHostUnavailable, EvidenceAvailable: false));
            }
        }
        return new(entries.ToArray(), operations.ToArray());
    }

    private static GameWalletBatchRequest FinancialRequest(SolitaireFinancialState state) =>
        new(state.Plan.Id, state.Digest, [new(state.Plan.AccountId, state.Plan.WagerCents, state.Plan.PayoutCents)]);

    private static SolitaireFinancialSnapshot ConfirmedFinancialProof(SolitaireFinancialState state, string kind, string roundId)
    {
        var request = FinancialRequest(state);
        var receipt = state.Wallet;
        if (receipt is null || receipt.BatchDeclined || !GameWalletBatchVerifier.MatchesSettlement(receipt, ComputeGameId, state.Plan.AccountId, request))
            throw new GameWalletUnavailableException();
        var account = receipt.Accounts.Single();
        var entries = new List<RoundAuditLedgerEntry>();
        if (account.WagerTransactionId is { } wager)
            entries.Add(new("balanceTransactions", wager, account.AccountId, kind, roundId, -state.Plan.WagerCents,
                RoundAuditFinancialProof.SignedWallet, state.Plan.Id, receipt.BatchId));
        if (account.PayoutTransactionId is { } payout)
            entries.Add(new("balanceTransactions", payout, account.AccountId, kind, roundId, state.Plan.PayoutCents,
                RoundAuditFinancialProof.SignedWallet, state.Plan.Id, receipt.BatchId));
        return new(entries.ToArray(), [new(FinancialOperations, state.Plan.Id, state.Digest,
            entries.Select(entry => entry.DocumentId).ToArray(), new Dictionary<string, GameWalletSettlementReceipt>(),
            BatchRequest: request, BatchReceipt: receipt)]);
    }

    private static RoundAuditCapture ProtectCompletion(RoundAuditCapture capture, SolitaireFinancialSnapshot proof) =>
        RoundAuditFinancialProof.Attach(capture, BackendOwner, FinancialProofContract, proof.Entries, proof.Operations);

    private void WriteClaimSecurityState(Transaction transaction, SolitaireFinancialState confirmed, SolitaireFrozenJournal journal)
    {
        var completion = confirmed.Plan.Completion ?? throw new InvalidOperationException("The Solitaire claim has no original completion anchor.");
        var proof = ConfirmedFinancialProof(confirmed, "solitaire-claim", journal.Match.MatchId);
        // Reuse the shared financial-proof owner for its canonical evidence shape and provenance.
        var protectedCapture = ProtectCompletion(CompetitiveSolitaireRoundAudit.Capture(journal.Match, journal.Players,
            journal.Match.CompletedAtUtc ?? journal.OccurredAtUtc), proof);
        using var document = JsonDocument.Parse(protectedCapture.PayloadJson);
        var financialProof = document.RootElement.GetProperty(RoundAuditFinancialProof.PropertyName).Clone();
        FirestoreGameAudit.WriteObservation(transaction, database, ComputeGameId, journal.Match.MatchId,
            journal.Players.Sum(player => (long)player.Version), journal.OccurredAtUtc,
            new
            {
                match = journal.Match, players = journal.Players, versionScope = "sum-of-seat-versions",
                completionEvidenceId = completion.EvidenceId, completionEvidenceSha256 = completion.EvidenceSha256,
                completionEvidenceAvailable = completion.EvidenceSha256 is not null,
                financialProof,
            }, journal.Players.Where(player => player.IsAccountBacked).Select(player => player.UserId), journal.Kind);
    }
}
