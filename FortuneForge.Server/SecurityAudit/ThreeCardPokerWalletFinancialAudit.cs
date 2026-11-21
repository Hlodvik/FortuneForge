using System.Text.Json;
using FortuneForge.Games.Abstractions.Auditing;
using FortuneForge.Games.Abstractions.Wallet;
using FortuneForge.Games.Firestore;
using FortuneForge.Games.ThreeCardPoker;
using FortuneForge.Games.ThreeCardPoker.Server;
using FortuneForge.Server.Accounts.Storage;
using FortuneForge.Server.Bots.ThreeCardPoker;
using FortuneForge.Server.Cards.ThreeCardPoker.Table;
using Google.Cloud.Firestore;

namespace FortuneForge.Server.SecurityAudit;

internal static class ThreeCardPokerWalletFinancialAudit
{
    private const string LegacyLedger = "balanceTransactions";
    private const string KindPrefix = "three-card-poker-table-";
    private const string OpeningWagerKind = KindPrefix + "wager";
    private const string PlayWagerKind = KindPrefix + "play";
    private const string WagerAdjustmentKind = KindPrefix + "wager-adjustment";
    private const string WagerReleaseKind = KindPrefix + "wager-release";
    private const string PayoutKind = KindPrefix + "payout";

    internal static async Task<IReadOnlyList<RoundAuditFinding>> InspectAsync(FirestoreDb database,
        RoundAuditSummary summary, string payloadJson, CancellationToken token)
    {
        if (summary.GameId != ThreeCardPokerBackend.GameId || summary.EvidenceKind != "three-card-poker-final-cards-v1") return [];
        try
        {
            using var document = JsonDocument.Parse(payloadJson);
            var root = document.RootElement;
            if (root.ValueKind != JsonValueKind.Object || !root.TryGetProperty(RoundAuditFinancialProof.PropertyName, out var proof))
                return [];
            if (GameSecurityEventDocuments.Hash(payloadJson) != summary.EvidenceSha256 ||
                root.GetProperty("tableId").GetString() != summary.TableId || root.GetProperty("roundNumber").GetInt32() != summary.RoundNumber ||
                summary.RoundId != $"{summary.TableId}-round-{summary.RoundNumber}" || !root.GetProperty("roundAccountingSettled").GetBoolean() ||
                proof.GetProperty("backendOwner").GetString() != ThreeCardPokerBackend.Owner ||
                proof.GetProperty("contractVersion").GetString() != ThreeCardPokerBackend.FinancialEvidenceContract)
                return Invalid();

            var players = root.GetProperty("players").Deserialize<ThreeCardPokerTablePlayer[]>(RoundAuditCapture.Json);
            if (players is null || players.Any(player => player is null || player.HostMetadata is null)) return Invalid();
            var participants = players.Where(player => player.TotalWagerCents > 0).ToDictionary(player => player.ActorId, StringComparer.Ordinal);
            if (summary.Participants.Count != participants.Count ||
                summary.Participants.Select(player => player.ActorId).Distinct(StringComparer.Ordinal).Count() != participants.Count ||
                summary.Participants.Any(player => !participants.TryGetValue(player.ActorId, out var saved) ||
                    player.IsAccountBacked == ThreeCardPokerManagedSeat.IsManaged(saved) ||
                    player.WagerCents != ThreeCardPokerTableEngine.TotalCommitted(saved) || player.PayoutCents != saved.PayoutCents ||
                    player.WagerCents <= 0 || player.PayoutCents < 0 || player.NetCents != checked(player.PayoutCents - player.WagerCents)))
                return Invalid();

            var accounts = summary.Participants.Where(player => player.IsAccountBacked)
                .ToDictionary(player => player.ActorId, StringComparer.Ordinal);
            var entries = proof.GetProperty("entries").Deserialize<RoundAuditLedgerEntry[]>(RoundAuditCapture.Json);
            var operations = proof.GetProperty("walletOperations").Deserialize<RoundAuditWalletOperation[]>(RoundAuditCapture.Json);
            if (accounts.Count == 0 || entries is null || operations is null ||
                entries.Any(entry => entry is null || !ValidEntry(entry, summary.RoundId, accounts)) ||
                entries.Select(entry => (entry.Collection, entry.DocumentId, entry.UserId)).Distinct().Count() != entries.Length ||
                operations.Any(operation => operation is null || operation.Collection != ThreeCardPokerBackend.TableOperations ||
                    !GameWalletBatchVerifier.IsValidAccountId(operation.DocumentId) || !GameSecurityEventDocuments.IsEventId(operation.OutcomeSha256) ||
                    operation.LedgerEvidenceIds is null || operation.ReceiptsByAccount is not { Count: > 0 } ||
                    operation.BatchRequest is not null || operation.BatchReceipt is not null) ||
                operations.Select(operation => operation.DocumentId).Distinct(StringComparer.Ordinal).Count() != operations.Length ||
                accounts.Values.Any(account => !MatchesTotals(account, entries.Where(entry => entry.UserId == account.ActorId))))
                return Invalid();

            var signed = entries.Where(entry => entry.Provenance == RoundAuditFinancialProof.SignedWallet).ToArray();
            var legacy = entries.Where(entry => entry.Provenance != RoundAuditFinancialProof.SignedWallet).ToArray();
            var provenance = (legacy.Length > 0, operations.Length > 0 || signed.Length > 0) switch
            {
                (true, true) => RoundAuditFinancialProof.MixedLegacyAndSignedWallet,
                (true, false) => RoundAuditFinancialProof.LegacyHost,
                (false, true) => RoundAuditFinancialProof.SignedWallet,
                _ => RoundAuditFinancialProof.NoFinancialEvidence,
            };
            if (proof.GetProperty("provenance").GetString() != provenance) return Invalid();
            var matches = await database.RunTransactionAsync(async transaction =>
            {
                var expected = new Dictionary<string, (RoundAuditLedgerEntry Proof, ThreeCardPokerTableLedgerEntry Frozen)>(StringComparer.Ordinal);
                var verifiedSettlements = new HashSet<string>(StringComparer.Ordinal);
                foreach (var operation in operations)
                    if (!await VerifyOperationAsync(database, transaction, summary, accounts, operation, expected,
                        verifiedSettlements, token)) return false;
                if (!expected.Values.Select(value => value.Proof).ToHashSet().SetEquals(signed)) return false;

                var signedRows = await ReadLedgerAsync(transaction, database, ThreeCardPokerBackend.TableLedgerEvidence,
                    summary.RoundId, accounts, token);
                if (signedRows.Length != expected.Count || signedRows.Any(row => !expected.TryGetValue(row.Id, out var entry) ||
                    !MatchesSignedRow(row, entry.Proof, entry.Frozen))) return false;
                var legacyRows = await ReadLedgerAsync(transaction, database, LegacyLedger, summary.RoundId, accounts, token);
                return legacyRows.Select(row => FirestoreWalletEvidence.Legacy(row, summary.RoundId)).ToHashSet()
                    .SetEquals(legacy.Where(entry => entry.EvidenceAvailable));
            }, cancellationToken: token);
            if (!matches) return Invalid();
            return legacy.Any(entry => !entry.EvidenceAvailable)
                ? [new(RoundAuditFinancialProof.UnavailableLegacyEvidenceCode, "warning",
                    "At least one original ledger entry is unavailable for independent financial verification.")]
                : [];
        }
        catch (Exception exception) when (exception is JsonException or ArgumentException or InvalidOperationException or
            ArithmeticException or KeyNotFoundException or FormatException or InvalidCastException or GameWalletUnavailableException)
        {
            return Invalid();
        }
    }

    private static async Task<bool> VerifyOperationAsync(FirestoreDb database, Transaction transaction, RoundAuditSummary summary,
        IReadOnlyDictionary<string, RoundAuditParticipant> accounts, RoundAuditWalletOperation operation,
        Dictionary<string, (RoundAuditLedgerEntry Proof, ThreeCardPokerTableLedgerEntry Frozen)> expected,
        HashSet<string> verifiedSettlements, CancellationToken token)
    {
        var snapshot = await transaction.GetSnapshotAsync(database.Collection(operation.Collection).Document(operation.DocumentId), token);
        if (!snapshot.TryGetValue<string>("preparedSha256", out var digest) || digest != operation.OutcomeSha256) return false;
        var frozen = FirestoreThreeCardPokerTableStore.ReadWalletAuditOperation(snapshot);
        var scopedLedger = frozen.Ledger.Where(entry => entry.Reference == summary.RoundId && accounts.ContainsKey(entry.UserId)).ToArray();
        if (frozen.OutcomeSha256 != operation.OutcomeSha256 ||
            !operation.LedgerEvidenceIds.SequenceEqual(frozen.Ledger.Select(entry => entry.Id), StringComparer.Ordinal) ||
            operation.ReceiptsByAccount.Count != frozen.Effects.Count ||
            scopedLedger.Length == 0 && !frozen.CompletedRounds.Contains(summary.RoundId, StringComparer.Ordinal))
            return false;
        foreach (var effect in frozen.Effects)
        {
            if (!operation.ReceiptsByAccount.TryGetValue(effect.AccountId, out var captured) ||
                captured is not { WasAlreadySettled: false, InsufficientFunds: false, InsufficientLegacyFreeGames: false,
                    LegacyFreeGamesCountMismatch: false, LegacyFreeGameTransactionId: null, LegacyFreeGamesRemaining: null } ||
                !verifiedSettlements.Add(captured.SettlementId) ||
                !GameWalletReceiptVerifier.MatchesSettlement(captured, summary.GameId, effect.AccountId, effect.Request))
                return false;
            var saved = FirestoreThreeCardPokerTableStore.ReadWalletAuditReceipt(
                await transaction.GetSnapshotAsync(effect.Receipt, token), frozen.OutcomeSha256);
            if (saved != captured) return false;
            var terms = WalletRoundSettlement.FromDirectRequest(summary.GameId, effect.AccountId, effect.Request);
            var platform = await FirestoreWalletRoundSettlementStore.StageAsync(database, transaction, terms,
                DateTimeOffset.UnixEpoch, token, requireExisting: true);
            if ((platform.ToWireReceipt() with { WasAlreadySettled = false }) != captured) return false;
        }
        foreach (var entry in scopedLedger)
        {
            var receipt = operation.ReceiptsByAccount[entry.UserId];
            var proof = new RoundAuditLedgerEntry(ThreeCardPokerBackend.TableLedgerEvidence, entry.Id, entry.UserId,
                entry.Type, entry.Reference, entry.AmountCents, RoundAuditFinancialProof.SignedWallet,
                operation.DocumentId, receipt.SettlementId);
            if (!expected.TryAdd(entry.Id, (proof, entry))) return false;
        }
        return true;
    }

    private static async Task<DocumentSnapshot[]> ReadLedgerAsync(Transaction transaction, FirestoreDb database,
        string collection, string roundId, IReadOnlyDictionary<string, RoundAuditParticipant> accounts, CancellationToken token)
    {
        var rows = await transaction.GetSnapshotAsync(database.Collection(collection).WhereEqualTo("idempotencyKey", roundId), token);
        return rows.Documents.Where(row => row.TryGetValue<string>("userId", out var user) && accounts.ContainsKey(user) &&
            (collection != LegacyLedger || row.TryGetValue<string>("type", out var kind) && kind.StartsWith(KindPrefix, StringComparison.Ordinal)))
            .ToArray();
    }

    private static bool MatchesSignedRow(DocumentSnapshot record, RoundAuditLedgerEntry proof, ThreeCardPokerTableLedgerEntry frozen) =>
        FirestoreWalletEvidence.Legacy(record, proof.RoundReference) == (proof with
        { Provenance = RoundAuditFinancialProof.LegacyHost, WalletOperationId = null, WalletSettlementId = null }) &&
        record.GetValue<string>("transactionId") == proof.DocumentId &&
        record.GetValue<string>("backendOwner") == ThreeCardPokerBackend.Owner &&
        record.GetValue<string>("walletMutationId") == proof.WalletOperationId &&
        record.GetValue<string>("walletSettlementId") == proof.WalletSettlementId &&
        record.GetValue<double>("balanceAfter") == (double)ThreeCardPokerMoney.ToRand(frozen.BalanceAfterCents) &&
        record.GetValue<Timestamp>("createdAt").ToDateTime().Ticks / TimeSpan.TicksPerMicrosecond ==
            frozen.CreatedAtUtc.Ticks / TimeSpan.TicksPerMicrosecond;

    private static bool ValidEntry(RoundAuditLedgerEntry entry, string roundId,
        IReadOnlyDictionary<string, RoundAuditParticipant> accounts)
    {
        if (entry.RoundReference != roundId || !accounts.ContainsKey(entry.UserId) ||
            !GameWalletBatchVerifier.IsValidAccountId(entry.DocumentId) || entry.AmountCents == 0) return false;
        if (entry.Provenance == RoundAuditFinancialProof.LegacyHostUnavailable)
            return entry.Collection == ThreeCardPokerBackend.TableState && entry.Kind == "legacy-prepaid-stake" &&
                entry.AmountCents < 0 && !entry.EvidenceAvailable && entry.WalletOperationId is null && entry.WalletSettlementId is null;

        var validKind = entry.Kind switch
        {
            OpeningWagerKind or PlayWagerKind => entry.AmountCents < 0,
            WagerAdjustmentKind => true,
            WagerReleaseKind or PayoutKind => entry.AmountCents > 0,
            _ => false,
        };
        if (!entry.EvidenceAvailable || !validKind) return false;
        return entry.Provenance == RoundAuditFinancialProof.SignedWallet
            ? entry.Collection == ThreeCardPokerBackend.TableLedgerEvidence && entry.WalletOperationId is not null && entry.WalletSettlementId is not null
            : entry.Provenance == RoundAuditFinancialProof.LegacyHost && entry.Collection == LegacyLedger &&
                entry.WalletOperationId is null && entry.WalletSettlementId is null;
    }

    private static bool MatchesTotals(RoundAuditParticipant participant, IEnumerable<RoundAuditLedgerEntry> entries)
    {
        var payout = entries.Where(entry => entry.Kind == PayoutKind).Sum(entry => entry.AmountCents);
        return payout == participant.PayoutCents && checked(payout - entries.Sum(entry => entry.AmountCents)) == participant.WagerCents;
    }

    private static IReadOnlyList<RoundAuditFinding> Invalid() => [new("three-card-poker-protected-wallet-mismatch", "error",
        "The protected Three Card Poker round cannot be reconciled with its frozen ledger and signed wallet receipts.")];
}
