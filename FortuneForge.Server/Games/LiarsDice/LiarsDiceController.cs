using FortuneForge.Games.LiarsDice;
using FortuneForge.Server.Accounts;
using FortuneForge.Server.Accounts.Models;
using FortuneForge.Server.Accounts.Security;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;

namespace FortuneForge.Server.Games.LiarsDice;

[ApiController]
[Route("api/games/liars-dice")]
public sealed class LiarsDiceController(LiarsDiceGameService games, AccountService accounts, IConfiguration configuration) : ControllerBase
{
    [HttpGet("status")]
    [EnableRateLimiting(RateLimitPolicies.SlotReads)]
    public async Task<ActionResult> Status(CancellationToken cancellationToken) => await WithAccount(
        cancellationToken, _ => Task.FromResult<ActionResult>(Ok(games.Status())));

    [HttpPost("matches")]
    [EnableRateLimiting(RateLimitPolicies.SlotSpins)]
    public async Task<ActionResult> Start(StartLiarsDiceMatchRequest? request, CancellationToken cancellationToken) =>
        await WithAccount(cancellationToken, account => ExecuteAsync(() => games.StartAsync(
            account.UserId, request ?? new(null, null), cancellationToken)));

    [HttpGet("matches/{matchId:guid}")]
    [EnableRateLimiting(RateLimitPolicies.SlotReads)]
    public async Task<ActionResult> Get(Guid matchId, CancellationToken cancellationToken) =>
        await WithAccount(cancellationToken, account => ExecuteAsync(() => games.GetAsync(
            account.UserId, matchId, cancellationToken)));

    [HttpPost("matches/{matchId:guid}/bid")]
    [EnableRateLimiting(RateLimitPolicies.SlotSpins)]
    public async Task<ActionResult> Bid(Guid matchId, PlaceLiarsDiceBidRequest request, CancellationToken cancellationToken) =>
        await WithAccount(cancellationToken, account => ExecuteAsync(() => games.BidAsync(
            account.UserId, matchId, request, cancellationToken)));

    [HttpPost("matches/{matchId:guid}/challenge")]
    [EnableRateLimiting(RateLimitPolicies.SlotSpins)]
    public async Task<ActionResult> Challenge(Guid matchId, CancellationToken cancellationToken) =>
        await WithAccount(cancellationToken, account => ExecuteAsync(() => games.ChallengeAsync(
            account.UserId, matchId, cancellationToken)));

    [HttpPost("matches/{matchId:guid}/spot-on")]
    [EnableRateLimiting(RateLimitPolicies.SlotSpins)]
    public async Task<ActionResult> SpotOn(Guid matchId, CancellationToken cancellationToken) =>
        await WithAccount(cancellationToken, account => ExecuteAsync(() => games.SpotOnAsync(
            account.UserId, matchId, cancellationToken)));

    [HttpPost("matches/{matchId:guid}/advance")]
    [EnableRateLimiting(RateLimitPolicies.SlotSpins)]
    public async Task<ActionResult> Advance(Guid matchId, CancellationToken cancellationToken) =>
        await WithAccount(cancellationToken, account => ExecuteAsync(() => games.AdvanceAsync(
            account.UserId, matchId, cancellationToken)));

    [HttpPost("matches/{matchId:guid}/next-round")]
    [EnableRateLimiting(RateLimitPolicies.SlotSpins)]
    public async Task<ActionResult> NextRound(Guid matchId, NextLiarsDiceRoundRequest? request, CancellationToken cancellationToken) =>
        await WithAccount(cancellationToken, account => ExecuteAsync(() => games.NextRoundAsync(
            account.UserId, matchId, request?.Seed, cancellationToken)));

    private async Task<ActionResult> WithAccount(CancellationToken cancellationToken, Func<AccountSummary, Task<ActionResult>> action)
    {
        if (Disabled() is { } unavailable) return unavailable;
        var account = await accounts.GetProfileAsync(AccountSessionCookie.Read(Request), cancellationToken);
        return account.Value is null
            ? Unauthorized(new LiarsDiceErrorResponse("liars-dice-authentication-required", "Sign in to play Liar's Dice."))
            : await action(account.Value);
    }

    private async Task<ActionResult> ExecuteAsync(Func<Task<LiarsDiceMatchResponse>> action)
    {
        try { return Ok(await action()); }
        catch (LiarsDiceAccessException exception) { return NotFound(new LiarsDiceErrorResponse("liars-dice-match-not-found", exception.Message)); }
        catch (Exception exception) when (exception is ArgumentException or ArgumentOutOfRangeException or LiarsDiceRuleException)
        { return BadRequest(new LiarsDiceErrorResponse("liars-dice-invalid-action", exception.Message)); }
    }

    private ActionResult? Disabled() => configuration.GetValue("Features:LiarsDiceEnabled", false)
        ? null
        : StatusCode(StatusCodes.Status503ServiceUnavailable, new LiarsDiceErrorResponse("liars-dice-disabled", "Liar's Dice is not available yet."));
}
