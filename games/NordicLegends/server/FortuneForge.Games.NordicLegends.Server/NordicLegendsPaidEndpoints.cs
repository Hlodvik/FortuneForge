using System.Globalization;
using FortuneForge.Games.Abstractions.Wallet;
using FortuneForge.Games.Hosting;
using Microsoft.AspNetCore.Mvc;

namespace FortuneForge.Games.NordicLegends.Server;

public static class NordicLegendsPaidRouteContract
{
    public const string SpinPath = "/api/slots/spins";
    public const string StatePath = "/api/slots/nordic-legends/state";
    public const string AuditGameId = "nordic-legends";
}

public sealed record NordicLegendsPaidSpinCommand(string GameId, long WagerPoints, bool UseFreeSpin, bool UseSpecialBoost);

public static class NordicLegendsPaidEndpoints
{
    public static IEndpointRouteBuilder MapNordicLegendsPaidRounds(this IEndpointRouteBuilder endpoints, Func<HttpContext, string?> resolveAccount)
    {
        endpoints.MapPreparedSlotSpin(new GameWorkerPublicRoute(NordicLegendsEngine.GameId, NordicLegendsPaidRouteContract.SpinPath, NordicLegendsPaidRouteContract.AuditGameId),
            async (NordicLegendsPaidSpinCommand request, HttpContext context,
            [FromServices] NordicLegendsRoundService rounds, IConfiguration configuration, CancellationToken token) =>
        {
            if (resolveAccount(context) is not { } account) return Results.Unauthorized();
            if (request.GameId != NordicLegendsEngine.GameId)
                return Results.BadRequest(new { error = "This endpoint accepts Nordic Legends spins." });
            var ids = context.Request.Headers["Idempotency-Key"];
            if (ids.Count != 1 || !NordicLegendsRoundRequest.IsValidOperationId(ids[0]))
                return Results.BadRequest(new { code = "invalid-spin-operation", error = "Include one valid Nordic Legends spin operation ID." });

            try
            {
                var terms = new NordicLegendsRoundRequest(ids[0]!, request.WagerPoints, request.UseFreeSpin, request.UseSpecialBoost);
                var result = await rounds.SpinAsync(account, terms, configuration.GetValue<bool>("NordicLegends:AllowNewPaidRounds"), token);
                if (result.Wallet.LegacyFreeGamesCountMismatch)
                    return Results.Conflict(new { code = "nordic-legends-legacy-free-spins-changed", error = "The available Nordic Legends free games changed. Refresh the game state." });
                if (result.Wallet.InsufficientLegacyFreeGames)
                    return Results.Conflict(new { code = "free-spins-unavailable", error = "No free spins are available.", freeSpinsRemaining = 0 });
                if (result.Wallet.InsufficientFunds)
                    return Results.Conflict(new { code = "insufficient-slot-credits", error = "There are insufficient slot credits for that wager.",
                        available = result.Wallet.BalanceBeforeCents / 100m, required = result.ChargedWagerCents / 100m,
                        preparedOperationPending = false });
                return Results.Ok(NordicLegendsSpinResponse.From(result));
            }
            catch (NordicLegendsFreeSpinsRequiredException exception)
            {
                return Results.Conflict(new { code = "free-spins-required", error = exception.Message });
            }
            catch (NordicLegendsFreeSpinsUnavailableException exception)
            {
                return Results.Conflict(new { code = "free-spins-unavailable", error = exception.Message, freeSpinsRemaining = 0 });
            }
            catch (NordicLegendsFreeWagerLockedException exception)
            {
                return Results.Conflict(new { code = "free-spin-wager-locked", error = exception.Message, lockedWagerPoints = exception.WagerPoints });
            }
            catch (NordicLegendsLegacyFreeWagerUnplayableException exception)
            {
                return Results.Conflict(new { code = "legacy-free-spin-wager-unplayable", error = exception.Message, lockedWagerPoints = exception.WagerPoints });
            }
            catch (NordicLegendsInsufficientSpecialPointsException exception)
            {
                return Results.Conflict(new { code = "insufficient-special-points", error = exception.Message,
                    available = exception.Available, required = exception.Required });
            }
            catch (NordicLegendsRoundConflictException exception)
            {
                return Results.Conflict(new { code = "slot-operation-conflict", error = exception.Message,
                    pendingOperation = exception.PendingRequest });
            }
            catch (NordicLegendsRoundCooldownException exception)
            {
                context.Response.Headers.RetryAfter = Math.Max(1, Math.Ceiling(exception.Remaining.TotalSeconds))
                    .ToString(CultureInfo.InvariantCulture);
                return Results.Json(new { error = exception.Message,
                    retryAfterMilliseconds = Math.Max(1, Math.Ceiling(exception.Remaining.TotalMilliseconds)) }, statusCode: 429);
            }
            catch (NordicLegendsRoundPausedException exception)
            {
                return Results.Json(new { code = "slot-prepared-start-paused", error = exception.Message }, statusCode: 503);
            }
            catch (GameWalletUnavailableException exception)
            {
                return Retry(exception);
            }
            catch (ArgumentException exception)
            {
                return Results.BadRequest(new { error = exception.Message });
            }
        });

        endpoints.MapGet(NordicLegendsPaidRouteContract.StatePath, async (HttpContext context,
            [FromServices] NordicLegendsRoundService rounds, CancellationToken token) =>
        {
            if (resolveAccount(context) is not { } account) return Results.Unauthorized();
            try { return Results.Ok(await rounds.ReadStateAsync(account, token)); }
            catch (GameWalletUnavailableException exception) { return Retry(exception); }
        }).WithMetadata(new GameWorkerPublicRoute(NordicLegendsEngine.GameId, NordicLegendsPaidRouteContract.StatePath, NordicLegendsPaidRouteContract.AuditGameId));

        return endpoints;
    }

    private static IResult Retry(GameWalletUnavailableException exception) =>
        Results.Json(new { code = "retry-required", error = exception.Message }, statusCode: 503);
}
