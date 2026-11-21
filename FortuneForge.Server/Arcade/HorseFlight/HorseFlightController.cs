using FortuneForge.Games.HorseFlight;
using FortuneForge.Server.Accounts.Security;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;

namespace FortuneForge.Server.Arcade.HorseFlight;

[ApiController]
[Route("api/games/horse-flight")]
public sealed class HorseFlightController(IConfiguration configuration) : ControllerBase
{
    internal const string EnabledSetting = "Features:HorseFlightEnabled";

    [HttpGet("status")]
    [EnableRateLimiting(RateLimitPolicies.SlotReads)]
    public ActionResult Status() => WorkerRequired();

    [HttpPost("runs")]
    [EnableRateLimiting(RateLimitPolicies.SlotSpins)]
    public ActionResult Start() => WorkerRequired();

    [HttpPost("runs/{runId}/complete")]
    [EnableRateLimiting(RateLimitPolicies.SlotSpins)]
    [RequestSizeLimit(CompleteHorseFlightRunRequest.MaximumBodyBytes)]
    public ActionResult Complete() => WorkerRequired();

    internal static bool IsEnabled(IConfiguration source) => source.GetValue(EnabledSetting, false);

    private ObjectResult WorkerRequired() => StatusCode(StatusCodes.Status503ServiceUnavailable,
        IsEnabled(configuration)
            ? new HorseFlightErrorResponse("horse-flight-worker-required", "Horse Flight is temporarily unavailable.")
            : new HorseFlightErrorResponse("horse-flight-disabled", "Horse Flight is still being verified and cannot start a run yet."));
}
