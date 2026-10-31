using FortuneForge.Games.Abstractions.Auditing;
using Google.Cloud.Firestore;

namespace FortuneForge.Server.SecurityAudit;

/// <summary>Acknowledged, durable write for engine observations outside Firestore transactions.</summary>
public interface IGameSecurityEventSink
{
    Task WriteAsync(GameSecurityEventDocument observation, CancellationToken cancellationToken = default);
    Task WriteCompletedAsync(GameSecurityEventDocument observation, RoundAuditCapture capture,
        CancellationToken cancellationToken = default);
}

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
            return true;
        }, cancellationToken: cancellationToken);
    }
}

public sealed class GameSecurityRecordingException(Exception innerException)
    : Exception("The game command could not be fully recorded for security auditing.", innerException);

internal static class GameSecurityEventSink
{
    internal static RoundAuditCapture FreeCompletion(string gameId, string instanceId, string tableId,
        int roundNumber, DateTime startedAtUtc, DateTime completedAtUtc, object state, string userId,
        string evidenceKind, IReadOnlyList<RoundAuditFinding>? findings = null,
        string? auditorVersion = null) => RoundAuditCapture.Create(gameId, instanceId, tableId, roundNumber,
        startedAtUtc, completedAtUtc, state, evidenceKind, auditorVersion ?? "free-engine-receipt-v1",
        [new RoundAuditParticipant(userId, userId, true, 0, 0, 0)],
        findings ?? [new RoundAuditFinding("independent-replay-pending", "warning",
            "The authoritative free-play state was retained; independent rule replay has not yet verified it.")]);

    internal static async Task WriteAsync(IGameSecurityEventSink? sink, string gameId, string instanceId,
        long version, DateTime occurredAtUtc, object state, IEnumerable<string> participantIds,
        string eventKind, string? tableId = null, RoundAuditCapture? completion = null,
        CancellationToken cancellationToken = default)
    {
        if (sink is null) return; // Unit-only engines are constructed directly without production services.
        try
        {
            var observation = GameSecurityEventDocuments.Capture(gameId, instanceId, version,
                occurredAtUtc, state, participantIds, eventKind, tableId);
            if (completion is null) await sink.WriteAsync(observation, cancellationToken);
            else await sink.WriteCompletedAsync(observation, completion, cancellationToken);
        }
        catch (Exception exception) when (exception is not OperationCanceledException)
        {
            throw new GameSecurityRecordingException(exception);
        }
    }
}
