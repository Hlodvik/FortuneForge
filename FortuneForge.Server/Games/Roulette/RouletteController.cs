using FortuneForge.Server.Accounts.Security;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;

namespace FortuneForge.Server.Games.Roulette;

[ApiController]
[Route("api/games/roulette")]
public sealed class RouletteController(IConfiguration configuration) : ControllerBase
{
    [HttpGet("status")]
    [EnableRateLimiting(RateLimitPolicies.SlotReads)]
    public ActionResult Status() => Unavailable();

    [HttpPost("rounds")]
    [EnableRateLimiting(RateLimitPolicies.SlotSpins)]
    public ActionResult Start() => Unavailable();

    [HttpGet("rounds/{roundId:guid}")]
    [EnableRateLimiting(RateLimitPolicies.SlotReads)]
    public ActionResult Get(Guid roundId) => Unavailable();

    [HttpGet("rounds/{roundId:guid}/history/{roundNumber:int}")]
    [EnableRateLimiting(RateLimitPolicies.SlotReads)]
    public ActionResult History(Guid roundId, int roundNumber) => Unavailable();

    [HttpPost("rounds/{roundId:guid}/bets")]
    [EnableRateLimiting(RateLimitPolicies.SlotSpins)]
    public ActionResult PlaceBet(Guid roundId, PlaceRouletteBetRequest request) => Unavailable();

    [HttpDelete("rounds/{roundId:guid}/bets/{betIndex:int}")]
    [EnableRateLimiting(RateLimitPolicies.SlotSpins)]
    public ActionResult RemoveBet(Guid roundId, int betIndex) => Unavailable();

    [HttpDelete("rounds/{roundId:guid}/bets")]
    [EnableRateLimiting(RateLimitPolicies.SlotSpins)]
    public ActionResult ClearBets(Guid roundId) => Unavailable();

    [HttpPost("rounds/{roundId:guid}/spin")]
    [EnableRateLimiting(RateLimitPolicies.SlotSpins)]
    public ActionResult Spin(Guid roundId) => Unavailable();

    // The authenticated worker gateway handles opted-in routes before MVC.
    private ActionResult Unavailable() => StatusCode(StatusCodes.Status503ServiceUnavailable,
        configuration.GetValue("Features:RouletteEnabled", false)
            ? new RouletteErrorResponse("roulette-worker-unavailable", "The Roulette service is temporarily unavailable.")
            : new RouletteErrorResponse("roulette-disabled", "Roulette is not available yet."));
}
