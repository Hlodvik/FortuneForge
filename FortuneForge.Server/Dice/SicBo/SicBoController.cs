using FortuneForge.Server.Accounts;
using FortuneForge.Server.Accounts.Models;
using FortuneForge.Server.Accounts.Security;
using Google.Cloud.Firestore;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;

namespace FortuneForge.Server.Dice.SicBo;

[ApiController]
[Route("api/games/sic-bo")]
public sealed class SicBoController(FirestoreDb database, AccountService accountService, IConfiguration configuration, ILogger<SicBoController> logger) : ControllerBase
{
    [HttpGet("status")]
    [EnableRateLimiting(RateLimitPolicies.SlotReads)]
    public async Task<ActionResult> Status(CancellationToken cancellationToken)
    {
        if (Disabled() is { } unavailable) return unavailable;
        var account = await AccountAsync(cancellationToken);
        return account is null
            ? Unauthorized(new SicBoErrorResponse("sic-bo-authentication-required", "Sign in to play Sic Bo."))
            : Ok(new SicBoStatusResponse(true, SicBoMoney.ToRand(SicBoMoney.MinimumStakeCents), SicBoMoney.ToRand(SicBoMoney.MaximumStakeCents), SicBoMoney.ToRand(SicBoMoney.StakeIncrementCents), SicBoMoney.MaximumBetsPerRound, account.Balances.SlotsCredits, "three-dice-sic-bo"));
    }

    [HttpPost("rounds")]
    [EnableRateLimiting(RateLimitPolicies.SlotSpins)]
    public async Task<ActionResult> Start(CreateSicBoRoundRequest request, [FromHeader(Name = "Idempotency-Key")] string? idempotencyKey, CancellationToken cancellationToken)
    {
        if (Disabled() is { } unavailable) return unavailable;
        var account = await AccountAsync(cancellationToken);
        if (account is null) return Unauthorized(new SicBoErrorResponse("sic-bo-authentication-required", "Sign in to play Sic Bo."));
        try
        {
            var result = await Service().StartAsync(account.UserId, request, idempotencyKey ?? string.Empty, cancellationToken);
            return CreatedAtAction(nameof(Get), new { roundId = result.RoundId }, result);
        }
        catch (Exception exception) { return SicBoHttp.FromException(this, exception, logger); }
    }

    [HttpGet("rounds/{roundId}")]
    [EnableRateLimiting(RateLimitPolicies.SlotReads)]
    public async Task<ActionResult> Get(string roundId, CancellationToken cancellationToken)
    {
        if (Disabled() is { } unavailable) return unavailable;
        var account = await AccountAsync(cancellationToken);
        if (account is null) return Unauthorized(new SicBoErrorResponse("sic-bo-authentication-required", "Sign in to play Sic Bo."));
        try { return Ok(await Service().GetAsync(account.UserId, roundId, cancellationToken)); }
        catch (Exception exception) { return SicBoHttp.FromException(this, exception, logger); }
    }

    internal static bool IsEnabled(IConfiguration source) => source.GetValue("Features:SicBoEnabled", false);
    private SicBoService Service() => new(new SicBoFirestoreStore(database), TimeProvider.System);
    private ActionResult? Disabled() => IsEnabled(configuration) ? null : StatusCode(StatusCodes.Status503ServiceUnavailable, new SicBoErrorResponse("sic-bo-disabled", "Sic Bo is still being verified and cannot accept a wager yet."));
    private async Task<AccountSummary?> AccountAsync(CancellationToken cancellationToken) => (await accountService.GetProfileAsync(AccountSessionCookie.Read(Request), cancellationToken)).Value;
}

internal static class SicBoHttp
{
    public static ActionResult FromException(ControllerBase controller, Exception exception, ILogger logger) => exception switch
    {
        SicBoRoundNotFoundException => controller.NotFound(new SicBoErrorResponse("sic-bo-round-not-found", exception.Message)),
        SicBoInsufficientCreditsException => controller.Conflict(new SicBoErrorResponse("insufficient-slot-credits", exception.Message)),
        SicBoRoundConflictException => controller.Conflict(new SicBoErrorResponse("sic-bo-round-conflict", exception.Message)),
        ArgumentException => controller.BadRequest(new SicBoErrorResponse("sic-bo-invalid-request", exception.Message)),
        _ => Unexpected(controller, exception, logger),
    };
    private static ActionResult Unexpected(ControllerBase controller, Exception exception, ILogger logger) { logger.LogError(exception, "Sic Bo request failed; trace {TraceIdentifier}.", controller.HttpContext.TraceIdentifier); return controller.Problem("The Sic Bo service could not complete the request.", statusCode: StatusCodes.Status500InternalServerError); }
}
