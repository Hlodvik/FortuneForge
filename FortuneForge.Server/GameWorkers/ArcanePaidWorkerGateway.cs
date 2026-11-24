using FortuneForge.Games.Abstractions.Auditing;
using FortuneForge.Games.ArcaneArchives;
using FortuneForge.Games.ArcaneArchives.Server;
using Microsoft.AspNetCore.Mvc;

namespace FortuneForge.Server.GameWorkers;

internal static class ArcanePaidWorkerGateway
{
    internal static Task<ContentResult> SpinAsync(GameWorkerClient? workers, HttpContext context,
        string accountId, ArcanePaidSpinCommand request, string? operationId, CancellationToken cancellationToken)
    {
        var path = IsPreparedPath(context.Request.Path) ? ArcanePaidRouteContract.PreparedSpinPath : ArcanePaidRouteContract.SpinPath;
        return SlotWorkerGateway.PostAsync(workers, context, ArcaneArchivesEngine.GameId, accountId,
            path, request, operationId ?? GameSecurityContext.Current?.RequestId ?? Guid.NewGuid().ToString("N"), cancellationToken);
    }

    internal static Task<ContentResult> StateAsync(GameWorkerClient? workers, HttpContext context, string accountId,
        CancellationToken cancellationToken) => SlotWorkerGateway.ReadStateAsync(workers, context,
        ArcaneArchivesEngine.GameId, accountId, ArcanePaidRouteContract.StatePath, cancellationToken);

    internal static bool IsPreparedPath(PathString path) =>
        string.Equals(path.Value?.TrimEnd('/'), ArcanePaidRouteContract.PreparedSpinPath, StringComparison.OrdinalIgnoreCase);
}
