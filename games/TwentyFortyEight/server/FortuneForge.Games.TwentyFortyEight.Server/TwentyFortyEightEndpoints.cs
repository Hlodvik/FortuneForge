using FortuneForge.Games.Firestore;
using Grpc.Core;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace FortuneForge.Games.TwentyFortyEight.Server;

public static class TwentyFortyEightEndpoints
{
    public static IEndpointRouteBuilder MapTwentyFortyEight(this IEndpointRouteBuilder endpoints,
        Func<HttpContext, string?> resolveUserId, Func<HttpContext, string?> resolveBodySha256)
    {
        var api = endpoints.MapGroup("/api/games/" + TwentyFortyEightSession.GameId);
        api.MapGet("/status", (HttpContext context) =>
            resolveUserId(context) is not null
                ? Results.Ok(TwentyFortyEightSession.Status())
                : Results.Json(TwentyFortyEightErrors.AuthenticationRequired(), statusCode: 401));
        api.MapPost("/games", (StartTwentyFortyEightRequest? request, HttpContext context) =>
            ExecuteAsync(context, resolveUserId, (store, userId, token) => store.StartAsync(userId, request?.Seed, token, resolveBodySha256(context))));
        api.MapGet("/games/{gameId:guid}", (Guid gameId, HttpContext context) =>
            ExecuteAsync(context, resolveUserId, (store, userId, token) => store.GetAsync(userId, gameId, token)));
        api.MapPost("/games/{gameId:guid}/move", (Guid gameId, TwentyFortyEightMoveRequest request, HttpContext context) =>
            ExecuteAsync(context, resolveUserId, (store, userId, token) => store.MoveAsync(userId, gameId, request.Direction, token, resolveBodySha256(context))));
        api.MapPost("/games/{gameId:guid}/undo", (Guid gameId, HttpContext context) =>
            ExecuteAsync(context, resolveUserId, (store, userId, token) => store.UndoAsync(userId, gameId, token, resolveBodySha256(context))));
        api.MapPost("/games/{gameId:guid}/continue", (Guid gameId, HttpContext context) =>
            ExecuteAsync(context, resolveUserId, (store, userId, token) => store.ContinueAsync(userId, gameId, token, resolveBodySha256(context))));
        api.MapPost("/games/{gameId:guid}/reset", (Guid gameId, StartTwentyFortyEightRequest? request, HttpContext context) =>
            ExecuteAsync(context, resolveUserId, (store, userId, token) => store.ResetAsync(userId, gameId, request?.Seed, token, resolveBodySha256(context))));
        return endpoints;
    }

    private static async Task<IResult> ExecuteAsync(HttpContext context, Func<HttpContext, string?> resolveUserId,
        Func<TwentyFortyEightFirestoreSessions, string, CancellationToken, Task<TwentyFortyEightGameResponse>> action)
    {
        context.Response.Headers.CacheControl = "no-store, max-age=0";
        if (resolveUserId(context) is not { } userId)
            return Results.Json(TwentyFortyEightErrors.AuthenticationRequired(), statusCode: 401);

        try
        {
            var store = context.RequestServices.GetRequiredService<TwentyFortyEightFirestoreSessions>();
            return Results.Ok(await action(store, userId, context.RequestAborted));
        }
        catch (TwentyFortyEightAccessException exception)
        {
            return Results.NotFound(TwentyFortyEightErrors.GameNotFound(exception));
        }
        catch (Exception exception) when (exception is TwentyFortyEightRuleException or ArgumentException)
        {
            return Results.BadRequest(TwentyFortyEightErrors.InvalidAction(exception.Message));
        }
        catch (Exception exception) when (exception is RpcException or FirestorePuzzleStorageException)
        {
            context.RequestServices.GetRequiredService<ILoggerFactory>().CreateLogger(nameof(TwentyFortyEightEndpoints))
                .LogWarning("2048 session request failed ({ErrorType}).", exception.GetType().Name);
            return Results.Json(TwentyFortyEightErrors.WorkerUnavailable(), statusCode: 503);
        }
    }
}
