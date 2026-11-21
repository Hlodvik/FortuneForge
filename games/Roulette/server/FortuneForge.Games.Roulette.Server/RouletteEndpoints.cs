using System.Text.Json;
using FortuneForge.Games.Roulette;
using FortuneForge.Games.Hosting;
using FortuneForge.Games.Abstractions.ManagedPlayers;
using FortuneForge.Games.Abstractions.Wallet;
using Google.Cloud.Firestore;
using Grpc.Core;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace FortuneForge.Server.Games.Roulette;

public static class RouletteEndpoints
{
    public static IServiceCollection AddRouletteServer(this IServiceCollection services)
    {
        services.TryAddSingleton<IManagedTablePopulationDirector, ManagedTablePopulationDirector>();
        services.TryAddSingleton<IRoulettePopulation, SignedRoulettePopulation>();
        services.AddSingleton(provider => new FirestoreRouletteService(provider.GetRequiredService<FirestoreDb>(),
            provider.GetRequiredService<IRoulettePopulation>(), () => provider.GetRequiredService<IConfiguration>().GetValue("Features:RouletteEnabled", false)));
        services.AddHostedService<RoulettePackageWorker>();
        return services;
    }
    public static IEndpointRouteBuilder MapRoulette(this IEndpointRouteBuilder endpoints, Func<HttpContext, string?> account)
    {
        var routes = endpoints.MapGroup("/api/games/roulette");
        routes.AddEndpointFilter(async (context, next) =>
        {
            context.HttpContext.Response.Headers.CacheControl = "no-store";
            if (account(context.HttpContext) is null) return Error(401, "roulette-authentication-required", "Sign in to play Roulette.");
            try { return await next(context); }
            catch (GameWalletUnavailableException) { return Error(503, "roulette-account-unavailable", "The authenticated player profile is unavailable. Retry the original action."); }
            catch (ManagedPlayerRosterUnavailableException error) { return Error(503, "roulette-retry-required", error.Message); }
            catch (RouletteAccessException error) { return Error(404, "roulette-round-not-found", error.Message); }
            catch (RouletteConflictException error) { return Error(409, "roulette-round-conflict", error.Message); }
            catch (RouletteUnavailableException error) { return Error(503, "roulette-disabled", error.Message); }
            catch (Exception error) when (error is ArgumentException or RouletteRuleException or JsonException or BadHttpRequestException)
            { return Error(400, "roulette-invalid-action", error.Message); }
            catch (InvalidOperationException)
            { return Error(503, "roulette-recovery-required", "The saved Roulette state could not be verified. Retry the original command."); }
            catch (RpcException error) when (error.StatusCode is StatusCode.Aborted or StatusCode.DeadlineExceeded or StatusCode.Unavailable)
            { context.HttpContext.Response.Headers.RetryAfter = "1"; return Error(503, "roulette-retry-required", "Retry the original command to recover its saved result."); }
        });
        static string Operation(HttpContext context) => context.Request.Headers["Idempotency-Key"].ToString();
        static async Task<long?> Revision(HttpContext context)
        {
            if (context.Request.ContentLength > 0 || context.Request.Headers.ContainsKey("Transfer-Encoding"))
                return (await context.Request.ReadFromJsonAsync<RouletteActionRequest>(context.RequestAborted))?.ExpectedRevision;
            return long.TryParse(context.Request.Headers.IfMatch.ToString().Trim('"'), out var value) ? value : null;
        }
        routes.MapGet("/status", (FirestoreRouletteService service) => Results.Ok(service.Status()));
        routes.MapPost("/rounds", async (HttpContext context, FirestoreRouletteService service, IGameWalletAccountReader accounts, CancellationToken token) =>
            Results.Ok(await service.StartAsync(account(context)!, async cancellation =>
            {
                var profile = await accounts.GetStateAsync(account(context)!, cancellation);
                return GameWalletAccountState.IsValidPlayerName(profile.PlayerName) ? profile.PlayerName! : throw new GameWalletUnavailableException();
            }, Operation(context), token)));
        routes.MapGet("/rounds/{roundId:guid}", async (HttpContext context, Guid roundId, FirestoreRouletteService service, CancellationToken token) =>
            Results.Ok(await service.GetAsync(account(context)!, roundId, token)));
        routes.MapGet("/rounds/{roundId:guid}/history/{roundNumber:int}", async (HttpContext context, Guid roundId, int roundNumber, FirestoreRouletteService service, CancellationToken token) =>
            Results.Ok(await service.HistoryAsync(account(context)!, roundId, roundNumber, token)));
        routes.MapPost("/rounds/{roundId:guid}/bets", async (HttpContext context, Guid roundId, PlaceRouletteBetRequest request, FirestoreRouletteService service, CancellationToken token) =>
            Results.Ok(await service.PlaceBetAsync(account(context)!, roundId, request, Operation(context), request.ExpectedRevision, token)));
        routes.MapDelete("/rounds/{roundId:guid}/bets/{betIndex:int}", async (HttpContext context, Guid roundId, int betIndex, FirestoreRouletteService service, CancellationToken token) =>
            Results.Ok(await service.RemoveBetAsync(account(context)!, roundId, betIndex, Operation(context), await Revision(context), token)));
        routes.MapDelete("/rounds/{roundId:guid}/bets", async (HttpContext context, Guid roundId, FirestoreRouletteService service, CancellationToken token) =>
            Results.Ok(await service.ClearBetsAsync(account(context)!, roundId, Operation(context), await Revision(context), token)));
        routes.MapPost("/rounds/{roundId:guid}/spin", async (HttpContext context, Guid roundId, FirestoreRouletteService service, CancellationToken token) =>
            Results.Ok(await service.SpinAsync(account(context)!, roundId, Operation(context), await Revision(context), token)));
        return endpoints;
    }
    private static IResult Error(int status, string code, string message) => Results.Json(new RouletteErrorResponse(code, message), statusCode: status);
}
internal sealed class RoulettePackageWorker(FirestoreRouletteService service, ILogger<RoulettePackageWorker> logger) : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        using var timer = new PeriodicTimer(TimeSpan.FromSeconds(1));
        while (await timer.WaitForNextTickAsync(stoppingToken))
        {
            try { await service.SweepAsync(DateTime.UtcNow, stoppingToken); }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested) { return; }
            catch (Exception error) { logger.LogError(error, "Roulette table maintenance failed."); }
        }
    }
}
