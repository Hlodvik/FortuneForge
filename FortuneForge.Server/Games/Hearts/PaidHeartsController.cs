using FortuneForge.Server.Accounts;
using FortuneForge.Server.Accounts.Security;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;

namespace FortuneForge.Server.Games.Hearts;

[ApiController]
[Route("api/games/hearts/paid")]
public sealed class PaidHeartsController(AccountService accounts, IConfiguration configuration) : ControllerBase
{
    [HttpGet("session")]
    [EnableRateLimiting(RateLimitPolicies.SlotReads)]
    public Task<ActionResult> Session(CancellationToken ct) =>
        HeartsHostFallback.ExecuteAsync(this, accounts, configuration, ct);

    [HttpGet("matches/{matchId}")]
    [EnableRateLimiting(RateLimitPolicies.SlotReads)]
    public Task<ActionResult> Get(string matchId, CancellationToken ct) =>
        HeartsHostFallback.ExecuteAsync(this, accounts, configuration, ct);

    [HttpPost("join")]
    [EnableRateLimiting(RateLimitPolicies.SlotSpins)]
    public Task<ActionResult> Join(JoinPaidHeartsRequest request, [FromHeader(Name = "Idempotency-Key")] string? key, CancellationToken ct) =>
        HeartsHostFallback.ExecuteAsync(this, accounts, configuration, ct);

    [HttpPost("matches/{matchId}/play-house")]
    [EnableRateLimiting(RateLimitPolicies.SlotSpins)]
    public Task<ActionResult> PlayHouse(string matchId, PaidHeartsVersionRequest request, [FromHeader(Name = "Idempotency-Key")] string? key, CancellationToken ct) =>
        HeartsHostFallback.ExecuteAsync(this, accounts, configuration, ct);

    [HttpPost("matches/{matchId}/pass")]
    [EnableRateLimiting(RateLimitPolicies.SlotSpins)]
    public Task<ActionResult> Pass(string matchId, PaidHeartsPassRequest request, [FromHeader(Name = "Idempotency-Key")] string? key, CancellationToken ct) =>
        HeartsHostFallback.ExecuteAsync(this, accounts, configuration, ct);

    [HttpPost("matches/{matchId}/card")]
    [EnableRateLimiting(RateLimitPolicies.SlotSpins)]
    public Task<ActionResult> Card(string matchId, PaidHeartsCardRequest request, [FromHeader(Name = "Idempotency-Key")] string? key, CancellationToken ct) =>
        HeartsHostFallback.ExecuteAsync(this, accounts, configuration, ct);

    [HttpPost("matches/{matchId}/advance")]
    [EnableRateLimiting(RateLimitPolicies.SlotReads)]
    public Task<ActionResult> Advance(string matchId, CancellationToken ct) =>
        HeartsHostFallback.ExecuteAsync(this, accounts, configuration, ct);

    [HttpPost("matches/{matchId}/next-round")]
    [EnableRateLimiting(RateLimitPolicies.SlotSpins)]
    public Task<ActionResult> NextRound(string matchId, PaidHeartsVersionRequest request, [FromHeader(Name = "Idempotency-Key")] string? key, CancellationToken ct) =>
        HeartsHostFallback.ExecuteAsync(this, accounts, configuration, ct);

    [HttpPost("matches/{matchId}/leave")]
    [EnableRateLimiting(RateLimitPolicies.SlotSpins)]
    public Task<ActionResult> Leave(string matchId, PaidHeartsVersionRequest request, [FromHeader(Name = "Idempotency-Key")] string? key, CancellationToken ct) =>
        HeartsHostFallback.ExecuteAsync(this, accounts, configuration, ct);

    [HttpGet("history")]
    [EnableRateLimiting(RateLimitPolicies.SlotReads)]
    public Task<ActionResult> History(CancellationToken ct) =>
        HeartsHostFallback.ExecuteAsync(this, accounts, configuration, ct);

}
