using FortuneForge.Games.Firestore;
using Grpc.Core;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace FortuneForge.Games.DropMerge.Server;

public static class DropMergeEndpoints
{
    public static IEndpointRouteBuilder MapDropMerge(this IEndpointRouteBuilder endpoints,
        Func<HttpContext, string?> resolveUserId, Func<HttpContext, string?> resolveBodySha256)
    {
        var api = endpoints.MapGroup("/api/games/" + DropMergeSession.GameId);
        api.MapGet("/status", (HttpContext context) =>
            resolveUserId(context) is not null
                ? Results.Ok(DropMergeSession.Status())
                : Results.Json(DropMergeErrors.AuthenticationRequired(), statusCode: 401));
        api.MapPost("/games", (StartDropMergeRequest? request, HttpContext context) =>
            ExecuteAsync(context, resolveUserId, (store, userId, token) => store.StartAsync(userId, request?.Seed, token, resolveBodySha256(context))));
        api.MapGet("/games/{gameId:guid}", (Guid gameId, HttpContext context) =>
            ExecuteAsync(context, resolveUserId, (store, userId, token) => store.GetAsync(userId, gameId, token)));
        api.MapPost("/games/{gameId:guid}/drop", (Guid gameId, DropMergeDropRequest request, HttpContext context) =>
            ExecuteAsync(context, resolveUserId, (store, userId, token) => store.DropAsync(userId, gameId, request.Column, token, resolveBodySha256(context))));
        api.MapPost("/games/{gameId:guid}/undo", (Guid gameId, HttpContext context) =>
            ExecuteAsync(context, resolveUserId, (store, userId, token) => store.UndoAsync(userId, gameId, token, resolveBodySha256(context))));
        api.MapPost("/games/{gameId:guid}/reset", (Guid gameId, StartDropMergeRequest? request, HttpContext context) =>
            ExecuteAsync(context, resolveUserId, (store, userId, token) => store.ResetAsync(userId, gameId, request?.Seed, token, resolveBodySha256(context))));
        return endpoints;
    }

    private static async Task<IResult> ExecuteAsync(HttpContext context, Func<HttpContext, string?> resolveUserId,
        Func<DropMergeFirestoreSessions, string, CancellationToken, Task<DropMergeGameResponse>> action)
    {
        context.Response.Headers.CacheControl = "no-store, max-age=0";
        if (resolveUserId(context) is not { } userId)
            return Results.Json(DropMergeErrors.AuthenticationRequired(), statusCode: 401);

        try
        {
            var store = context.RequestServices.GetRequiredService<DropMergeFirestoreSessions>();
            return Results.Ok(await action(store, userId, context.RequestAborted));
        }
        catch (DropMergeAccessException exception)
        {
            return Results.NotFound(DropMergeErrors.GameNotFound(exception));
        }
        catch (Exception exception) when (exception is DropMergeRuleException or ArgumentException)
        {
            return Results.BadRequest(DropMergeErrors.InvalidAction(exception.Message));
        }
        catch (Exception exception) when (exception is RpcException or FirestorePuzzleStorageException)
        {
            context.RequestServices.GetRequiredService<ILoggerFactory>().CreateLogger(nameof(DropMergeEndpoints))
                .LogWarning("Drop Merge session request failed ({ErrorType}).", exception.GetType().Name);
            return Results.Json(DropMergeErrors.WorkerUnavailable(), statusCode: 503);
        }
    }
}
