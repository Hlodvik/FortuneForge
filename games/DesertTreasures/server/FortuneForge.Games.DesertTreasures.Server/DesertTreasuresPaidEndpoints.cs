using System.Globalization;
using FortuneForge.Games.Abstractions.Wallet;
using FortuneForge.Games.Hosting;
using Microsoft.AspNetCore.Mvc;

namespace FortuneForge.Games.DesertTreasures.Server;

public static class DesertTreasuresPaidRouteContract
{
    public const string SpinPath = "/api/slots/spins";
    public const string StatePath = "/api/slots/desert-treasures/state";
    public const string AuditGameId = "desert-treasures";
}

public sealed record DesertTreasuresPaidSpinCommand(string GameId, long WagerPoints, bool UseFreeSpin, bool UseSpecialBoost);

public static class DesertTreasuresPaidEndpoints
{
    public static IEndpointRouteBuilder MapDesertTreasuresPaidRounds(this IEndpointRouteBuilder endpoints, Func<HttpContext, string?> resolveAccount)
    {
        endpoints.MapPreparedSlotSpin(new GameWorkerPublicRoute(DesertTreasuresEngine.GameId, DesertTreasuresPaidRouteContract.SpinPath, DesertTreasuresPaidRouteContract.AuditGameId),
            async (DesertTreasuresPaidSpinCommand request, HttpContext context,
            [FromServices] DesertTreasuresRoundService rounds, IConfiguration configuration, CancellationToken token) =>
        {
            if (resolveAccount(context) is not { } account) return Results.Unauthorized();
            if (request.GameId != DesertTreasuresEngine.GameId)
                return Results.BadRequest(new { error = "This endpoint accepts Desert Treasures spins." });
            var ids = context.Request.Headers["Idempotency-Key"];
            if (ids.Count != 1 || !DesertTreasuresRoundRequest.IsValidOperationId(ids[0]))
                return Results.BadRequest(new { code = "invalid-spin-operation", error = "Include one valid Desert Treasures spin operation ID." });

            try
            {
                var terms = new DesertTreasuresRoundRequest(ids[0]!, request.WagerPoints, request.UseFreeSpin, request.UseSpecialBoost);
                var result = await rounds.SpinAsync(account, terms, configuration.GetValue<bool>("DesertTreasures:AllowNewPaidRounds"), token);
                if (result.Wallet.LegacyFreeGamesCountMismatch)
                    return Results.Conflict(new { code = "desert-treasures-legacy-free-spins-changed", error = "The available Desert Treasures free games changed. Refresh the game state." });
                if (result.Wallet.InsufficientLegacyFreeGames)
                    return Results.Conflict(new { code = "free-spins-unavailable", error = "No free spins are available.", freeSpinsRemaining = 0 });
                if (result.Wallet.InsufficientFunds)
                    return Results.Conflict(new { code = "insufficient-slot-credits", error = "There are insufficient slot credits for that wager.",
                        available = result.Wallet.BalanceBeforeCents / 100m, required = result.ChargedWagerCents / 100m,
                        preparedOperationPending = false });
                return Results.Ok(DesertTreasuresSpinResponse.From(result));
            }
            catch (DesertTreasuresFreeSpinsRequiredException exception)
            {
                return Results.Conflict(new { code = "free-spins-required", error = exception.Message });
            }
            catch (DesertTreasuresFreeSpinsUnavailableException exception)
            {
                return Results.Conflict(new { code = "free-spins-unavailable", error = exception.Message, freeSpinsRemaining = 0 });
            }
            catch (DesertTreasuresFreeWagerLockedException exception)
            {
                return Results.Conflict(new { code = "free-spin-wager-locked", error = exception.Message, lockedWagerPoints = exception.WagerPoints });
            }
            catch (DesertTreasuresLegacyFreeWagerUnplayableException exception)
            {
                return Results.Conflict(new { code = "legacy-free-spin-wager-unplayable", error = exception.Message, lockedWagerPoints = exception.WagerPoints });
            }
            catch (DesertTreasuresInsufficientSpecialPointsException exception)
            {
                return Results.Conflict(new { code = "insufficient-special-points", error = exception.Message,
                    available = exception.Available, required = exception.Required });
            }
            catch (DesertTreasuresRoundConflictException exception)
            {
                return Results.Conflict(new { code = "slot-operation-conflict", error = exception.Message,
                    pendingOperation = exception.PendingRequest });
            }
            catch (DesertTreasuresRoundCooldownException exception)
            {
                context.Response.Headers.RetryAfter = Math.Max(1, Math.Ceiling(exception.Remaining.TotalSeconds))
                    .ToString(CultureInfo.InvariantCulture);
                return Results.Json(new { error = exception.Message,
                    retryAfterMilliseconds = Math.Max(1, Math.Ceiling(exception.Remaining.TotalMilliseconds)) }, statusCode: 429);
            }
            catch (DesertTreasuresRoundPausedException exception)
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

        endpoints.MapGet(DesertTreasuresPaidRouteContract.StatePath, async (HttpContext context,
            [FromServices] DesertTreasuresRoundService rounds, CancellationToken token) =>
        {
            if (resolveAccount(context) is not { } account) return Results.Unauthorized();
            try { return Results.Ok(await rounds.ReadStateAsync(account, token)); }
            catch (GameWalletUnavailableException exception) { return Retry(exception); }
        }).WithMetadata(new GameWorkerPublicRoute(DesertTreasuresEngine.GameId, DesertTreasuresPaidRouteContract.StatePath, DesertTreasuresPaidRouteContract.AuditGameId));

        return endpoints;
    }

    private static IResult Retry(GameWalletUnavailableException exception) =>
        Results.Json(new { code = "retry-required", error = exception.Message }, statusCode: 503);
}
