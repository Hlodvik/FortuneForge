using System.Net;
using System.Text.Json;
using FortuneForge.Games.Abstractions.Wallet;
using FortuneForge.Games.Hosting;
using FortuneForge.Server.Accounts.Storage;
using Google.Cloud.Firestore;
using Microsoft.AspNetCore.Http.Features;

namespace FortuneForge.Server.Accounts.Wallet;

internal static partial class GameWalletSettlementEndpoints
{
    internal static void MapGameWalletSettlement(this IEndpointRouteBuilder endpoints)
    {
        endpoints.MapPost(GameWalletAuthentication.SettlementRoutePattern, SettleAsync);
        endpoints.MapGet(GameWalletAuthentication.AccountStateRoutePattern, GetStateAsync);
    }

    private sealed record WalletCaller(string AccountId, string BodySha256, IConfigurationSection Settings, TimeProvider Clock);

    private static async Task<IResult> SettleAsync(HttpContext context, string gameId, IConfiguration configuration,
        IHostEnvironment environment, IWalletRoundSettlementStore wallet, CancellationToken cancellationToken)
    {
        var (caller, failure) = await AuthenticateAsync(context, gameId, GameWalletAuthentication.SettlementRoute(gameId),
            configuration, environment, cancellationToken);
        if (caller is null) return failure!;

        if (!context.Request.HasJsonContentType())
            return InvalidSettlement();

        GameWalletSettlementRequest? request;
        try
        {
            request = await context.Request.ReadFromJsonAsync<GameWalletSettlementRequest>(GameWalletAuthentication.WireJson,
                cancellationToken);
        }
        catch (JsonException) { return InvalidSettlement(); }

        var allowLegacyFreeGames = caller.Settings.GetValue<bool>("AllowLegacyFreeGames");
        var allowCashWagerWithLegacyFreeGame = caller.Settings.GetValue<bool>("AllowCashWagerWithLegacyFreeGame");
        if (request is null || request.ExpectedLegacyFreeGamesRemaining < 0 ||
            !GameWalletBalanceRequirement.IsValid(request.Phase, request.MinimumBalanceBeforeCents) ||
            (request.Phase != GameWalletSettlementPhase.Direct && !caller.Settings.GetValue<bool>("AllowWagerReservations")) ||
            (!allowLegacyFreeGames && (request.SpendLegacyFreeGame || request.ExpectedLegacyFreeGamesRemaining is not null)) ||
            (request.SpendLegacyFreeGame && request.WagerCents != 0 && !allowCashWagerWithLegacyFreeGame))
            return InvalidSettlement();

        WalletRoundLedgerLayout? layout = null;
        if (caller.Settings.GetValue<bool>("LegacySlotLedger"))
        {
            if (!Guid.TryParseExact(request.OperationId, "N", out var spinId) || spinId == Guid.Empty ||
                spinId.ToString("N") != request.OperationId)
                return InvalidSettlement();
            layout = WalletRoundLedgerLayout.LegacySlot(spinId);
        }

        try
        {
            var receipt = await wallet.SettleAsync(new(gameId, caller.AccountId, request.OperationId, request.OutcomeSha256,
                request.WagerCents, request.PayoutCents, request.SpendLegacyFreeGame, allowLegacyFreeGames, caller.BodySha256,
                request.ExpectedLegacyFreeGamesRemaining, allowCashWagerWithLegacyFreeGame, request.Phase,
                request.ReservationSha256, request.MinimumBalanceBeforeCents),
                caller.Clock.GetUtcNow(), cancellationToken, layout);
            context.Response.Headers[GameWalletAuthentication.RequestSha256Header] = caller.BodySha256;
            return Results.Json(receipt.ToWireReceipt(), GameWalletAuthentication.WireJson);
        }
        catch (WalletRoundSettlementConflictException)
        {
            return Results.Conflict(new { code = "wallet-settlement-conflict" });
        }
        catch (Exception error) when (error is ArgumentException or OverflowException)
        {
            return InvalidSettlement();
        }
    }

    private static async Task<IResult> GetStateAsync(HttpContext context, string gameId, IConfiguration configuration,
        IHostEnvironment environment, FirestoreDb database, CancellationToken cancellationToken)
    {
        var (caller, failure) = await AuthenticateAsync(context, gameId, GameWalletAuthentication.AccountStateRoute(gameId),
            configuration, environment, cancellationToken);
        if (caller is null) return failure!;
        var allowLegacyFreeGames = caller.Settings.GetValue<bool>("AllowLegacyFreeGames");
        var allowAccountBalanceRead = caller.Settings.GetValue<bool>("AllowAccountBalanceRead");
        var allowPlayerNameRead = caller.Settings.GetValue<bool>("AllowPlayerNameRead");
        if (!allowLegacyFreeGames && !allowAccountBalanceRead && !allowPlayerNameRead) return Results.NotFound();
        if (caller.BodySha256 != GameWalletAuthentication.EmptyBodySha256) return InvalidSettlement();

        var remaining = allowLegacyFreeGames
            ? FirestoreWalletDocuments.ReadLegacyFreeGamesAsync(database, caller.AccountId, gameId, cancellationToken)
            : Task.FromResult(0L);
        var balance = allowAccountBalanceRead
            ? FirestoreWalletDocuments.ReadSlotsCreditCentsAsync(database, caller.AccountId,
                caller.Settings.GetValue<bool>("LegacySlotLedger"), cancellationToken)
            : Task.FromResult(0L);
        var playerName = allowPlayerNameRead ? ReadPlayerNameAsync() : Task.FromResult<string?>(null);
        await Task.WhenAll(remaining, balance, playerName);
        context.Response.Headers[GameWalletAuthentication.StateScopeHeader] = GameWalletAuthentication.AccountStateScope(gameId, caller.AccountId);
        return Results.Json(new GameWalletAccountState(await remaining, allowAccountBalanceRead ? await balance : null, await playerName),
            GameWalletAuthentication.WireJson);

        async Task<string?> ReadPlayerNameAsync()
        {
            var profile = await database.Collection("users").Document(caller.AccountId).GetSnapshotAsync(cancellationToken);
            return profile.Exists && profile.TryGetValue<object>("userId", out var storedAccount) &&
                storedAccount is string account && account == caller.AccountId &&
                profile.TryGetValue<object>("playerName", out var storedName) && storedName is string name &&
                GameWalletAccountState.IsValidPlayerName(name) ? name : null;
        }
    }

    private static async Task<(WalletCaller? Caller, IResult? Failure)> AuthenticateAsync(HttpContext context, string gameId,
        string exactRoute, IConfiguration configuration, IHostEnvironment environment, CancellationToken cancellationToken)
    {
        context.Response.Headers.CacheControl = "no-store, max-age=0";
        if (!GameWalletAuthentication.IsValidGameId(gameId) ||
            !string.Equals(context.Request.Path.Value, exactRoute, StringComparison.Ordinal) ||
            context.Request.PathBase.HasValue || context.Request.QueryString.HasValue)
            return (null, Results.NotFound());

        var settings = configuration.GetSection($"GameWallet:Games:{gameId}");
        if (string.IsNullOrWhiteSpace(settings["SigningKey"]))
            return (null, Results.NotFound());
        if (environment.IsDevelopment() && !context.Request.IsHttps &&
            (context.Connection.RemoteIpAddress is not { } address || !IPAddress.IsLoopback(address)))
            return (null, Results.StatusCode(StatusCodes.Status403Forbidden));

        byte[] key;
        try { key = GameWorkerAuthentication.ReadSigningKey(settings["SigningKey"]!); }
        catch (Exception error) when (error is FormatException or ArgumentException)
        {
            return (null, Results.StatusCode(StatusCodes.Status503ServiceUnavailable));
        }

        if (context.Features.Get<IHttpMaxRequestBodySizeFeature>() is { IsReadOnly: false } limit)
            limit.MaxRequestBodySize = GameWalletAuthentication.MaximumBodyBytes;
        var clock = context.RequestServices.GetService<TimeProvider>() ?? TimeProvider.System;
        var signed = await GameWalletAuthentication.VerifyRequestAsync(context.Request, gameId, key, clock.GetUtcNow(), cancellationToken);
        if (signed is not { } identity || identity.AccountId.Length > 256 || identity.AccountId.Any(char.IsControl) ||
            identity.AccountId.Contains('/') || identity.AccountId.Contains('\\'))
            return (null, Results.StatusCode(StatusCodes.Status403Forbidden));

        return (new(identity.AccountId, identity.BodySha256, settings, clock), null);
    }

    private static IResult InvalidSettlement() => Results.BadRequest(new { code = "invalid-wallet-settlement" });
}
