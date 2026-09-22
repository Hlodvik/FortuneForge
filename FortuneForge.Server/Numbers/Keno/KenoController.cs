using FortuneForge.Server.Accounts;
using FortuneForge.Server.Accounts.Models;
using FortuneForge.Server.Accounts.Security;
using Google.Cloud.Firestore;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;

namespace FortuneForge.Server.Numbers.Keno;

[ApiController]
[Route("api/games/keno")]
public sealed class KenoController(FirestoreDb database, AccountService accountService, IConfiguration configuration, ILogger<KenoController> logger) : ControllerBase
{
    [HttpGet("status")]
    [EnableRateLimiting(RateLimitPolicies.SlotReads)]
    public async Task<ActionResult> Status(CancellationToken cancellationToken)
    {
        if (Disabled() is { } unavailable) return unavailable;
        var account = await AccountAsync(cancellationToken);
        return account is null ? Unauthorized(new KenoErrorResponse("keno-authentication-required", "Sign in to play Keno.")) : Ok(new KenoStatusResponse(true, account.Balances.SlotsCredits, "free-play-keno"));
    }

    [HttpPost("rounds")]
    [EnableRateLimiting(RateLimitPolicies.SlotSpins)]
    public async Task<ActionResult> Start(CreateKenoRoundRequest request, [FromHeader(Name = "Idempotency-Key")] string? idempotencyKey, CancellationToken cancellationToken)
    {
        if (Disabled() is { } unavailable) return unavailable;
        var account = await AccountAsync(cancellationToken);
        if (account is null) return Unauthorized(new KenoErrorResponse("keno-authentication-required", "Sign in to play Keno."));
        try { var result = await Service().StartAsync(account.UserId, request, idempotencyKey ?? string.Empty, cancellationToken); return CreatedAtAction(nameof(Get), new { roundId = result.RoundId }, result); }
        catch (Exception exception) { return KenoHttp.FromException(this, exception, logger); }
    }

    [HttpGet("rounds/{roundId}")]
    [EnableRateLimiting(RateLimitPolicies.SlotReads)]
    public async Task<ActionResult> Get(string roundId, CancellationToken cancellationToken)
    {
        if (Disabled() is { } unavailable) return unavailable;
        var account = await AccountAsync(cancellationToken);
        if (account is null) return Unauthorized(new KenoErrorResponse("keno-authentication-required", "Sign in to play Keno."));
        try { return Ok(await Service().GetAsync(account.UserId, roundId, cancellationToken)); }
        catch (Exception exception) { return KenoHttp.FromException(this, exception, logger); }
    }

    internal static bool IsEnabled(IConfiguration source) => source.GetValue("Features:KenoEnabled", false);
    private KenoService Service() => new(new KenoFirestoreStore(database), TimeProvider.System);
    private ActionResult? Disabled() => IsEnabled(configuration) ? null : StatusCode(StatusCodes.Status503ServiceUnavailable, new KenoErrorResponse("keno-disabled", "Keno is still being verified and cannot start a round yet."));
    private async Task<AccountSummary?> AccountAsync(CancellationToken cancellationToken) => (await accountService.GetProfileAsync(AccountSessionCookie.Read(Request), cancellationToken)).Value;
}

internal static class KenoHttp
{
    public static ActionResult FromException(ControllerBase controller, Exception exception, ILogger logger) => exception switch
    {
        KenoRoundNotFoundException => controller.NotFound(new KenoErrorResponse("keno-round-not-found", exception.Message)),
        KenoRoundConflictException => controller.Conflict(new KenoErrorResponse("keno-round-conflict", exception.Message)),
        ArgumentException => controller.BadRequest(new KenoErrorResponse("keno-invalid-request", exception.Message)),
        _ => Unexpected(controller, exception, logger),
    };
    private static ActionResult Unexpected(ControllerBase controller, Exception exception, ILogger logger) { logger.LogError(exception, "Keno request failed; trace {TraceIdentifier}.", controller.HttpContext.TraceIdentifier); return controller.Problem("The Keno service could not complete the request.", statusCode: StatusCodes.Status500InternalServerError); }
}
