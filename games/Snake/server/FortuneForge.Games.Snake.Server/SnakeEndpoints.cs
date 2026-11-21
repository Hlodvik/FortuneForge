using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.DependencyInjection;
using Grpc.Core;

namespace FortuneForge.Games.Snake.Server;

public static class SnakeEndpoints
{
    public static IEndpointRouteBuilder MapSnake(this IEndpointRouteBuilder endpoints,
        Func<HttpContext, string?> resolveUserId, Func<HttpContext, string?> resolveBodySha256)
    {
        var api = endpoints.MapGroup("/api/games/snake");
        api.MapGet("/status", (HttpContext context) => resolveUserId(context) is null
            ? Results.Json(new SnakeErrorResponse("snake-authentication-required", "Sign in to play Snake."), statusCode: 401)
            : Results.Ok(context.RequestServices.GetRequiredService<FirestoreSnakeGameService>().Status()));
        api.MapPost("/games", (SnakeStartRequest? request, HttpContext context) =>
            Execute(context, (store, user, token) => store.StartAsync(user, request, token, resolveBodySha256(context))));
        api.MapGet("/games/{gameId:guid}", (Guid gameId, HttpContext context) =>
            Execute(context, (store, user, token) => store.GetAsync(user, gameId, token)));
        api.MapPost("/games/{gameId:guid}/turn", (Guid gameId, SnakeTurnRequest request, HttpContext context) =>
            Execute(context, (store, user, token) => store.TurnAsync(user, gameId, request.Direction, token, resolveBodySha256(context))));
        api.MapPost("/games/{gameId:guid}/tick", (Guid gameId, HttpContext context) =>
            Execute(context, (store, user, token) => store.TickAsync(user, gameId, token, resolveBodySha256(context))));
        api.MapPost("/games/{gameId:guid}/reset", (Guid gameId, SnakeStartRequest? request, HttpContext context) =>
            Execute(context, (store, user, token) => store.ResetAsync(user, gameId, request, token, resolveBodySha256(context))));
        return endpoints;

        async Task<IResult> Execute(HttpContext context, Func<FirestoreSnakeGameService, string, CancellationToken, Task<SnakeGameResponse>> action)
        {
            context.Response.Headers.CacheControl = "no-store, max-age=0";
            if (resolveUserId(context) is not { } user)
                return Results.Json(new SnakeErrorResponse("snake-authentication-required", "Sign in to play Snake."), statusCode: 401);
            try { return Results.Ok(await action(context.RequestServices.GetRequiredService<FirestoreSnakeGameService>(), user, context.RequestAborted)); }
            catch (SnakeAccessException exception) { return Results.NotFound(new SnakeErrorResponse("snake-game-not-found", exception.Message)); }
            catch (Exception exception) when (exception is SnakeRuleException or ArgumentException or InvalidOperationException)
            { return Results.BadRequest(new SnakeErrorResponse("snake-invalid-action", exception.Message)); }
            catch (RpcException) { return Results.Json(new SnakeErrorResponse("snake-worker-unavailable", "Snake is temporarily unavailable."), statusCode: 503); }
        }
    }
}
