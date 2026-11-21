using FortuneForge.Games.Abstractions.Analytics;
using FortuneForge.Games.Abstractions.Auditing;
using FortuneForge.Games.TwentyFortyEight;
using FortuneForge.Games.TwentyFortyEight.Server;
using FortuneForge.Server.GameWorkers;

namespace FortuneForge.Server.Games.TwentyFortyEight;

/// <summary>Binds worker compute to package-owned in-memory sessions on this host.</summary>
public sealed class TwentyFortyEightGameService(
    IGameAnalyticsSink? analytics = null,
    IGameSecurityEventSink? securityEvents = null,
    GameWorkerClient? worker = null)
    : LegacyTwentyFortyEightGameService(
        analytics,
        securityEvents,
        (userId, request, token) => worker?.IsConfigured(TwentyFortyEightSession.GameId) == true
            ? worker.InvokeAsync<TwentyFortyEightComputeStart, TwentyFortyEightState>(
                TwentyFortyEightSession.GameId, userId, TwentyFortyEightSession.StartComputeRoute, request, token)
            : null,
        (userId, request, token) => worker?.IsConfigured(TwentyFortyEightSession.GameId) == true
            ? worker.InvokeAsync<TwentyFortyEightComputeMove, TwentyFortyEightTransition>(
                TwentyFortyEightSession.GameId, userId, TwentyFortyEightSession.MoveComputeRoute, request, token)
            : null)
{
}
