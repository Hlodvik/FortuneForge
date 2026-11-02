using FortuneForge.Games.Abstractions.Analytics;
using FortuneForge.Server.Accounts;
using FortuneForge.Server.Accounts.Security;
using FortuneForge.Server.GameReports;
using Grpc.Core;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;
using Microsoft.Extensions.Options;

namespace FortuneForge.Server.Activity;

public sealed record GamePresenceRequest(string GameId, string SessionId, bool Visible);

[ApiController]
[Route("api/games/activity/presence")]
public sealed class GamePresenceController(IServiceProvider services) : ControllerBase
{
    [HttpPost]
    [RequestSizeLimit(2048)]
    [EnableRateLimiting(RateLimitPolicies.SlotReads)]
    public async Task<IActionResult> Update(GamePresenceRequest request, CancellationToken ct)
    {
        if (!GameAnalyticsOutbox.GameIds.Contains(request.GameId ?? "") || request.SessionId is null || request.SessionId.Length is < 8 or > 80 || request.SessionId.Any(character => !char.IsAsciiLetterOrDigit(character) && character != '-'))
            return BadRequest(new { error = "Choose a valid game and tab session." });
        if (!GameReportRequestSecurity.AllowsWrite(Request, services.GetRequiredService<IOptions<GameReportsOptions>>().Value, "X-FortuneForge-Presence"))
            return StatusCode(403, new { error = "Send presence from the game page." });
        var account = await services.GetRequiredService<AccountService>().GetAccessContextAsync(AccountSessionCookie.Read(Request), ct);
        if (account is null) return Unauthorized(new { error = "Sign in to update game presence." });
        try
        {
            await services.GetRequiredService<FirestoreGameAnalyticsStore>().TouchPresenceAsync(account.UserId, request.GameId!, request.SessionId, request.Visible, ct);
            return NoContent();
        }
        catch (RpcException) { return StatusCode(503, new { error = "Game presence is temporarily unavailable." }); }
    }
}
