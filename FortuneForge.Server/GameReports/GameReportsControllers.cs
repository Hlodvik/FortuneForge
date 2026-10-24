using FortuneForge.Server.Accounts;
using FortuneForge.Server.Accounts.Security;
using FortuneForge.Server.Admin.Operations;
using Grpc.Core;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;
using Microsoft.Extensions.Options;

namespace FortuneForge.Server.GameReports;

[ApiController]
[Route("api/game-reports")]
public sealed class GameReportsController(IServiceProvider services) : ControllerBase
{
    [HttpPost]
    [RequestSizeLimit(16 * 1024)]
    [EnableRateLimiting(GameReportsConfiguration.WritePolicy)]
    public async Task<IActionResult> Create(CreateGameReportRequest request, CancellationToken ct)
    {
        Response.Headers.CacheControl = "no-store, max-age=0";
        var account = await services.GetRequiredService<IGameReportAccountResolver>().ResolveAsync(Request, ct);
        if (account is null) return Unauthorized(new { error = "Sign in to send a game report." });
        if (!GameReportRequestSecurity.AllowsWrite(Request, services.GetRequiredService<IOptions<GameReportsOptions>>().Value, "X-FortuneForge-Report"))
            return StatusCode(403, new { error = "Send reports from the game’s report button." });
        try
        {
            var receipt = await services.GetRequiredService<GameReportsService>().CreateAsync(account.UserId,
                Request.Headers["Idempotency-Key"].ToString(), request, services.GetRequiredService<TimeProvider>().GetUtcNow().UtcDateTime, ct);
            return StatusCode(201, receipt);
        }
        catch (GameReportInputException error) { return BadRequest(new { error = error.Message }); }
        catch (GameReportConflictException error) { return Conflict(new { error = error.Message }); }
        catch (RpcException) { return StatusCode(503, new { error = "Your report could not be confirmed. Retry to send the same report safely." }); }
    }
}

[ApiController]
[Route("api/admin/game-reports")]
[EnableRateLimiting(RateLimitPolicies.AdminOperationsReads)]
public sealed class AdminGameReportsController(IServiceProvider services) : ControllerBase
{
    [HttpGet]
    public Task<IActionResult> List([FromQuery] int limit = 25, [FromQuery] string? cursor = null, CancellationToken ct = default) =>
        Execute(false, "game-reports:list", async _ => Ok(await Service.ListAsync(limit, cursor, ct)), ct);
    [HttpGet("{id}")]
    public Task<IActionResult> Detail(string id, CancellationToken ct) => Execute(false, "game-reports:detail", async _ =>
    {
        var report = await Service.GetAsync(id, ct);
        return report is null ? NotFound(new { error = "Report not found." }) : Ok(report);
    }, ct);
    [HttpPost("{id}/status")]
    [RequestSizeLimit(8 * 1024)]
    [EnableRateLimiting(Admin.Management.AdminManagementConfiguration.WriteRateLimitPolicy)]
    public Task<IActionResult> Update(string id, UpdateGameReportRequest request, CancellationToken ct) => Execute(true, "game-reports:status", async actor =>
    {
        var report = await Service.UpdateAsync(id, actor, request, services.GetRequiredService<TimeProvider>().GetUtcNow().UtcDateTime, ct);
        return report is null ? NotFound(new { error = "Report not found." }) : Ok(report);
    }, ct);
    private GameReportsService Service => services.GetRequiredService<GameReportsService>();
    private async Task<IActionResult> Execute(bool write, string operation, Func<string, Task<IActionResult>> action, CancellationToken ct)
    {
        Response.Headers.CacheControl = "no-store, max-age=0";
        if (!AdminOperationsFeature.IsEnabled(services.GetRequiredService<IConfiguration>()))
            return StatusCode(503, new { error = "The admin reports surface is disabled." });
        var access = await services.GetRequiredService<IAdminOperationsAuthorizer>().AuthorizeAsync(Request, ct);
        if (access.Status == AdminOperationsAccessStatus.Unauthenticated) return Unauthorized(new { error = "Sign in to read game reports." });
        if (access.Status != AdminOperationsAccessStatus.Authorized || string.IsNullOrWhiteSpace(access.UserId))
            return StatusCode(403, new { error = "Administrator access is required." });
        if (write && !GameReportRequestSecurity.AllowsWrite(Request, services.GetRequiredService<IOptions<GameReportsOptions>>().Value, "X-FortuneForge-Admin"))
            return StatusCode(403, new { error = "Use the same-site admin portal to update reports." });
        await services.GetRequiredService<IAdminOperationsStore>().AppendAuthorizedAccessAuditAsync(access.UserId, operation,
            services.GetRequiredService<TimeProvider>().GetUtcNow().UtcDateTime, ct);
        try { return await action(access.UserId); }
        catch (GameReportInputException error) { return BadRequest(new { error = error.Message }); }
        catch (AdminOperationsQueryException error) { return BadRequest(new { error = error.Message }); }
        catch (GameReportConflictException error) { return Conflict(new { error = error.Message }); }
        catch (RpcException) { return StatusCode(503, new { error = "Reports are temporarily unavailable. Refresh before retrying a change." }); }
    }
}

internal sealed class GameReportAccountResolver(AccountService accounts) : IGameReportAccountResolver
{
    public Task<Accounts.Models.AccountAccessContext?> ResolveAsync(HttpRequest request, CancellationToken ct) =>
        accounts.GetAccessContextAsync(AccountSessionCookie.Read(request), ct);
}
