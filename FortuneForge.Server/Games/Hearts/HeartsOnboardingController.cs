using FortuneForge.Server.Accounts;
using FortuneForge.Server.Accounts.Models;
using FortuneForge.Server.Accounts.Security;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;

namespace FortuneForge.Server.Games.Hearts;

[ApiController]
[Route("api/games/hearts")]
public sealed class HeartsOnboardingController(
    IHeartsOnboardingStore onboarding, HeartsGameService games, AccountService accounts,
    IConfiguration configuration, TimeProvider timeProvider) : ControllerBase
{
    [HttpGet("onboarding")]
    [EnableRateLimiting(RateLimitPolicies.SlotReads)]
    public Task<ActionResult> Get(CancellationToken cancellationToken) =>
        WithAccount(cancellationToken, async account =>
        {
            await games.RetryTrialCompletionAsync(account.UserId, cancellationToken);
            return Ok(await onboarding.GetAsync(account.UserId, cancellationToken));
        });

    [HttpPost("onboarding")]
    [EnableRateLimiting(RateLimitPolicies.SlotSpins)]
    public Task<ActionResult> Confirm(ConfirmHeartsExperienceRequest request, CancellationToken cancellationToken) =>
        WithAccount(cancellationToken, async account => request.HasPlayedBefore
            ? Ok(await onboarding.ConfirmExperienceAsync(account.UserId, timeProvider.GetUtcNow().UtcDateTime, cancellationToken))
            : BadRequest(new HeartsErrorResponse("hearts-invalid-confirmation", "Confirm that you have played before, or choose Learn to play.")));

    [HttpPost("trial")]
    [EnableRateLimiting(RateLimitPolicies.SlotSpins)]
    public Task<ActionResult> StartTrial(CancellationToken cancellationToken) =>
        WithAccount(cancellationToken, async account => Ok(await games.StartTrialAsync(account.UserId, cancellationToken)));

    [HttpPost("trial/{matchId:guid}/cancel")]
    [EnableRateLimiting(RateLimitPolicies.SlotSpins)]
    public Task<ActionResult> CancelTrial(Guid matchId, CancellationToken cancellationToken) =>
        WithAccount(cancellationToken, async account =>
        {
            try { await games.CancelTrialAsync(account.UserId, matchId, cancellationToken); }
            catch (HeartsAccessException exception) { return NotFound(new HeartsErrorResponse("hearts-match-not-found", exception.Message)); }
            catch (FortuneForge.Games.Hearts.HeartsRuleException exception)
            { return BadRequest(new HeartsErrorResponse("hearts-invalid-action", exception.Message)); }
            return Ok(await onboarding.GetAsync(account.UserId, cancellationToken));
        });

    private async Task<ActionResult> WithAccount(CancellationToken cancellationToken, Func<AccountSummary, Task<ActionResult>> action)
    {
        if (!configuration.GetValue("Features:HeartsEnabled", false))
            return StatusCode(503, new HeartsErrorResponse("hearts-disabled", "Hearts is not available yet."));
        var account = (await accounts.GetProfileAsync(AccountSessionCookie.Read(Request), cancellationToken)).Value;
        return account is null
            ? Unauthorized(new HeartsErrorResponse("hearts-authentication-required", "Sign in to play Hearts."))
            : await action(account);
    }
}
