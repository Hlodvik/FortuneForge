using System.Globalization;
using FortuneForge.Games.Abstractions.Wallet;
using FortuneForge.Games.Hosting;
using Microsoft.AspNetCore.Mvc;

namespace FortuneForge.Games.MidnightVault.Server;

public static class MidnightVaultPaidRouteContract
{
    public const string SpinPath = "/api/slots/spins";
    public const string StatePath = "/api/slots/midnight-vault/state";
    public const string AuditGameId = "dragon-hoard";
}

public sealed record MidnightVaultPaidSpinCommand(string GameId, long WagerPoints, bool UseFreeSpin, bool UseSpecialBoost);

public static class MidnightVaultPaidEndpoints
{
    public static IEndpointRouteBuilder MapMidnightVaultPaidRounds(this IEndpointRouteBuilder endpoints, Func<HttpContext, string?> resolveAccount)
    {
        endpoints.MapPreparedSlotSpin(new GameWorkerPublicRoute(MidnightVaultEngine.GameId, MidnightVaultPaidRouteContract.SpinPath, MidnightVaultPaidRouteContract.AuditGameId),
            async (MidnightVaultPaidSpinCommand request, HttpContext context,
            [FromServices] MidnightVaultRoundService rounds, IConfiguration configuration, CancellationToken token) =>
        {
            if (resolveAccount(context) is not { } account) return Results.Unauthorized();
            if (request.GameId != MidnightVaultEngine.GameId)
                return Results.BadRequest(new { error = "This endpoint accepts Midnight Vault spins." });
            var ids = context.Request.Headers["Idempotency-Key"];
            if (ids.Count != 1 || !MidnightVaultRoundRequest.IsValidOperationId(ids[0]))
                return Results.BadRequest(new { code = "invalid-spin-operation", error = "Include one valid Midnight Vault spin operation ID." });

            try
            {
                var terms = new MidnightVaultRoundRequest(ids[0]!, request.WagerPoints, request.UseFreeSpin, request.UseSpecialBoost);
                var result = await rounds.SpinAsync(account, terms, configuration.GetValue<bool>("MidnightVault:AllowNewPaidRounds"), token);
                if (result.Wallet.LegacyFreeGamesCountMismatch)
                    return Results.Conflict(new { code = "midnight-vault-legacy-free-spins-changed", error = "The available Midnight Vault free games changed. Refresh the game state." });
                if (result.Wallet.InsufficientLegacyFreeGames)
                    return Results.Conflict(new { code = "free-spins-unavailable", error = "No free spins are available.", freeSpinsRemaining = 0 });
                if (result.Wallet.InsufficientFunds)
                    return Results.Conflict(new { code = "insufficient-slot-credits", error = "There are insufficient slot credits for that wager.",
                        available = result.Wallet.BalanceBeforeCents / 100m, required = result.ChargedWagerCents / 100m,
                        preparedOperationPending = false });
                return Results.Ok(MidnightVaultSpinResponse.From(result));
            }
            catch (MidnightVaultFreeSpinsRequiredException exception)
            {
                return Results.BadRequest(new { code = "free-spins-required", error = exception.Message });
            }
            catch (MidnightVaultFreeSpinsUnavailableException exception)
            {
                return Results.Conflict(new { code = "free-spins-unavailable", error = exception.Message, freeSpinsRemaining = 0 });
            }
            catch (MidnightVaultFreeWagerLockedException exception)
            {
                return Results.Conflict(new { code = "midnight-vault-free-wager-locked", error = exception.Message, lockedWagerPoints = exception.WagerPoints });
            }
            catch (MidnightVaultInsufficientSpecialPointsException exception)
            {
                return Results.Conflict(new { code = "insufficient-special-points", error = exception.Message,
                    available = exception.Available, required = exception.Required });
            }
            catch (MidnightVaultRoundConflictException exception)
            {
                return Results.Conflict(new { code = "slot-operation-conflict", error = exception.Message,
                    pendingOperation = exception.PendingRequest });
            }
            catch (MidnightVaultRoundCooldownException exception)
            {
                context.Response.Headers.RetryAfter = Math.Max(1, Math.Ceiling(exception.Remaining.TotalSeconds))
                    .ToString(CultureInfo.InvariantCulture);
                return Results.Json(new { error = exception.Message,
                    retryAfterMilliseconds = Math.Max(1, Math.Ceiling(exception.Remaining.TotalMilliseconds)) }, statusCode: 429);
            }
            catch (MidnightVaultRoundPausedException exception)
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

        endpoints.MapGet(MidnightVaultPaidRouteContract.StatePath, async (HttpContext context,
            [FromServices] MidnightVaultRoundService rounds, CancellationToken token) =>
        {
            if (resolveAccount(context) is not { } account) return Results.Unauthorized();
            try { return Results.Ok(await rounds.ReadStateAsync(account, token)); }
            catch (GameWalletUnavailableException exception) { return Retry(exception); }
        }).WithMetadata(new GameWorkerPublicRoute(MidnightVaultEngine.GameId, MidnightVaultPaidRouteContract.StatePath, MidnightVaultPaidRouteContract.AuditGameId));

        return endpoints;
    }

    private static IResult Retry(GameWalletUnavailableException exception) =>
        Results.Json(new { code = "retry-required", error = exception.Message }, statusCode: 503);
}
