using FortuneForge.Server.Accounts.Security;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;

namespace FortuneForge.Server.Games.Craps;

[ApiController]
[Route("api/games/craps")]
public sealed class CrapsController : ControllerBase
{
    [HttpGet("status")]
    [HttpGet("rounds/active")]
    [HttpGet("rounds/{roundId:guid}")]
    [EnableRateLimiting(RateLimitPolicies.SlotReads)]
    public ActionResult Read() => WorkerRequired();

    [HttpPost("rounds")]
    [HttpPost("rounds/{roundId:guid}/roll")]
    [HttpPost("rounds/{roundId:guid}/odds")]
    [HttpPost("rounds/{roundId:guid}/wagers")]
    [EnableRateLimiting(RateLimitPolicies.SlotSpins)]
    public ActionResult Mutate() => WorkerRequired();

    private ActionResult WorkerRequired() => StatusCode(StatusCodes.Status503ServiceUnavailable,
        new CrapsErrorResponse("craps-retry-required", "Retry the original action to recover its saved result."));
}
