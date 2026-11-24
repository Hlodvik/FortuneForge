using System.Globalization;
using FortuneForge.Games.Abstractions.Wallet;
using FortuneForge.Games.Hosting;
using Microsoft.AspNetCore.Mvc;

namespace FortuneForge.Games.CosmicFortune.Server;

public static class CosmicFortunePaidRouteContract
{
    public const string SpinPath = "/api/slots/spins";
    public const string StatePath = "/api/slots/cosmic-fortune/state";
    public const string ActionPath = "/api/slots/cosmic-fortune/actions";
    public const string AuditGameId = "cosmic-fortune";
}

public sealed record CosmicFortunePaidSpinCommand(string GameId, long WagerPoints, bool UseFreeSpin, bool UseSpecialBoost);

public static class CosmicFortunePaidEndpoints
{
    public static IEndpointRouteBuilder MapCosmicFortunePaidRounds(this IEndpointRouteBuilder endpoints, Func<HttpContext, string?> resolveAccount)
    {
        endpoints.MapPreparedSlotSpin(new GameWorkerPublicRoute(CosmicFortuneEngine.GameId, CosmicFortunePaidRouteContract.SpinPath, CosmicFortunePaidRouteContract.AuditGameId),
            async (CosmicFortunePaidSpinCommand request, HttpContext context,
            [FromServices] CosmicFortuneRoundService rounds, IConfiguration configuration, CancellationToken token) =>
        {
            if (resolveAccount(context) is not { } account) return Results.Unauthorized();
            if (request.GameId != CosmicFortuneEngine.GameId)
                return Results.BadRequest(new { error = "This endpoint accepts Pharaoh's Ascent spins." });
            var ids = context.Request.Headers["Idempotency-Key"];
            if (ids.Count != 1 || !CosmicFortuneRoundRequest.IsValidOperationId(ids[0]))
                return Results.BadRequest(new { code = "invalid-spin-operation", error = "Include one valid Pharaoh's Ascent spin operation ID." });

            try
            {
                var terms = new CosmicFortuneRoundRequest(ids[0]!, request.WagerPoints, request.UseFreeSpin, request.UseSpecialBoost);
                var result = await rounds.SpinAsync(account, terms, configuration.GetValue<bool>("CosmicFortune:AllowNewPaidRounds"), token);
                if (result.Wallet.LegacyFreeGamesCountMismatch)
                    return Results.Conflict(new { code = "cosmic-fortune-legacy-free-spins-changed", error = "The available Pharaoh's Ascent free games changed. Refresh the game state." });
                if (result.Wallet.InsufficientLegacyFreeGames)
                    return Results.Conflict(new { code = "free-spins-unavailable", error = "No free spins are available.", freeSpinsRemaining = 0 });
                if (result.Wallet.InsufficientFunds)
                    return Results.Conflict(new { code = "insufficient-slot-credits", error = "There are insufficient slot credits for that wager.",
                        available = result.Wallet.BalanceBeforeCents / 100m, required = result.ChargedWagerCents / 100m,
                        preparedOperationPending = false });
                return Results.Ok(CosmicFortuneSpinResponse.From(result));
            }
            catch (CosmicFortuneFreeSpinsRequiredException exception)
            {
                return Results.Conflict(new { code = "free-spins-required", error = exception.Message });
            }
            catch (SealedTombConflictException exception)
            {
                return Results.Conflict(new { code = exception.Code, error = exception.Message });
            }
            catch (CosmicFortuneFreeSpinsUnavailableException exception)
            {
                return Results.Conflict(new { code = "free-spins-unavailable", error = exception.Message, freeSpinsRemaining = 0 });
            }
            catch (CosmicFortuneFreeWagerLockedException exception)
            {
                return Results.Conflict(new { code = "free-spin-wager-locked", error = exception.Message, lockedWagerPoints = exception.WagerPoints });
            }
            catch (CosmicFortuneLegacyFreeWagerUnplayableException exception)
            {
                return Results.Conflict(new { code = "legacy-free-spin-wager-unplayable", error = exception.Message, lockedWagerPoints = exception.WagerPoints });
            }
            catch (CosmicFortuneInsufficientSpecialPointsException exception)
            {
                return Results.Conflict(new { code = "insufficient-special-points", error = exception.Message,
                    available = exception.Available, required = exception.Required });
            }
            catch (CosmicFortuneRoundConflictException exception)
            {
                return Results.Conflict(new { code = "slot-operation-conflict", error = exception.Message,
                    pendingOperation = exception.PendingRequest });
            }
            catch (CosmicFortuneRoundCooldownException exception)
            {
                context.Response.Headers.RetryAfter = Math.Max(1, Math.Ceiling(exception.Remaining.TotalSeconds))
                    .ToString(CultureInfo.InvariantCulture);
                return Results.Json(new { error = exception.Message,
                    retryAfterMilliseconds = Math.Max(1, Math.Ceiling(exception.Remaining.TotalMilliseconds)) }, statusCode: 429);
            }
            catch (CosmicFortuneRoundPausedException exception)
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

        endpoints.MapGet(CosmicFortunePaidRouteContract.StatePath, async (HttpContext context,
            [FromServices] CosmicFortuneRoundService rounds, CancellationToken token) =>
        {
            if (resolveAccount(context) is not { } account) return Results.Unauthorized();
            try { return Results.Ok(await rounds.ReadStateAsync(account, token)); }
            catch (GameWalletUnavailableException exception) { return Retry(exception); }
        }).WithMetadata(new GameWorkerPublicRoute(CosmicFortuneEngine.GameId, CosmicFortunePaidRouteContract.StatePath, CosmicFortunePaidRouteContract.AuditGameId));

        endpoints.MapPost(CosmicFortunePaidRouteContract.ActionPath, async (SealedTombAction action, HttpContext context,
            [FromServices] CosmicFortuneRoundService rounds, CancellationToken token) =>
        {
            if (resolveAccount(context) is not { } account) return Results.Unauthorized();
            var operations = context.Request.Headers["Idempotency-Key"];
            if (operations.Count != 1 || !CosmicFortuneRoundRequest.IsValidOperationId(operations[0]))
                return Results.BadRequest(new { code = "invalid-tomb-operation", error = "Include one valid Sealed Tomb action operation ID." });
            try { return Results.Ok(await rounds.ActAsync(account, operations[0]!, action, token)); }
            catch (SealedTombConflictException exception)
            { return Results.Conflict(new { code = exception.Code, error = exception.Message }); }
            catch (CosmicFortuneRoundConflictException exception)
            { return Results.Conflict(new { code = "tomb-operation-pending", error = exception.Message }); }
            catch (CosmicFortuneRoundCooldownException exception)
            { return Results.Json(new { error = exception.Message, retryAfterMilliseconds = Math.Ceiling(exception.Remaining.TotalMilliseconds) }, statusCode: 429); }
            catch (GameWalletUnavailableException exception) { return Retry(exception); }
            catch (ArgumentException exception) { return Results.BadRequest(new { error = exception.Message }); }
        }).WithMetadata(new GameWorkerPublicRoute(CosmicFortuneEngine.GameId, CosmicFortunePaidRouteContract.ActionPath, CosmicFortunePaidRouteContract.AuditGameId));

        return endpoints;
    }

    private static IResult Retry(GameWalletUnavailableException exception) =>
        Results.Json(new { code = "retry-required", error = exception.Message }, statusCode: 503);
}
