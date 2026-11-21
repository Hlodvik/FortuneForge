using System.Globalization;
using FortuneForge.Games.Abstractions.Wallet;
using FortuneForge.Games.Hosting;
using Microsoft.AspNetCore.Mvc;

namespace FortuneForge.Games.GoldFoundry.Server;

public static class GoldFoundryPaidRouteContract
{
    public const string SpinPath = "/api/slots/spins";
    public const string StatePath = "/api/slots/gold-foundry/state";
    public const string AuditGameId = "gold-foundry";
}

public sealed record GoldFoundryPaidSpinCommand(string GameId, long WagerPoints, bool UseFreeSpin, bool UseSpecialBoost);

public static class GoldFoundryPaidEndpoints
{
    public static IEndpointRouteBuilder MapGoldFoundryPaidRounds(this IEndpointRouteBuilder endpoints,
        Func<HttpContext, string?> resolveAccount)
    {
        endpoints.MapPreparedSlotSpin(new GameWorkerPublicRoute(GoldFoundryEngine.GameId, GoldFoundryPaidRouteContract.SpinPath, GoldFoundryPaidRouteContract.AuditGameId),
            async (GoldFoundryPaidSpinCommand request, HttpContext context,
            [FromServices] GoldFoundryRoundService rounds, IConfiguration configuration, CancellationToken token) =>
        {
            if (resolveAccount(context) is not { } account) return Results.Unauthorized();
            if (request.GameId != GoldFoundryEngine.GameId)
                return Results.BadRequest(new { error = "This endpoint accepts Gold Foundry spins." });
            var keys = context.Request.Headers["Idempotency-Key"];
            if (keys.Count != 1 || !GoldFoundryRoundRequest.IsValidOperationId(keys[0]))
                return Results.BadRequest(new { code = "invalid-spin-operation", error = "Include one valid Gold Foundry spin operation ID." });

            try
            {
                var terms = new GoldFoundryRoundRequest(keys[0]!, request.WagerPoints, request.UseFreeSpin, request.UseSpecialBoost);
                var result = await rounds.SpinAsync(account, terms, configuration.GetValue<bool>("GoldFoundry:AllowNewPaidRounds"), token);
                if (result.Wallet.InsufficientFunds)
                    return Results.Conflict(new
                    {
                        code = "insufficient-slot-credits",
                        error = "There are insufficient slot credits for that wager.",
                        available = result.Wallet.BalanceBeforeCents / 100m,
                        required = result.ChargedWagerCents / 100m,
                        preparedOperationPending = false
                    });
                return Results.Ok(GoldFoundrySpinResponse.From(result));
            }
            catch (GoldFoundryRoundConflictException exception)
            {
                return Results.Conflict(new { code = "slot-operation-conflict", error = exception.Message, pendingOperation = exception.PendingRequest });
            }
            catch (GoldFoundryRoundCooldownException exception)
            {
                context.Response.Headers.RetryAfter = Math.Max(1, Math.Ceiling(exception.Remaining.TotalSeconds))
                    .ToString(CultureInfo.InvariantCulture);
                return Results.Json(new { error = exception.Message,
                    retryAfterMilliseconds = Math.Max(1, Math.Ceiling(exception.Remaining.TotalMilliseconds)) }, statusCode: 429);
            }
            catch (GoldFoundryRoundPausedException exception)
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

        endpoints.MapGet(GoldFoundryPaidRouteContract.StatePath, async (HttpContext context,
            [FromServices] GoldFoundryRoundService rounds, CancellationToken token) =>
        {
            if (resolveAccount(context) is not { } account) return Results.Unauthorized();
            try { return Results.Ok(await rounds.ReadStateAsync(account, token)); }
            catch (GameWalletUnavailableException exception) { return Retry(exception); }
        }).WithMetadata(new GameWorkerPublicRoute(GoldFoundryEngine.GameId, GoldFoundryPaidRouteContract.StatePath,
            GoldFoundryPaidRouteContract.AuditGameId));

        return endpoints;
    }

    private static IResult Retry(GameWalletUnavailableException exception) =>
        Results.Json(new { code = "retry-required", error = exception.Message }, statusCode: 503);
}
