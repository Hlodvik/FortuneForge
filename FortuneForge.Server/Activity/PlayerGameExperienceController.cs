using FortuneForge.Server.Accounts.Security;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;

namespace FortuneForge.Server.Activity;

[ApiController]
[Route("api/games/experience")]
public sealed class PlayerGameExperienceController(IServiceProvider services) : ControllerBase
{
    [HttpGet]
    [EnableRateLimiting(RateLimitPolicies.SlotReads)]
    public async Task<ActionResult> Get(CancellationToken ct)
    {
        Response.Headers.CacheControl = "no-store";
        var userId = await services.GetRequiredService<IGameActivityAccountResolver>().UserIdAsync(HttpContext, ct);
        if (userId is null) return Unauthorized(new { error = "Sign in to read your game experience." });
        return Ok(await services.GetRequiredService<IPlayerGameExperienceStore>().ReadAsync(userId, ct));
    }
}
