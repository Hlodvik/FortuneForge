using FortuneForge.Server.Accounts;
using FortuneForge.Server.Accounts.Security;
using Google.Cloud.Firestore;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;

namespace FortuneForge.Server.Cards.HouseHoldem;

/// <summary>Public route discovery for the authenticated worker gateway. Lifecycle belongs to the game worker.</summary>
[ApiController]
[Route("api/games/house-holdem")]
public sealed class HouseHoldemController : ControllerBase
{
    private readonly IConfiguration configuration;

    public HouseHoldemController(FirestoreDb database, AccountService accountService, IConfiguration configuration,
        ILogger<HouseHoldemController> logger)
    {
        this.configuration = configuration;
    }

    [HttpGet("status")]
    [EnableRateLimiting(RateLimitPolicies.SlotReads)]
    public Task<ActionResult> Status(CancellationToken cancellationToken) => Unavailable();

    [HttpPost("rounds")]
    [EnableRateLimiting(RateLimitPolicies.SlotSpins)]
    public Task<ActionResult> Start(CreateHouseHoldemRoundRequest request,
        [FromHeader(Name = "Idempotency-Key")] string? idempotencyKey, CancellationToken cancellationToken) => Unavailable();

    [HttpGet("rounds/{roundId}")]
    [EnableRateLimiting(RateLimitPolicies.SlotReads)]
    public Task<ActionResult> Get(string roundId, CancellationToken cancellationToken) => Unavailable();

    [HttpPost("rounds/{roundId}/actions")]
    [EnableRateLimiting(RateLimitPolicies.SlotSpins)]
    public Task<ActionResult> Action(string roundId, HouseHoldemActionRequest request,
        [FromHeader(Name = "Idempotency-Key")] string? idempotencyKey, CancellationToken cancellationToken) => Unavailable();

    [HttpPost("rounds/{roundId}/advance")]
    [EnableRateLimiting(RateLimitPolicies.SlotSpins)]
    public Task<ActionResult> Advance(string roundId,
        [FromHeader(Name = "Idempotency-Key")] string? idempotencyKey, CancellationToken cancellationToken) => Unavailable();

    internal static bool IsEnabled(IConfiguration source) => source.GetValue(HouseHoldemBackend.EnabledSetting, false);
    private Task<ActionResult> Unavailable() => Task.FromResult<ActionResult>(StatusCode(StatusCodes.Status503ServiceUnavailable,
        IsEnabled(configuration)
            ? new HouseHoldemErrorResponse("house-holdem-worker-unavailable", "Retry the original request when the House Hold'em worker is available.")
            : new HouseHoldemErrorResponse("house-holdem-disabled", "This game is not accepting wagers yet.")));
}
