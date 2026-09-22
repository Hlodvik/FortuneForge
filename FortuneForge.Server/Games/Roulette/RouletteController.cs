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
        await WithAccount(cancellationToken, account => Execute(() => games.Start(account.UserId)));

    [HttpGet("rounds/{roundId:guid}")]
    [EnableRateLimiting(RateLimitPolicies.SlotReads)]
    public async Task<ActionResult> Get(Guid roundId, CancellationToken cancellationToken) =>
        await WithAccount(cancellationToken, account => Execute(() => games.Get(account.UserId, roundId)));

    [HttpPost("rounds/{roundId:guid}/bets")]
    [EnableRateLimiting(RateLimitPolicies.SlotSpins)]
    public async Task<ActionResult> PlaceBet(Guid roundId, PlaceRouletteBetRequest request, CancellationToken cancellationToken) =>
        await WithAccount(cancellationToken, account => Execute(() => games.PlaceBet(account.UserId, roundId, request)));

    [HttpDelete("rounds/{roundId:guid}/bets/{betIndex:int}")]
    [EnableRateLimiting(RateLimitPolicies.SlotSpins)]
    public async Task<ActionResult> RemoveBet(Guid roundId, int betIndex, CancellationToken cancellationToken) =>
        await WithAccount(cancellationToken, account => Execute(() => games.RemoveBet(account.UserId, roundId, betIndex)));

    [HttpDelete("rounds/{roundId:guid}/bets")]
    [EnableRateLimiting(RateLimitPolicies.SlotSpins)]
    public async Task<ActionResult> ClearBets(Guid roundId, CancellationToken cancellationToken) =>
        await WithAccount(cancellationToken, account => Execute(() => games.ClearBets(account.UserId, roundId)));

    [HttpPost("rounds/{roundId:guid}/spin")]
    [EnableRateLimiting(RateLimitPolicies.SlotSpins)]
    public async Task<ActionResult> Spin(Guid roundId, CancellationToken cancellationToken) =>
        await WithAccount(cancellationToken, account => Execute(() => games.Spin(account.UserId, roundId)));

    private async Task<ActionResult> WithAccount(CancellationToken cancellationToken, Func<AccountSummary, ActionResult> action)
    {
        if (Disabled() is { } unavailable) return unavailable;
        var account = await accounts.GetProfileAsync(AccountSessionCookie.Read(Request), cancellationToken);
        return account.Value is null
            ? Unauthorized(new RouletteErrorResponse("roulette-authentication-required", "Sign in to play Roulette."))
            : action(account.Value);
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
