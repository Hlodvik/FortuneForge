using FortuneForge.Games.Roulette;
using FortuneForge.Server.Accounts;
using FortuneForge.Server.Accounts.Models;
using FortuneForge.Server.Accounts.Security;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;

namespace FortuneForge.Server.Games.Roulette;

[ApiController]
[Route("api/games/roulette")]
public sealed class RouletteController(RouletteGameService games, AccountService accounts, IConfiguration configuration) : ControllerBase
{
    [HttpGet("status")]
    [EnableRateLimiting(RateLimitPolicies.SlotReads)]
    public async Task<ActionResult> Status(CancellationToken cancellationToken) =>
        await WithAccount(cancellationToken, _ => Ok(games.Status()));

    [HttpPost("rounds")]
    [EnableRateLimiting(RateLimitPolicies.SlotSpins)]
    public async Task<ActionResult> Start(CancellationToken cancellationToken) =>
        await WithAccountAsync(cancellationToken, account => ExecuteAsync(() => games.StartAuditedAsync(account.UserId, account.PlayerName, cancellationToken)));

    [HttpGet("rounds/{roundId:guid}")]
    [EnableRateLimiting(RateLimitPolicies.SlotReads)]
    public async Task<ActionResult> Get(Guid roundId, CancellationToken cancellationToken) =>
        await WithAccountAsync(cancellationToken, account => ExecuteAsync(() => games.GetAuditedAsync(account.UserId, roundId, cancellationToken)));

    [HttpPost("rounds/{roundId:guid}/bets")]
    [EnableRateLimiting(RateLimitPolicies.SlotSpins)]
    public async Task<ActionResult> PlaceBet(Guid roundId, PlaceRouletteBetRequest request, CancellationToken cancellationToken) =>
        await WithAccountAsync(cancellationToken, account => ExecuteAsync(() => games.PlaceBetAuditedAsync(account.UserId, roundId, request, cancellationToken)));

    [HttpDelete("rounds/{roundId:guid}/bets/{betIndex:int}")]
    [EnableRateLimiting(RateLimitPolicies.SlotSpins)]
    public async Task<ActionResult> RemoveBet(Guid roundId, int betIndex, CancellationToken cancellationToken) =>
        await WithAccountAsync(cancellationToken, account => ExecuteAsync(() => games.RemoveBetAuditedAsync(account.UserId, roundId, betIndex, cancellationToken)));

    [HttpDelete("rounds/{roundId:guid}/bets")]
    [EnableRateLimiting(RateLimitPolicies.SlotSpins)]
    public async Task<ActionResult> ClearBets(Guid roundId, CancellationToken cancellationToken) =>
        await WithAccountAsync(cancellationToken, account => ExecuteAsync(() => games.ClearBetsAuditedAsync(account.UserId, roundId, cancellationToken)));

    [HttpPost("rounds/{roundId:guid}/spin")]
    [EnableRateLimiting(RateLimitPolicies.SlotSpins)]
    public async Task<ActionResult> Spin(Guid roundId, CancellationToken cancellationToken) =>
        await WithAccountAsync(cancellationToken, account => ExecuteAsync(() => games.SpinAuditedAsync(account.UserId, roundId, cancellationToken)));

    private async Task<ActionResult> WithAccount(CancellationToken cancellationToken, Func<AccountSummary, ActionResult> action)
    {
        if (Disabled() is { } unavailable) return unavailable;
        var account = await accounts.GetProfileAsync(AccountSessionCookie.Read(Request), cancellationToken);
        return account.Value is null
            ? Unauthorized(new RouletteErrorResponse("roulette-authentication-required", "Sign in to play Roulette."))
            : action(account.Value);
    }

    private async Task<ActionResult> WithAccountAsync(CancellationToken cancellationToken, Func<AccountSummary, Task<ActionResult>> action)
    {
        if (Disabled() is { } unavailable) return unavailable;
        var account = await accounts.GetProfileAsync(AccountSessionCookie.Read(Request), cancellationToken);
        return account.Value is null
            ? Unauthorized(new RouletteErrorResponse("roulette-authentication-required", "Sign in to play Roulette."))
            : await action(account.Value);
    }

    private async Task<ActionResult> ExecuteAsync(Func<Task<RouletteRoundResponse>> action)
    {
        try { return Ok(await action()); }
        catch (RouletteAccessException exception) { return NotFound(new RouletteErrorResponse("roulette-round-not-found", exception.Message)); }
        catch (Exception exception) when (exception is ArgumentException or ArgumentOutOfRangeException or RouletteRuleException)
        { return BadRequest(new RouletteErrorResponse("roulette-invalid-action", exception.Message)); }
    }

    private ActionResult Execute(Func<RouletteRoundResponse> action)
    {
        try { return Ok(action()); }
        catch (RouletteAccessException exception) { return NotFound(new RouletteErrorResponse("roulette-round-not-found", exception.Message)); }
        catch (Exception exception) when (exception is ArgumentException or ArgumentOutOfRangeException or RouletteRuleException)
        { return BadRequest(new RouletteErrorResponse("roulette-invalid-action", exception.Message)); }
    }

    private ActionResult? Disabled() => configuration.GetValue("Features:RouletteEnabled", false)
        ? null
        : StatusCode(StatusCodes.Status503ServiceUnavailable, new RouletteErrorResponse("roulette-disabled", "Roulette is not available yet."));
}
