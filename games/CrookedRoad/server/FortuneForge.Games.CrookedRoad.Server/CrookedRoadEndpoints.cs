using FortuneForge.Games.Abstractions.Wallet;
using FortuneForge.Games.Hosting;
using FortuneForge.Games.Slots;
using FortuneForge.Games.Slots.Persistence;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace FortuneForge.Games.CrookedRoad.Server;

public static class CrookedRoadEndpoints
{
    public static IServiceCollection AddCrookedRoadPaid(this IServiceCollection services)
    {
        services.TryAddSingleton(TimeProvider.System);
        services.TryAddSingleton<IGameRandomSource, CrookedRoadRandom>();
        services.AddSingleton<CrookedRoadRoundStore>();
        services.AddSingleton<CrookedRoadActionStore>();
        services.AddSingleton<CrookedRoadRoundService>();
        return services;
    }

    public static IEndpointRouteBuilder MapCrookedRoadPaidRounds(this IEndpointRouteBuilder endpoints, Func<HttpContext, string?> resolveAccount)
    {
        endpoints.MapPreparedSlotSpin(new(CrookedRoadRules.GameId, CrookedRoadPaidRouteContract.SpinPath, CrookedRoadPaidRouteContract.AuditGameId),
            async (CrookedRoadSpinCommand request, HttpContext context, [FromServices] CrookedRoadRoundService rounds,
                IConfiguration configuration, CancellationToken token) =>
            {
                if (resolveAccount(context) is not { } account) return Results.Unauthorized();
                if (request.GameId != CrookedRoadRules.GameId) return Results.BadRequest(new { error = "Choose The Crooked Road." });
                return await Respond(async () =>
                {
                    var operation = Operation(context);
                    var result = await rounds.SpinAsync(account, new(operation, request.WagerPoints, request.UseFreeSpin, request.UseSpecialBoost),
                        configuration.GetValue<bool>("CrookedRoad:AllowNewPaidRounds"), token);
                    if (result.Wallet.InsufficientFunds) return Results.Conflict(new { code = "insufficient-slot-credits", error = "There are insufficient slot credits for that wager.",
                        available = result.Wallet.BalanceBeforeCents / 100m, required = result.ChargedWagerCents / 100m, preparedOperationPending = false });
                    if (!result.Settled || result.Round is null) return Results.Conflict(new { code = "crooked-road-wallet-state-changed", error = "Refresh the game state before another play." });
                    return Results.Ok(CrookedRoadSpinResponse.From(result.Round.Outcome, result.ChargedWagerCents, result.Wallet.BalanceAfterCents));
                });
            });
        endpoints.MapGet(CrookedRoadPaidRouteContract.StatePath, async (HttpContext context, [FromServices] CrookedRoadRoundService rounds, CancellationToken token) =>
        {
            if (resolveAccount(context) is not { } account) return Results.Unauthorized();
            context.Response.Headers.CacheControl = "no-store";
            return await Respond(async () => Results.Ok(await rounds.ReadStateAsync(account, token)));
        }).WithMetadata(new GameWorkerPublicRoute(CrookedRoadRules.GameId, CrookedRoadPaidRouteContract.StatePath, CrookedRoadPaidRouteContract.AuditGameId));
        endpoints.MapPost(CrookedRoadPaidRouteContract.ActionPath, async (CrookedRoadAction action, HttpContext context,
            [FromServices] CrookedRoadRoundService rounds, CancellationToken token) =>
        {
            if (resolveAccount(context) is not { } account) return Results.Unauthorized();
            return await Respond(async () => Results.Ok(await rounds.ActAsync(account, Operation(context), action, token)));
        }).WithMetadata(new GameWorkerPublicRoute(CrookedRoadRules.GameId, CrookedRoadPaidRouteContract.ActionPath, CrookedRoadPaidRouteContract.AuditGameId));
        return endpoints;
    }

    internal static string Operation(HttpContext context)
    {
        var values = context.Request.Headers["Idempotency-Key"];
        if (values.Count != 1 || !IClassicSlotRoundRequest.IsValidOperationId(values[0]))
            throw new ArgumentException("Include one valid Idempotency-Key.");
        return values[0]!;
    }

    internal static async Task<IResult> Respond(Func<Task<IResult>> handler)
    {
        try { return await handler(); }
        catch (CrookedRoadConflictException exception)
        {
            return Results.Json(new { code = exception.Code, error = exception.Message },
                statusCode: exception.Code == "slot-prepared-start-paused" ? 503 : 409);
        }
        catch (CrookedRoadPendingException exception)
        { return Results.Conflict(new { code = "slot-operation-conflict", error = exception.Message, pendingOperation = exception.PendingOperation }); }
        catch (CrookedRoadCooldownException exception)
        { return Results.Json(new { error = exception.Message, retryAfterMilliseconds = Math.Ceiling(exception.Remaining.TotalMilliseconds) }, statusCode: 429); }
        catch (GameWalletUnavailableException exception)
        { return Results.Json(new { code = "retry-required", error = exception.Message }, statusCode: 503); }
        catch (ArgumentException exception) { return Results.BadRequest(new { error = exception.Message }); }
    }
}
