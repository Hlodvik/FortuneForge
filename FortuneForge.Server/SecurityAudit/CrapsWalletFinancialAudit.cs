using System.Text.Json;
using FortuneForge.Games.Abstractions.Auditing;
using FortuneForge.Server.Accounts.Storage;
using FortuneForge.Server.Games.Craps;
using Google.Cloud.Firestore;

namespace FortuneForge.Server.SecurityAudit;

internal static class CrapsWalletFinancialAudit
{
    private sealed record ActionEvidence(string UserId, PaidCrapsAction[]? Actions);

    internal static async Task<IReadOnlyList<RoundAuditFinding>> InspectAsync(FirestoreDb database,
        RoundAuditSummary summary, string? payload, CancellationToken cancellationToken)
    {
        var findings = await PaidCrapsReplayAudit.InspectFinancialAsync(database, summary, payload, cancellationToken);
        if (!PaidCrapsReplayAudit.Supports(summary) || payload is null || findings.Any(finding => finding.Severity == "error"))
            return findings;

        try
        {
            using var evidence = JsonDocument.Parse(payload);
            var round = evidence.RootElement.GetProperty("data").Deserialize<ActionEvidence>(RoundAuditCapture.Json);
            if (round?.Actions is not { Length: > 0 } actions) return findings;

            foreach (var action in actions)
            {
                var id = GameSecurityEventDocuments.Hash($"{round.UserId}\n{action.OperationId}");
                var operation = await database.Collection("crapsPaidOperations").Document(id).GetSnapshotAsync(cancellationToken);
                if (!operation.Exists || !operation.ContainsField("walletStatus")) continue;
                var signed = FirestorePaidCrapsStore.VerifiedWalletAction(operation, round.UserId, action) ??
                    throw new InvalidOperationException("The confirmed wallet action is missing.");
                var terms = WalletRoundSettlement.FromDirectRequest(summary.GameId, round.UserId, signed.Request);
                var stored = await database.RunTransactionAsync(transaction => FirestoreWalletRoundSettlementStore.StageAsync(
                    database, transaction, terms, signed.Receipt.SettledAtUtc, cancellationToken, requireExisting: true),
                    cancellationToken: cancellationToken);
                if ((stored.ToWireReceipt() with { WasAlreadySettled = false }) !=
                    (signed.Receipt with { WasAlreadySettled = false }))
                    throw new InvalidOperationException("The accepted wallet receipt changed.");
            }

            return findings;
        }
        catch (Exception exception) when (exception is InvalidOperationException or ArgumentException or JsonException or
            KeyNotFoundException or FormatException or ArithmeticException)
        {
            return [..findings, new("craps-paid-ledger-mismatch", "error",
                "A paid Craps wallet receipt or ledger differs from its accepted action.")];
        }
    }
}
