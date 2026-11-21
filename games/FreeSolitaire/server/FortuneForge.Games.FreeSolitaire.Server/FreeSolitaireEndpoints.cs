using FortuneForge.Games.Hosting;
using FortuneForge.Games.Solitaire;
using Grpc.Core;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace FortuneForge.Games.FreeSolitaire.Server;

public static class FreeSolitaireRouteContract
{
    public const string PathPrefix = "/api/solitaire/free";
    // Existing requests under /api/solitaire were recorded in this audit scope.
    public const string AuditGameId = SolitaireFreeRunIdentity.LegacyGameId;
    public static GameWorkerPublicRoute WorkerRoute { get; } =
        new(SolitaireFreeRunIdentity.StandaloneGameId, PathPrefix, AuditGameId);
}

public static class FreeSolitaireEndpoints
{
    public static IEndpointRouteBuilder MapFreeSolitaireRuns(this IEndpointRouteBuilder endpoints,
        Func<HttpContext, string?> resolveAccount)
    {
        var api = endpoints.MapGroup(FreeSolitaireRouteContract.PathPrefix)
            .WithMetadata(FreeSolitaireRouteContract.WorkerRoute);
        api.MapPost("/runs", (SolitaireFreeRunStartRequest request, HttpContext context) =>
            ExecuteAsync(context, resolveAccount, "Sign in to play free Solitaire.", (service, userId, token) =>
            {
                var keys = context.Request.Headers["Idempotency-Key"];
                if (keys.Count != 1)
                    throw new ArgumentException("Include one valid Solitaire free-run idempotency key.");
                return service.StartAsync(keys[0]!, userId, request.DrawCount, token,
                    request.ReplayRunId, request.GameId ?? SolitaireFreeRunIdentity.LegacyGameId);
            }));
        api.MapGet("/runs/{runId}", (string runId, HttpContext context) =>
            ExecuteAsync(context, resolveAccount, "Sign in to reopen free Solitaire.",
                (service, userId, token) => service.GetAsync(runId, userId, token)));
        api.MapPost("/runs/{runId}/replay", (string runId, SolitaireFreeRunReplayRequest request, HttpContext context) =>
            ExecuteAsync(context, resolveAccount, "Sign in to submit free Solitaire.",
                (service, userId, token) => service.CompleteAsync(runId, userId, request.Commands ?? [], token)));
        api.MapPost("/runs/{runId}/pause", (string runId, HttpContext context) =>
            ExecuteAsync(context, resolveAccount, "Sign in to pause free Solitaire.",
                (service, userId, token) => service.PauseAsync(runId, userId, token)));
        api.MapPost("/runs/{runId}/resume", (string runId, HttpContext context) =>
            ExecuteAsync(context, resolveAccount, "Sign in to resume free Solitaire.",
                (service, userId, token) => service.ResumeAsync(runId, userId, token)));
        return endpoints;
    }

    /// <summary>Marks the existing MVC compatibility endpoints for generic signed forwarding.</summary>
    public static TBuilder WithFreeSolitaireWorkerRoutes<TBuilder>(this TBuilder endpoints)
        where TBuilder : IEndpointConventionBuilder
    {
        endpoints.Add(builder =>
        {
            if (builder is RouteEndpointBuilder route && route.RoutePattern.RawText is { } pattern &&
                ("/" + pattern.TrimStart('/')).StartsWith(FreeSolitaireRouteContract.PathPrefix + "/",
                    StringComparison.OrdinalIgnoreCase))
                builder.Metadata.Add(FreeSolitaireRouteContract.WorkerRoute);
        });
        return endpoints;
    }

    private static async Task<IResult> ExecuteAsync<T>(HttpContext context, Func<HttpContext, string?> resolveAccount,
        string authenticationError,
        Func<FirestoreSolitaireFreeRunService, string, CancellationToken, Task<T>> action)
    {
        context.Response.Headers.CacheControl = "no-store, max-age=0";
        if (resolveAccount(context) is not { Length: > 0 } userId)
            return Results.Json(new { error = authenticationError }, statusCode: StatusCodes.Status401Unauthorized);
        try
        {
            return Results.Ok(await action(context.RequestServices.GetRequiredService<FirestoreSolitaireFreeRunService>(),
                userId, context.RequestAborted));
        }
        catch (ArgumentException exception)
        {
            return Results.BadRequest(new { error = exception.Message });
        }
        catch (InvalidOperationException exception)
        {
            return Results.Conflict(new { error = exception.Message });
        }
        catch (RpcException exception)
        {
            context.RequestServices.GetRequiredService<ILoggerFactory>().CreateLogger(nameof(FreeSolitaireEndpoints))
                .LogWarning("Solitaire free-run request failed ({ErrorType}).", exception.GetType().Name);
            return Results.Json(new { error = "The Solitaire game service is temporarily unavailable. Retry the original request." },
                statusCode: StatusCodes.Status503ServiceUnavailable);
        }
    }
}
