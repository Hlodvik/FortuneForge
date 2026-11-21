using System.Globalization;
using FortuneForge.Games.Abstractions.Wallet;
using FortuneForge.Games.Hosting;
using Microsoft.AspNetCore.Mvc;

namespace FortuneForge.Games.JadeDragon.Server;

public static class JadePaidRouteContract
{
    public const string SpinPath = "/api/slots/spins";
    public const string StatePath = "/api/slots/jade-dragon/state";
    public const string AuditGameId = "empty-slots-game";
}

public sealed record JadePaidSpinCommand(string GameId, long WagerPoints, bool UseFreeSpin, bool UseSpecialBoost,
    string? JadeDragonEngineVersion);

public static class JadePaidEndpoints
{
    public static IEndpointRouteBuilder MapJadePaidRounds(this IEndpointRouteBuilder endpoints, Func<HttpContext, string?> resolveAccount)
    {
        endpoints.MapPreparedSlotSpin(new GameWorkerPublicRoute(JadeDragonEngine.GameId, JadePaidRouteContract.SpinPath, JadePaidRouteContract.AuditGameId),
            async (JadePaidSpinCommand request, HttpContext context,
            [FromServices] JadeRoundService rounds, IConfiguration configuration, CancellationToken token) =>
        {
            if (resolveAccount(context) is not { } account) return Results.Unauthorized();
            if (request.GameId != JadeDragonEngine.GameId)
                return Results.BadRequest(new { error = "This endpoint accepts Jade Dragon spins." });
            if (request.JadeDragonEngineVersion != JadeDragonCascadeRules.Version)
                return Results.BadRequest(new { error = "Reload Jade Dragon to use the current game rules." });
            var ids = context.Request.Headers["Idempotency-Key"];
            if (ids.Count != 1 || !JadeRoundRequest.IsValidOperationId(ids[0]))
                return Results.BadRequest(new { code = "invalid-spin-operation", error = "Include one valid Jade Dragon spin operation ID." });

            try
            {
                var terms = new JadeRoundRequest(ids[0]!, request.WagerPoints, request.UseFreeSpin, request.UseSpecialBoost);
                var result = await rounds.SpinAsync(account, terms, configuration.GetValue<bool>("JadeDragon:AllowNewPaidRounds"), token);
                if (result.Wallet.LegacyFreeGamesCountMismatch)
                    return Results.Conflict(new { code = "jade-legacy-free-spins-changed", error = "The available Jade Dragon free games changed. Refresh the game state." });
                if (result.Wallet.InsufficientLegacyFreeGames)
                    return Results.Conflict(new { code = "free-spins-unavailable", error = "No free spins are available.", freeSpinsRemaining = 0 });
                if (result.Wallet.InsufficientFunds)
                    return Results.Conflict(new { code = "insufficient-slot-credits", error = "There are insufficient slot credits for that wager.",
                        available = result.Wallet.BalanceBeforeCents / 100m, required = result.ChargedWagerCents / 100m,
                        preparedOperationPending = false });
                return Results.Ok(JadeSpinResponse.From(result));
            }
            catch (JadeFreeSpinsRequiredException exception)
            {
                return Results.Conflict(new { code = "free-spins-required", error = exception.Message });
            }
            catch (JadeFreeSpinsUnavailableException exception)
            {
                return Results.Conflict(new { code = "free-spins-unavailable", error = exception.Message, freeSpinsRemaining = 0 });
            }
            catch (JadeFreeWagerLockedException exception)
            {
                return Results.Conflict(new { code = "jade-free-wager-locked", error = exception.Message, lockedWagerPoints = exception.WagerPoints });
            }
            catch (JadeInsufficientSpecialPointsException exception)
            {
                return Results.Conflict(new { code = "insufficient-special-points", error = exception.Message,
                    available = exception.Available, required = exception.Required });
            }
            catch (JadeRoundConflictException exception)
            {
                return Results.Conflict(new { code = "slot-operation-conflict", error = exception.Message,
                    pendingOperation = exception.PendingRequest });
            }
            catch (JadeRoundCooldownException exception)
            {
                context.Response.Headers.RetryAfter = Math.Max(1, Math.Ceiling(exception.Remaining.TotalSeconds))
                    .ToString(CultureInfo.InvariantCulture);
                return Results.Json(new { error = exception.Message,
                    retryAfterMilliseconds = Math.Max(1, Math.Ceiling(exception.Remaining.TotalMilliseconds)) }, statusCode: 429);
            }
            catch (JadeRoundPausedException exception)
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

        endpoints.MapGet(JadePaidRouteContract.StatePath, async (HttpContext context,
            [FromServices] JadeRoundService rounds, CancellationToken token) =>
        {
            if (resolveAccount(context) is not { } account) return Results.Unauthorized();
            try { return Results.Ok(await rounds.ReadStateAsync(account, token)); }
            catch (GameWalletUnavailableException exception) { return Retry(exception); }
        }).WithMetadata(new GameWorkerPublicRoute(JadeDragonEngine.GameId, JadePaidRouteContract.StatePath, JadePaidRouteContract.AuditGameId));

        return endpoints;
    }

    private static IResult Retry(GameWalletUnavailableException exception) =>
        Results.Json(new { code = "retry-required", error = exception.Message }, statusCode: 503);
}
