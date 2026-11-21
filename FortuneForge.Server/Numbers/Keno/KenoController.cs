using FortuneForge.Games.Keno.Server;
using FortuneForge.Server.Accounts.Security;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;

namespace FortuneForge.Server.Numbers.Keno;

[ApiController]
[Route("api/games/keno")]
public sealed class KenoController(IConfiguration configuration) : ControllerBase
{
    [HttpGet("status")]
    [EnableRateLimiting(RateLimitPolicies.SlotReads)]
    public ActionResult Status(CancellationToken cancellationToken) => Unavailable();

    [HttpPost("rounds")]
    [EnableRateLimiting(RateLimitPolicies.SlotSpins)]
    public ActionResult Start(CreateKenoRoundRequest request, [FromHeader(Name = "Idempotency-Key")] string? idempotencyKey,
        CancellationToken cancellationToken) => Unavailable();

    [HttpGet("rounds/{roundId}")]
    [EnableRateLimiting(RateLimitPolicies.SlotReads)]
    public ActionResult Get(string roundId, CancellationToken cancellationToken) => Unavailable();

    internal static bool IsEnabled(IConfiguration source) => source.GetValue("Features:KenoEnabled", false);

    private ActionResult Unavailable() => StatusCode(StatusCodes.Status503ServiceUnavailable, IsEnabled(configuration)
        ? new KenoErrorResponse("keno-retry-required", "Retry the original request when the Keno worker is available.")
        : new KenoErrorResponse("keno-disabled", "Keno is still being verified and cannot start a round yet."));
}
