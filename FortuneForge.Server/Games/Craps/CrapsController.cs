using FortuneForge.Games.Craps;
using FortuneForge.Server.Accounts;
using FortuneForge.Server.Accounts.Models;
using FortuneForge.Server.Accounts.Security;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;

namespace FortuneForge.Server.Games.Craps;

[ApiController]
[Route("api/games/craps")]
public sealed class CrapsController(FirestorePaidCrapsStore games, AccountService accounts, IConfiguration configuration) : ControllerBase
{
    [HttpGet("status")]
    [EnableRateLimiting(RateLimitPolicies.SlotReads)]
    public async Task<ActionResult> Status(CancellationToken cancellationToken) =>
        await WithAccountAsync(cancellationToken, async account => Ok(await games.StatusAsync(account.UserId, cancellationToken)));

    [HttpPost("rounds")]
    [EnableRateLimiting(RateLimitPolicies.SlotSpins)]
    public async Task<ActionResult> Start(StartCrapsRoundRequest request, CancellationToken cancellationToken) =>
        await WithAccountAsync(cancellationToken, account => ExecuteAsync(() => games.StartAsync(account.UserId, request, cancellationToken)));

    [HttpGet("rounds/active")]
    [EnableRateLimiting(RateLimitPolicies.SlotReads)]
    public async Task<ActionResult> Active(CancellationToken cancellationToken) =>
        await WithAccountAsync(cancellationToken, async account =>
            await games.ActiveAsync(account.UserId, cancellationToken) is { } round ? Ok(round) : NoContent());

    [HttpGet("rounds/{roundId:guid}")]
    [EnableRateLimiting(RateLimitPolicies.SlotReads)]
    public async Task<ActionResult> Get(Guid roundId, CancellationToken cancellationToken) =>
        await WithAccountAsync(cancellationToken, account => ExecuteAsync(() => games.GetAsync(account.UserId, roundId, cancellationToken)));

    [HttpPost("rounds/{roundId:guid}/roll")]
    [EnableRateLimiting(RateLimitPolicies.SlotSpins)]
    public async Task<ActionResult> Roll(Guid roundId, CrapsOperationRequest request, CancellationToken cancellationToken) =>
        await WithAccountAsync(cancellationToken, account => ExecuteAsync(() => games.RollAsync(account.UserId, roundId, request, cancellationToken)));

    [HttpPost("rounds/{roundId:guid}/odds")]
    [EnableRateLimiting(RateLimitPolicies.SlotSpins)]
    public async Task<ActionResult> PlaceOdds(Guid roundId, PlaceCrapsOddsRequest request, CancellationToken cancellationToken) =>
        await WithAccountAsync(cancellationToken, account => ExecuteAsync(() => games.PlaceOddsAsync(account.UserId, roundId, request, cancellationToken)));

    [HttpPost("rounds/{roundId:guid}/wagers")]
    [EnableRateLimiting(RateLimitPolicies.SlotSpins)]
    public async Task<ActionResult> SetWager(Guid roundId, SetCrapsWagerRequest request, CancellationToken cancellationToken) =>
        await WithAccountAsync(cancellationToken, account => ExecuteAsync(() => games.SetWagerAsync(account.UserId, roundId, request, cancellationToken)));

    private async Task<ActionResult> WithAccountAsync(CancellationToken cancellationToken, Func<AccountSummary, Task<ActionResult>> action)
    {
        Response.Headers.CacheControl = "no-store";
        if (Disabled() is { } unavailable) return unavailable;
        var account = await accounts.GetProfileAsync(AccountSessionCookie.Read(Request), cancellationToken);
        return account.Value is null
            ? Unauthorized(new CrapsErrorResponse("craps-authentication-required", "Sign in to play Craps."))
            : await action(account.Value);
    }

    private async Task<ActionResult> ExecuteAsync(Func<Task<CrapsRoundResponse>> action)
    {
        try { return Ok(await action()); }
        catch (CrapsAccessException exception) { return NotFound(new CrapsErrorResponse("craps-round-not-found", exception.Message)); }
        catch (CrapsPaidException exception) { return StatusCode(exception.StatusCode, new CrapsErrorResponse(exception.Code, exception.Message)); }
        catch (Exception exception) when (exception is ArgumentException or ArgumentOutOfRangeException or CrapsRuleException)
        { return BadRequest(new CrapsErrorResponse("craps-invalid-action", exception.Message)); }
    }

    private ActionResult? Disabled() => configuration.GetValue("Features:CrapsEnabled", false)
        ? null
        : StatusCode(StatusCodes.Status503ServiceUnavailable, new CrapsErrorResponse("craps-disabled", "Craps is not available yet."));
}
