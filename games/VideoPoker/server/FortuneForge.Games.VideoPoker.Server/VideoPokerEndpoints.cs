using FortuneForge.Games.Abstractions.Wallet;
using Grpc.Core;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace FortuneForge.Server.Cards.VideoPoker;

public static class VideoPokerEndpoints
{
    public static IEndpointRouteBuilder MapVideoPoker(this IEndpointRouteBuilder endpoints, Func<HttpContext, string?> resolveUserId)
    {
        var api = endpoints.MapGroup("/api/games/video-poker");
        api.MapGet("/status", (Func<HttpContext, Task<IResult>>)(context => Run(context, resolveUserId, async (user, token) =>
        {
            var state = await context.RequestServices.GetRequiredService<IGameWalletAccountReader>().GetStateAsync(user, token);
            if (state.SlotsCreditsBalanceCents is not >= 0) throw new GameWalletUnavailableException();
            var available = context.RequestServices.GetRequiredService<IConfiguration>().GetValue("Features:VideoPokerEnabled", true);
            return Results.Ok(new VideoPokerStatusResponse(available, VideoPokerMoney.MinimumCoinsWagered,
                VideoPokerMoney.MaximumCoinsWagered, VideoPokerMoney.ToRand(VideoPokerMoney.CoinValueCents),
                VideoPokerMoney.ToRand(state.SlotsCreditsBalanceCents.Value), [1, 3, 5]));
        })));
        api.MapPost("/rounds", (CreateVideoPokerRoundRequest request, HttpContext context) => Run(context, resolveUserId, async (user, token) =>
        {
            var store = context.RequestServices.GetRequiredService<VideoPokerFirestoreStore>();
            var key = context.Request.Headers["Idempotency-Key"].ToString();
            VideoPokerService.ValidateIdempotencyKey(key);
            if (!context.RequestServices.GetRequiredService<IConfiguration>().GetValue("Features:VideoPokerEnabled", true) &&
                !await store.HasStartAsync(user, key, token))
                return Results.Json(new VideoPokerErrorResponse("video-poker-disabled", "Video Poker cannot accept a new wager yet."), statusCode: 503);
            var result = await Service(context).StartAsync(user, request, key, token);
            return Results.Created($"/api/games/video-poker/rounds/{result.RoundId}", result);
        }));
        api.MapGet("/rounds/{roundId}", (string roundId, HttpContext context) => Run(context, resolveUserId,
            async (user, token) => Results.Ok(await Service(context).GetAsync(user, roundId, token))));
        api.MapPost("/rounds/{roundId}/draw", (string roundId, DrawVideoPokerRoundRequest request, HttpContext context) => Run(context, resolveUserId,
            async (user, token) => Results.Ok(await Service(context).DrawAsync(user, roundId, request,
                context.Request.Headers["Idempotency-Key"].ToString(), token))));
        api.MapGet("/history", (int? limit, HttpContext context) => Run(context, resolveUserId,
            async (user, token) => Results.Ok(await context.RequestServices.GetRequiredService<VideoPokerFirestoreStore>()
                .HistoryAsync(user, limit ?? 20, token))));
        return endpoints;
    }

    private static VideoPokerService Service(HttpContext context) => new(
        context.RequestServices.GetRequiredService<VideoPokerFirestoreStore>(),
        context.RequestServices.GetRequiredService<TimeProvider>());

    private static async Task<IResult> Run(HttpContext context, Func<HttpContext, string?> resolveUserId,
        Func<string, CancellationToken, Task<IResult>> action)
    {
        context.Response.Headers.CacheControl = "no-store, max-age=0";
        if (resolveUserId(context) is not { Length: > 0 } userId)
            return Results.Json(new VideoPokerErrorResponse("video-poker-authentication-required", "Sign in to play Video Poker."), statusCode: 401);
        try { return await action(userId, context.RequestAborted); }
        catch (VideoPokerRoundNotFoundException exception) { return Error(404, "video-poker-round-not-found", exception.Message); }
        catch (VideoPokerRoundConflictException exception) { return Error(409, "video-poker-round-conflict", exception.Message); }
        catch (VideoPokerInsufficientCreditsException exception) { return Error(409, "insufficient-slot-credits", exception.Message); }
        catch (ArgumentException exception) { return Error(400, "video-poker-invalid-request", exception.Message); }
        catch (Exception exception) when (exception is GameWalletUnavailableException or GameWalletConflictException or RpcException)
        {
            return Error(503, "video-poker-unavailable", "The game service could not confirm the action. Retry its original operation ID.");
        }
    }

    private static IResult Error(int status, string code, string message) => Results.Json(new VideoPokerErrorResponse(code, message), statusCode: status);
}
