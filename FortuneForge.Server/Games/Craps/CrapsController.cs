using FortuneForge.Games.Craps;
using FortuneForge.Server.Accounts;
using FortuneForge.Server.Accounts.Models;
using FortuneForge.Server.Accounts.Security;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;

namespace FortuneForge.Server.Games.Craps;

[ApiController]
[Route("api/games/craps")]
public sealed class CrapsController(CrapsGameService games, AccountService accounts, IConfiguration configuration) : ControllerBase
{
    [HttpGet("status")]
    [EnableRateLimiting(RateLimitPolicies.SlotReads)]
    public async Task<ActionResult> Status(CancellationToken cancellationToken) => await WithAccount(cancellationToken, _ => Ok(games.Status()));

    [HttpPost("rounds")]
    [EnableRateLimiting(RateLimitPolicies.SlotSpins)]
    public async Task<ActionResult> Start(StartCrapsRoundRequest request, CancellationToken cancellationToken) =>
        await WithAccountAsync(cancellationToken, account => ExecuteAsync(() =>
            games.StartAuditedAsync(account.UserId, request, cancellationToken)));

    [HttpGet("rounds/{roundId:guid}")]
    [EnableRateLimiting(RateLimitPolicies.SlotReads)]
    public async Task<ActionResult> Get(Guid roundId, CancellationToken cancellationToken) =>
        await WithAccount(cancellationToken, account => Execute(() => games.Get(account.UserId, roundId)));

    [HttpPost("rounds/{roundId:guid}/roll")]
    [EnableRateLimiting(RateLimitPolicies.SlotSpins)]
    public async Task<ActionResult> Roll(Guid roundId, CancellationToken cancellationToken) =>
        await WithAccountAsync(cancellationToken, account => ExecuteAsync(() =>
            games.RollAuditedAsync(account.UserId, roundId, cancellationToken)));

    [HttpPost("rounds/{roundId:guid}/odds")]
    [EnableRateLimiting(RateLimitPolicies.SlotSpins)]
    public async Task<ActionResult> PlaceOdds(Guid roundId, PlaceCrapsOddsRequest request, CancellationToken cancellationToken) =>
        await WithAccountAsync(cancellationToken, account => ExecuteAsync(() =>
            games.PlaceOddsAuditedAsync(account.UserId, roundId, request.Stake, cancellationToken)));

    private async Task<ActionResult> WithAccount(CancellationToken cancellationToken, Func<AccountSummary, ActionResult> action)
    {
        if (Disabled() is { } unavailable) return unavailable;
        var account = await accounts.GetProfileAsync(AccountSessionCookie.Read(Request), cancellationToken);
        return account.Value is null
            ? Unauthorized(new CrapsErrorResponse("craps-authentication-required", "Sign in to play Craps."))
            : action(account.Value);
    }

    private async Task<ActionResult> WithAccountAsync(CancellationToken cancellationToken,
        Func<AccountSummary, Task<ActionResult>> action)
    {
        if (Disabled() is { } unavailable) return unavailable;
        var account = await accounts.GetProfileAsync(AccountSessionCookie.Read(Request), cancellationToken);
        return account.Value is null
            ? Unauthorized(new CrapsErrorResponse("craps-authentication-required", "Sign in to play Craps."))
            : await action(account.Value);
    }

    private ActionResult Execute(Func<CrapsRoundResponse> action)
    {
        try { return Ok(action()); }
        catch (CrapsAccessException exception) { return NotFound(new CrapsErrorResponse("craps-round-not-found", exception.Message)); }
        catch (Exception exception) when (exception is ArgumentException or ArgumentOutOfRangeException or CrapsRuleException)
        { return BadRequest(new CrapsErrorResponse("craps-invalid-action", exception.Message)); }
    }

    private async Task<ActionResult> ExecuteAsync(Func<Task<CrapsRoundResponse>> action)
    {
        try { return Ok(await action()); }
        catch (CrapsAccessException exception) { return NotFound(new CrapsErrorResponse("craps-round-not-found", exception.Message)); }
        catch (Exception exception) when (exception is ArgumentException or ArgumentOutOfRangeException or CrapsRuleException)
        { return BadRequest(new CrapsErrorResponse("craps-invalid-action", exception.Message)); }
    }

    private ActionResult? Disabled() => configuration.GetValue("Features:CrapsEnabled", false)
        ? null
        : StatusCode(StatusCodes.Status503ServiceUnavailable, new CrapsErrorResponse("craps-disabled", "Craps is not available yet."));
}
