using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.Json.Serialization;
using FortuneForge.Games.Abstractions.Wallet;

namespace FortuneForge.Games.Abstractions.Auditing;

public sealed record RoundAuditLedgerEntry(string Collection, string DocumentId, string UserId, string Kind,
    string RoundReference, long AmountCents, string Provenance, string? WalletOperationId = null,
    string? WalletSettlementId = null, bool EvidenceAvailable = true);

public sealed record RoundAuditWalletOperation(string Collection, string DocumentId, string OutcomeSha256,
    IReadOnlyList<string> LedgerEvidenceIds, IReadOnlyDictionary<string, GameWalletSettlementReceipt> ReceiptsByAccount,
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] GameWalletBatchRequest? BatchRequest = null,
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] GameWalletBatchReceipt? BatchReceipt = null);

/// <summary>Attaches privately gathered financial evidence to the immutable round capture.</summary>
public static class RoundAuditFinancialProof
{
    public const string PropertyName = "financialProof";
    public const string LegacyHost = "legacy-host";
    public const string LegacyHostUnavailable = "legacy-host-unavailable";
    public const string SignedWallet = "signed-wallet";
    public const string MixedLegacyAndSignedWallet = "mixed-legacy-and-signed-wallet";
    public const string NoFinancialEvidence = "none";
    public const string UnavailableLegacyEvidenceCode = "legacy-wallet-evidence-unavailable";

    public static RoundAuditCapture Attach(RoundAuditCapture capture, string backendOwner, string contractVersion,
        IReadOnlyCollection<RoundAuditLedgerEntry> entries, IReadOnlyCollection<RoundAuditWalletOperation> operations)
    {
        ArgumentNullException.ThrowIfNull(capture);
        ArgumentException.ThrowIfNullOrWhiteSpace(backendOwner);
        ArgumentException.ThrowIfNullOrWhiteSpace(contractVersion);
        ArgumentNullException.ThrowIfNull(entries);
        ArgumentNullException.ThrowIfNull(operations);

        if (JsonNode.Parse(capture.PayloadJson) is not JsonObject payload)
            throw new ArgumentException("Round audit evidence must be a JSON object.", nameof(capture));
        if (payload.ContainsKey(PropertyName))
            throw new InvalidOperationException("The round capture already has financial proof.");

        var ledgerEntries = entries.Distinct().ToArray();
        var walletOperations = UniqueOperations(operations);
        var hasLegacy = ledgerEntries.Any(entry => entry.Provenance.StartsWith("legacy-", StringComparison.Ordinal));
        var hasSigned = walletOperations.Length > 0 || ledgerEntries.Any(entry => entry.Provenance == SignedWallet);
        var provenance = (hasLegacy, hasSigned) switch
        {
            (true, true) => MixedLegacyAndSignedWallet,
            (true, false) => LegacyHost,
            (false, true) => SignedWallet,
            _ => NoFinancialEvidence,
        };

        payload.Add(PropertyName, JsonSerializer.SerializeToNode(new
        {
            contractVersion,
            backendOwner,
            provenance,
            entries = ledgerEntries,
            walletOperations,
        }, RoundAuditCapture.Json));

        var json = payload.ToJsonString(RoundAuditCapture.Json);
        var summary = capture.Summary with { EvidenceSha256 = GameSecurityEventDocuments.Hash(json) };
        if (ledgerEntries.Any(entry => !entry.EvidenceAvailable))
        {
            var findings = summary.Findings;
            if (!findings.Any(finding => finding.Code == UnavailableLegacyEvidenceCode))
                findings = [..findings, new(UnavailableLegacyEvidenceCode, "warning",
                    "At least one original ledger entry is unavailable for independent financial verification.")];
            var status = summary.AuditStatus == "flagged" || findings.Any(finding => finding.Severity == "error")
                ? "flagged" : "incomplete";
            summary = summary with { Findings = findings, AuditStatus = status };
        }

        return capture with { Summary = summary, PayloadJson = json };
    }

    private static RoundAuditWalletOperation[] UniqueOperations(IReadOnlyCollection<RoundAuditWalletOperation> operations)
    {
        var unique = new Dictionary<(string Collection, string DocumentId), RoundAuditWalletOperation>();
        foreach (var operation in operations)
        {
            var key = (operation.Collection, operation.DocumentId);
            if (unique.TryAdd(key, operation)) continue;
            var original = unique[key];
            if (original.OutcomeSha256 != operation.OutcomeSha256 ||
                !original.LedgerEvidenceIds.SequenceEqual(operation.LedgerEvidenceIds, StringComparer.Ordinal) ||
                original.ReceiptsByAccount.Count != operation.ReceiptsByAccount.Count ||
                original.ReceiptsByAccount.Any(entry => !operation.ReceiptsByAccount.TryGetValue(entry.Key, out var receipt) || entry.Value != receipt) ||
                !SameBatchRequest(original.BatchRequest, operation.BatchRequest) ||
                !SameBatchReceipt(original.BatchReceipt, operation.BatchReceipt))
                throw new ArgumentException("A financial wallet operation has conflicting evidence.", nameof(operations));
        }
        return unique.Values.ToArray();
    }

    private static bool SameBatchRequest(GameWalletBatchRequest? original, GameWalletBatchRequest? duplicate)
    {
        if (original is null || duplicate is null) return original == duplicate;

        return original.OperationId == duplicate.OperationId && original.OutcomeSha256 == duplicate.OutcomeSha256 &&
            original.Effects.SequenceEqual(duplicate.Effects);
    }

    private static bool SameBatchReceipt(GameWalletBatchReceipt? original, GameWalletBatchReceipt? duplicate)
    {
        if (original is null || duplicate is null) return original == duplicate;

        return original.BatchId == duplicate.BatchId && original.ActorAccountId == duplicate.ActorAccountId &&
            original.SettledAtUtc.EqualsExact(duplicate.SettledAtUtc) &&
            original.WasAlreadySettled == duplicate.WasAlreadySettled && original.BatchDeclined == duplicate.BatchDeclined &&
            original.Accounts.SequenceEqual(duplicate.Accounts);
    }
}
