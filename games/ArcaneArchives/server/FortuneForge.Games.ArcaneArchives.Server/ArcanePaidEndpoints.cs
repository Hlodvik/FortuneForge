using FortuneForge.Games.Abstractions.Wallet;
using FortuneForge.Games.Hosting;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace FortuneForge.Games.ArcaneArchives.Server;

public static class ArcanePaidEndpoints
{
    public static IEndpointRouteBuilder MapArcanePaidRounds(this IEndpointRouteBuilder endpoints,
        Func<HttpContext, string?> resolveAccount)
    {
        async Task<IResult> SpinAsync(ArcanePaidSpinCommand request, HttpContext context,
            [FromServices] ArcaneRoundService rounds, CancellationToken token)
        {
            if (resolveAccount(context) is not { } userId) return Results.Unauthorized();
            if (request.GameId != ArcaneArchivesEngine.GameId)
                return Results.BadRequest(new { error = "This endpoint accepts Arcane Archives spins." });

            var operationIds = context.Request.Headers["Idempotency-Key"];
            if (operationIds.Count != 1 || !ArcaneRoundRequest.IsValidOperationId(operationIds[0]))
                return Results.BadRequest(new { code = "invalid-spin-operation", error = "Include one valid Arcane spin operation ID." });

            try
            {
                var configuration = context.RequestServices.GetRequiredService<IConfiguration>();
                var result = await rounds.SpinAsync(userId,
                    new(operationIds[0]!, request.WagerPoints, request.UseFreeSpin, request.UseSpecialBoost),
                    configuration.GetValue<bool>("Arcane:AllowNewPaidRounds"), token);
                if (result.Wallet.InsufficientLegacyFreeGames)
                    return Results.Conflict(new { code = "free-spins-unavailable", error = "No free spins are available.", freeSpinsRemaining = 0 });
                if (result.Wallet.InsufficientFunds)
                    return Results.Conflict(new
                    {
                        code = "insufficient-slot-credits",
                        error = "There are insufficient slot credits for that wager.",
                        available = result.Wallet.BalanceBeforeCents / 100m,
                        required = request.WagerPoints * ArcaneArchivesEngine.CreateGame().Wagering.PointValueInCents / 100m
                    });
                return Results.Ok(ArcaneSpinResponse.From(result));
            }
            catch (ArcaneObsoleteRoundException exception)
            {
                return Results.Conflict(new { code = ArcanePaidRouteContract.ObsoleteRoundCode, error = exception.Message });
            }
            catch (GameWalletUnavailableException exception)
            {
                return Results.Json(new { code = "retry-required", error = exception.Message }, statusCode: 503);
            }
            catch (ArcaneRoundConflictException exception)
            {
                return Results.Conflict(new { code = "slot-operation-conflict", error = exception.Message, pendingOperation = exception.PendingRequest });
            }
            catch (ArcaneRoundStartPausedException exception)
            {
                return Results.Json(new { code = "slot-prepared-start-paused", error = exception.Message }, statusCode: 503);
            }
            catch (ArcaneRoundCooldownException exception)
            {
                var retryAfterMilliseconds = Math.Max(1, (int)Math.Ceiling(exception.Remaining.TotalMilliseconds));
                context.Response.Headers.RetryAfter = Math.Max(1, (int)Math.Ceiling(exception.Remaining.TotalSeconds))
                    .ToString(System.Globalization.CultureInfo.InvariantCulture);
                return Results.Json(new { error = "Wait for the previous spin to finish.", retryAfterMilliseconds }, statusCode: 429);
            }
            catch (ArcaneTomeActivationRequiredException exception)
            {
                return Results.Conflict(new { code = "arcane-tome-required", error = exception.Message });
            }
            catch (ArcanePotionWagerLockedException exception)
            {
                return Results.Conflict(new
                {
                    code = "arcane-potion-wager-locked", error = exception.Message, lockedWagerPoints = exception.LockedWagerPoints
                });
            }
            catch (ArgumentException exception)
            {
                return Results.BadRequest(new { error = exception.Message });
            }
        }

        endpoints.MapPost(ArcanePaidRouteContract.SpinPath, SpinAsync)
            .WithMetadata(new GameWorkerPublicRoute(ArcaneArchivesEngine.GameId, ArcanePaidRouteContract.SpinPath, ArcaneFeatureEndpoints.AuditGameId));
        endpoints.MapPost(ArcanePaidRouteContract.PreparedSpinPath, SpinAsync)
            .WithMetadata(new GameWorkerPublicRoute(ArcaneArchivesEngine.GameId, ArcanePaidRouteContract.PreparedSpinPath, ArcaneFeatureEndpoints.AuditGameId));
        endpoints.MapGet(ArcanePaidRouteContract.StatePath, async (HttpContext context,
            [FromServices] ArcaneRoundStore rounds, [FromServices] IGameWalletAccountReader wallet, CancellationToken token) =>
        {
            if (resolveAccount(context) is not { } userId) return Results.Unauthorized();
            try
            {
                var roundState = rounds.ReadAsync(userId, token);
                var walletState = wallet.GetStateAsync(userId, token);
                await Task.WhenAll(roundState, walletState);
                var state = await roundState;
                var freeSpinsRemaining = checked((int)(await walletState).LegacyFreeGamesRemaining);
                return Results.Ok(new
                {
                    freeSpinsRemaining,
                    freeSpinWagerPoints = freeSpinsRemaining > 0 ? state.FreeSpinWagerPoints : null,
                    specialPointsBalance = 0,
                    energyBalance = 0,
                    sealCollections = Array.Empty<object>(),
                    freeSpinFeatureMode = freeSpinsRemaining > 0 ? state.FreeSpinFeatureMode : null,
                    arcaneTome = ArcaneTome.ToView(state.Tome),
                    arcanePotions = ArcanePotionView.FromState(state.Potions),
                    pendingOperation = state.PendingRequest
                });
            }
            catch (ArcaneObsoleteRoundException exception)
            {
                return Results.Conflict(new { code = ArcanePaidRouteContract.ObsoleteRoundCode, error = exception.Message });
            }
            catch (GameWalletUnavailableException exception)
            {
                return Results.Json(new { code = "retry-required", error = exception.Message }, statusCode: 503);
            }
        }).WithMetadata(new GameWorkerPublicRoute(ArcaneArchivesEngine.GameId, ArcanePaidRouteContract.StatePath, ArcaneFeatureEndpoints.AuditGameId));
        return endpoints;
    }
}
