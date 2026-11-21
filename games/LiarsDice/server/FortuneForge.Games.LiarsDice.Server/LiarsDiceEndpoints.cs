using FortuneForge.Games.Abstractions.ManagedPlayers;
using FortuneForge.Games.Firestore;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.DependencyInjection;
using Grpc.Core;

namespace FortuneForge.Games.LiarsDice.Server;

public static class LiarsDiceEndpoints
{
    public static IEndpointRouteBuilder MapLiarsDice(this IEndpointRouteBuilder endpoints,
        Func<HttpContext, string?> resolveUserId, Func<HttpContext, string?> resolveBodySha256)
    {
        var api = endpoints.MapGroup("/api/games/liars-dice");
        api.MapGet("/status", (HttpContext context) => resolveUserId(context) is null
            ? Results.Json(new LiarsDiceErrorResponse("liars-dice-authentication-required", "Sign in to play Liar's Dice."), statusCode: 401)
            : Results.Ok(context.RequestServices.GetRequiredService<LiarsDiceGameService>().Status()));
        api.MapPost("/matches", (StartLiarsDiceMatchRequest? request, HttpContext context) =>
            Execute(context, (store, user, token) => store.StartAsync(user, request ?? new(null, null), token, resolveBodySha256(context))));
        api.MapGet("/matches/{matchId:guid}", (Guid matchId, HttpContext context) =>
            Execute(context, (store, user, token) => store.GetAsync(user, matchId, token)));
        api.MapPost("/matches/{matchId:guid}/bid", (Guid matchId, PlaceLiarsDiceBidRequest request, HttpContext context) =>
            Execute(context, (store, user, token) => store.BidAsync(user, matchId, request, token, resolveBodySha256(context))));
        api.MapPost("/matches/{matchId:guid}/challenge", (Guid matchId, HttpContext context) =>
            Execute(context, (store, user, token) => store.ChallengeAsync(user, matchId, token, resolveBodySha256(context))));
        api.MapPost("/matches/{matchId:guid}/spot-on", (Guid matchId, HttpContext context) =>
            Execute(context, (store, user, token) => store.SpotOnAsync(user, matchId, token, resolveBodySha256(context))));
        api.MapPost("/matches/{matchId:guid}/advance", (Guid matchId, HttpContext context) =>
            Execute(context, (store, user, token) => store.AdvanceAsync(user, matchId, token, resolveBodySha256(context))));
        api.MapPost("/matches/{matchId:guid}/next-round", (Guid matchId, NextLiarsDiceRoundRequest? request, HttpContext context) =>
            Execute(context, (store, user, token) => store.NextRoundAsync(user, matchId, request?.Seed, token, resolveBodySha256(context))));
        return endpoints;

        async Task<IResult> Execute(HttpContext context, Func<LiarsDiceGameService, string, CancellationToken, Task<LiarsDiceMatchResponse>> action)
        {
            context.Response.Headers.CacheControl = "no-store, max-age=0";
            if (resolveUserId(context) is not { } user)
                return Results.Json(new LiarsDiceErrorResponse("liars-dice-authentication-required", "Sign in to play Liar's Dice."), statusCode: 401);
            try { return Results.Ok(await action(context.RequestServices.GetRequiredService<LiarsDiceGameService>(), user, context.RequestAborted)); }
            catch (LiarsDiceAccessException exception) { return Results.NotFound(new LiarsDiceErrorResponse("liars-dice-match-not-found", exception.Message)); }
            catch (ManagedPlayerRosterUnavailableException) { return Unavailable(); }
            catch (LiarsDiceManagedPlayerSupplyException) { return Unavailable(); }
            catch (Exception exception) when (exception is LiarsDiceRuleException or ArgumentException)
            { return Results.BadRequest(new LiarsDiceErrorResponse("liars-dice-invalid-action", exception.Message)); }
            catch (Exception exception) when (exception is RpcException or FirestorePuzzleStorageException) { return Unavailable(); }
        }
        static IResult Unavailable() => Results.Json(new LiarsDiceErrorResponse("liars-dice-worker-unavailable",
            "Liar's Dice is temporarily unavailable."), statusCode: 503);
    }
}
