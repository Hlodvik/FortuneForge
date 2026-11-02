using FortuneForge.Server.Accounts;
using FortuneForge.Server.Accounts.Security;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;

namespace FortuneForge.Server.Activity;

[ApiController]
[Route("api/catalog/recent-games")]
public sealed class RecentGameActivityController(AccountService accounts, IServiceProvider services) : ControllerBase
{
    [HttpGet]
    [EnableRateLimiting(RateLimitPolicies.SlotReads)]
    public async Task<ActionResult> Get(CancellationToken ct)
    {
        var account = await accounts.GetAccessContextAsync(AccountSessionCookie.Read(Request), ct);
        if (account is null) return Unauthorized(new { error = "Sign in to see your recent games." });
        var games = await services.GetRequiredService<IGameActivityStore>().ReadAsync(account.UserId, ct);
        return Ok(new { games });
    }
}

