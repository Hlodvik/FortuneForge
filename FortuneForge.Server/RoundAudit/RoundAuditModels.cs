using FortuneForge.Games.Abstractions.Auditing;
using Google.Cloud.Firestore;

namespace FortuneForge.Server.RoundAudit;

internal static class RoundAuditFirestore
{
    internal static void Write(Transaction transaction, FirestoreDb database, RoundAuditCapture capture)
    {
        var documents = RoundAuditDocuments.Create(capture);
        transaction.Create(database.Collection(RoundAuditDocuments.Summaries).Document(capture.Summary.Id), documents.Summary);
        transaction.Create(database.Collection(RoundAuditDocuments.Evidence).Document(capture.Summary.Id), documents.Evidence);
    }
}
