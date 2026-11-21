using FortuneForge.Games.Abstractions.Wallet;
using FortuneForge.Games.CasinoWar;
using FortuneForge.Games.Hosting;
using Grpc.Core;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace FortuneForge.Server.Cards.CasinoWar;

public static class CasinoWarRouteContract
{
    public const string GameId = CasinoWarModule.GameId;
    public const string PathPrefix = CasinoWarModule.ApiBasePath;
}

internal sealed record CasinoWarHttpFailure(int StatusCode, CasinoWarErrorResponse Error);
internal static class CasinoWarErrors
{
    internal static CasinoWarHttpFailure? From(Exception exception) => exception switch
    {
        CasinoWarRoundNotFoundException => new(404, new("casino-war-round-not-found", exception.Message)),
        CasinoWarInsufficientCreditsException => new(409, new("insufficient-slot-credits", exception.Message)),
        CasinoWarRoundConflictException or CasinoWarPendingActionException => new(409, new("casino-war-round-conflict", exception.Message)),
        CasinoWarAdmissionPausedException => new(503, new("casino-war-disabled", exception.Message)),
        GameWalletUnavailableException or GameWalletConflictException or RpcException or InvalidOperationException =>
            new(503, new("casino-war-unavailable", "The Casino War service could not confirm the action. Retry the original request or recover the pending round.")),
        ArgumentException => new(400, new("casino-war-invalid-request", exception.Message)),
        _ => null,
    };
}

public static class CasinoWarEndpoints
{
    public static IEndpointRouteBuilder MapCasinoWar(this IEndpointRouteBuilder endpoints,
        Func<HttpContext, string?> resolveAccount, bool allowNewPaidRounds)
    {
        var api = endpoints.MapGroup(CasinoWarRouteContract.PathPrefix).WithMetadata(
            new GameWorkerPublicRoute(CasinoWarRouteContract.GameId, CasinoWarRouteContract.PathPrefix, CasinoWarRouteContract.GameId));
        api.MapGet("/status", (Func<HttpContext, Task<IResult>>)((HttpContext context) => ExecuteAsync(context, resolveAccount, async (store, user, token) =>
            Results.Ok(new CasinoWarStatusResponse(allowNewPaidRounds,
                CasinoWarMoney.ToRand(CasinoWarMoney.MinimumPrimaryStakeCents), CasinoWarMoney.ToRand(CasinoWarMoney.MaximumPrimaryStakeCents),
                CasinoWarMoney.ToRand(CasinoWarMoney.StakeIncrementCents), CasinoWarMoney.ToRand(CasinoWarMoney.MaximumTieStakeCents),
                CasinoWarMoney.ToRand(await store.BalanceAsync(user, token)), "six-deck-casino-war")))));
        api.MapPost("/rounds", (CreateCasinoWarRoundRequest request, HttpContext context) =>
            ExecuteAsync(context, resolveAccount, async (_, user, token) =>
            {
                var result = await context.RequestServices.GetRequiredService<CasinoWarService>()
                    .StartAsync(user, request, Key(context), token);
                return Results.Created($"{CasinoWarRouteContract.PathPrefix}/rounds/{result.RoundId}", result);
            }));
        api.MapGet("/rounds/{roundId}", (string roundId, HttpContext context) =>
            ExecuteAsync(context, resolveAccount, async (_, user, token) => Results.Ok(
                await context.RequestServices.GetRequiredService<CasinoWarService>().GetAsync(user, roundId, token))));
        api.MapPost("/rounds/{roundId}/decision", (string roundId, DecideCasinoWarRoundRequest request, HttpContext context) =>
            ExecuteAsync(context, resolveAccount, async (_, user, token) => Results.Ok(
                await context.RequestServices.GetRequiredService<CasinoWarService>().DecideAsync(user, roundId, request, Key(context), token))));
        api.MapGet("/rounds/pending", (Func<HttpContext, Task<IResult>>)((HttpContext context) => ExecuteAsync(context, resolveAccount,
            async (store, user, token) => Results.Ok(await store.RecoverAsync(user, token)))));
        api.MapGet("/rounds/history", (int? limit, HttpContext context) => ExecuteAsync(context, resolveAccount,
            async (store, user, token) => Results.Ok(await store.HistoryAsync(user, limit ?? 20, token))));
        return endpoints;
    }

    private static string Key(HttpContext context)
    {
        var values = context.Request.Headers["Idempotency-Key"];
        if (values.Count != 1) throw new ArgumentException("Include one valid Casino War idempotency key.");
        return values[0]!;
    }

    private static async Task<IResult> ExecuteAsync(HttpContext context, Func<HttpContext, string?> resolveAccount,
        Func<CasinoWarFirestoreStore, string, CancellationToken, Task<IResult>> action)
    {
        context.Response.Headers.CacheControl = "no-store, max-age=0";
        if (resolveAccount(context) is not { Length: > 0 } user)
            return Results.Json(new CasinoWarErrorResponse("casino-war-authentication-required", "Sign in to play Casino War."), statusCode: 401);
        try
        {
            return await action(context.RequestServices.GetRequiredService<CasinoWarFirestoreStore>(), user, context.RequestAborted);
        }
        catch (Exception exception) when (CasinoWarErrors.From(exception) is not null)
        {
            var error = CasinoWarErrors.From(exception)!;
            if (error.StatusCode == 503)
                context.RequestServices.GetRequiredService<ILoggerFactory>().CreateLogger(nameof(CasinoWarEndpoints))
                    .LogWarning("Casino War action could not be confirmed ({ErrorType}).", exception.GetType().Name);
            return Results.Json(error.Error, statusCode: error.StatusCode);
        }
    }
}
