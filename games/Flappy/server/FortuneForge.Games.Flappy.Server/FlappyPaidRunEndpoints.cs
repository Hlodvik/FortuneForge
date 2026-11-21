using FortuneForge.Games.Abstractions.Wallet;
using FortuneForge.Games.Arcade.Competition;
using FortuneForge.Games.Hosting;
using FortuneForge.Server.Arcade.Competition;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace FortuneForge.Games.Flappy.Server;

public static class FlappyPaidRunEndpoints
{
    public const string RoutePrefix = "/api/arcade-competitions/" + FlappyModule.GameId;
    public static IEndpointRouteBuilder MapFlappyPaidRuns(this IEndpointRouteBuilder endpoints,
        Func<HttpContext, string?> resolveUserId, Func<HttpContext, string?> resolveBodySha256)
    {
        var api = endpoints.MapGroup(RoutePrefix).WithMetadata(new GameWorkerPublicRoute(FlappyModule.GameId, RoutePrefix, FlappyModule.GameId));
        api.MapPost("/{period}/attempts", (string period, bool? recoverOnly, ArcadeCompetitionPaidAdmission? admission, HttpContext context) =>
            Execute(context, resolveUserId, period, false, async (service, user, kind, token) =>
            {
                var key = context.Request.Headers["Idempotency-Key"].ToString();
                if (string.IsNullOrWhiteSpace(key)) return Error(400, "arcade-competition-idempotency-key-required");
                var bodySha256 = resolveBodySha256(context);
                if (bodySha256 is not { Length: 64 }) return Error(503, "arcade-competition-attempt-unavailable");
                var admitted = await service.StartAsync(key, user, kind, token, admission, recoverOnly == true, bodySha256);
                var attempt = admitted.Attempt;
                return Results.Ok(new { attempt.AttemptId, attempt.Competition.GameId,
                    period = attempt.Competition.WindowKind.ToString().ToLowerInvariant(), attempt.Competition.StartsAtUtc,
                    attempt.Competition.EndsAtUtc, attempt.EntryFeeCents, wasReplay = admitted.WasAlreadyRecorded,
                    runId = admitted.Run.RunId, seed = admitted.Run.Seed });
            }));
        api.MapPost("/{period}/runs/{runId}/replay", (string period, string runId, FlappyRecordedReplayRequest? request, HttpContext context) =>
            Execute(context, resolveUserId, period, true, async (service, user, kind, token) =>
            {
                if (request?.FlapTicks is null) return Error(400, "arcade-flappy-replay-invalid");
                var replay = new FlappyReplay(request.TotalTicks, [.. request.FlapTicks]);
                return Results.Ok(await service.CompleteAsync(kind, runId, user, replay, token));
            }));
        api.MapGet("/{period}/runs/{runId}", (string period, string runId, HttpContext context) =>
            Execute(context, resolveUserId, period, true,
                async (service, user, kind, token) => Results.Ok(await service.GetAsync(kind, runId, user, token))));
        return endpoints;
    }

    private static async Task<IResult> Execute(HttpContext context, Func<HttpContext, string?> resolveUserId,
        string period, bool replay, Func<FlappyPaidRunService, string, ArcadeCompetitionWindowKind, CancellationToken, Task<IResult>> action)
    {
        context.Response.Headers.CacheControl = "no-store, max-age=0";
        if (period == "all-time") return Error(400, "arcade-competition-period-not-startable");
        if (!ArcadeCompetitionStandings.TryParsePeriod(period, out var kind)) return Error(400, "arcade-competition-period-invalid");
        if (resolveUserId(context) is not { } userId) return Error(401, "arcade-competition-authentication-required");
        try
        {
            return await action(context.RequestServices.GetRequiredService<FlappyPaidRunService>(), userId, kind, context.RequestAborted);
        }
        catch (ArcadeCompetitionInsufficientCreditsException) { return Error(409, "arcade-competition-insufficient-credits"); }
        catch (ArcadeCompetitionSettlementCompletedException) { return Error(409, "arcade-competition-settlement-closed"); }
        catch (ArcadeCompetitionEntryPausedException) { return Error(409, "arcade-competition-entry-not-recorded"); }
        catch (ArgumentException) { return Error(400, replay ? "arcade-flappy-replay-invalid" : "arcade-competition-attempt-invalid"); }
        catch (GameWalletUnavailableException) { return Error(503, "arcade-competition-attempt-unavailable"); }
        catch (InvalidOperationException) { return Error(409, replay ? "arcade-flappy-run-conflict" : "arcade-competition-attempt-conflict"); }
        catch (Exception exception) when (!context.RequestAborted.IsCancellationRequested)
        {
            context.RequestServices.GetRequiredService<ILoggerFactory>().CreateLogger(nameof(FlappyPaidRunEndpoints))
                .LogWarning("Flappy paid run request failed ({ErrorType}).", exception.GetType().Name);
            return Error(500, replay ? "arcade-flappy-replay-failed" : "arcade-competition-attempt-failed");
        }
    }
    private static IResult Error(int status, string code) => Results.Json(new { code }, statusCode: status);
}
