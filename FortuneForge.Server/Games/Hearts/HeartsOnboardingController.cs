using FortuneForge.Server.Accounts;
using FortuneForge.Server.Accounts.Security;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;

namespace FortuneForge.Server.Games.Hearts;

[ApiController]
[Route("api/games/hearts")]
public sealed class HeartsOnboardingController(AccountService accounts, IConfiguration configuration) : ControllerBase
{
    [HttpGet("onboarding")]
    [EnableRateLimiting(RateLimitPolicies.SlotReads)]
    public Task<ActionResult> Get(CancellationToken cancellationToken) =>
        HeartsHostFallback.ExecuteAsync(this, accounts, configuration, cancellationToken);

    [HttpPost("onboarding")]
    [EnableRateLimiting(RateLimitPolicies.SlotSpins)]
    public Task<ActionResult> Confirm(ConfirmHeartsExperienceRequest request, CancellationToken cancellationToken) =>
        HeartsHostFallback.ExecuteAsync(this, accounts, configuration, cancellationToken);

    [HttpPost("trial")]
    [EnableRateLimiting(RateLimitPolicies.SlotSpins)]
    public Task<ActionResult> StartTrial(CancellationToken cancellationToken) =>
        HeartsHostFallback.ExecuteAsync(this, accounts, configuration, cancellationToken);

    [HttpPost("trial/{matchId:guid}/cancel")]
    [EnableRateLimiting(RateLimitPolicies.SlotSpins)]
    public Task<ActionResult> CancelTrial(Guid matchId, CancellationToken cancellationToken) =>
        HeartsHostFallback.ExecuteAsync(this, accounts, configuration, cancellationToken);

}
