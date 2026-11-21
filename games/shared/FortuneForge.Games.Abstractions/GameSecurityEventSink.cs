namespace FortuneForge.Games.Abstractions.Auditing;

/// <summary>Acknowledged, durable write for engine observations outside Firestore transactions.</summary>
public interface IGameSecurityEventSink
{
    Task WriteAsync(GameSecurityEventDocument observation, CancellationToken cancellationToken = default);
    Task WriteCompletedAsync(GameSecurityEventDocument observation, RoundAuditCapture capture,
        CancellationToken cancellationToken = default);
}

public sealed class GameSecurityRecordingException(Exception innerException)
    : Exception("The game command could not be fully recorded for security auditing.", innerException);

public static class GameSecurityEventSink
{
    public static RoundAuditCapture FreeCompletion(string gameId, string instanceId, string tableId,
        int roundNumber, DateTime startedAtUtc, DateTime completedAtUtc, object state, string userId,
        string evidenceKind, IReadOnlyList<RoundAuditFinding>? findings = null,
        string? auditorVersion = null) => RoundAuditCapture.Create(gameId, instanceId, tableId, roundNumber,
        startedAtUtc, completedAtUtc, state, evidenceKind, auditorVersion ?? "free-engine-receipt-v1",
        [new RoundAuditParticipant(userId, userId, true, 0, 0, 0)],
        findings ?? [new RoundAuditFinding("independent-replay-pending", "warning",
            "The authoritative free-play state was retained; independent rule replay has not yet verified it.")]);

    public static async Task WriteAsync(IGameSecurityEventSink? sink, string gameId, string instanceId,
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
