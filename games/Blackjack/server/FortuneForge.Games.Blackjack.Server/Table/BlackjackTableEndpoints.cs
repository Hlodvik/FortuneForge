using FortuneForge.Games.Abstractions.ManagedPlayers;
using FortuneForge.Games.Abstractions.Wallet;
using FortuneForge.Games.Hosting;
using FortuneForge.Server.Bots.Blackjack;
using Google.Cloud.Firestore;
using Grpc.Core;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace FortuneForge.Server.Cards.Blackjack.Table;

public static class BlackjackTableEndpoints
{
    public static IServiceCollection AddBlackjackTableBackend(this IServiceCollection services)
    {
        services.AddManagedPlayerRosterClient("blackjack");
        services.AddSingleton<IManagedTablePopulationDirector, ManagedTablePopulationDirector>();
        services.AddSingleton<IBlackjackTableStore>(provider => new FirestoreBlackjackTableStore(
            provider.GetRequiredService<FirestoreDb>(), provider.GetRequiredService<IGameWalletClient>(),
            provider.GetRequiredService<IGameWalletAccountReader>(), provider.GetRequiredService<IManagedPlayerRosterClient>(),
            managedPopulationDirector: provider.GetRequiredService<IManagedTablePopulationDirector>(),
            multiplayerEnabled: provider.GetRequiredService<IConfiguration>().GetValue("Features:BlackjackMultiplayerEnabled", false),
            allowNewRounds: () => provider.GetRequiredService<IConfiguration>().GetValue("Features:BlackjackTableEnabled", false),
            logger: provider.GetService<ILogger<FirestoreBlackjackTableStore>>()));
        services.AddSingleton<BlackjackTableService>();
        if (services.Any(service => service.ServiceType == typeof(FirestoreDb)))
            services.AddHostedService<BlackjackTableDeadlines>();
        return services;
    }

    public static IEndpointRouteBuilder MapBlackjackTable(this IEndpointRouteBuilder endpoints, Func<HttpContext, string?> account)
    {
        var api = endpoints.MapGroup("/api/cards/blackjack/table")
            .WithMetadata(new GameWorkerPublicRoute("blackjack", "/api/cards/blackjack/table", "blackjack"));
        api.MapGet("/status", IResult (HttpContext context, IConfiguration configuration) => account(context) is null
            ? Results.Unauthorized() : Results.Ok(Status(configuration.GetValue("Features:BlackjackMultiplayerEnabled", false))));
        api.MapGet("/session", new Func<HttpContext, Task<IResult>>(context => Execute(context, account,
            async (service, user, token) => Results.Ok((await service.GetSessionAsync(user, token)).Session))));
        api.MapGet("/snapshot", new Func<HttpContext, Task<IResult>>(context => Execute(context, account,
            async (service, user, token) => Results.Ok(Mutation(await service.GetSessionAsync(user, token))))));
        api.MapGet("/history", (HttpContext context, int? limit) => Execute(context, account,
            async (service, user, token) => Results.Ok(await service.GetHistoryAsync(user, limit ?? 20, token))));
        api.MapPost("/history/{resultId}/seen", (string resultId, HttpContext context) => Execute(context, account,
            async (service, user, token) => Results.Ok(await service.MarkHistorySeenAsync(user, resultId, token))));
        api.MapPost("/queue", (JoinBlackjackTableQueueRequest request, HttpContext context) => Execute(context, account,
            async (service, user, token) =>
            {
                var key = Key(context);
                BlackjackTableService.ValidateJoin(request, key);
                if (context.RequestServices.GetRequiredService<IBlackjackTableStore>() is FirestoreBlackjackTableStore store &&
                    await store.TryReplayJoinAsync(user, request.ExpectedVersion, key, DateTime.UtcNow, token) is { } replay)
                    return Results.Ok(Mutation(replay));
                var profile = await context.RequestServices.GetRequiredService<IGameWalletAccountReader>().GetStateAsync(user, token);
                if (!GameWalletAccountState.IsValidPlayerName(profile.PlayerName)) throw new GameWalletUnavailableException();
                return Results.Ok(Mutation(await service.JoinAsync(user, profile.PlayerName!, request, key, token)));
            }));
        api.MapDelete("/queue/{ticketId}", (string ticketId, [FromBody] BlackjackTableVersionRequest request, HttpContext context) => Execute(context, account,
            async (service, user, token) => Results.Ok(Mutation(await service.CancelAsync(user, ticketId, request, Key(context), token)))));
        api.MapPost("/tables/{tableId}/wagers", (string tableId, BlackjackTableWagerRequest request, HttpContext context) => Execute(context, account,
            async (service, user, token) => Results.Ok(Mutation(await service.WagerAsync(user, tableId, request, Key(context), token)))));
        api.MapPost("/tables/{tableId}/sit-out", (string tableId, BlackjackTableVersionRequest request, HttpContext context) => Execute(context, account,
            async (service, user, token) => Results.Ok(Mutation(await service.SitOutAsync(user, tableId, request, Key(context), token)))));
        api.MapPost("/tables/{tableId}/actions", (string tableId, BlackjackTableActionRequest request, HttpContext context) => Execute(context, account,
            async (service, user, token) => Results.Ok(Mutation(await service.ActionAsync(user, tableId, request, Key(context), token)))));
        api.MapPost("/tables/{tableId}/leave", (string tableId, BlackjackTableVersionRequest request, HttpContext context) => Execute(context, account,
            async (service, user, token) => Results.Ok(Mutation(await service.LeaveAsync(user, tableId, request, Key(context), token)))));
        return endpoints;
    }

    public static BlackjackTableStatusResponse Status(bool multiplayer) => new(true,
        BlackjackMoney.ToRand(BlackjackMoney.MinimumWagerCents), BlackjackMoney.ToRand(BlackjackMoney.MaximumWagerCents),
        BlackjackMoney.ToRand(BlackjackMoney.WagerIncrementCents), multiplayer ? BlackjackManagedTablePolicy.MinimumStartOccupancy : 1,
        multiplayer ? BlackjackTableEngine.Capacity : 1, multiplayer ? (int)BlackjackTableEngine.HumanGrace.TotalSeconds : 0,
        multiplayer ? (int)BlackjackTableEngine.ActionDuration.TotalSeconds : 0, 1, "Dealer stands on all 17s", "3:2", true, true, true, true);

    private static string Key(HttpContext context) => context.Request.Headers["Idempotency-Key"].ToString();
    private static BlackjackTableMutationResponse Mutation(BlackjackTableStoreResult result) => new(result.Session, BlackjackMoney.ToRand(result.BalanceCents));
    private static async Task<IResult> Execute(HttpContext context, Func<HttpContext, string?> account,
        Func<BlackjackTableService, string, CancellationToken, Task<IResult>> action)
    {
        context.Response.Headers.CacheControl = "no-store, max-age=0";
        if (account(context) is not { } user) return Results.Unauthorized();
        try { return await action(context.RequestServices.GetRequiredService<BlackjackTableService>(), user, context.RequestAborted); }
        catch (BlackjackTableNotFoundException error) { return Results.NotFound(new { error = error.Message }); }
        catch (BlackjackTableInsufficientCreditsException error)
        { return Results.Conflict(new { code = "insufficient-slot-credits", error = error.Message, error.Available, error.Required }); }
        catch (BlackjackTableConflictException error)
        { return Results.Conflict(new { code = "blackjack-table-state-conflict", error = error.Message }); }
        catch (BlackjackTableIllegalActionException error)
        { return Results.BadRequest(new { code = "illegal-blackjack-action", error = error.Message }); }
        catch (BlackjackAdmissionPausedException error)
        { return Results.Json(new { code = "blackjack-table-disabled", error = error.Message }, statusCode: 503); }
        catch (ArgumentException error) { return Results.BadRequest(new { error = error.Message }); }
        catch (Exception error) when (error is GameWalletUnavailableException or GameWalletConflictException or ManagedPlayerRosterUnavailableException or RpcException)
        { return Results.Json(new { code = "game-worker-unavailable", error = "Retry the original Blackjack table request." }, statusCode: 503); }
    }

    private sealed class BlackjackTableDeadlines(IBlackjackTableStore store, ILogger<BlackjackTableDeadlines> logger) : BackgroundService
    {
        protected override async Task ExecuteAsync(CancellationToken stoppingToken)
        {
            using var timer = new PeriodicTimer(TimeSpan.FromMilliseconds(500));
            while (await timer.WaitForNextTickAsync(stoppingToken))
            {
                try { await store.SweepAsync(DateTime.UtcNow, stoppingToken); }
                catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested) { return; }
                catch (Exception error) { logger.LogWarning(error, "Blackjack table recovery/deadline sweep failed."); }
            }
        }
    }
}
