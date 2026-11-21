using FortuneForge.Games.Abstractions.Auditing;
using FortuneForge.Server.Games.Roulette;

namespace FortuneForge.Server.Tests.Games.Roulette;

internal sealed class RouletteSecurityEventRecorder(IGameSecurityEventSink sink) : IRouletteEventRecorder
{
    public Task WriteAsync(RouletteGameService.RouletteAuditSnapshot snapshot, CancellationToken token) =>
        GameSecurityEventSink.WriteAsync(sink, "roulette", snapshot.InstanceId, snapshot.Version, snapshot.OccurredAtUtc,
            snapshot.State, snapshot.ParticipantIds, snapshot.EventKind, snapshot.TableId, snapshot.Completion, token);
}
