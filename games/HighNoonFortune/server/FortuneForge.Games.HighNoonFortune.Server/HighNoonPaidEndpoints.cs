using System.Globalization;
using FortuneForge.Games.Abstractions.Wallet;
using FortuneForge.Games.Hosting;
using Microsoft.AspNetCore.Mvc;

namespace FortuneForge.Games.HighNoonFortune.Server;

public static class HighNoonPaidRouteContract
{
    public const string SpinPath = "/api/slots/spins";
    public const string StatePath = "/api/slots/high-noon-fortune/state";
    public const string AuditGameId = "high-noon-fortune";
}

public sealed record HighNoonPaidSpinCommand(string GameId, long WagerPoints, bool UseFreeSpin, bool UseSpecialBoost);

public static class HighNoonPaidEndpoints
{
    public static IEndpointRouteBuilder MapHighNoonPaidRounds(this IEndpointRouteBuilder endpoints, Func<HttpContext, string?> resolveAccount)
    {
        endpoints.MapPreparedSlotSpin(new GameWorkerPublicRoute(HighNoonFortuneEngine.GameId, HighNoonPaidRouteContract.SpinPath, HighNoonPaidRouteContract.AuditGameId),
            async (HighNoonPaidSpinCommand request, HttpContext context,
            [FromServices] HighNoonRoundService rounds, IConfiguration configuration, CancellationToken token) =>
        {
            if (resolveAccount(context) is not { } account) return Results.Unauthorized();
            if (request.GameId != HighNoonFortuneEngine.GameId)
                return Results.BadRequest(new { error = "This endpoint accepts High Noon Fortune spins." });
            var ids = context.Request.Headers["Idempotency-Key"];
            if (ids.Count != 1 || !HighNoonRoundRequest.IsValidOperationId(ids[0]))
                return Results.BadRequest(new { code = "invalid-spin-operation", error = "Include one valid High Noon Fortune spin operation ID." });

            try
            {
                var terms = new HighNoonRoundRequest(ids[0]!, request.WagerPoints, request.UseFreeSpin, request.UseSpecialBoost);
                var result = await rounds.SpinAsync(account, terms, configuration.GetValue<bool>("HighNoonFortune:AllowNewPaidRounds"), token);
                if (result.Wallet.LegacyFreeGamesCountMismatch)
                    return Results.Conflict(new { code = "high-noon-fortune-legacy-free-spins-changed", error = "The available High Noon Fortune free games changed. Refresh the game state." });
                if (result.Wallet.InsufficientLegacyFreeGames)
                    return Results.Conflict(new { code = "free-spins-unavailable", error = "No free spins are available.", freeSpinsRemaining = 0 });
                if (result.Wallet.InsufficientFunds)
                    return Results.Conflict(new { code = "insufficient-slot-credits", error = "There are insufficient slot credits for that wager.",
                        available = result.Wallet.BalanceBeforeCents / 100m, required = result.ChargedWagerCents / 100m,
                        preparedOperationPending = false });
                return Results.Ok(HighNoonSpinResponse.From(result));
            }
            catch (HighNoonFreeSpinsRequiredException exception)
            {
                return Results.Conflict(new { code = "free-spins-required", error = exception.Message });
            }
            catch (HighNoonFreeSpinsUnavailableException exception)
            {
                return Results.Conflict(new { code = "free-spins-unavailable", error = exception.Message, freeSpinsRemaining = 0 });
            }
            catch (HighNoonFreeWagerLockedException exception)
            {
                return Results.Conflict(new { code = "free-spin-wager-locked", error = exception.Message, lockedWagerPoints = exception.WagerPoints });
            }
            catch (HighNoonLegacyFreeWagerUnplayableException exception)
            {
                return Results.Conflict(new { code = "legacy-free-spin-wager-unplayable", error = exception.Message, lockedWagerPoints = exception.WagerPoints });
            }
            catch (HighNoonInsufficientSpecialPointsException exception)
            {
                return Results.Conflict(new { code = "insufficient-special-points", error = exception.Message,
                    available = exception.Available, required = exception.Required });
            }
            catch (HighNoonRoundConflictException exception)
            {
                return Results.Conflict(new { code = "slot-operation-conflict", error = exception.Message,
                    pendingOperation = exception.PendingRequest });
            }
            catch (HighNoonRoundCooldownException exception)
            {
                context.Response.Headers.RetryAfter = Math.Max(1, Math.Ceiling(exception.Remaining.TotalSeconds))
                    .ToString(CultureInfo.InvariantCulture);
                return Results.Json(new { error = exception.Message,
                    retryAfterMilliseconds = Math.Max(1, Math.Ceiling(exception.Remaining.TotalMilliseconds)) }, statusCode: 429);
            }
            catch (HighNoonRoundPausedException exception)
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

        endpoints.MapGet(HighNoonPaidRouteContract.StatePath, async (HttpContext context,
            [FromServices] HighNoonRoundService rounds, CancellationToken token) =>
        {
            if (resolveAccount(context) is not { } account) return Results.Unauthorized();
            try { return Results.Ok(await rounds.ReadStateAsync(account, token)); }
            catch (GameWalletUnavailableException exception) { return Retry(exception); }
        }).WithMetadata(new GameWorkerPublicRoute(HighNoonFortuneEngine.GameId, HighNoonPaidRouteContract.StatePath, HighNoonPaidRouteContract.AuditGameId));

        return endpoints;
    }

    private static IResult Retry(GameWalletUnavailableException exception) =>
        Results.Json(new { code = "retry-required", error = exception.Message }, statusCode: 503);
}
