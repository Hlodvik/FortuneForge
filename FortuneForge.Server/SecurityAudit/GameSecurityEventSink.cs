using FortuneForge.Games.Abstractions.Auditing;
using FortuneForge.Server.Activity;
using Google.Cloud.Firestore;

namespace FortuneForge.Server.SecurityAudit;

internal sealed class FirestoreGameSecurityEventSink(FirestoreDb database) : IGameSecurityEventSink
{
    public async Task WriteAsync(GameSecurityEventDocument observation, CancellationToken cancellationToken = default)
    {
        var gameId = (string)observation.Fields["gameId"];
        var instanceId = (string)observation.Fields["instanceId"];
        var batch = database.StartBatch();
        batch.Set(database.Collection(GameSecurityEventDocuments.CollectionName).Document(observation.Id), observation.Fields);
        batch.Set(database.Collection(GameSecurityEventDocuments.ManifestCollectionName)
                .Document(GameSecurityEventDocuments.ManifestId(gameId, instanceId)),
            GameSecurityJournal.ManifestFields(gameId, instanceId, observation.Id), SetOptions.MergeAll);
        GameExperienceSecurityWitness.Write(database, observation,
            (reference, fields) => batch.Set(reference, fields, SetOptions.MergeAll));
        await batch.CommitAsync(cancellationToken);
    }

    public Task WriteCompletedAsync(GameSecurityEventDocument observation, RoundAuditCapture capture,
        CancellationToken cancellationToken = default)
    {
        var summary = database.Collection(RoundAuditDocuments.Summaries).Document(capture.Summary.Id);
        var evidence = database.Collection(RoundAuditDocuments.Evidence).Document(capture.Summary.Id);
        var eventReference = database.Collection(GameSecurityEventDocuments.CollectionName).Document(observation.Id);
        var documents = RoundAuditDocuments.Create(capture);
        return database.RunTransactionAsync(async transaction =>
        {
            var prior = await transaction.GetAllSnapshotsAsync([summary, evidence], cancellationToken);
            if (prior[0].Exists != prior[1].Exists)
                throw new InvalidOperationException("An audit summary is missing its private evidence.");
            if (prior[0].Exists)
            {
                // A free run can be undone and complete again. Preserve its first immutable
                // completion snapshot; every later transition still receives its own event.
                if (!prior[0].TryGetValue<string>("evidenceSha256", out var digest) ||
                    !prior[1].TryGetValue<string>("sha256", out var evidenceDigest) || evidenceDigest != digest)
                    throw new InvalidOperationException("A completed audit has inconsistent private evidence.");
            }
            else
            {
                transaction.Create(summary, documents.Summary);
                transaction.Create(evidence, documents.Evidence);
            }
            transaction.Set(eventReference, observation.Fields);
            GameSecurityJournal.WriteManifest(transaction, database,
                (string)observation.Fields["gameId"], (string)observation.Fields["instanceId"], observation.Id);
            GameExperienceSecurityWitness.Write(database, observation,
                (reference, fields) => transaction.Set(reference, fields, SetOptions.MergeAll));
            return true;
        }, cancellationToken: cancellationToken);
    }
}
