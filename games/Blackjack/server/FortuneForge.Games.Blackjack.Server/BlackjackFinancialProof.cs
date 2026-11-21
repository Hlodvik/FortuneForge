global using BlackjackLedgerProof = FortuneForge.Games.Abstractions.Auditing.RoundAuditLedgerEntry;
global using BlackjackWalletOperationProof = FortuneForge.Games.Abstractions.Auditing.RoundAuditWalletOperation;
using FortuneForge.Games.Abstractions.Auditing;
using FortuneForge.Games.Firestore;
using Google.Cloud.Firestore;

namespace FortuneForge.Server.Cards.Blackjack;

internal sealed record BlackjackFinancialSnapshot(BlackjackLedgerProof[] Entries, BlackjackWalletOperationProof[] Operations)
{
    internal static BlackjackFinancialSnapshot Empty => new([], []);
}

internal static class BlackjackFinancialProof
{
    internal const string Contract = "blackjack-protected-wallet-evidence-v1";
    internal static RoundAuditCapture Protect(RoundAuditCapture capture, BlackjackFinancialSnapshot proof) =>
        RoundAuditFinancialProof.Attach(capture, BlackjackBackend.Owner, Contract, proof.Entries, proof.Operations);

    internal static BlackjackLedgerProof Legacy(DocumentSnapshot snapshot, string roundReference) =>
        FirestoreWalletEvidence.Legacy(snapshot, roundReference);
}
