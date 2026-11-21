using FortuneForge.Games.Abstractions.Wallet;
using FortuneForge.Games.Craps;
using FortuneForge.Server.Games.Craps;
using Google.Cloud.Firestore;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace FortuneForge.Games.Craps.Server;

public static class CrapsWorkerEndpoints
{
    public static void AddCrapsWorker(this IServiceCollection services, IConfiguration configuration)
    {
        services.AddSingleton(provider => new FirestorePaidCrapsStore(
            provider.GetRequiredService<FirestoreDb>(),
            provider.GetRequiredService<IGameWalletClient>(),
            provider.GetRequiredService<IGameWalletAccountReader>(),
            acceptsNewRounds: () => configuration.GetValue("Features:CrapsEnabled", false)));
    }

    public static void MapCrapsWorker(this IEndpointRouteBuilder endpoints, Func<HttpContext, string?> resolveUserId)
    {
        Task<IResult> WithAccount(HttpContext context, Func<string, Task<IResult>> action) =>
            resolveUserId(context) is { } userId
                ? action(userId)
                : Task.FromResult(Results.Json(
                    new CrapsErrorResponse("craps-authentication-required", "Sign in to play Craps."),
                    statusCode: StatusCodes.Status401Unauthorized));

        var routes = endpoints.MapGroup("/api/games/craps");
        routes.AddEndpointFilter(async (context, next) =>
        {
            var http = context.HttpContext;
            http.Response.Headers.CacheControl = "no-store";
            try { return await next(context); }
            catch (GameWalletUnavailableException)
            {
                http.Response.Headers.RetryAfter = "1";
                return Results.Json(new CrapsErrorResponse("craps-retry-required", "Retry the original action to recover its saved result."), statusCode: 503);
            }
            catch (GameWalletConflictException error)
            {
                return Results.Json(new CrapsErrorResponse("craps-operation-conflict", error.Message), statusCode: 409);
            }
            catch (CrapsAccessException error)
            {
                return Results.NotFound(new CrapsErrorResponse("craps-round-not-found", error.Message));
            }
            catch (CrapsPaidException error)
            {
                return Results.Json(new CrapsErrorResponse(error.Code, error.Message), statusCode: error.StatusCode);
            }
            catch (Exception error) when (error is ArgumentException or CrapsRuleException)
            {
                return Results.BadRequest(new CrapsErrorResponse("craps-invalid-action", error.Message));
            }
            catch (InvalidOperationException)
            {
                return Results.Json(new CrapsErrorResponse("craps-recovery-required", "The saved Craps state could not be verified. Retry the original action."), statusCode: 503);
            }
        });

        routes.MapGet("/status", (HttpContext context, FirestorePaidCrapsStore store, CancellationToken token) =>
            WithAccount(context, async userId => Results.Ok(await store.StatusAsync(userId, token))));
        routes.MapGet("/rounds/active", (HttpContext context, FirestorePaidCrapsStore store, CancellationToken token) =>
            WithAccount(context, async userId =>
                await store.ActiveAsync(userId, token) is { } round ? Results.Ok(round) : Results.NoContent()));
        routes.MapGet("/rounds/{roundId:guid}", (HttpContext context, Guid roundId, FirestorePaidCrapsStore store, CancellationToken token) =>
            WithAccount(context, async userId => Results.Ok(await store.GetAsync(userId, roundId, token))));
        routes.MapPost("/rounds", (HttpContext context, StartCrapsRoundRequest request, FirestorePaidCrapsStore store, CancellationToken token) =>
            WithAccount(context, async userId => Results.Ok(await store.StartAsync(userId, request, token))));
        routes.MapPost("/rounds/{roundId:guid}/roll", (HttpContext context, Guid roundId, CrapsOperationRequest request, FirestorePaidCrapsStore store, CancellationToken token) =>
            WithAccount(context, async userId => Results.Ok(await store.RollAsync(userId, roundId, request, token))));
        routes.MapPost("/rounds/{roundId:guid}/odds", (HttpContext context, Guid roundId, PlaceCrapsOddsRequest request, FirestorePaidCrapsStore store, CancellationToken token) =>
            WithAccount(context, async userId => Results.Ok(await store.PlaceOddsAsync(userId, roundId, request, token))));
        routes.MapPost("/rounds/{roundId:guid}/wagers", (HttpContext context, Guid roundId, SetCrapsWagerRequest request, FirestorePaidCrapsStore store, CancellationToken token) =>
            WithAccount(context, async userId => Results.Ok(await store.SetWagerAsync(userId, roundId, request, token))));
    }
}
