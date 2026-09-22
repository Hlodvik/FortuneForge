using FortuneForge.Games.Hearts;
using FortuneForge.Server.Accounts;
using FortuneForge.Server.Accounts.Models;
using FortuneForge.Server.Accounts.Security;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;

namespace FortuneForge.Server.Games.Hearts;

[ApiController]
[Route("api/games/hearts")]
public sealed class HeartsController(
    HeartsGameService games,
    AccountService accounts,
    IConfiguration configuration) : ControllerBase
{
    [HttpGet("status")]
    [EnableRateLimiting(RateLimitPolicies.SlotReads)]
    public async Task<ActionResult> Status(CancellationToken cancellationToken)
    {
        if (Disabled() is { } unavailable) return unavailable;
        return await AccountAsync(cancellationToken) is null
            ? Unauthorized(new HeartsErrorResponse("hearts-authentication-required", "Sign in to play Hearts."))
            : Ok(new HeartsStatusResponse(true, HeartsMatchEngine.DefaultTargetScore, "free-play-bots"));
    }

    [HttpPost("matches")]
    [EnableRateLimiting(RateLimitPolicies.SlotSpins)]
    public async Task<ActionResult> Start(StartHeartsMatchRequest? request, CancellationToken cancellationToken)
    {
        if (Disabled() is { } unavailable) return unavailable;
        var account = await AccountAsync(cancellationToken);
        if (account is null) return Unauthorized(new HeartsErrorResponse("hearts-authentication-required", "Sign in to play Hearts."));
        return Execute(() => games.Start(account.UserId, request ?? new(null, null)));
    }

    [HttpGet("matches/{matchId:guid}")]
    [EnableRateLimiting(RateLimitPolicies.SlotReads)]
    public async Task<ActionResult> Get(Guid matchId, CancellationToken cancellationToken)
    {
        if (Disabled() is { } unavailable) return unavailable;
        var account = await AccountAsync(cancellationToken);
        if (account is null) return Unauthorized(new HeartsErrorResponse("hearts-authentication-required", "Sign in to play Hearts."));
        return Execute(() => games.Get(account.UserId, matchId));
    }

    [HttpPost("matches/{matchId:guid}/pass")]
    [EnableRateLimiting(RateLimitPolicies.SlotSpins)]
    public async Task<ActionResult> Pass(Guid matchId, PassHeartsRequest request, CancellationToken cancellationToken) =>
        await WithAccount(cancellationToken, account => Execute(() => games.Pass(account.UserId, matchId, request.Cards ?? [])));

    [HttpPost("matches/{matchId:guid}/card")]
    [EnableRateLimiting(RateLimitPolicies.SlotSpins)]
    public async Task<ActionResult> Card(Guid matchId, PlayHeartsCardRequest request, CancellationToken cancellationToken) =>
        await WithAccount(cancellationToken, account => Execute(() => games.PlayCard(account.UserId, matchId, request.Card ?? string.Empty)));

    [HttpPost("matches/{matchId:guid}/advance")]
    [EnableRateLimiting(RateLimitPolicies.SlotSpins)]
    public async Task<ActionResult> Advance(Guid matchId, CancellationToken cancellationToken) =>
        await WithAccount(cancellationToken, account => Execute(() => games.Advance(account.UserId, matchId)));

    [HttpPost("matches/{matchId:guid}/next-round")]
    [EnableRateLimiting(RateLimitPolicies.SlotSpins)]
    public async Task<ActionResult> NextRound(Guid matchId, NextHeartsRoundRequest? request, CancellationToken cancellationToken) =>
        await WithAccount(cancellationToken, account => Execute(() => games.NextRound(account.UserId, matchId, request?.Seed)));

    private async Task<ActionResult> WithAccount(CancellationToken cancellationToken, Func<AccountSummary, ActionResult> action)
    {
        if (Disabled() is { } unavailable) return unavailable;
        var account = await AccountAsync(cancellationToken);
        return account is null
            ? Unauthorized(new HeartsErrorResponse("hearts-authentication-required", "Sign in to play Hearts."))
            : action(account);
    }

    private ActionResult Execute(Func<HeartsMatchResponse> action)
    {
        try { return Ok(action()); }
        catch (HeartsAccessException exception) { return NotFound(new HeartsErrorResponse("hearts-match-not-found", exception.Message)); }
        catch (Exception exception) when (exception is ArgumentException or ArgumentOutOfRangeException or HeartsRuleException)
        { return BadRequest(new HeartsErrorResponse("hearts-invalid-action", exception.Message)); }
    }

    private ActionResult? Disabled() => configuration.GetValue("Features:HeartsEnabled", false)
        ? null
        : StatusCode(StatusCodes.Status503ServiceUnavailable, new HeartsErrorResponse("hearts-disabled", "Hearts is not available yet."));
    private async Task<AccountSummary?> AccountAsync(CancellationToken cancellationToken) =>
        (await accounts.GetProfileAsync(AccountSessionCookie.Read(Request), cancellationToken)).Value;
}
