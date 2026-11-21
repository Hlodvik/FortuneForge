using FortuneForge.Games.Abstractions.Wallet;
using Google.Cloud.Firestore;

namespace FortuneForge.Server.Cards.Blackjack.Table;

internal sealed partial class FirestoreBlackjackTableStore
{
    private async Task<BlackjackFinancialSnapshot> ReadRoundFinancialEvidenceAsync(Transaction transaction, string stateId,
        BlackjackTableLobbyState after, BlackjackTableJournal journal, CancellationToken token)
    {
        var entries = new List<BlackjackLedgerProof>();
        var operations = new Dictionary<string, BlackjackWalletOperationProof>(StringComparer.Ordinal);
        var savedOperations = new Dictionary<string, StoredTableCommand>(StringComparer.Ordinal);
        foreach (var audit in journal.RoundAudits)
        {
            var roundId = audit.Summary.RoundId;
            var accounts = audit.Summary.Participants.Where(player => player.IsAccountBacked).Select(player => player.ActorId).ToHashSet(StringComparer.Ordinal);
            var archive = await transaction.GetSnapshotAsync(database.Collection(BlackjackBackend.TableLedgerEvidence)
                .WhereEqualTo("idempotencyKey", roundId), token);
            foreach (var snapshot in archive.Documents)
            {
                var entry = BlackjackFinancialProof.Legacy(snapshot, roundId);
                if (!accounts.Contains(entry.UserId)) continue;
                var operationId = snapshot.GetValue<string>("walletMutationId");
                if (!operations.TryGetValue(operationId, out var operation))
                {
                    var saved = ReadPrepared(await transaction.GetSnapshotAsync(PreparedCommand(operationId), token));
                    if (!saved.Finalized || saved.Declined) throw new GameWalletUnavailableException();
                    operation = TableFinancialOperation(saved.Terms, saved.Receipts);
                    operations.Add(operationId, operation);
                    savedOperations.Add(operationId, saved);
                }
                var expected = savedOperations[operationId].Terms.Journal.Ledger.Single(value => value.Id == entry.DocumentId);
                if (expected.UserId != entry.UserId || expected.AmountCents != entry.AmountCents || expected.Type != entry.Kind ||
                    expected.Reference != roundId) throw new GameWalletUnavailableException();
                entries.Add(entry with { Collection = BlackjackBackend.TableLedgerEvidence, Provenance = "signed-wallet",
                    WalletOperationId = operationId, WalletSettlementId = operation.ReceiptsByAccount[entry.UserId].SettlementId });
            }
            var legacy = await transaction.GetSnapshotAsync(database.Collection("balanceTransactions")
                .WhereEqualTo("idempotencyKey", roundId), token);
            foreach (var snapshot in legacy.Documents)
            {
                var entry = BlackjackFinancialProof.Legacy(snapshot, roundId);
                if (accounts.Contains(entry.UserId) && entry.Kind.StartsWith("blackjack-table-", StringComparison.Ordinal)) entries.Add(entry);
            }
            // Very old accepted state can outlive its original platform ledger retention.
            // Preserve that prepaid hand and make the missing historical evidence explicit.
            foreach (var player in audit.Summary.Participants.Where(player => player.IsAccountBacked))
            {
                var recordedWager = -entries.Where(entry => entry.RoundReference == roundId && entry.UserId == player.ActorId &&
                    !entry.Kind.Contains("payout", StringComparison.Ordinal)).Sum(entry => entry.AmountCents);
                recordedWager -= journal.Ledger.Where(entry => entry.Reference == roundId && entry.UserId == player.ActorId &&
                    !entry.Type.Contains("payout", StringComparison.Ordinal)).Sum(entry => entry.AmountCents);
                if (recordedWager < player.WagerCents)
                    entries.Add(new(BlackjackBackend.TableState, stateId, player.ActorId, "legacy-prepaid-stake", roundId,
                        checked(recordedWager - player.WagerCents), "legacy-host-unavailable", EvidenceAvailable: false));
            }
        }
        return new(entries.ToArray(), operations.Values.ToArray());
    }

    private static BlackjackTableJournal ProtectedJournal(TableCommandTerms terms, Dictionary<string, GameWalletSettlementReceipt> receipts)
    {
        var current = terms.Journal.Ledger.Select(entry => new BlackjackLedgerProof(BlackjackBackend.TableLedgerEvidence,
            entry.Id, entry.UserId, entry.Type, entry.Reference, entry.AmountCents, "signed-wallet", terms.Id,
            receipts[entry.UserId].SettlementId)).ToArray();
        var all = (terms.PriorFinancialEvidence ?? BlackjackFinancialSnapshot.Empty).Entries.Concat(current).ToArray();
        var operations = (terms.PriorFinancialEvidence ?? BlackjackFinancialSnapshot.Empty).Operations.Append(TableFinancialOperation(terms, receipts)).ToArray();
        return new BlackjackTableJournal
        {
            Ledger = terms.Journal.Ledger, Revenue = terms.Journal.Revenue, Results = terms.Journal.Results,
            ManagedPlayerReleases = terms.Journal.ManagedPlayerReleases,
            RoundAudits = terms.Journal.RoundAudits.Select(audit => BlackjackFinancialProof.Protect(audit,
                new(all.Where(entry => entry.RoundReference == audit.Summary.RoundId).ToArray(), operations))).ToList()
        };
    }

    private static BlackjackWalletOperationProof TableFinancialOperation(TableCommandTerms terms,
        Dictionary<string, GameWalletSettlementReceipt> receipts) => new(BlackjackBackend.TableWalletCommands,
            terms.Id, TermsHash(terms), terms.Journal.Ledger.Select(entry => entry.Id).ToArray(), receipts);
}
