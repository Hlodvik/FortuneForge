using FortuneForge.Server.Accounts;
using FortuneForge.Server.Accounts.Models;
using FortuneForge.Server.Accounts.Security;
using Google.Cloud.Firestore;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;

namespace FortuneForge.Server.Cards.CasinoWar;

[ApiController]
[Route("api/games/casino-war")]
public sealed class CasinoWarController(FirestoreDb database, AccountService accountService, IConfiguration configuration, ILogger<CasinoWarController> logger) : ControllerBase
{
    [HttpGet("status")][EnableRateLimiting(RateLimitPolicies.SlotReads)] public async Task<ActionResult> Status(CancellationToken cancellationToken)
    {
        if (Disabled() is { } unavailable) return unavailable; var account = await AccountAsync(cancellationToken);
        return account is null ? Unauthorized(new CasinoWarErrorResponse("casino-war-authentication-required", "Sign in to play Casino War.")) : Ok(new CasinoWarStatusResponse(true, CasinoWarMoney.ToRand(CasinoWarMoney.MinimumPrimaryStakeCents), CasinoWarMoney.ToRand(CasinoWarMoney.MaximumPrimaryStakeCents), CasinoWarMoney.ToRand(CasinoWarMoney.StakeIncrementCents), CasinoWarMoney.ToRand(CasinoWarMoney.MaximumTieStakeCents), account.Balances.SlotsCredits, "six-deck-casino-war"));
    }
    [HttpPost("rounds")][EnableRateLimiting(RateLimitPolicies.SlotSpins)] public async Task<ActionResult> Start(CreateCasinoWarRoundRequest request, [FromHeader(Name = "Idempotency-Key")] string? idempotencyKey, CancellationToken cancellationToken)
    {
        if (Disabled() is { } unavailable) return unavailable; var account = await AccountAsync(cancellationToken); if (account is null) return Unauthorized(new CasinoWarErrorResponse("casino-war-authentication-required", "Sign in to play Casino War."));
        try { var result = await Service().StartAsync(account.UserId, request, idempotencyKey ?? string.Empty, cancellationToken); return CreatedAtAction(nameof(Get), new { roundId = result.RoundId }, result); } catch (Exception exception) { return CasinoWarHttp.FromException(this, exception, logger); }
    }
    [HttpGet("rounds/{roundId}")][EnableRateLimiting(RateLimitPolicies.SlotReads)] public async Task<ActionResult> Get(string roundId, CancellationToken cancellationToken)
    {
        if (Disabled() is { } unavailable) return unavailable; var account = await AccountAsync(cancellationToken); if (account is null) return Unauthorized(new CasinoWarErrorResponse("casino-war-authentication-required", "Sign in to play Casino War."));
        try { return Ok(await Service().GetAsync(account.UserId, roundId, cancellationToken)); } catch (Exception exception) { return CasinoWarHttp.FromException(this, exception, logger); }
    }
    [HttpPost("rounds/{roundId}/decision")][EnableRateLimiting(RateLimitPolicies.SlotSpins)] public async Task<ActionResult> Decide(string roundId, DecideCasinoWarRoundRequest request, [FromHeader(Name = "Idempotency-Key")] string? idempotencyKey, CancellationToken cancellationToken)
    {
        if (Disabled() is { } unavailable) return unavailable; var account = await AccountAsync(cancellationToken); if (account is null) return Unauthorized(new CasinoWarErrorResponse("casino-war-authentication-required", "Sign in to play Casino War."));
        try { return Ok(await Service().DecideAsync(account.UserId, roundId, request, idempotencyKey ?? string.Empty, cancellationToken)); } catch (Exception exception) { return CasinoWarHttp.FromException(this, exception, logger); }
    }
    internal static bool IsEnabled(IConfiguration source) => source.GetValue("Features:CasinoWarEnabled", false);
    private CasinoWarService Service() => new(new CasinoWarFirestoreStore(database), TimeProvider.System);
    private ActionResult? Disabled() => IsEnabled(configuration) ? null : StatusCode(StatusCodes.Status503ServiceUnavailable, new CasinoWarErrorResponse("casino-war-disabled", "Casino War is still being verified and cannot accept a wager yet."));
    private async Task<AccountSummary?> AccountAsync(CancellationToken cancellationToken) => (await accountService.GetProfileAsync(AccountSessionCookie.Read(Request), cancellationToken)).Value;
}

internal static class CasinoWarHttp
{
    public static ActionResult FromException(ControllerBase controller, Exception exception, ILogger logger) => exception switch
    {
        CasinoWarRoundNotFoundException => controller.NotFound(new CasinoWarErrorResponse("casino-war-round-not-found", exception.Message)),
        CasinoWarInsufficientCreditsException => controller.Conflict(new CasinoWarErrorResponse("insufficient-slot-credits", exception.Message)),
        CasinoWarRoundConflictException => controller.Conflict(new CasinoWarErrorResponse("casino-war-round-conflict", exception.Message)),
        ArgumentException => controller.BadRequest(new CasinoWarErrorResponse("casino-war-invalid-request", exception.Message)),
        _ => Unexpected(controller, exception, logger),
    };
    private static ActionResult Unexpected(ControllerBase controller, Exception exception, ILogger logger) { logger.LogError(exception, "Casino War request failed; trace {TraceIdentifier}.", controller.HttpContext.TraceIdentifier); return controller.Problem("The Casino War service could not complete the request.", statusCode: StatusCodes.Status500InternalServerError); }
}
