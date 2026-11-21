using System.Net;
using System.Security.Cryptography;
using System.Text.Json;
using FortuneForge.Games.Abstractions.Wallet;
using FortuneForge.Games.Hosting;
using Google.Cloud.Firestore;
using Microsoft.AspNetCore.Http.Features;

namespace FortuneForge.Server.Accounts.Wallet;

internal static class GameWalletBatchEndpoints
{
    internal static void MapGameWalletBatchSettlement(this IEndpointRouteBuilder endpoints) =>
        endpoints.MapPost(GameWalletBatchAuthentication.RoutePattern, SettleAsync);

    private static async Task<IResult> SettleAsync(HttpContext context, string gameId, IConfiguration configuration,
        IHostEnvironment environment, FirestoreDb database, CancellationToken cancellationToken)
    {
        context.Response.Headers.CacheControl = "no-store, max-age=0";
        if (!GameWalletBatchAuthentication.IsSupportedGameId(gameId) || context.Request.Path.Value != GameWalletBatchAuthentication.Route(gameId) ||
            context.Request.PathBase.HasValue || context.Request.QueryString.HasValue)
            return Results.NotFound();
        var games = configuration.GetSection("GameWallet:Games");
        var game = games.GetSection(gameId);
        if (!game.GetValue<bool>("AllowMultiAccountBatch") || game["BatchSigningKey"] is not { Length: > 0 } encodedKey)
            return Results.NotFound();
        if (environment.IsDevelopment() && !context.Request.IsHttps &&
            (context.Connection.RemoteIpAddress is not { } address || !IPAddress.IsLoopback(address)))
            return Results.StatusCode(StatusCodes.Status403Forbidden);

        byte[] key;
        try
        {
            key = GameWorkerAuthentication.ReadSigningKey(encodedKey);
            foreach (var configuredGame in games.GetChildren())
            {
                if (configuredGame["SigningKey"] is { Length: > 0 } ordinary &&
                    TrySameSigningKey(key, ordinary) || configuredGame.Key != gameId &&
                    configuredGame["BatchSigningKey"] is { Length: > 0 } other && TrySameSigningKey(key, other))
                    return Results.StatusCode(StatusCodes.Status503ServiceUnavailable);
            }
        }
        catch (Exception error) when (error is FormatException or ArgumentException)
        {
            return Results.StatusCode(StatusCodes.Status503ServiceUnavailable);
        }

        if (context.Features.Get<IHttpMaxRequestBodySizeFeature>() is { IsReadOnly: false } limit)
            limit.MaxRequestBodySize = GameWalletBatchAuthentication.MaximumBodyBytes;
        var clock = context.RequestServices.GetService<TimeProvider>() ?? TimeProvider.System;
        var signed = await GameWalletBatchAuthentication.VerifyRequestAsync(context.Request, gameId, key,
            clock.GetUtcNow(), cancellationToken);
        if (signed is not { } identity || !GameWalletBatchVerifier.IsValidAccountId(identity.AccountId))
            return Results.StatusCode(StatusCodes.Status403Forbidden);

        if (!context.Request.HasJsonContentType()) return InvalidBatch();
        GameWalletBatchRequest? request;
        try
        {
            request = await context.Request.ReadFromJsonAsync<GameWalletBatchRequest>(GameWalletAuthentication.WireJson,
                cancellationToken);
        }
        catch (JsonException) { return InvalidBatch(); }
        if (request is null || !GameWalletBatchVerifier.IsValidRequest(request)) return InvalidBatch();

        try
        {
            var receipt = await new GameWalletBatchFirestoreStore(database).SettleAsync(gameId, identity.AccountId, request,
                identity.BodySha256, clock.GetUtcNow(), cancellationToken);
            context.Response.Headers[GameWalletAuthentication.RequestSha256Header] = identity.BodySha256;
            return Results.Json(receipt, GameWalletAuthentication.WireJson);
        }
        catch (GameWalletConflictException) { return Results.Conflict(new { code = "wallet-batch-conflict" }); }
        catch (Exception error) when (error is ArgumentException or OverflowException) { return InvalidBatch(); }
        catch (Exception error) when (error is InvalidOperationException or JsonException)
        {
            return Results.StatusCode(StatusCodes.Status503ServiceUnavailable);
        }
    }

    private static bool TrySameSigningKey(byte[] key, string encoded)
    {
        try { return CryptographicOperations.FixedTimeEquals(key, GameWorkerAuthentication.ReadSigningKey(encoded)); }
        catch (Exception error) when (error is FormatException or ArgumentException) { return false; }
    }

    private static IResult InvalidBatch() => Results.BadRequest(new { code = "invalid-wallet-batch" });
}
