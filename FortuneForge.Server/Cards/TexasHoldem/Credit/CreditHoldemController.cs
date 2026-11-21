using FortuneForge.Server.Accounts;
using FortuneForge.Server.Accounts.Security;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;

namespace FortuneForge.Server.Cards.TexasHoldem.Credit;

[ApiController]
[Route("api/cards/texas-holdem/credit")]
public sealed class CreditHoldemController : ControllerBase
{
    private readonly IConfiguration configuration;
    public CreditHoldemController(IServiceProvider services, AccountService accountService,
        IConfiguration configuration, ILogger<CreditHoldemController> logger)
    {
        this.configuration = configuration;
    }

    [HttpGet("status")]
    [EnableRateLimiting(RateLimitPolicies.CreditHoldemReads)]
    public Task<ActionResult> Status(CancellationToken cancellationToken) => Unavailable();

    [HttpGet("session")]
    [EnableRateLimiting(RateLimitPolicies.CreditHoldemReads)]
    public Task<ActionResult> Session(CancellationToken cancellationToken) => Unavailable();

    [HttpPost("queue")]
    [EnableRateLimiting(RateLimitPolicies.CreditHoldemWrites)]
    public Task<ActionResult> Join(JoinCreditHoldemQueueRequest request, [FromHeader(Name = "Idempotency-Key")] string? idempotencyKey, CancellationToken cancellationToken) => Unavailable();

    [HttpDelete("queue/{ticketId}")]
    [EnableRateLimiting(RateLimitPolicies.CreditHoldemWrites)]
    public Task<ActionResult> Cancel(string ticketId, CreditHoldemVersionRequest request, [FromHeader(Name = "Idempotency-Key")] string? idempotencyKey, CancellationToken cancellationToken) => Unavailable();

    [HttpPost("matches/{matchId}/actions")]
    [EnableRateLimiting(RateLimitPolicies.CreditHoldemWrites)]
    public Task<ActionResult> Action(string matchId, CreditHoldemActionRequest request, [FromHeader(Name = "Idempotency-Key")] string? idempotencyKey, CancellationToken cancellationToken) => Unavailable();

    [HttpPost("matches/{matchId}/next-hand")]
    [EnableRateLimiting(RateLimitPolicies.CreditHoldemWrites)]
    public Task<ActionResult> NextHand(string matchId, CreditHoldemVersionRequest request, [FromHeader(Name = "Idempotency-Key")] string? idempotencyKey, CancellationToken cancellationToken) => Unavailable();

    [HttpPost("matches/{matchId}/leave")]
    [EnableRateLimiting(RateLimitPolicies.CreditHoldemWrites)]
    public Task<ActionResult> Leave(string matchId, CreditHoldemVersionRequest request, [FromHeader(Name = "Idempotency-Key")] string? idempotencyKey, CancellationToken cancellationToken) => Unavailable();

    [HttpGet("history")]
    [EnableRateLimiting(RateLimitPolicies.CreditHoldemReads)]
    public Task<ActionResult> History([FromQuery] int limit = 30, CancellationToken cancellationToken = default) => Unavailable();

    [HttpPost("history/{eventId}/seen")]
    [EnableRateLimiting(RateLimitPolicies.CreditHoldemWrites)]
    public Task<ActionResult> MarkHistorySeen(string eventId, CancellationToken cancellationToken) => Unavailable();

    internal static bool IsEnabled(IConfiguration source) => source.GetValue(CreditHoldemBackend.EnabledSetting, false);
    internal static CreditHoldemStatusResponse StatusContract(IConfiguration configuration) => CreditHoldemServerContract.Status(configuration);
    private Task<ActionResult> Unavailable() => Task.FromResult<ActionResult>(StatusCode(StatusCodes.Status503ServiceUnavailable,
        new { code = IsEnabled(configuration) ? "credit-holdem-worker-unavailable" : "credit-texas-holdem-disabled",
            error = "Retry the original request when the Texas Hold'em worker is available." }));
}
