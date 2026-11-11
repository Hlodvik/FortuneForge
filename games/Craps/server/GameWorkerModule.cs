using FortuneForge.Games.Craps;
using FortuneForge.Server.Games.Craps;
using Google.Cloud.Firestore;

namespace FortuneForge.Games.PackagedGame.Server;

internal static class GameWorkerModule
{
    internal const string GameId = "craps";

    internal static void Configure(WebApplicationBuilder builder) =>
        builder.Services.AddSingleton(provider => new FirestorePaidCrapsStore(provider.GetRequiredService<FirestoreDb>()));

    internal static void Map(WebApplication app, DemoGameAccount? account)
    {
        if (account is null) return;

        var userId = account.UserId;
        var routes = app.MapGroup("/api/games/craps");
        routes.AddEndpointFilter(async (context, next) =>
        {
            context.HttpContext.Response.Headers.CacheControl = "no-store";
            try { return await next(context); }
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
        });

        routes.MapGet("/status", (FirestorePaidCrapsStore store, CancellationToken token) =>
            store.StatusAsync(userId, token));
        routes.MapGet("/rounds/active", async (FirestorePaidCrapsStore store, CancellationToken token) =>
            await store.ActiveAsync(userId, token) is { } round ? Results.Ok(round) : Results.NoContent());
        routes.MapGet("/rounds/{roundId:guid}", (Guid roundId, FirestorePaidCrapsStore store, CancellationToken token) =>
            store.GetAsync(userId, roundId, token));
        routes.MapPost("/rounds", (StartCrapsRoundRequest request, FirestorePaidCrapsStore store, CancellationToken token) =>
            store.StartAsync(userId, request, token));
        routes.MapPost("/rounds/{roundId:guid}/roll", (Guid roundId, CrapsOperationRequest request, FirestorePaidCrapsStore store, CancellationToken token) =>
            store.RollAsync(userId, roundId, request, token));
        routes.MapPost("/rounds/{roundId:guid}/odds", (Guid roundId, PlaceCrapsOddsRequest request, FirestorePaidCrapsStore store, CancellationToken token) =>
            store.PlaceOddsAsync(userId, roundId, request, token));
        routes.MapPost("/rounds/{roundId:guid}/wagers", (Guid roundId, SetCrapsWagerRequest request, FirestorePaidCrapsStore store, CancellationToken token) =>
            store.SetWagerAsync(userId, roundId, request, token));
    }
}
