using FortuneForge.Games.Abstractions.Auditing;
using FortuneForge.Games.Firestore;
using Google.Cloud.Firestore;

namespace FortuneForge.Server.RoundAudit;

internal static class RoundAuditFirestore
{
    internal static void Write(Transaction transaction, FirestoreDb database, RoundAuditCapture capture) =>
        FirestoreGameAudit.WriteCompletion(transaction, database, capture);
}
