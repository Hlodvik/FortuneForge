using FortuneForge.Games.Abstractions;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;

namespace FortuneForge.Games.Hosting;

/// <summary>Identifies an existing public route a private game worker can serve.</summary>
public sealed record GameWorkerPublicRoute(string WorkerGameId, string PathPrefix, string AuditGameId);

public static class PreparedSlotSpinRoutes
{
    public const string Segment = "prepared-spins";
    public const string Path = "/api/slots/" + Segment;

    public static void MapPreparedSlotSpin(this IEndpointRouteBuilder endpoints, GameWorkerPublicRoute route, Delegate handler)
    {
        endpoints.MapPost(route.PathPrefix, handler).WithMetadata(route).AddEndpointFilter(DetectLegacyAdmissionAsync);
        endpoints.MapPost(Path, handler).WithMetadata(route with { PathPrefix = Path }).AddEndpointFilter(DetectLegacyAdmissionAsync);
    }

    private static async ValueTask<object?> DetectLegacyAdmissionAsync(EndpointFilterInvocationContext context,
        EndpointFilterDelegate next)
    {
        try { return await next(context); }
        catch (LegacySlotAdmissionPendingException exception)
        {
            return Results.Conflict(new { code = LegacySlotAdmissionPendingException.Code, error = exception.Message });
        }
    }
}
