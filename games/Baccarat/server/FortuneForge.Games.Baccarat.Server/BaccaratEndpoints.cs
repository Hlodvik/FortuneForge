using System.Text.Json;
using FortuneForge.Games.Abstractions.Wallet;
using FortuneForge.Games.Hosting;
using Google.Cloud.Firestore;
using Grpc.Core;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace FortuneForge.Server.Cards.Baccarat;

public sealed class BaccaratUnavailableException() : Exception("Baccarat is not accepting new wagers.");

public static class BaccaratEndpoints
{
    public static IServiceCollection AddBaccaratServer(this IServiceCollection services)
    {
        services.AddTransient(provider => new BaccaratFirestoreStore(provider.GetRequiredService<FirestoreDb>(),
            provider.GetRequiredService<IGameWalletClient>(), provider.GetRequiredService<IGameWalletAccountReader>(),
            () => provider.GetRequiredService<IConfiguration>().GetValue("Features:BaccaratEnabled", false)));
        return services;
    }

    public static IEndpointRouteBuilder MapBaccarat(this IEndpointRouteBuilder endpoints, Func<HttpContext, string?> accountId)
    {
        var api = endpoints.MapGroup("/api/games/baccarat");
        api.AddEndpointFilter(async (context, next) =>
        {
            context.HttpContext.Response.Headers.CacheControl = "no-store";
            if (accountId(context.HttpContext) is null)
                return Error(401, "baccarat-authentication-required", "Sign in to play Baccarat.");
            try { return await next(context); }
            catch (BaccaratRoundNotFoundException error) { return Error(404, "baccarat-round-not-found", error.Message); }
            catch (BaccaratRoundConflictException error) { return Error(409, "baccarat-round-conflict", error.Message); }
            catch (GameWalletConflictException error) { return Error(409, "baccarat-round-conflict", error.Message); }
            catch (BaccaratInsufficientCreditsException error) { return Error(409, "insufficient-slot-credits", error.Message); }
            catch (BaccaratUnavailableException error) { return Error(503, "baccarat-disabled", error.Message); }
            catch (Exception error) when (error is ArgumentException or JsonException or BadHttpRequestException)
            { return Error(400, "baccarat-invalid-request", error.Message); }
            catch (InvalidOperationException)
            { return Error(503, "baccarat-recovery-required", "The saved Baccarat state could not be verified. Retry the original wager."); }
            catch (Exception error) when (error is GameWalletUnavailableException || error is RpcException rpc && rpc.StatusCode is StatusCode.Aborted or StatusCode.DeadlineExceeded or StatusCode.Unavailable)
            {
                context.HttpContext.Response.Headers.RetryAfter = "1";
                return Error(503, "baccarat-retry-required", "Retry the original wager to recover its saved result.");
            }
        });
        api.MapGet("/status", async (HttpContext context, IGameWalletAccountReader wallet, IConfiguration configuration, CancellationToken token) =>
        {
            var account = await wallet.GetStateAsync(accountId(context)!, token);
            return Results.Ok(new BaccaratStatusResponse(configuration.GetValue("Features:BaccaratEnabled", false),
                0.25m, 100m, 0.25m, BaccaratMoney.ToRand(account.SlotsCreditsBalanceCents ?? throw new GameWalletUnavailableException()),
                "eight-deck-punto-banco", true));
        });
        api.MapPost("/rounds", async (HttpContext context, CreateBaccaratRoundRequest request, BaccaratFirestoreStore store, CancellationToken token) =>
        {
            var result = await new BaccaratService(store, TimeProvider.System).StartAsync(accountId(context)!, request,
                context.Request.Headers["Idempotency-Key"].ToString(), token);
            return Results.Created($"/api/games/baccarat/rounds/{result.RoundId}", result);
        });
        api.MapGet("/rounds/latest", async (HttpContext context, BaccaratFirestoreStore store, CancellationToken token) =>
            await store.LatestAsync(accountId(context)!, token) is { } round ? Results.Ok(BaccaratService.ToResponse(round)) : Results.NoContent());
        api.MapGet("/rounds/{roundId}", async (HttpContext context, string roundId, BaccaratFirestoreStore store, CancellationToken token) =>
            Results.Ok(await new BaccaratService(store, TimeProvider.System).GetAsync(accountId(context)!, roundId, token)));
        return endpoints;
    }
    private static IResult Error(int status, string code, string message) => Results.Json(new BaccaratErrorResponse(code, message), statusCode: status);
}
