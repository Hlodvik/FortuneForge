using FortuneForge.Games.Hosting;
using FortuneForge.Server.Activity;
using FortuneForge.Games.Craps;
using FortuneForge.Server.Games.Craps;
using Google.Cloud.Firestore;

namespace FortuneForge.Games.PackagedGame.Server;

internal static class GameWorkerModule
{
    internal const string GameId = "craps";

    internal static void Configure(WebApplicationBuilder builder) =>
        builder.Services.AddSingleton(provider =>
        {
            var database = provider.GetRequiredService<FirestoreDb>();
            return new FirestorePaidCrapsStore(database, writeActivity: (transaction, observation) =>
                AcceptedGameTransactionWitness.Write(database, observation,
                    (reference, fields) => transaction.Set(reference, fields, SetOptions.MergeAll)));
        });

    internal static void Map(WebApplication app, GameWorkerAccountResolver resolveUserId)
    {
        Task<IResult> WithAccount(HttpContext context, Func<string, Task<IResult>> action) =>
            resolveUserId(context) is { } userId
                ? action(userId)
                : Task.FromResult(Results.Json(
                    new CrapsErrorResponse("craps-authentication-required", "Sign in to play Craps."),
                    statusCode: StatusCodes.Status401Unauthorized));

        var routes = app.MapGroup("/api/games/craps");
        routes.AddEndpointFilter(async (context, next) =>
        {
            var http = context.HttpContext;
            http.Response.Headers.CacheControl = "no-store";
            if (GameWorkerAuthentication.GetAccountId(http) is not null &&
                !app.Configuration.GetValue("Features:CrapsEnabled", false))
            {
                return Results.Json(new CrapsErrorResponse("craps-disabled", "Craps is not available yet."),
                    statusCode: StatusCodes.Status503ServiceUnavailable);
            }
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
