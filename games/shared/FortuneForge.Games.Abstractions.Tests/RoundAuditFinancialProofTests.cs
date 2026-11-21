using System.Text.Json;
using FortuneForge.Games.Abstractions.Auditing;
using FortuneForge.Games.Abstractions.Wallet;
using Xunit;

namespace FortuneForge.Games.Abstractions.Tests;

public sealed class RoundAuditFinancialProofTests
{
    [Fact]
    public void SignedEvidencePreservesExistingPayloadAndRequestAndResealsTheCapture()
    {
        var request = new GameRequestObservation("req-1", "alice", null, null, DateTime.UnixEpoch,
            "POST", "/api/example", "example", "round-1", "command-1", 1, null, "absent", "none");
        var capture = Capture() with { Request = request };
        var ledger = new RoundAuditLedgerEntry("gameLedger", "debit", "alice", "wager", "round-1", -500,
            RoundAuditFinancialProof.SignedWallet, "command-1", "settlement-1");
        var attached = RoundAuditFinancialProof.Attach(capture, "example-worker", "proof-v1", [ledger], [Operation()]);

        using var original = JsonDocument.Parse(capture.PayloadJson);
        using var updated = JsonDocument.Parse(attached.PayloadJson);
        foreach (var property in original.RootElement.EnumerateObject())
            Assert.Equal(property.Value.GetRawText(), updated.RootElement.GetProperty(property.Name).GetRawText());
        var proof = updated.RootElement.GetProperty(RoundAuditFinancialProof.PropertyName);
        Assert.Equal("example-worker", proof.GetProperty("backendOwner").GetString());
        Assert.Equal("proof-v1", proof.GetProperty("contractVersion").GetString());
        Assert.Equal(RoundAuditFinancialProof.SignedWallet, proof.GetProperty("provenance").GetString());
        Assert.Equal(-500L, proof.GetProperty("entries")[0].GetProperty("amountCents").GetInt64());
        Assert.True(proof.GetProperty("entries")[0].GetProperty("evidenceAvailable").GetBoolean());
        var operation = proof.GetProperty("walletOperations")[0];
        Assert.Equal("settlement-1", operation.GetProperty("receiptsByAccount")
            .GetProperty("alice").GetProperty("settlementId").GetString());
        Assert.False(operation.TryGetProperty("batchRequest", out _));
        Assert.False(operation.TryGetProperty("batchReceipt", out _));
        Assert.False(original.RootElement.TryGetProperty(RoundAuditFinancialProof.PropertyName, out _));
        Assert.Same(request, attached.Request);
        Assert.Same(capture.Summary.Participants, attached.Summary.Participants);
        Assert.Equal("passed", attached.Summary.AuditStatus);
        Assert.NotEqual(capture.Summary.EvidenceSha256, attached.Summary.EvidenceSha256);
        var (summary, evidence) = RoundAuditDocuments.Create(attached);
        var digest = GameSecurityEventDocuments.Hash(attached.PayloadJson);
        Assert.Equal(digest, attached.Summary.EvidenceSha256);
        Assert.Equal(digest, summary["evidenceSha256"]);
        Assert.Equal(digest, evidence["sha256"]);
    }

    [Fact]
    public void MixedProofPreservesMultipleMissingWitnessesOnTheSameStateAndRequiresCoverage()
    {
        var first = new RoundAuditLedgerEntry("tableState", "old-round", "alice", "old-stake", "round-1", -500,
            RoundAuditFinancialProof.LegacyHostUnavailable, EvidenceAvailable: false);
        var second = first with { UserId = "bob", AmountCents = -775 };
        var attached = RoundAuditFinancialProof.Attach(Capture(), "example-worker", "proof-v1", [first, first, second], [Operation()]);

        using var document = JsonDocument.Parse(attached.PayloadJson);
        var proof = document.RootElement.GetProperty(RoundAuditFinancialProof.PropertyName);
        Assert.Equal(RoundAuditFinancialProof.MixedLegacyAndSignedWallet, proof.GetProperty("provenance").GetString());
        var entries = proof.GetProperty("entries");
        Assert.Equal(new[] { "alice", "bob" }, entries.EnumerateArray().Select(entry => entry.GetProperty("userId").GetString()));
        Assert.All(entries.EnumerateArray(), entry => Assert.False(entry.GetProperty("evidenceAvailable").GetBoolean()));
        Assert.Equal("incomplete", attached.Summary.AuditStatus);
        var warning = Assert.Single(attached.Summary.Findings);
        Assert.Equal(RoundAuditFinancialProof.UnavailableLegacyEvidenceCode, warning.Code);
        Assert.Equal("warning", warning.Severity);
    }

    [Fact]
    public void LegacyMissingEvidenceKeepsPreviouslyFlaggedCaptureAndExistingWarnings()
    {
        var error = new RoundAuditFinding("invalid-evidence", "error", "Bad evidence.");
        var warning = new RoundAuditFinding(RoundAuditFinancialProof.UnavailableLegacyEvidenceCode, "warning", "Existing warning.");
        var ledger = new RoundAuditLedgerEntry("gameState", "legacy-row", "alice", "prepaid", "round-1", -500,
            RoundAuditFinancialProof.LegacyHostUnavailable, EvidenceAvailable: false);
        var attached = RoundAuditFinancialProof.Attach(Capture(error, warning), "example-worker", "proof-v1", [ledger], []);

        using var document = JsonDocument.Parse(attached.PayloadJson);
        Assert.Equal(RoundAuditFinancialProof.LegacyHost, document.RootElement.GetProperty(RoundAuditFinancialProof.PropertyName)
            .GetProperty("provenance").GetString());
        Assert.Equal("flagged", attached.Summary.AuditStatus);
        Assert.Equal(new[] { error, warning }, attached.Summary.Findings);
    }

    [Fact]
    public void SignedOperationsWithoutLedgerRowsStillHaveSignedProvenance()
    {
        var attached = RoundAuditFinancialProof.Attach(Capture(), "example-worker", "proof-v1", [],
            [Operation() with { LedgerEvidenceIds = [] }]);
        using var document = JsonDocument.Parse(attached.PayloadJson);
        Assert.Equal(RoundAuditFinancialProof.SignedWallet, document.RootElement.GetProperty(RoundAuditFinancialProof.PropertyName)
            .GetProperty("provenance").GetString());
    }

    [Fact]
    public void IdenticalWalletOperationsCoalesceAndConflictingOrReattachedProofIsRejected()
    {
        var capture = Capture();
        var operation = Operation();
        var attached = RoundAuditFinancialProof.Attach(capture, "example-worker", "proof-v1", [], [operation, Operation()]);
        using var document = JsonDocument.Parse(attached.PayloadJson);
        Assert.Equal(1, document.RootElement.GetProperty(RoundAuditFinancialProof.PropertyName)
            .GetProperty("walletOperations").GetArrayLength());
        Assert.Throws<ArgumentException>(() => RoundAuditFinancialProof.Attach(capture, "example-worker", "proof-v1", [],
            [operation, operation with { OutcomeSha256 = new string('b', 64) }]));
        Assert.Throws<InvalidOperationException>(() => RoundAuditFinancialProof.Attach(attached, "example-worker", "proof-v1", [], []));
    }

    [Fact]
    public void IdenticalBatchProofsCoalesceAndPreserveActualRequestAndReceiptInProtectedEvidence()
    {
        var original = BatchOperation();
        var attached = RoundAuditFinancialProof.Attach(Capture(), "example-worker", "proof-v1", [],
            [original, BatchOperation()]);

        using var document = JsonDocument.Parse(attached.PayloadJson);
        var proof = document.RootElement.GetProperty(RoundAuditFinancialProof.PropertyName);
        var operation = Assert.Single(proof.GetProperty("walletOperations").EnumerateArray());
        Assert.Empty(operation.GetProperty("receiptsByAccount").EnumerateObject());
        var request = operation.GetProperty("batchRequest");
        var receipt = operation.GetProperty("batchReceipt");
        Assert.Equal("batch-command", request.GetProperty("operationId").GetString());
        Assert.Equal(750L, request.GetProperty("effects")[0].GetProperty("minimumBalanceBeforeCents").GetInt64());
        Assert.Equal(500L, request.GetProperty("effects")[1].GetProperty("payoutCents").GetInt64());
        Assert.Equal(original.BatchReceipt!.BatchId, receipt.GetProperty("batchId").GetString());
        Assert.Equal("alice", receipt.GetProperty("actorAccountId").GetString());
        Assert.Equal(original.BatchReceipt.Accounts[0].WagerTransactionId,
            receipt.GetProperty("accounts")[0].GetProperty("wagerTransactionId").GetString());
        Assert.Equal(original.BatchReceipt.Accounts[1].PayoutTransactionId,
            receipt.GetProperty("accounts")[1].GetProperty("payoutTransactionId").GetString());
        Assert.Equal(GameSecurityEventDocuments.Hash(attached.PayloadJson), attached.Summary.EvidenceSha256);
    }

    [Fact]
    public void DuplicateBatchOperationRejectsChangedEffectsOrReceiptEvidence()
    {
        var operation = BatchOperation();
        var request = operation.BatchRequest!;
        var receipt = operation.BatchReceipt!;
        var conflicts = new[]
        {
            operation with { BatchRequest = null },
            operation with { BatchReceipt = null },
            operation with { BatchRequest = request with { Effects = request.Effects.Reverse().ToArray() } },
            operation with
            {
                BatchRequest = request with { Effects = [request.Effects[0] with { MinimumBalanceBeforeCents = 900 }, request.Effects[1]] },
            },
            operation with { BatchReceipt = receipt with { WasAlreadySettled = true } },
            operation with
            {
                BatchReceipt = receipt with { Accounts = [receipt.Accounts[0], receipt.Accounts[1] with { PayoutTransactionId = "wrong" }] },
            },
        };

        foreach (var conflict in conflicts)
            Assert.Throws<ArgumentException>(() => RoundAuditFinancialProof.Attach(Capture(), "example-worker", "proof-v1", [],
                [operation, conflict]));
    }

    private static RoundAuditCapture Capture(params RoundAuditFinding[] findings) => RoundAuditCapture.Create(
        "example", "round-1", "table-1", 1, DateTime.UnixEpoch, DateTime.UnixEpoch.AddMinutes(1),
        new { data = new { secret = new[] { "private", "round" }, count = 2, hint = (string?)null }, version = 1 },
        "sample-evidence-v1", "test-audit-v1", [new("alice", "Alice", true, 500, 0, -500)], findings);

    private static RoundAuditWalletOperation Operation() => new("gameWallet", "command-1", new string('a', 64),
        ["debit"], new Dictionary<string, GameWalletSettlementReceipt>
        {
            ["alice"] = new("settlement-1", "debit", null, 1_000, 500, 500, DateTimeOffset.UnixEpoch, false, false),
        });

    private static RoundAuditWalletOperation BatchOperation()
    {
        var outcome = new string('b', 64);
        var request = new GameWalletBatchRequest("batch-command", outcome,
            [new("alice", 500, 0, 750), new("bob", 0, 500)]);
        var batchId = GameWalletBatchIdentity.CreateId("example", request.OperationId);
        var debit = $"{GameWalletBatchIdentity.AccountLedgerPrefix(batchId, "alice")}-wager";
        var credit = $"{GameWalletBatchIdentity.AccountLedgerPrefix(batchId, "bob")}-payout";
        var receipt = new GameWalletBatchReceipt(batchId, "alice",
            [new("alice", 1_000, 500, false, debit, null), new("bob", 500, 1_000, false, null, credit)],
            DateTimeOffset.UnixEpoch, false, false);
        return new("gameWallet", request.OperationId, outcome, [debit, credit],
            new Dictionary<string, GameWalletSettlementReceipt>(), request, receipt);
    }
}
