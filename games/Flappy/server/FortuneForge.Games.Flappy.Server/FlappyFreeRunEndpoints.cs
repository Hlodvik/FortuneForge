using System.Collections.Immutable;
using FortuneForge.Games.Hosting;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace FortuneForge.Games.Flappy.Server;

/// <summary>The authenticated practice API keeps its original public URLs.</summary>
public static class FlappyFreeRunEndpoints
{
    public const string PathPrefix = "/api/arcade-competitions/flappy/free/runs";
    public static IEndpointRouteBuilder MapFlappyFreeRuns(this IEndpointRouteBuilder endpoints, Func<HttpContext, string?> resolveUserId)
    {
        var api = endpoints.MapGroup(PathPrefix).WithMetadata(new GameWorkerPublicRoute("flappy", PathPrefix, "flappy"));
        api.MapPost("", (Func<HttpContext, Task<IResult>>)((HttpContext context) => ExecuteAsync(context, resolveUserId, async (store, user, token) =>
        {
            var key = context.Request.Headers["Idempotency-Key"].ToString();
            if (string.IsNullOrWhiteSpace(key)) return Error("arcade-flappy-free-idempotency-key-required", 400);
            var result = await store.StartAsync(key, user, token);
            return Results.Ok(new FlappyRecordedRunStart(result.Run.RunId, result.Run.Seed, result.StartedAtUtc, result.WasAlreadyStarted));
        }, starting: true)));
        api.MapPost("/{runId}/replay", (string runId, FlappyRecordedReplayRequest request, HttpContext context) =>
            ExecuteAsync(context, resolveUserId, async (store, user, token) =>
            {
                if (request.FlapTicks is null) return Error("arcade-flappy-replay-invalid", 400);
                var replay = new FlappyReplay(request.TotalTicks, request.FlapTicks.ToImmutableArray());
                var result = await store.CompleteAsync(runId, user, replay, token);
                return Results.Ok(new FlappyRecordedRunCompletion(result.RunId, result.Score, result.Terminal, result.WasAlreadyCompleted));
            }));
        return endpoints;
    }

    private static async Task<IResult> ExecuteAsync(HttpContext context, Func<HttpContext, string?> resolveUserId,
        Func<FlappyFreeRunStore, string, CancellationToken, Task<IResult>> action, bool starting = false)
    {
        context.Response.Headers.CacheControl = "no-store, max-age=0";
        if (resolveUserId(context) is not { } user) return Error("arcade-competition-authentication-required", 401);
        try { return await action(context.RequestServices.GetRequiredService<FlappyFreeRunStore>(), user, context.RequestAborted); }
        catch (ArgumentException) { return Error(starting ? "arcade-flappy-free-run-invalid" : "arcade-flappy-replay-invalid", 400); }
        catch (InvalidOperationException) { return Error("arcade-flappy-free-run-conflict", 409); }
        catch (Exception exception)
        {
            context.RequestServices.GetRequiredService<ILoggerFactory>().CreateLogger(nameof(FlappyFreeRunEndpoints))
                .LogWarning("Flappy recorded run failed ({ErrorType}).", exception.GetType().Name);
            return Error("arcade-flappy-free-run-failed", 500);
        }
    }
    private static IResult Error(string code, int status) => Results.Json(new { code }, statusCode: status);
}

public sealed record FlappyRecordedReplayRequest(int TotalTicks, IReadOnlyList<int>? FlapTicks);
public sealed record FlappyRecordedRunStart(string RunId, uint Seed, DateTimeOffset StartedAtUtc, bool WasReplay);
public sealed record FlappyRecordedRunCompletion(string RunId, long Score, string Terminal, bool WasReplay);
