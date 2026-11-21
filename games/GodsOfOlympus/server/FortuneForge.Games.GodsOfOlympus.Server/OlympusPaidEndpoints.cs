using System.Globalization;
using FortuneForge.Games.Abstractions.Wallet;
using FortuneForge.Games.Hosting;
using Microsoft.AspNetCore.Mvc;

namespace FortuneForge.Games.GodsOfOlympus.Server;

public static class OlympusPaidRouteContract
{
    public const string SpinPath = "/api/slots/spins";
    public const string StatePath = "/api/slots/gods-of-olympus/state";
    public const string AuditGameId = "gods-of-olympus";
}

public sealed record OlympusPaidSpinCommand(string GameId, long WagerPoints, bool UseFreeSpin, bool UseSpecialBoost);

public static class OlympusPaidEndpoints
{
    public static IEndpointRouteBuilder MapOlympusPaidRounds(this IEndpointRouteBuilder endpoints, Func<HttpContext, string?> resolveAccount)
    {
        endpoints.MapPreparedSlotSpin(new GameWorkerPublicRoute(GodsOfOlympusEngine.GameId, OlympusPaidRouteContract.SpinPath, OlympusPaidRouteContract.AuditGameId),
            async (OlympusPaidSpinCommand request, HttpContext context,
            [FromServices] OlympusRoundService rounds, IConfiguration configuration, CancellationToken token) =>
        {
            if (resolveAccount(context) is not { } account) return Results.Unauthorized();
            if (request.GameId != GodsOfOlympusEngine.GameId)
                return Results.BadRequest(new { error = "This endpoint accepts Gods of Olympus spins." });
            var ids = context.Request.Headers["Idempotency-Key"];
            if (ids.Count != 1 || !OlympusRoundRequest.IsValidOperationId(ids[0]))
                return Results.BadRequest(new { code = "invalid-spin-operation", error = "Include one valid Gods of Olympus spin operation ID." });

            try
            {
                var terms = new OlympusRoundRequest(ids[0]!, request.WagerPoints, request.UseFreeSpin, request.UseSpecialBoost);
                var result = await rounds.SpinAsync(account, terms, configuration.GetValue<bool>("GodsOfOlympus:AllowNewPaidRounds"), token);
                if (result.Wallet.LegacyFreeGamesCountMismatch)
                    return Results.Conflict(new { code = "gods-of-olympus-legacy-free-spins-changed", error = "The available Gods of Olympus free games changed. Refresh the game state." });
                if (result.Wallet.InsufficientLegacyFreeGames)
                    return Results.Conflict(new { code = "free-spins-unavailable", error = "No free spins are available.", freeSpinsRemaining = 0 });
                if (result.Wallet.InsufficientFunds)
                    return Results.Conflict(new { code = "insufficient-slot-credits", error = "There are insufficient slot credits for that wager.",
                        available = result.Wallet.BalanceBeforeCents / 100m, required = result.ChargedWagerCents / 100m,
                        preparedOperationPending = false });
                return Results.Ok(OlympusSpinResponse.From(result));
            }
            catch (OlympusFreeSpinsRequiredException exception)
            {
                return Results.Conflict(new { code = "free-spins-required", error = exception.Message });
            }
            catch (OlympusFreeSpinsUnavailableException exception)
            {
                return Results.Conflict(new { code = "free-spins-unavailable", error = exception.Message, freeSpinsRemaining = 0 });
            }
            catch (OlympusFreeWagerLockedException exception)
            {
                return Results.Conflict(new { code = "free-spin-wager-locked", error = exception.Message, lockedWagerPoints = exception.WagerPoints });
            }
            catch (OlympusLegacyFreeWagerUnplayableException exception)
            {
                return Results.Conflict(new { code = "legacy-free-spin-wager-unplayable", error = exception.Message, lockedWagerPoints = exception.WagerPoints });
            }
            catch (OlympusInsufficientSpecialPointsException exception)
            {
                return Results.Conflict(new { code = "insufficient-special-points", error = exception.Message,
                    available = exception.Available, required = exception.Required });
            }
            catch (OlympusRoundConflictException exception)
            {
                return Results.Conflict(new { code = "slot-operation-conflict", error = exception.Message,
                    pendingOperation = exception.PendingRequest });
            }
            catch (OlympusRoundCooldownException exception)
            {
                context.Response.Headers.RetryAfter = Math.Max(1, Math.Ceiling(exception.Remaining.TotalSeconds))
                    .ToString(CultureInfo.InvariantCulture);
                return Results.Json(new { error = exception.Message,
                    retryAfterMilliseconds = Math.Max(1, Math.Ceiling(exception.Remaining.TotalMilliseconds)) }, statusCode: 429);
            }
            catch (OlympusRoundPausedException exception)
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

        endpoints.MapGet(OlympusPaidRouteContract.StatePath, async (HttpContext context,
            [FromServices] OlympusRoundService rounds, CancellationToken token) =>
        {
            if (resolveAccount(context) is not { } account) return Results.Unauthorized();
            try { return Results.Ok(await rounds.ReadStateAsync(account, token)); }
            catch (GameWalletUnavailableException exception) { return Retry(exception); }
        }).WithMetadata(new GameWorkerPublicRoute(GodsOfOlympusEngine.GameId, OlympusPaidRouteContract.StatePath, OlympusPaidRouteContract.AuditGameId));

        return endpoints;
    }

    private static IResult Retry(GameWalletUnavailableException exception) =>
        Results.Json(new { code = "retry-required", error = exception.Message }, statusCode: 503);
}
