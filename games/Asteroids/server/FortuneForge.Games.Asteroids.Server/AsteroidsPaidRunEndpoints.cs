using FortuneForge.Games.Abstractions.Wallet;
using FortuneForge.Games.Arcade.Competition;
using FortuneForge.Server.Arcade.Competition;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.DependencyInjection;

namespace FortuneForge.Games.Asteroids.Server;

public static class AsteroidsPaidRunEndpoints
{
    public const string RoutePrefix = "/api/arcade-competitions/" + AsteroidsModule.GameId;

    public static IEndpointRouteBuilder MapAsteroidsPaidRuns(this IEndpointRouteBuilder endpoints,
        Func<HttpContext, string?> resolveUserId, Func<HttpContext, string?> resolveBodySha256)
    {
        ArgumentNullException.ThrowIfNull(resolveBodySha256);
        var api = endpoints.MapGroup(RoutePrefix);
        api.MapPost("/{period}/attempts", (string period, bool? recoverOnly, ArcadeCompetitionPaidAdmission? admission, HttpContext context) =>
            Execute(context, resolveUserId, period, false, async (service, user, kind, token) =>
            {
                var key = context.Request.Headers["Idempotency-Key"].ToString();
                if (string.IsNullOrWhiteSpace(key)) return Error(400, "arcade-competition-idempotency-key-required");
                var bodySha256 = resolveBodySha256(context);
                if (string.IsNullOrWhiteSpace(bodySha256)) return Error(503, "arcade-competition-attempt-unavailable");
                var admitted = await service.StartAsync(key, user, kind, token, admission, recoverOnly == true,
                    bodySha256);
                var attempt = admitted.Attempt;
                return Results.Ok(new { attempt.AttemptId, attempt.Competition.GameId,
                    period = attempt.Competition.WindowKind.ToString().ToLowerInvariant(), attempt.Competition.StartsAtUtc,
                    attempt.Competition.EndsAtUtc, attempt.EntryFeeCents, wasReplay = admitted.WasAlreadyRecorded,
                    runId = admitted.Run.Run.RunId, seedHex = admitted.Run.Run.Seed.ToString("x16") });
            }));
        api.MapPost("/{period}/runs/{runId}/replay", (string period, string runId, AsteroidsReplayRequest? request,
            HttpContext context) => Execute(context, resolveUserId, period, true, async (service, user, kind, token) =>
            {
                if (request is null || request.RulesVersion != AsteroidsReplayEvaluator.RulesVersion ||
                    request.Commands is null || request.Commands.Any(command => command is null))
                    return Error(400, "arcade-asteroids-replay-invalid");
                var replay = new AsteroidsReplay(request.TotalSteps,
                    request.Commands.Select(command => new AsteroidsInputCommand(command.Step, (AsteroidsInput)command.Input)).ToArray());
                var result = await service.CompleteAsync(kind, runId, user, replay, token);
                return Results.Ok(new { result.RunId, result.Score,
                    terminal = result.Terminal.ToString().ToLowerInvariant(), wasReplay = result.WasAlreadyCompleted });
            }));
        api.MapGet("/{period}/runs/{runId}", (string period, string runId, HttpContext context) =>
            Execute(context, resolveUserId, period, true,
                async (service, user, kind, token) => Results.Ok(await service.GetAsync(kind, runId, user, token))));
        return endpoints;
    }

    private static async Task<IResult> Execute(HttpContext context, Func<HttpContext, string?> resolveUserId,
        string period, bool replay, Func<AsteroidsPaidRunService, string, ArcadeCompetitionWindowKind,
            CancellationToken, Task<IResult>> action)
    {
        context.Response.Headers.CacheControl = "no-store, max-age=0";
        if (period == "all-time") return Error(400, "arcade-competition-period-not-startable");
        if (!ArcadeCompetitionStandings.TryParsePeriod(period, out var kind)) return Error(400, "arcade-competition-period-invalid");
        if (resolveUserId(context) is not { } userId) return Error(401, "arcade-competition-authentication-required");
        try
        {
            return await action(context.RequestServices.GetRequiredService<AsteroidsPaidRunService>(), userId, kind, context.RequestAborted);
        }
        catch (ArcadeCompetitionInsufficientCreditsException) { return Error(409, "arcade-competition-insufficient-credits"); }
        catch (ArcadeCompetitionSettlementCompletedException) { return Error(409, "arcade-competition-settlement-closed"); }
        catch (ArcadeCompetitionEntryPausedException) { return Error(409, "arcade-competition-entry-not-recorded"); }
        catch (ArgumentException) { return Error(400, replay ? "arcade-asteroids-replay-invalid" : "arcade-competition-attempt-invalid"); }
        catch (GameWalletUnavailableException) { return Error(503, "arcade-competition-attempt-unavailable"); }
        catch (InvalidOperationException) { return Error(409, replay ? "arcade-asteroids-run-conflict" : "arcade-competition-attempt-conflict"); }
        catch (Grpc.Core.RpcException) { return Error(500, replay ? "arcade-asteroids-replay-failed" : "arcade-competition-attempt-failed"); }
    }

    private static IResult Error(int status, string code) => Results.Json(new { code }, statusCode: status);
}
