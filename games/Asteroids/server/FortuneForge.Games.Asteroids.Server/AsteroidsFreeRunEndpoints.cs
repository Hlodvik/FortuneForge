using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.DependencyInjection;

namespace FortuneForge.Games.Asteroids.Server;

/// <summary>Existing public wire routes; authentication is supplied by the signed worker boundary.</summary>
public static class AsteroidsFreeRunEndpoints
{
    public const string RoutePrefix = "/api/arcade-competitions/" + AsteroidsModule.GameId + "/free/runs";

    public static IEndpointRouteBuilder MapAsteroidsFreeRuns(this IEndpointRouteBuilder endpoints,
        Func<HttpContext, string?> resolveUserId)
    {
        var api = endpoints.MapGroup(RoutePrefix);
        api.MapPost("", (Delegate)((HttpContext context) => Execute(context, resolveUserId, async (store, user, token) =>
        {
            var key = context.Request.Headers["Idempotency-Key"].ToString();
            if (string.IsNullOrWhiteSpace(key))
                return Results.BadRequest(new { code = "arcade-asteroids-free-idempotency-key-required" });
            var started = await store.StartAsync(key, user, token);
            return Results.Ok(new { runId = started.Run.RunId, seedHex = started.Run.Seed.ToString("x16"),
                startedAtUtc = started.StartedAtUtc, wasReplay = started.WasAlreadyStarted });
        }, "arcade-asteroids-free-run-invalid")));
        api.MapGet("/{runId}", (string runId, HttpContext context) => Execute(context, resolveUserId,
            async (store, user, token) => Results.Ok(await store.GetAsync(runId, user, token))));
        api.MapPost("/{runId}/replay", (string runId, AsteroidsReplayRequest? request, HttpContext context) =>
            Execute(context, resolveUserId, async (store, user, token) =>
            {
                if (request is null || request.RulesVersion != AsteroidsReplayEvaluator.RulesVersion || request.Commands is null ||
                    request.Commands.Any(command => command is null))
                    return Results.BadRequest(new { code = "arcade-asteroids-replay-invalid" });
                var replay = new AsteroidsReplay(request.TotalSteps,
                    request.Commands.Select(command => new AsteroidsInputCommand(command.Step, (AsteroidsInput)command.Input)).ToArray());
                var result = await store.CompleteAsync(runId, user, replay, token);
                return Results.Ok(new { result.RunId, result.Score,
                    terminal = result.Terminal.ToString().ToLowerInvariant(), wasReplay = result.WasAlreadyCompleted });
            }));
        return endpoints;
    }

    private static async Task<IResult> Execute(HttpContext context, Func<HttpContext, string?> resolveUserId,
        Func<AsteroidsFreeRunStore, string, CancellationToken, Task<IResult>> action,
        string invalidCode = "arcade-asteroids-replay-invalid")
    {
        context.Response.Headers.CacheControl = "no-store, max-age=0";
        if (resolveUserId(context) is not { } userId)
            return Results.Json(new { code = "arcade-competition-authentication-required" }, statusCode: 401);
        try
        {
            return await action(context.RequestServices.GetRequiredService<AsteroidsFreeRunStore>(), userId, context.RequestAborted);
        }
        catch (ArgumentException)
        {
            return Results.BadRequest(new { code = invalidCode });
        }
        catch (InvalidOperationException)
        {
            return Results.Conflict(new { code = "arcade-asteroids-free-run-conflict" });
        }
        catch (Grpc.Core.RpcException)
        {
            return Results.Json(new { code = "arcade-asteroids-free-run-failed" }, statusCode: 500);
        }
    }
}

public sealed record AsteroidsReplayRequest(int RulesVersion, int TotalSteps, AsteroidsWireCommand[]? Commands);
public sealed record AsteroidsWireCommand(int Step, int Input);
