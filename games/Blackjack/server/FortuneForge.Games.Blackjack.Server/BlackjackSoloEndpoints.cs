using FortuneForge.Games.Abstractions.Wallet;
using FortuneForge.Games.Hosting;
using Google.Cloud.Firestore;
using Grpc.Core;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Configuration;

namespace FortuneForge.Server.Cards.Blackjack;

public static class BlackjackSoloEndpoints
{
    public static IServiceCollection AddBlackjackSolo(this IServiceCollection services)
    {
        services.AddGameWalletClient("blackjack");
        services.AddTransient(provider => new BlackjackSoloStore(provider.GetRequiredService<FirestoreDb>(),
            provider.GetRequiredService<IGameWalletClient>(), provider.GetRequiredService<IGameWalletAccountReader>(),
            () => provider.GetRequiredService<IConfiguration>().GetValue("Features:BlackjackEnabled", false)));
        services.AddTransient<IBlackjackStore>(provider => provider.GetRequiredService<BlackjackSoloStore>());
        services.AddTransient<BlackjackService>();
        return services;
    }

    public static IEndpointRouteBuilder MapBlackjackSolo(this IEndpointRouteBuilder endpoints, Func<HttpContext, string?> account)
    {
        var group = endpoints.MapGroup("/api/cards/blackjack")
            .WithMetadata(new GameWorkerPublicRoute("blackjack", "/api/cards/blackjack", "blackjack"));
        group.MapGet("/status", IResult (HttpContext context) => account(context) is null
            ? Results.Unauthorized()
            : Results.Ok(new BlackjackStatusResponse(true, BlackjackMoney.ToRand(BlackjackMoney.MinimumWagerCents),
                BlackjackMoney.ToRand(BlackjackMoney.MaximumWagerCents), BlackjackMoney.ToRand(BlackjackMoney.WagerIncrementCents),
                "Dealer stands on all 17s", "3:2", true, false, false)));
        group.MapPost("/games", (BlackjackStartRequest request, HttpContext context) => Execute(context, account,
            async (service, user, token) =>
            {
                var response = await service.StartAsync(user, request, Key(context), token);
                return Results.Created($"/api/cards/blackjack/games/{response.GameId}", response);
            }));
        group.MapGet("/games/{gameId}", (string gameId, HttpContext context) => Execute(context, account,
            async (service, user, token) => Results.Ok(await service.GetAsync(user, gameId, token))));
        group.MapPost("/games/{gameId}/actions", (string gameId, BlackjackActionRequest request, HttpContext context) => Execute(context, account,
            async (service, user, token) => Results.Ok(await service.ActAsync(user, gameId, request, Key(context), token))));
        return endpoints;
    }

    private static string Key(HttpContext context) => context.Request.Headers["Idempotency-Key"].ToString();
    private static async Task<IResult> Execute(HttpContext context, Func<HttpContext, string?> account,
        Func<BlackjackService, string, CancellationToken, Task<IResult>> action)
    {
        context.Response.Headers.CacheControl = "no-store, max-age=0";
        if (account(context) is not { } user) return Results.Unauthorized();
        try
        {
            return await action(context.RequestServices.GetRequiredService<BlackjackService>(), user, context.RequestAborted);
        }
        catch (BlackjackNotFoundException error) { return Results.NotFound(new { error = error.Message }); }
        catch (BlackjackAdmissionPausedException error)
        { return Results.Json(new { code = "blackjack-disabled", error = error.Message }, statusCode: 503); }
        catch (BlackjackInsufficientCreditsException error)
        { return Results.Conflict(new { code = "insufficient-slot-credits", error = error.Message, error.Available, error.Required }); }
        catch (BlackjackConflictException error)
        { return Results.Conflict(new { code = "blackjack-state-conflict", error = error.Message }); }
        catch (GameWalletConflictException error)
        { return Results.Conflict(new { code = "blackjack-state-conflict", error = error.Message }); }
        catch (ArgumentException error) { return Results.BadRequest(new { error = error.Message }); }
        catch (Exception error) when (error is GameWalletUnavailableException or RpcException)
        { return Results.Json(new { code = "game-worker-unavailable", error = "Retry the original Blackjack operation." }, statusCode: 503); }
    }
}
