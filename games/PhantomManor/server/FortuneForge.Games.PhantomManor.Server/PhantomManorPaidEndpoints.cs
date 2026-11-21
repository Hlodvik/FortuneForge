using System.Globalization;
using FortuneForge.Games.Abstractions.Wallet;
using FortuneForge.Games.Hosting;
using Microsoft.AspNetCore.Mvc;

namespace FortuneForge.Games.PhantomManor.Server;

public static class PhantomManorPaidRouteContract
{
    public const string SpinPath = "/api/slots/spins";
    public const string StatePath = "/api/slots/phantom-manor/state";
    public const string AuditGameId = "phantom-manor";
}

public sealed record PhantomManorPaidSpinCommand(string GameId, long WagerPoints, bool UseFreeSpin, bool UseSpecialBoost);

public static class PhantomManorPaidEndpoints
{
    public static IEndpointRouteBuilder MapPhantomManorPaidRounds(this IEndpointRouteBuilder endpoints, Func<HttpContext, string?> resolveAccount)
    {
        endpoints.MapPreparedSlotSpin(new GameWorkerPublicRoute(PhantomManorEngine.GameId, PhantomManorPaidRouteContract.SpinPath, PhantomManorPaidRouteContract.AuditGameId),
            async (PhantomManorPaidSpinCommand request, HttpContext context,
            [FromServices] PhantomManorRoundService rounds, IConfiguration configuration, CancellationToken token) =>
        {
            if (resolveAccount(context) is not { } account) return Results.Unauthorized();
            if (request.GameId != PhantomManorEngine.GameId)
                return Results.BadRequest(new { error = "This endpoint accepts Phantom Manor spins." });
            var ids = context.Request.Headers["Idempotency-Key"];
            if (ids.Count != 1 || !PhantomManorRoundRequest.IsValidOperationId(ids[0]))
                return Results.BadRequest(new { code = "invalid-spin-operation", error = "Include one valid Phantom Manor spin operation ID." });

            try
            {
                var terms = new PhantomManorRoundRequest(ids[0]!, request.WagerPoints, request.UseFreeSpin, request.UseSpecialBoost);
                var result = await rounds.SpinAsync(account, terms, configuration.GetValue<bool>("PhantomManor:AllowNewPaidRounds"), token);
                if (result.Wallet.LegacyFreeGamesCountMismatch)
                    return Results.Conflict(new { code = "phantom-manor-legacy-free-spins-changed", error = "The available Phantom Manor free games changed. Refresh the game state." });
                if (result.Wallet.InsufficientLegacyFreeGames)
                    return Results.Conflict(new { code = "free-spins-unavailable", error = "No free spins are available.", freeSpinsRemaining = 0 });
                if (result.Wallet.InsufficientFunds)
                    return Results.Conflict(new { code = "insufficient-slot-credits", error = "There are insufficient slot credits for that wager.",
                        available = result.Wallet.BalanceBeforeCents / 100m, required = result.ChargedWagerCents / 100m,
                        preparedOperationPending = false });
                return Results.Ok(PhantomManorSpinResponse.From(result));
            }
            catch (PhantomManorFreeSpinsRequiredException exception)
            {
                return Results.Conflict(new { code = "free-spins-required", error = exception.Message });
            }
            catch (PhantomManorFreeSpinsUnavailableException exception)
            {
                return Results.Conflict(new { code = "free-spins-unavailable", error = exception.Message, freeSpinsRemaining = 0 });
            }
            catch (PhantomManorFreeWagerLockedException exception)
            {
                return Results.Conflict(new { code = "free-spin-wager-locked", error = exception.Message, lockedWagerPoints = exception.WagerPoints });
            }
            catch (PhantomManorLegacyFreeWagerUnplayableException exception)
            {
                return Results.Conflict(new { code = "legacy-free-spin-wager-unplayable", error = exception.Message, lockedWagerPoints = exception.WagerPoints });
            }
            catch (PhantomManorInsufficientSpecialPointsException exception)
            {
                return Results.Conflict(new { code = "insufficient-special-points", error = exception.Message,
                    available = exception.Available, required = exception.Required });
            }
            catch (PhantomManorRoundConflictException exception)
            {
                return Results.Conflict(new { code = "slot-operation-conflict", error = exception.Message,
                    pendingOperation = exception.PendingRequest });
            }
            catch (PhantomManorRoundCooldownException exception)
            {
                context.Response.Headers.RetryAfter = Math.Max(1, Math.Ceiling(exception.Remaining.TotalSeconds))
                    .ToString(CultureInfo.InvariantCulture);
                return Results.Json(new { error = exception.Message,
                    retryAfterMilliseconds = Math.Max(1, Math.Ceiling(exception.Remaining.TotalMilliseconds)) }, statusCode: 429);
            }
            catch (PhantomManorRoundPausedException exception)
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

        endpoints.MapGet(PhantomManorPaidRouteContract.StatePath, async (HttpContext context,
            [FromServices] PhantomManorRoundService rounds, CancellationToken token) =>
        {
            if (resolveAccount(context) is not { } account) return Results.Unauthorized();
            try { return Results.Ok(await rounds.ReadStateAsync(account, token)); }
            catch (GameWalletUnavailableException exception) { return Retry(exception); }
        }).WithMetadata(new GameWorkerPublicRoute(PhantomManorEngine.GameId, PhantomManorPaidRouteContract.StatePath, PhantomManorPaidRouteContract.AuditGameId));

        return endpoints;
    }

    private static IResult Retry(GameWalletUnavailableException exception) =>
        Results.Json(new { code = "retry-required", error = exception.Message }, statusCode: 503);
}
