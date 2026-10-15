using FortuneForge.Games.HouseHoldem;
using FortuneForge.Server.Accounts;
using FortuneForge.Server.Accounts.Models;
using FortuneForge.Server.Accounts.Security;
using Google.Cloud.Firestore;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;

namespace FortuneForge.Server.Cards.HouseHoldem;

[ApiController]
[Route("api/games/house-holdem")]
public sealed class HouseHoldemController(
    FirestoreDb database,
    AccountService accountService,
    IConfiguration configuration,
    ILogger<HouseHoldemController> logger) : ControllerBase
{
    [HttpGet("status")]
    [EnableRateLimiting(RateLimitPolicies.SlotReads)]
    public async Task<ActionResult> Status(CancellationToken cancellationToken)
    {
        if (Disabled() is { } unavailable) return unavailable;
        var account = await AuthenticatedAccountAsync(cancellationToken);
        if (account is null)
            return Unauthorized(new HouseHoldemErrorResponse("house-holdem-authentication-required", "Sign in to play."));
        try
        {
            return Ok(new HouseHoldemStatusResponse(
                true,
                account.Balances.SlotsCredits,
                HouseHoldemMoney.ToRand(HouseHoldemRules.MinimumBetCents),
                HouseHoldemMoney.ToRand(HouseHoldemRules.MaximumAnteCents),
                HouseHoldemMoney.ToRand(HouseHoldemRules.BetIncrementCents),
                HouseHoldemMoney.ToRand(HouseHoldemRules.MaximumTripsCents),
                await new HouseHoldemFirestoreStore(database).GetActiveRoundIdAsync(account.UserId, cancellationToken)));
        }
        catch (Exception exception) { return HouseHoldemHttp.FromException(this, exception, logger); }
    }

    [HttpPost("rounds")]
    [EnableRateLimiting(RateLimitPolicies.SlotSpins)]
    public async Task<ActionResult> Start(
        CreateHouseHoldemRoundRequest request,
        [FromHeader(Name = "Idempotency-Key")] string? idempotencyKey,
        CancellationToken cancellationToken)
    {
        if (Disabled() is { } unavailable) return unavailable;
        var account = await AuthenticatedAccountAsync(cancellationToken);
        if (account is null)
            return Unauthorized(new HouseHoldemErrorResponse("house-holdem-authentication-required", "Sign in to play."));
        try
        {
            var result = await Service().StartAsync(account.UserId, request, idempotencyKey ?? string.Empty, cancellationToken);
            return CreatedAtAction(nameof(Get), new { roundId = result.RoundId }, result);
        }
        catch (Exception exception) { return HouseHoldemHttp.FromException(this, exception, logger); }
    }

    [HttpGet("rounds/{roundId}")]
    [EnableRateLimiting(RateLimitPolicies.SlotReads)]
    public async Task<ActionResult> Get(string roundId, CancellationToken cancellationToken)
    {
        if (Disabled() is { } unavailable) return unavailable;
        var account = await AuthenticatedAccountAsync(cancellationToken);
        if (account is null)
            return Unauthorized(new HouseHoldemErrorResponse("house-holdem-authentication-required", "Sign in to play."));
        try { return Ok(await Service().GetAsync(account.UserId, roundId, cancellationToken)); }
        catch (Exception exception) { return HouseHoldemHttp.FromException(this, exception, logger); }
    }

    [HttpPost("rounds/{roundId}/actions")]
    [EnableRateLimiting(RateLimitPolicies.SlotSpins)]
    public async Task<ActionResult> Action(
        string roundId, HouseHoldemActionRequest request,
        [FromHeader(Name = "Idempotency-Key")] string? idempotencyKey,
        CancellationToken cancellationToken)
    {
        if (Disabled() is { } unavailable) return unavailable;
        var account = await AuthenticatedAccountAsync(cancellationToken);
        if (account is null)
            return Unauthorized(new HouseHoldemErrorResponse("house-holdem-authentication-required", "Sign in to play."));
        try { return Ok(await Service().ActionAsync(account.UserId, roundId, request, idempotencyKey ?? string.Empty, cancellationToken)); }
        catch (Exception exception) { return HouseHoldemHttp.FromException(this, exception, logger); }
    }

    [HttpPost("rounds/{roundId}/advance")]
    [EnableRateLimiting(RateLimitPolicies.SlotSpins)]
    public async Task<ActionResult> Advance(
        string roundId,
        [FromHeader(Name = "Idempotency-Key")] string? idempotencyKey,
        CancellationToken cancellationToken)
    {
        if (Disabled() is { } unavailable) return unavailable;
        var account = await AuthenticatedAccountAsync(cancellationToken);
        if (account is null)
            return Unauthorized(new HouseHoldemErrorResponse("house-holdem-authentication-required", "Sign in to play."));
        try { return Ok(await Service().AdvanceAsync(account.UserId, roundId, idempotencyKey ?? string.Empty, cancellationToken)); }
        catch (Exception exception) { return HouseHoldemHttp.FromException(this, exception, logger); }
    }

    internal static bool IsEnabled(IConfiguration source) => source.GetValue("Features:HouseHoldemEnabled", false);

    private HouseHoldemService Service() => new(new HouseHoldemFirestoreStore(database), TimeProvider.System);

    private ActionResult? Disabled() => IsEnabled(configuration)
        ? null
        : StatusCode(StatusCodes.Status503ServiceUnavailable, new HouseHoldemErrorResponse(
            "house-holdem-disabled", "This game is not accepting wagers yet."));

    private async Task<AccountSummary?> AuthenticatedAccountAsync(CancellationToken cancellationToken) =>
        (await accountService.GetProfileAsync(AccountSessionCookie.Read(Request), cancellationToken)).Value;
}

internal static class HouseHoldemHttp
{
    public static ActionResult FromException(ControllerBase controller, Exception exception, ILogger logger) => exception switch
    {
        HouseHoldemRoundNotFoundException => controller.NotFound(new HouseHoldemErrorResponse(
            "house-holdem-round-not-found", exception.Message)),
        HouseHoldemInsufficientCreditsException => controller.Conflict(new HouseHoldemErrorResponse(
            "insufficient-slot-credits", exception.Message)),
        HouseHoldemRoundConflictException => controller.Conflict(new HouseHoldemErrorResponse(
            "house-holdem-round-conflict", exception.Message)),
        ArgumentException => controller.BadRequest(new HouseHoldemErrorResponse(
            "house-holdem-invalid-request", exception.Message)),
        _ => Unexpected(controller, exception, logger),
    };

    private static ActionResult Unexpected(ControllerBase controller, Exception exception, ILogger logger)
    {
        logger.LogError(exception, "House Hold'em request failed; trace {TraceIdentifier}.", controller.HttpContext.TraceIdentifier);
        return controller.Problem("The game could not complete the request.", statusCode: StatusCodes.Status500InternalServerError);
    }
}
