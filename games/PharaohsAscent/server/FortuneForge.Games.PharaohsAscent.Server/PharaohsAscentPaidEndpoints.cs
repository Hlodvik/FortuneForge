using System.Globalization;
using FortuneForge.Games.Abstractions.Wallet;
using FortuneForge.Games.Hosting;
using Microsoft.AspNetCore.Mvc;

namespace FortuneForge.Games.PharaohsAscent.Server;

public static class PharaohsAscentPaidRouteContract
{
    public const string SpinPath = "/api/slots/spins";
    public const string StatePath = "/api/slots/pharaohs-ascent/state";
    public const string ActionPath = "/api/slots/pharaohs-ascent/actions";
    public const string AuditGameId = "pharaohs-ascent";
}

public sealed record PharaohsAscentPaidSpinCommand(string GameId, long WagerPoints, bool UseFreeSpin, bool UseSpecialBoost);

public static class PharaohsAscentPaidEndpoints
{
    public static IEndpointRouteBuilder MapPharaohsAscentPaidRounds(this IEndpointRouteBuilder endpoints, Func<HttpContext, string?> resolveAccount)
    {
        endpoints.MapPreparedSlotSpin(new GameWorkerPublicRoute(PharaohsAscentEngine.GameId, PharaohsAscentPaidRouteContract.SpinPath, PharaohsAscentPaidRouteContract.AuditGameId),
            async (PharaohsAscentPaidSpinCommand request, HttpContext context,
            [FromServices] PharaohsAscentRoundService rounds, IConfiguration configuration, CancellationToken token) =>
        {
            if (resolveAccount(context) is not { } account) return Results.Unauthorized();
            if (request.GameId != PharaohsAscentEngine.GameId)
                return Results.BadRequest(new { error = "This endpoint accepts Pharaoh's Ascent spins." });
            var ids = context.Request.Headers["Idempotency-Key"];
            if (ids.Count != 1 || !PharaohsAscentRoundRequest.IsValidOperationId(ids[0]))
                return Results.BadRequest(new { code = "invalid-spin-operation", error = "Include one valid Pharaoh's Ascent spin operation ID." });

            try
            {
                var terms = new PharaohsAscentRoundRequest(ids[0]!, request.WagerPoints, request.UseFreeSpin, request.UseSpecialBoost);
                var result = await rounds.SpinAsync(account, terms, configuration.GetValue<bool>("PharaohsAscent:AllowNewPaidRounds"), token);
                if (result.Wallet.LegacyFreeGamesCountMismatch)
                    return Results.Conflict(new { code = "pharaohs-ascent-legacy-free-spins-changed", error = "The available Pharaoh's Ascent free games changed. Refresh the game state." });
                if (result.Wallet.InsufficientLegacyFreeGames)
                    return Results.Conflict(new { code = "free-spins-unavailable", error = "No free spins are available.", freeSpinsRemaining = 0 });
                if (result.Wallet.InsufficientFunds)
                    return Results.Conflict(new { code = "insufficient-slot-credits", error = "There are insufficient slot credits for that wager.",
                        available = result.Wallet.BalanceBeforeCents / 100m, required = result.ChargedWagerCents / 100m,
                        preparedOperationPending = false });
                return Results.Ok(PharaohsAscentSpinResponse.From(result));
            }
            catch (PharaohsAscentFreeSpinsRequiredException exception)
            {
                return Results.Conflict(new { code = "free-spins-required", error = exception.Message });
            }
            catch (SealedTombConflictException exception)
            {
                return Results.Conflict(new { code = exception.Code, error = exception.Message });
            }
            catch (PharaohsAscentFreeSpinsUnavailableException exception)
            {
                return Results.Conflict(new { code = "free-spins-unavailable", error = exception.Message, freeSpinsRemaining = 0 });
            }
            catch (PharaohsAscentFreeWagerLockedException exception)
            {
                return Results.Conflict(new { code = "free-spin-wager-locked", error = exception.Message, lockedWagerPoints = exception.WagerPoints });
            }
            catch (PharaohsAscentLegacyFreeWagerUnplayableException exception)
            {
                return Results.Conflict(new { code = "legacy-free-spin-wager-unplayable", error = exception.Message, lockedWagerPoints = exception.WagerPoints });
            }
            catch (PharaohsAscentInsufficientSpecialPointsException exception)
            {
                return Results.Conflict(new { code = "insufficient-special-points", error = exception.Message,
                    available = exception.Available, required = exception.Required });
            }
            catch (PharaohsAscentRoundConflictException exception)
            {
                return Results.Conflict(new { code = "slot-operation-conflict", error = exception.Message,
                    pendingOperation = exception.PendingRequest });
            }
            catch (PharaohsAscentRoundCooldownException exception)
            {
                context.Response.Headers.RetryAfter = Math.Max(1, Math.Ceiling(exception.Remaining.TotalSeconds))
                    .ToString(CultureInfo.InvariantCulture);
                return Results.Json(new { error = exception.Message,
                    retryAfterMilliseconds = Math.Max(1, Math.Ceiling(exception.Remaining.TotalMilliseconds)) }, statusCode: 429);
            }
            catch (PharaohsAscentRoundPausedException exception)
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

        endpoints.MapGet(PharaohsAscentPaidRouteContract.StatePath, async (HttpContext context,
            [FromServices] PharaohsAscentRoundService rounds, CancellationToken token) =>
        {
            if (resolveAccount(context) is not { } account) return Results.Unauthorized();
            try { return Results.Ok(await rounds.ReadStateAsync(account, token)); }
            catch (GameWalletUnavailableException exception) { return Retry(exception); }
        }).WithMetadata(new GameWorkerPublicRoute(PharaohsAscentEngine.GameId, PharaohsAscentPaidRouteContract.StatePath, PharaohsAscentPaidRouteContract.AuditGameId));

        endpoints.MapPost(PharaohsAscentPaidRouteContract.ActionPath, async (SealedTombAction action, HttpContext context,
            [FromServices] PharaohsAscentRoundService rounds, CancellationToken token) =>
        {
            if (resolveAccount(context) is not { } account) return Results.Unauthorized();
            var operations = context.Request.Headers["Idempotency-Key"];
            if (operations.Count != 1 || !PharaohsAscentRoundRequest.IsValidOperationId(operations[0]))
                return Results.BadRequest(new { code = "invalid-tomb-operation", error = "Include one valid Sealed Tomb action operation ID." });
            try { return Results.Ok(await rounds.ActAsync(account, operations[0]!, action, token)); }
            catch (SealedTombConflictException exception)
            { return Results.Conflict(new { code = exception.Code, error = exception.Message }); }
            catch (PharaohsAscentRoundConflictException exception)
            { return Results.Conflict(new { code = "tomb-operation-pending", error = exception.Message }); }
            catch (PharaohsAscentRoundCooldownException exception)
            { return Results.Json(new { error = exception.Message, retryAfterMilliseconds = Math.Ceiling(exception.Remaining.TotalMilliseconds) }, statusCode: 429); }
            catch (GameWalletUnavailableException exception) { return Retry(exception); }
            catch (ArgumentException exception) { return Results.BadRequest(new { error = exception.Message }); }
        }).WithMetadata(new GameWorkerPublicRoute(PharaohsAscentEngine.GameId, PharaohsAscentPaidRouteContract.ActionPath, PharaohsAscentPaidRouteContract.AuditGameId));

        return endpoints;
    }

    private static IResult Retry(GameWalletUnavailableException exception) =>
        Results.Json(new { code = "retry-required", error = exception.Message }, statusCode: 503);
}
