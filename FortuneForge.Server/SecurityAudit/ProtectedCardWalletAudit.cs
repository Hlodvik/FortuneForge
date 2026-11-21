using System.Text.Json;
using FortuneForge.Games.Abstractions.Auditing;
using FortuneForge.Games.Abstractions.Wallet;
using FortuneForge.Games.Firestore;
using FortuneForge.Server.Accounts.Storage;
using FortuneForge.Server.Cards.CasinoWar;
using FortuneForge.Server.Cards.HouseHoldem;
using FortuneForge.Server.Cards.VideoPoker;
using Google.Cloud.Firestore;

namespace FortuneForge.Server.SecurityAudit;

internal static class ProtectedCardWalletAudit
{
    private const string Ledger = "balanceTransactions";
    private const string WalletReceipts = "walletRoundSettlements";
    private sealed record Contract(string Owner, string Version, string Rounds, string Commands);

    internal static async Task<IReadOnlyList<RoundAuditFinding>?> TryInspectAsync(FirestoreDb database,
        RoundAuditSummary summary, string payloadJson, CancellationToken token)
    {
        var contract = ContractFor(summary.GameId);
        if (contract is null) return null;
        try
        {
            using var document = JsonDocument.Parse(payloadJson);
            var root = document.RootElement;
            if (root.ValueKind != JsonValueKind.Object || !root.TryGetProperty(RoundAuditFinancialProof.PropertyName, out var proof))
                return null;
            if (GameSecurityEventDocuments.Hash(payloadJson) != summary.EvidenceSha256 ||
                summary.Participants.Count != 1 || summary.Participants[0] is not
                    { IsAccountBacked: true, WagerCents: > 0, PayoutCents: >= 0 } participant ||
                !GameWalletBatchVerifier.IsValidAccountId(participant.ActorId) || !GameSecurityEventDocuments.IsEventId(summary.RoundId) ||
                root.GetProperty("sourceCollection").GetString() != contract.Rounds ||
                root.GetProperty("sourceDocumentId").GetString() != summary.RoundId ||
                root.GetProperty("wagerCents").GetInt64() != participant.WagerCents ||
                root.GetProperty("payoutCents").GetInt64() != participant.PayoutCents ||
                root.GetProperty("netCents").GetInt64() != participant.NetCents ||
                participant.NetCents != checked(participant.PayoutCents - participant.WagerCents) ||
                proof.GetProperty("backendOwner").GetString() != contract.Owner ||
                proof.GetProperty("contractVersion").GetString() != contract.Version)
                return Invalid();

            var entries = proof.GetProperty("entries").Deserialize<RoundAuditLedgerEntry[]>(RoundAuditCapture.Json);
            var operations = proof.GetProperty("walletOperations").Deserialize<RoundAuditWalletOperation[]>(RoundAuditCapture.Json);
            if (entries is null || operations is not { Length: > 0 } ||
                entries.Any(entry => entry is null || !ValidEntry(entry, summary, contract, participant.ActorId)) ||
                entries.Select(entry => (entry.Collection, entry.DocumentId)).Distinct().Count() != entries.Length ||
                operations.Any(operation => operation is null || operation.Collection != contract.Commands ||
                    !GameWalletBatchVerifier.IsValidAccountId(operation.DocumentId) ||
                    !GameSecurityEventDocuments.IsEventId(operation.OutcomeSha256) || operation.LedgerEvidenceIds is null ||
                    operation.ReceiptsByAccount?.Count != 1 || operation.BatchRequest is not null || operation.BatchReceipt is not null) ||
                operations.Select(operation => operation.DocumentId).Distinct(StringComparer.Ordinal).Count() != operations.Length ||
                checked(-entries.Where(entry => entry.AmountCents < 0).Sum(entry => entry.AmountCents)) != participant.WagerCents ||
                entries.Where(entry => entry.AmountCents > 0).Sum(entry => entry.AmountCents) != participant.PayoutCents)
                return Invalid();

            var historical = entries.Where(entry => entry.Provenance != RoundAuditFinancialProof.SignedWallet).ToArray();
            var provenance = historical.Length == 0 ? RoundAuditFinancialProof.SignedWallet :
                RoundAuditFinancialProof.MixedLegacyAndSignedWallet;
            if (proof.GetProperty("provenance").GetString() != provenance)
                return Invalid();

            var terminalSettlementId = summary.GameId == "house-holdem"
                ? root.GetProperty("data").GetProperty("walletSettlementId").GetString() : null;
            if (summary.GameId == "house-holdem" &&
                !operations.Any(operation => operation.ReceiptsByAccount.TryGetValue(participant.ActorId, out var receipt) &&
                    receipt is not null && receipt.SettlementId == terminalSettlementId &&
                    operation.OutcomeSha256 == root.GetProperty("data").GetProperty("outcomeSha256").GetString()))
                return Invalid();

            var signed = entries.Where(entry => entry.Provenance == RoundAuditFinancialProof.SignedWallet)
                .ToDictionary(entry => entry.DocumentId, StringComparer.Ordinal);
            var valid = await database.RunTransactionAsync(async transaction =>
            {
                var verifiedSettlements = new HashSet<string>(StringComparer.Ordinal);
                foreach (var entry in historical.Where(entry => entry.EvidenceAvailable))
                {
                    var snapshot = await transaction.GetSnapshotAsync(database.Collection(Ledger).Document(entry.DocumentId), token);
                    if (!snapshot.Exists || FirestoreWalletEvidence.Legacy(snapshot, summary.RoundId) != entry) return false;
                }
                foreach (var operation in operations)
                    if (!await VerifyOperationAsync(database, transaction, summary, participant.ActorId, operation, signed, verifiedSettlements,
                        terminalSettlementId, token)) return false;
                return signed.Count == operations.Sum(operation => operation.LedgerEvidenceIds.Count);
            }, cancellationToken: token);
            if (!valid) return Invalid();
            return historical.Any(entry => !entry.EvidenceAvailable)
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
        string actor, RoundAuditWalletOperation operation, IReadOnlyDictionary<string, RoundAuditLedgerEntry> entries,
        HashSet<string> verifiedSettlements, string? terminalSettlementId, CancellationToken token)
    {
        if (!operation.ReceiptsByAccount.TryGetValue(actor, out var receipt) ||
            receipt is not { InsufficientFunds: false, InsufficientLegacyFreeGames: false, LegacyFreeGamesCountMismatch: false,
                LegacyFreeGameTransactionId: null, LegacyFreeGamesRemaining: null } ||
            !GameWalletBatchVerifier.IsValidAccountId(receipt.SettlementId) || !verifiedSettlements.Add(receipt.SettlementId))
            return false;
        var archive = await transaction.GetSnapshotAsync(database.Collection(WalletReceipts).Document(receipt.SettlementId), token);
        if (!archive.Exists || !archive.TryGetValue<string>("operationId", out var walletId) ||
            !MatchesOperationId(summary, operation.DocumentId, walletId))
            return false;

        var entryOperationId = summary.GameId == "casino-war" ? walletId : operation.DocumentId;
        if (!ReadLeg(receipt.WagerTransactionId, -1, out var wager) || !ReadLeg(receipt.PayoutTransactionId, 1, out var payout))
            return false;
        var exactIds = new[] { receipt.WagerTransactionId, receipt.PayoutTransactionId }.OfType<string>();
        if (!operation.LedgerEvidenceIds.SequenceEqual(exactIds, StringComparer.Ordinal) ||
            summary.GameId == "house-holdem" && wager == 0 && payout == 0 && receipt.SettlementId != terminalSettlementId ||
            summary.GameId == "video-poker" && (operation.DocumentId == summary.RoundId ? payout != 0 : wager != 0))
            return false;

        long? minimum = archive.ContainsField("minimumBalanceBeforeCents") ? archive.GetValue<long>("minimumBalanceBeforeCents") : null;
        var request = new GameWalletSettlementRequest(walletId, operation.OutcomeSha256, wager, payout,
            MinimumBalanceBeforeCents: minimum);
        if (!GameWalletReceiptVerifier.MatchesSettlement(receipt, summary.GameId, actor, request))
            return false;
        var terms = WalletRoundSettlement.FromDirectRequest(summary.GameId, actor, request);
        var replayed = await FirestoreWalletRoundSettlementStore.StageAsync(database, transaction, terms,
            DateTimeOffset.UnixEpoch, token, requireExisting: true);
        return (replayed.ToWireReceipt() with { WasAlreadySettled = false }) == (receipt with { WasAlreadySettled = false });

        bool ReadLeg(string? id, int sign, out long amount)
        {
            amount = 0;
            if (id is null) return true;
            if (!entries.TryGetValue(id, out var entry) || Math.Sign(entry.AmountCents) != sign ||
                entry.WalletOperationId != entryOperationId || entry.WalletSettlementId != receipt.SettlementId)
                return false;
            amount = checked(entry.AmountCents * sign);
            return true;
        }
    }

    private static bool MatchesOperationId(RoundAuditSummary summary, string documentId, string walletId) => summary.GameId switch
    {
        "house-holdem" => documentId.StartsWith(summary.RoundId + "-", StringComparison.Ordinal) &&
            GameSecurityEventDocuments.IsEventId(documentId[(summary.RoundId.Length + 1)..]) && walletId == $"house-holdem-{documentId}",
        "video-poker" => GameSecurityEventDocuments.IsEventId(documentId) &&
            walletId == $"video-poker-{summary.RoundId}-{(documentId == summary.RoundId ? "deal" : "draw")}",
        "casino-war" => GameSecurityEventDocuments.IsEventId(documentId) &&
            walletId == GameSecurityEventDocuments.Hash($"casino-war\n{documentId}"),
        _ => false,
    };

    private static bool ValidEntry(RoundAuditLedgerEntry entry, RoundAuditSummary summary, Contract contract, string actor)
    {
        if (entry.UserId != actor || entry.RoundReference != summary.RoundId || entry.AmountCents == 0 ||
            !GameWalletBatchVerifier.IsValidAccountId(entry.DocumentId))
            return false;
        if (entry.Provenance == RoundAuditFinancialProof.LegacyHostUnavailable && summary.GameId == "house-holdem")
            return entry.Collection == contract.Rounds && entry.DocumentId == summary.RoundId &&
                entry.Kind == "legacy-prepaid-stake" && entry.AmountCents < 0 && !entry.EvidenceAvailable &&
                entry.WalletOperationId is null && entry.WalletSettlementId is null;
        if (entry.Collection != Ledger || !KnownLedgerKind(summary.GameId, entry)) return false;
        if (entry.Provenance == RoundAuditFinancialProof.SignedWallet)
            return entry.EvidenceAvailable && entry.WalletOperationId is not null && entry.WalletSettlementId is not null;
        return (entry.Provenance == RoundAuditFinancialProof.LegacyHost && entry.EvidenceAvailable ||
                entry.Provenance == RoundAuditFinancialProof.LegacyHostUnavailable && !entry.EvidenceAvailable) &&
            entry.WalletOperationId is null && entry.WalletSettlementId is null &&
            entry.DocumentId.StartsWith($"{summary.GameId}-{summary.RoundId}-", StringComparison.Ordinal);
    }

    private static bool KnownLedgerKind(string game, RoundAuditLedgerEntry entry) => (game, entry.Kind, entry.AmountCents) switch
    {
        ("house-holdem", "house-holdem-wager" or "house-holdem-play-wager", < 0) => true,
        ("house-holdem", "house-holdem-payout", > 0) => true,
        ("video-poker", "video-poker-wager", < 0) => true,
        ("video-poker", "video-poker-payout", > 0) => true,
        ("casino-war", "casino-war-opening-wager" or "casino-war-war-wager", < 0) => true,
        ("casino-war", "casino-war-opening-payout" or "casino-war-tie-payout" or "casino-war-primary-payout", > 0) => true,
        _ => false,
    };

    private static Contract? ContractFor(string game) => game switch
    {
        "house-holdem" => new(HouseHoldemBackend.Owner, HouseHoldemFinancialProof.Contract,
            HouseHoldemBackend.RoundsCollection, HouseHoldemBackend.CommandsCollection),
        "video-poker" => new(VideoPokerBackend.Owner, VideoPokerBackend.FinancialEvidenceContract,
            VideoPokerBackend.RoundCollection, VideoPokerBackend.OperationCollection),
        "casino-war" => new(CasinoWarRoundCodec.BackendOwner, CasinoWarFinancialProof.Contract,
            CasinoWarRoundCodec.Rounds, CasinoWarFirestoreStore.Commands),
        _ => null,
    };

    private static IReadOnlyList<RoundAuditFinding> Invalid() => [new("paid-card-protected-wallet-mismatch", "error",
        "The protected card round cannot be reconciled with its signed wallet receipts and ledger evidence.")];
}
