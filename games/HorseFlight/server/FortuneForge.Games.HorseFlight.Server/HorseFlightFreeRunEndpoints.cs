using FortuneForge.Games.Abstractions.Wallet;
using FortuneForge.Games.Hosting;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace FortuneForge.Games.HorseFlight.Server;

public static class HorseFlightFreeRunEndpoints
{
    public const string GamePath = "/api/games/horse-flight";
    public const string CompetitionPath = "/api/arcade-competitions/horse-flight/free/runs";
    public static IEndpointRouteBuilder MapHorseFlightFreeRuns(this IEndpointRouteBuilder endpoints,
        Func<HttpContext, string?> resolveUserId, Func<bool> enabled)
    {
        var game = endpoints.MapGroup(GamePath).WithMetadata(new GameWorkerPublicRoute("horse-flight", GamePath, "horse-flight"));
        game.MapGet("/status", (Func<HttpContext, Task<IResult>>)((HttpContext context) => ExecuteAsync(context, resolveUserId, enabled, async (_, user, token) =>
        {
            var account = await context.RequestServices.GetRequiredService<IGameWalletAccountReader>().GetStateAsync(user, token);
            var balance = account.SlotsCreditsBalanceCents ?? throw new InvalidOperationException("The account balance is unavailable.");
            return Results.Ok(new HorseFlightStatusResponse(true, HorseFlightEngine.TickMilliseconds, balance / 100m, "recorded-free-play"));
        })));
        game.MapPost("/runs", (Func<HttpContext, Task<IResult>>)((HttpContext context) => ExecuteAsync(context, resolveUserId, enabled, async (store, user, token) =>
            Results.Ok(await store.StartAsync(user, context.Request.Headers["Idempotency-Key"].ToString(), token)))));
        game.MapPost("/runs/{runId}/complete", (string runId, CompleteHorseFlightRunRequest request, HttpContext context) =>
            ExecuteAsync(context, resolveUserId, enabled, async (store, user, token) =>
                Results.Ok(await store.CompleteAsync(user, runId, request, context.Request.Headers["Idempotency-Key"].ToString(), token))));

        var competition = endpoints.MapGroup(CompetitionPath)
            .WithMetadata(new GameWorkerPublicRoute("horse-flight", CompetitionPath, "horse-flight"));
        competition.MapPost("", (Func<HttpContext, Task<IResult>>)((HttpContext context) => ExecuteAsync(context, resolveUserId, enabled, async (store, user, token) =>
        {
            var key = context.Request.Headers["Idempotency-Key"].ToString();
            if (string.IsNullOrWhiteSpace(key)) return Error("arcade-horse-flight-free-idempotency-key-required", "An idempotency key is required.", 400, true);
            var result = await store.StartAsync(user, key, token);
            return Results.Ok(new HorseFlightRecordedRunStart(result.RunId, result.Seed, result.TickMilliseconds, result.WasReplay) { RulesVersion = result.RulesVersion });
        }, competition: true, starting: true)));
        competition.MapPost("/{runId}/replay", (string runId, CompleteHorseFlightRunRequest request, HttpContext context) =>
            ExecuteAsync(context, resolveUserId, enabled, async (store, user, token) =>
            {
                var result = await store.CompleteAsync(user, runId, request, $"horse-flight-free-complete-{runId}", token);
                return Results.Ok(new HorseFlightRecordedRunCompletion(result.RunId, result.Score, result.Phase,
                    result.TotalTicks, result.JumpTicks, result.WasReplay) { RightClickTicks = result.RightClickTicks });
            }, competition: true));
        return endpoints;
    }

    private static async Task<IResult> ExecuteAsync(HttpContext context, Func<HttpContext, string?> resolveUserId, Func<bool> enabled,
        Func<HorseFlightFreeRunStore, string, CancellationToken, Task<IResult>> action, bool competition = false, bool starting = false)
    {
        context.Response.Headers.CacheControl = "no-store, max-age=0";
        if (!enabled()) return Error(competition ? "arcade-horse-flight-disabled" : "horse-flight-disabled",
            "Horse Flight is still being verified and cannot start a run yet.", 503, competition);
        if (resolveUserId(context) is not { } user) return Error(competition ? "arcade-competition-authentication-required" : "horse-flight-authentication-required",
            "Sign in to play Horse Flight.", 401, competition);
        try { return await action(context.RequestServices.GetRequiredService<HorseFlightFreeRunStore>(), user, context.RequestAborted); }
        catch (HorseFlightRunNotFoundException exception) { return Error(competition ? "arcade-horse-flight-free-run-not-found" : "horse-flight-run-not-found", exception.Message, 404, competition); }
        catch (HorseFlightRunConflictException exception) { return Error(competition ? "arcade-horse-flight-free-run-conflict" : "horse-flight-run-conflict", exception.Message, 409, competition); }
        catch (ArgumentException exception) { return Error(competition ? starting ? "arcade-horse-flight-free-run-invalid" : "arcade-horse-flight-replay-invalid" : "horse-flight-invalid-request", exception.Message, 400, competition); }
        catch (Exception exception)
        {
            context.RequestServices.GetRequiredService<ILoggerFactory>().CreateLogger(nameof(HorseFlightFreeRunEndpoints))
                .LogWarning("Horse Flight recorded run failed ({ErrorType}).", exception.GetType().Name);
            return competition ? Error("arcade-horse-flight-free-run-failed", "The run could not be completed.", 500, true)
                : Results.Problem("The Horse Flight run could not be completed.", statusCode: 500);
        }
    }
    private static IResult Error(string code, string message, int status, bool competition) => competition
        ? Results.Json(new { code }, statusCode: status)
        : Results.Json(new HorseFlightErrorResponse(code, message), statusCode: status);
}

public sealed record HorseFlightRecordedRunStart(string RunId, uint Seed, int TickMilliseconds, bool WasReplay)
{
    public int RulesVersion { get; init; } = HorseFlightEngine.CurrentRulesVersion;
}
public sealed record HorseFlightRecordedRunCompletion(string RunId, int Score, string Phase, int TotalTicks, IReadOnlyList<int> JumpTicks, bool WasReplay)
{
    public IReadOnlyList<int> RightClickTicks { get; init; } = [];
}
