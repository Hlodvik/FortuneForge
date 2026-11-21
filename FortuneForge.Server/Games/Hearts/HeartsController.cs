using FortuneForge.Server.Accounts;
using FortuneForge.Server.Accounts.Security;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;

namespace FortuneForge.Server.Games.Hearts;

[ApiController]
[Route("api/games/hearts")]
public sealed class HeartsController(AccountService accounts, IConfiguration configuration) : ControllerBase
{
    [HttpGet("status")]
    [EnableRateLimiting(RateLimitPolicies.SlotReads)]
    public Task<ActionResult> Status(CancellationToken cancellationToken) =>
        HeartsHostFallback.ExecuteAsync(this, accounts, configuration, cancellationToken);

    [HttpPost("matches")]
    [EnableRateLimiting(RateLimitPolicies.SlotSpins)]
    public Task<ActionResult> Start(StartHeartsMatchRequest? request, CancellationToken cancellationToken) =>
        HeartsHostFallback.ExecuteAsync(this, accounts, configuration, cancellationToken);

    [HttpGet("matches/{matchId:guid}")]
    [EnableRateLimiting(RateLimitPolicies.SlotReads)]
    public Task<ActionResult> Get(Guid matchId, CancellationToken cancellationToken) =>
        HeartsHostFallback.ExecuteAsync(this, accounts, configuration, cancellationToken);

    [HttpPost("matches/{matchId:guid}/pass")]
    [EnableRateLimiting(RateLimitPolicies.SlotSpins)]
    public Task<ActionResult> Pass(Guid matchId, PassHeartsRequest request, CancellationToken cancellationToken) =>
        HeartsHostFallback.ExecuteAsync(this, accounts, configuration, cancellationToken);

    [HttpPost("matches/{matchId:guid}/card")]
    [EnableRateLimiting(RateLimitPolicies.SlotSpins)]
    public Task<ActionResult> Card(Guid matchId, PlayHeartsCardRequest request, CancellationToken cancellationToken) =>
        HeartsHostFallback.ExecuteAsync(this, accounts, configuration, cancellationToken);

    [HttpPost("matches/{matchId:guid}/advance")]
    [EnableRateLimiting(RateLimitPolicies.SlotSpins)]
    public Task<ActionResult> Advance(Guid matchId, CancellationToken cancellationToken) =>
        HeartsHostFallback.ExecuteAsync(this, accounts, configuration, cancellationToken);

    [HttpPost("matches/{matchId:guid}/next-round")]
    [EnableRateLimiting(RateLimitPolicies.SlotSpins)]
    public Task<ActionResult> NextRound(Guid matchId, NextHeartsRoundRequest? request, CancellationToken cancellationToken) =>
        HeartsHostFallback.ExecuteAsync(this, accounts, configuration, cancellationToken);

    [HttpPost("matches/{matchId:guid}/leave")]
    [EnableRateLimiting(RateLimitPolicies.SlotSpins)]
    public Task<ActionResult> Leave(Guid matchId, CancellationToken cancellationToken) =>
        HeartsHostFallback.ExecuteAsync(this, accounts, configuration, cancellationToken);

}
