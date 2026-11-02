using FortuneForge.Server.Accounts.Security;
using FortuneForge.Server.Activity;
using FortuneForge.Server.Admin.Operations;
using FortuneForge.Server.Admin.Players;
using FortuneForge.Server.RoundAudit;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;

namespace FortuneForge.Server.Admin.GameAnalytics;

[ApiController]
[Route("api/admin/game-analytics")]
[EnableRateLimiting(RateLimitPolicies.AdminOperationsReads)]
public sealed class GameAnalyticsController(IServiceProvider services) : ControllerBase
{
    [HttpGet]
    public Task<IActionResult> Metrics([FromQuery] string period = "day", [FromQuery] string? date = null,
        [FromQuery] string? userId = null, CancellationToken ct = default) => Execute("game-analytics:read", async () =>
        {
            if (userId is not null) AdminPlayersController.ValidateUserId(userId);
            return Ok(await services.GetRequiredService<IGameAnalyticsReader>().ReadAsync(period, date, userId, ct));
        }, ct);

    [HttpGet("rounds")]
    public Task<IActionResult> Rounds([FromQuery] string? gameId = null, [FromQuery] string? status = null,
        [FromQuery] int limit = 25, [FromQuery] string? cursor = null, CancellationToken ct = default) =>
        Execute("round-audits:list", async () => Ok(await services.GetRequiredService<IGameRoundAuditStore>().ListAsync(gameId, status, limit, cursor, ct)), ct);

    [HttpGet("rounds/{id}")]
    public Task<IActionResult> Evidence(string id, CancellationToken ct = default) => Execute("round-audits:private-evidence", async () =>
    {
        var detail = await services.GetRequiredService<IGameRoundAuditStore>().ReadAsync(id, ct);
        return detail is null ? NotFound(new { error = "The audit record was not found." }) : Ok(detail);
    }, ct);

    private async Task<IActionResult> Execute(string operation, Func<Task<IActionResult>> action, CancellationToken ct)
    {
        if (!AdminOperationsFeature.IsEnabled(services.GetRequiredService<IConfiguration>()))
            return StatusCode(503, new { error = "Admin operations are disabled." });
        var access = await services.GetRequiredService<IAdminOperationsAuthorizer>().AuthorizeAsync(Request, ct);
        if (access.Status == AdminOperationsAccessStatus.Unauthenticated) return Unauthorized(new { error = "Sign in to access analytics." });
        if (access.Status != AdminOperationsAccessStatus.Authorized) return StatusCode(403, new { error = "Administrator access is required." });
        await services.GetRequiredService<IAdminOperationsStore>().AppendAuthorizedAccessAuditAsync(access.UserId!, operation,
            services.GetRequiredService<TimeProvider>().GetUtcNow().UtcDateTime, ct);
        Response.Headers.CacheControl = "no-store";
        try { return await action(); }
        catch (Exception exception) when (exception is AdminOperationsQueryException or GameAnalyticsQueryException)
        { return BadRequest(new { error = exception.Message }); }
    }
}
