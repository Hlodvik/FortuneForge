using System.Globalization;
using FortuneForge.Games.Abstractions.Wallet;
using FortuneForge.Games.Hosting;
using Microsoft.AspNetCore.Mvc;

namespace FortuneForge.Games.NeonNights.Server;

public static class NeonPrimaryRouteContract
{
    public const string SpinPath = "/api/slots/spins";
    public const string StatePath = "/api/slots/neon/state";
    public const string NitroPath = "/api/slots/neon/nitro-choice";
    public const string AdminPath = "/api/slots/admin/neon-trigger";
    public const string LegacyNitroCode = "neon-legacy-nitro-choice";
    internal const string AuditGameId = "neon-nights";
    public static bool IsSpecificActionPath(string? path) => path is StatePath or NitroPath or AdminPath;
}

public static class NeonPrimaryRoundEndpoints
{
    public static IEndpointRouteBuilder MapNeonPrimaryRounds(this IEndpointRouteBuilder endpoints,
        Func<HttpContext, string?> resolveAccount)
    {
        endpoints.MapPreparedSlotSpin(Route(NeonPrimaryRouteContract.SpinPath), (NeonPrimarySpinCommand request, HttpContext context,
            [FromServices] NeonPrimaryRoundService rounds, CancellationToken token) => Run(context, resolveAccount, async account =>
        {
            if (request.GameId != NeonArcadeSlotDefinitions.GameId)
                return Results.BadRequest(new { error = "This endpoint accepts Neon Nights spins." });
            if (ReadOperation(context) is not { } operation)
                return InvalidOperationId();
            var result = await rounds.SpinAsync(account, new(operation, request.WagerPoints, request.UseFreeSpin,
                request.UseSpecialBoost), AllowStart(context), token);
            return WriteResult(result);
        }));

        endpoints.MapGet(NeonPrimaryRouteContract.StatePath, (HttpContext context,
            [FromServices] NeonPrimaryRoundService rounds, CancellationToken token) => Run(context, resolveAccount, async account =>
            Results.Ok(await rounds.ReadStateAsync(account, token)))).WithMetadata(Route(NeonPrimaryRouteContract.StatePath));

        endpoints.MapPost(NeonPrimaryRouteContract.NitroPath, (NeonPrimaryNitroCommand request, HttpContext context,
            [FromServices] NeonPrimaryRoundService rounds, CancellationToken token) => Run(context, resolveAccount, async account =>
        {
            if (request.SpinId == Guid.Empty || request.ReelIndex is not { } reel || !NeonNitroChoice.IsChoiceReel(reel))
                return Results.BadRequest(new { error = "Choose one of the five reels for Nitro Wild." });
            return WriteResult(await rounds.SelectNitroAsync(account, request.SpinId, reel, token));
        })).WithMetadata(Route(NeonPrimaryRouteContract.NitroPath));

        endpoints.MapPost(NeonPrimaryRouteContract.AdminPath, (NeonPrimaryAdminCommand request, HttpContext context,
            [FromServices] NeonPrimaryRoundService rounds, CancellationToken token) => Run(context, resolveAccount, async account =>
        {
            if (request.Symbol is not ("car" or "disco-ball"))
                return Results.BadRequest(new { error = "Unknown Neon Special Plays symbol." });
            if (ReadOperation(context) is not { } operation)
                return InvalidOperationId();
            if (!AllowStart(context)) throw new NeonPrimaryRoundPausedException();
            if (!await rounds.GrantAsync(account, operation, request.WagerPoints, request.Symbol == "car",
                    request.Symbol == "disco-ball", token))
                return Results.Conflict(new { error = "Finish the current Neon spin or Special Plays round first." });
            return Results.Ok(await rounds.ReadStateAsync(account, token));
        })).WithMetadata(Route(NeonPrimaryRouteContract.AdminPath));

        return endpoints;
    }

    private static bool AllowStart(HttpContext context) =>
        context.RequestServices.GetRequiredService<IConfiguration>().GetValue<bool>("Neon:AllowNewPaidRounds");

    private static string? ReadOperation(HttpContext context)
    {
        var values = context.Request.Headers["Idempotency-Key"];
        return values.Count == 1 && NeonRoundRequest.IsValidOperationId(values[0]) ? values[0] : null;
    }

    private static IResult InvalidOperationId() =>
        Results.BadRequest(new { code = "invalid-spin-operation", error = "Include one valid Neon operation ID." });

    private static GameWorkerPublicRoute Route(string path) =>
        new(NeonArcadeSlotDefinitions.GameId, path, NeonPrimaryRouteContract.AuditGameId);

    private static async Task<IResult> Run(HttpContext context, Func<HttpContext, string?> resolveAccount,
        Func<string, Task<IResult>> action)
    {
        if (resolveAccount(context) is not { } account) return Results.Unauthorized();
        try { return await action(account); }
        catch (NeonLegacyNitroChoiceException)
        {
            return Results.Conflict(new { code = NeonPrimaryRouteContract.LegacyNitroCode });
        }
        catch (NeonPrimaryNitroConflictException exception)
        {
            return Results.Conflict(new { code = "nitro-choice-conflict", error = exception.Message });
        }
        catch (NeonPrimaryRoundConflictException exception)
        {
            return Results.Conflict(new { code = "slot-operation-conflict", error = exception.Message, pendingOperation = exception.PendingRequest });
        }
        catch (NeonDiscoWagerLockedException exception)
        {
            return Results.Conflict(new { code = "neon-disco-wager-locked", error = exception.Message, lockedWagerPoints = exception.LockedWagerPoints });
        }
        catch (NeonPrimaryFreeSpinsException exception)
        {
            return Results.Conflict(new { code = "free-spins-unavailable", error = exception.Message, freeSpinsRemaining = 0 });
        }
        catch (NeonPrimaryFreeSpinsRequiredException exception)
        {
            return Results.Conflict(new { code = "free-spins-required", error = exception.Message });
        }
        catch (NeonPrimaryRoundPausedException exception)
        {
            return Results.Json(new { code = "slot-prepared-start-paused", error = exception.Message }, statusCode: 503);
        }
        catch (NeonPrimaryRoundCooldownException exception)
        {
            context.Response.Headers.RetryAfter = Math.Max(1, Math.Ceiling(exception.Remaining.TotalSeconds)).ToString(CultureInfo.InvariantCulture);
            return Results.Json(new { error = exception.Message,
                retryAfterMilliseconds = Math.Max(1, Math.Ceiling(exception.Remaining.TotalMilliseconds)) }, statusCode: 429);
        }
        catch (GameWalletUnavailableException exception)
        {
            return Results.Json(new { code = "retry-required", error = exception.Message }, statusCode: 503);
        }
        catch (ArgumentException exception)
        {
            return Results.BadRequest(new { error = exception.Message });
        }
    }

    private static IResult WriteResult(NeonRoundResult result)
    {
        if (result.PendingNeonNitroChoice is { } choice)
            return Results.Accepted(value: new { pendingNeonNitroChoice = choice });
        if (result.Wallet is not { } wallet) throw new GameWalletUnavailableException();
        if (wallet.LegacyFreeGamesCountMismatch)
            return Results.Conflict(new { code = "neon-legacy-plays-changed", error = "The available Neon plays changed. Refresh the game state.",
                freeSpinsRemaining = wallet.LegacyFreeGamesRemaining });
        if (wallet.InsufficientLegacyFreeGames)
            return Results.Conflict(new { code = "free-spins-unavailable", error = "No free spins are available.", freeSpinsRemaining = 0 });
        if (wallet.InsufficientFunds)
            return Results.Conflict(new { code = "insufficient-slot-credits",
                error = "There are insufficient slot credits for that wager.", available = wallet.BalanceBeforeCents / 100m,
                required = result.ChargedWagerCents / 100m, preparedOperationPending = false });
        return Results.Ok(result.Spin ?? throw new GameWalletUnavailableException());
    }
}

public sealed record NeonPrimarySpinCommand(string GameId, long WagerPoints, bool UseFreeSpin, bool UseSpecialBoost);
public sealed record NeonPrimaryNitroCommand(Guid SpinId, int? ReelIndex);
public sealed record NeonPrimaryAdminCommand(string Symbol, long WagerPoints);
