using FortuneForge.Games.Abstractions.Analytics;
using FortuneForge.Games.Abstractions.Auditing;
using FortuneForge.Games.DropMerge;
using FortuneForge.Games.DropMerge.Server;
using FortuneForge.Server.GameWorkers;

namespace FortuneForge.Server.Games.DropMerge;

/// <summary>Binds worker compute to package-owned in-memory sessions on this host.</summary>
public sealed class DropMergeGameService(
    IGameAnalyticsSink? analytics = null,
    IGameSecurityEventSink? securityEvents = null,
    GameWorkerClient? worker = null)
    : LegacyDropMergeGameService(
        analytics,
        securityEvents,
        (userId, request, token) => worker?.IsConfigured(DropMergeSession.GameId) == true
            ? worker.InvokeAsync<DropMergeComputeStart, DropMergeState>(
                DropMergeSession.GameId, userId, DropMergeSession.StartComputeRoute, request, token)
            : null,
        (userId, request, token) => worker?.IsConfigured(DropMergeSession.GameId) == true
            ? worker.InvokeAsync<DropMergeComputeDrop, DropMergeTransition>(
                DropMergeSession.GameId, userId, DropMergeSession.DropComputeRoute, request, token)
            : null)
{
}
