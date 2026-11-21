using FortuneForge.Games.Abstractions.Auditing;
using FortuneForge.Games.Abstractions.Wallet;
using FortuneForge.Games.Firestore;
using FortuneForge.Games.ThreeCardPoker.Server;
using Google.Cloud.Firestore;

namespace FortuneForge.Server.Cards.ThreeCardPoker.Table;

internal sealed record ThreeCardPokerWalletAuditEffect(string AccountId, GameWalletSettlementRequest Request,
    DocumentReference Receipt);

internal sealed record ThreeCardPokerWalletAuditOperation(IReadOnlyList<ThreeCardPokerTableLedgerEntry> Ledger,
    IReadOnlyList<ThreeCardPokerWalletAuditEffect> Effects, IReadOnlyList<string> CompletedRounds, string OutcomeSha256);

internal sealed partial class FirestoreThreeCardPokerTableStore
{
    internal static ThreeCardPokerWalletAuditOperation ReadWalletAuditOperation(DocumentSnapshot snapshot)
    {
        var frozen = ReadFrozen(snapshot);
        var validId = frozen.Key is null
            ? Guid.TryParseExact(frozen.OperationId, "N", out var scheduledId) && scheduledId != Guid.Empty &&
                scheduledId.ToString("N") == frozen.OperationId
            : frozen.UserId is { Length: > 0 } commandActor &&
                frozen.OperationId == ThreeCardPokerTableIds.Hash($"{commandActor}\n{frozen.Key}");
        if (!validId || ReadString(snapshot, "status") is not ("applied" or "completed") || ReadString(snapshot, "stateId") != frozen.StateId ||
            snapshot.GetValue<long>("schemaVersion") != 1)
            throw new GameWalletUnavailableException();

        var funding = FundingFor(frozen.Journal.Ledger, frozen.BeforeBalances, frozen.AfterBalances);
        if (funding.Count != frozen.Deltas.Count || funding.Count != frozen.MinimumBalances.Count ||
            funding.Any(effect =>
                !frozen.Deltas.TryGetValue(effect.AccountId, out var delta) || effect.NetCents != delta ||
                !frozen.MinimumBalances.TryGetValue(effect.AccountId, out var minimum) || effect.MinimumBalanceBeforeCents != minimum ||
                minimum > 0 && effect.AccountId != frozen.UserId))
            throw new GameWalletUnavailableException();

        var effects = FinancialActors(frozen).Select(actor => new ThreeCardPokerWalletAuditEffect(actor,
            WalletRequest(frozen, actor), WalletReceipt(snapshot.Reference, actor))).ToArray();
        return new(frozen.Journal.Ledger, effects, frozen.Journal.Audits.Select(audit => audit.Summary.RoundId).ToArray(), Hash(frozen));
    }

    internal static GameWalletSettlementReceipt ReadWalletAuditReceipt(DocumentSnapshot snapshot, string outcomeSha256)
    {
        if (ReadString(snapshot, "outcomeSha256") != outcomeSha256) throw new GameWalletUnavailableException();
        return Deserialize<GameWalletSettlementReceipt>(snapshot.GetValue<string>("json"));
    }

    private async Task<FinancialSnapshot> ReadFinancialEvidenceAsync(Transaction transaction, string stateId,
        ThreeCardPokerTableJournal journal, CancellationToken token)
    {
        var entries = new List<RoundAuditLedgerEntry>();
        var operations = new Dictionary<string, (FrozenMutation Frozen, RoundAuditWalletOperation Proof)>(StringComparer.Ordinal);
        foreach (var audit in journal.RoundAudits)
        {
            var roundId = audit.Summary.RoundId;
            var accountsInRound = audit.Summary.Participants.Where(player => player.IsAccountBacked).Select(player => player.ActorId).ToHashSet(StringComparer.Ordinal);
            var archive = await transaction.GetSnapshotAsync(database.Collection(ThreeCardPokerBackend.TableLedgerEvidence).WhereEqualTo("idempotencyKey", roundId), token);
            foreach (var document in archive.Documents)
            {
                var entry = FirestoreWalletEvidence.Legacy(document, roundId);
                if (!accountsInRound.Contains(entry.UserId)) continue;
                var operationId = ReadString(document, "walletMutationId");
                if (!operations.TryGetValue(operationId, out var saved))
                {
                    var snapshot = await transaction.GetSnapshotAsync(Operation(operationId), token);
                    var frozen = ReadFrozen(snapshot);
                    if (ReadString(snapshot, "status") is not ("applied" or "completed")) throw new GameWalletUnavailableException();
                    var actors = FinancialActors(frozen);
                    var receiptDocuments = await transaction.GetAllSnapshotsAsync(actors.Select(actor => WalletReceipt(operationId, actor)), token);
                    var receipts = new Dictionary<string, GameWalletSettlementReceipt>(StringComparer.Ordinal);
                    for (var index = 0; index < actors.Length; index++)
                    {
                        if (!receiptDocuments[index].Exists) throw new GameWalletUnavailableException();
                        var receipt = Deserialize<GameWalletSettlementReceipt>(receiptDocuments[index].GetValue<string>("json"));
                        if (receipt.InsufficientFunds || !GameWalletReceiptVerifier.MatchesSettlement(receipt,
                            ThreeCardPokerBackend.GameId, actors[index], WalletRequest(frozen, actors[index]))) throw new GameWalletUnavailableException();
                        receipts.Add(actors[index], receipt);
                    }
                    saved = (frozen, FinancialOperation(frozen, receipts));
                    operations.Add(operationId, saved);
                }
                var expected = saved.Frozen.Journal.Ledger.SingleOrDefault(value => value.Id == entry.DocumentId);
                if (expected is null || expected.UserId != entry.UserId || expected.AmountCents != entry.AmountCents ||
                    expected.Type != entry.Kind || expected.Reference != roundId || !saved.Proof.ReceiptsByAccount.TryGetValue(entry.UserId, out var walletReceipt))
                    throw new GameWalletUnavailableException();
                entries.Add(entry with { Provenance = RoundAuditFinancialProof.SignedWallet,
                    WalletOperationId = operationId, WalletSettlementId = walletReceipt.SettlementId });
            }
            var legacy = await transaction.GetSnapshotAsync(database.Collection("balanceTransactions").WhereEqualTo("idempotencyKey", roundId), token);
            foreach (var document in legacy.Documents)
                if (accountsInRound.Contains(ReadString(document, "userId")) && ReadString(document, "type").StartsWith("three-card-poker-table-", StringComparison.Ordinal))
                    entries.Add(FirestoreWalletEvidence.Legacy(document, roundId));

            foreach (var player in audit.Summary.Participants.Where(player => player.IsAccountBacked))
            {
                var recorded = -entries.Where(entry => entry.RoundReference == roundId && entry.UserId == player.ActorId &&
                    !entry.Kind.Contains("payout", StringComparison.Ordinal)).Sum(entry => entry.AmountCents);
                recorded -= journal.Ledger.Where(entry => entry.Reference == roundId && entry.UserId == player.ActorId &&
                    !entry.Type.Contains("payout", StringComparison.Ordinal)).Sum(entry => entry.AmountCents);
                if (recorded < player.WagerCents)
                    entries.Add(new(ThreeCardPokerBackend.TableState, stateId, player.ActorId, "legacy-prepaid-stake", roundId,
                        checked(recorded - player.WagerCents), RoundAuditFinancialProof.LegacyHostUnavailable, EvidenceAvailable: false));
            }
        }
        return new(entries.ToArray(), operations.Values.Select(operation => operation.Proof).ToArray());
    }

    private static ThreeCardPokerTableJournal ProtectedJournal(FrozenMutation frozen, IReadOnlyDictionary<string, GameWalletSettlementReceipt> receipts,
        FinancialSnapshot previous)
    {
        var journal = frozen.Journal.ToJournal();
        if (journal.RoundAudits.Count == 0) return journal;
        var current = frozen.Journal.Ledger.Select(entry => new RoundAuditLedgerEntry(ThreeCardPokerBackend.TableLedgerEvidence,
            entry.Id, entry.UserId, entry.Type, entry.Reference, entry.AmountCents, RoundAuditFinancialProof.SignedWallet,
            frozen.OperationId, receipts[entry.UserId].SettlementId)).ToArray();
        var all = previous.Entries.Concat(current).ToArray();
        var operations = receipts.Count == 0 ? previous.Operations
            : previous.Operations.Append(FinancialOperation(frozen, receipts)).ToArray();
        var captures = journal.RoundAudits.Select(capture => RoundAuditFinancialProof.Attach(capture,
            ThreeCardPokerBackend.Owner, ThreeCardPokerBackend.FinancialEvidenceContract,
            all.Where(entry => entry.RoundReference == capture.Summary.RoundId).ToArray(), operations)).ToArray();
        journal.RoundAudits.Clear();
        journal.RoundAudits.AddRange(captures);
        return journal;
    }

    private static RoundAuditWalletOperation FinancialOperation(FrozenMutation frozen, IReadOnlyDictionary<string, GameWalletSettlementReceipt> receipts) =>
        new(ThreeCardPokerBackend.TableOperations, frozen.OperationId, Hash(frozen),
            frozen.Journal.Ledger.Select(entry => entry.Id).ToArray(), receipts);

    private sealed record FinancialSnapshot(RoundAuditLedgerEntry[] Entries, RoundAuditWalletOperation[] Operations);
}
