using FortuneForge.Games.Abstractions.ManagedPlayers;
using FortuneForge.Games.Abstractions.Wallet;
using FortuneForge.Games.Hosting;
using FortuneForge.Server.Cards.ThreeCardPoker.Table;
using Grpc.Core;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace FortuneForge.Games.ThreeCardPoker.Server;

public static class ThreeCardPokerEndpoints
{
    public static IEndpointRouteBuilder MapThreeCardPoker(this IEndpointRouteBuilder endpoints, Func<HttpContext, string?> resolveUserId)
    {
        var api = endpoints.MapGroup(ThreeCardPokerBackend.PublicPath)
            .WithMetadata(new GameWorkerPublicRoute(ThreeCardPokerBackend.GameId, ThreeCardPokerBackend.PublicPath, ThreeCardPokerBackend.GameId));
        api.MapGet("/status", (Func<HttpContext, Task<IResult>>)(context => Run(context, resolveUserId,
            (_, _) => Task.FromResult<IResult>(Results.Ok(ThreeCardPokerBackend.StatusContract(Enabled(context)))))));
        api.MapGet("/session", (Func<HttpContext, Task<IResult>>)(context => Run(context, resolveUserId,
            async (user, token) => Results.Ok((await Service(context).GetSessionAsync(user, token)).Session))));
        api.MapGet("/snapshot", (Func<HttpContext, Task<IResult>>)(context => Run(context, resolveUserId,
            async (user, token) => Results.Ok(Mutation(await Service(context).GetSessionAsync(user, token))))));
        api.MapGet("/history", (int? limit, HttpContext context) => Run(context, resolveUserId,
            async (user, token) => Results.Ok(await Service(context).GetHistoryAsync(user, limit ?? 20, token))));
        api.MapPost("/history/{resultId}/seen", (string resultId, HttpContext context) => Run(context, resolveUserId,
            async (user, token) => Results.Ok(await Service(context).MarkHistorySeenAsync(user, resultId, token))));
        api.MapPost("/queue", (JoinThreeCardPokerTableQueueRequest request, HttpContext context) => Run(context, resolveUserId,
            async (user, token) =>
            {
                var key = Key(context);
                ThreeCardPokerTableService.ValidateJoinRequest(request, key);
                if (await Store(context).TryReplayJoinAsync(user, key, request.ExpectedVersion, token) is { } prior)
                    return Results.Ok(Mutation(prior));
                if (!Enabled(context)) return Paused();
                var profile = await context.RequestServices.GetRequiredService<IGameWalletAccountReader>().GetStateAsync(user, token);
                if (!GameWalletAccountState.IsValidPlayerName(profile.PlayerName)) throw new GameWalletUnavailableException();
                return Results.Ok(Mutation(await Service(context).JoinAsync(user, profile.PlayerName!, request, key, token)));
            }));
        api.MapDelete("/queue/{ticketId}", (string ticketId, [FromBody] ThreeCardPokerTableVersionRequest request, HttpContext context) =>
            Run(context, resolveUserId, async (user, token) => Results.Ok(Mutation(await Service(context).CancelAsync(user, ticketId, request, Key(context), token)))));
        api.MapPost("/tables/{tableId}/wagers", (string tableId, ThreeCardPokerTableWagerRequest request, HttpContext context) => Run(context, resolveUserId,
            async (user, token) =>
            {
                var key = Key(context);
                if (!Enabled(context) && !await Store(context).HasCommandAsync(user, key, token)) return Paused();
                return Results.Ok(Mutation(await Service(context).WagerAsync(user, tableId, request, key, token)));
            }));
        api.MapPost("/tables/{tableId}/actions", (string tableId, ThreeCardPokerTableActionRequest request, HttpContext context) => Run(context, resolveUserId,
            async (user, token) => Results.Ok(Mutation(await Service(context).ActionAsync(user, tableId, request, Key(context), token)))));
        api.MapPost("/tables/{tableId}/sit-out", (string tableId, ThreeCardPokerTableVersionRequest request, HttpContext context) => Run(context, resolveUserId,
            async (user, token) => Results.Ok(Mutation(await Service(context).SitOutAsync(user, tableId, request, Key(context), token)))));
        api.MapPost("/tables/{tableId}/leave", (string tableId, ThreeCardPokerTableVersionRequest request, HttpContext context) => Run(context, resolveUserId,
            async (user, token) => Results.Ok(Mutation(await Service(context).LeaveAsync(user, tableId, request, Key(context), token)))));
        return endpoints;
    }

    private static async Task<IResult> Run(HttpContext context, Func<HttpContext, string?> resolveUserId, Func<string, CancellationToken, Task<IResult>> action)
    {
        context.Response.Headers.CacheControl = "no-store, max-age=0";
        if (resolveUserId(context) is not { Length: > 0 } user) return Results.Json(new { error = "Sign in to play Three Card Poker table mode." }, statusCode: 401);
        try { return await action(user, context.RequestAborted); }
        catch (ThreeCardPokerTableNotFoundException error) { return Results.NotFound(new { error = error.Message }); }
        catch (ThreeCardPokerTableInsufficientCreditsException error)
        { return Results.Conflict(new { code = "insufficient-slot-credits", error = error.Message, available = error.Available, required = error.Required }); }
        catch (ThreeCardPokerTableConflictException error) { return Results.Conflict(new { code = "three-card-poker-table-state-conflict", error = error.Message }); }
        catch (ThreeCardPokerTableIllegalActionException error) { return Results.BadRequest(new { code = "illegal-three-card-poker-action", error = error.Message }); }
        catch (ArgumentException error) { return Results.BadRequest(new { error = error.Message }); }
        catch (Exception error) when (error is GameWalletUnavailableException or GameWalletConflictException or ManagedPlayerRosterUnavailableException or RpcException)
        { return Results.Json(new { code = "game-worker-unavailable", error = "The Three Card Poker service could not confirm the action. Retry its original operation ID." }, statusCode: 503); }
    }

    private static bool Enabled(HttpContext context) => context.RequestServices.GetRequiredService<IConfiguration>().GetValue("Features:ThreeCardPokerTableEnabled", false);
    private static IResult Paused() => Results.Json(new { code = "three-card-poker-table-disabled", error = "Three Card Poker table mode is currently unavailable." }, statusCode: 503);
    private static ThreeCardPokerTableService Service(HttpContext context) => context.RequestServices.GetRequiredService<ThreeCardPokerTableService>();
    private static FirestoreThreeCardPokerTableStore Store(HttpContext context) => context.RequestServices.GetRequiredService<FirestoreThreeCardPokerTableStore>();
    private static string Key(HttpContext context)
    {
        var key = context.Request.Headers["Idempotency-Key"].ToString();
        ThreeCardPokerTableService.ValidateIdempotencyKey(key);
        return key;
    }
    private static ThreeCardPokerTableMutationResponse Mutation(ThreeCardPokerTableStoreResult result) => new(result.Session, ThreeCardPokerMoney.ToRand(result.BalanceCents));
}
