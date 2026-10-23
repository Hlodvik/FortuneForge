using FortuneForge.Server.Accounts.Security;
using FortuneForge.Server.Admin.Operations;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;
using Microsoft.Extensions.Options;

namespace FortuneForge.Server.Admin.Management;

[ApiController]
[Route("api/admin/management")]
[EnableRateLimiting(RateLimitPolicies.AdminOperationsReads)]
public sealed class AdminManagementController(IServiceProvider services) : ControllerBase
{
    [HttpGet]
    public Task<IActionResult> Get(CancellationToken cancellationToken) => Execute(false, () => Service.GetAsync(cancellationToken), cancellationToken);

    [HttpGet("audit")]
    public Task<IActionResult> Audit([FromQuery] int limit = 25, [FromQuery] string? cursor = null, CancellationToken cancellationToken = default) =>
        Execute(false, () => Service.AuditAsync(limit, cursor, cancellationToken), cancellationToken);

    [HttpPost("bots")]
    [EnableRateLimiting(AdminManagementConfiguration.WriteRateLimitPolicy)]
    public Task<IActionResult> GlobalBots(AdminBotSwitchRequest request, CancellationToken cancellationToken) =>
        Change("bots-global", null, request.Enabled, request.ExpectedVersion, request.Reason, cancellationToken);

    [HttpPost("games/{gameId}/bots")]
    [EnableRateLimiting(AdminManagementConfiguration.WriteRateLimitPolicy)]
    public Task<IActionResult> GameBots(string gameId, AdminBotSwitchRequest request, CancellationToken cancellationToken) =>
        Change("bots-game", gameId, request.Enabled, request.ExpectedVersion, request.Reason, cancellationToken);

    [HttpPost("games/{gameId}/visibility")]
    [EnableRateLimiting(AdminManagementConfiguration.WriteRateLimitPolicy)]
    public Task<IActionResult> Visibility(string gameId, AdminVisibilityRequest request, CancellationToken cancellationToken) =>
        Change("catalog-visibility", gameId, request.Visible, request.ExpectedVersion, request.Reason, cancellationToken);

    private AdminManagementService Service => services.GetRequiredService<AdminManagementService>();
    private Task<IActionResult> Change(string operation, string? gameId, bool? value, long? version, string? reason, CancellationToken ct) =>
        Execute(true, () => Service.ChangeAsync(HttpContext.Items[ActorKey]!.ToString()!, Request.Headers["Idempotency-Key"].ToString(),
            operation, gameId, value, version, reason, services.GetRequiredService<TimeProvider>().GetUtcNow().UtcDateTime, ct), ct);
    private const string ActorKey = "FortuneForge.AdminManagement.Actor";

    private async Task<IActionResult> Execute<T>(bool write, Func<Task<T>> action, CancellationToken cancellationToken)
    {
        Response.Headers.CacheControl = "no-store, max-age=0";
        if (!AdminManagementFeature.Enabled(services.GetRequiredService<IConfiguration>()))
            return StatusCode(503, new { code = "admin-management-disabled", error = "Admin management is disabled." });
        var access = await services.GetRequiredService<IAdminOperationsAuthorizer>().AuthorizeAsync(Request, cancellationToken);
        if (access.Status == AdminOperationsAccessStatus.Unauthenticated) return Unauthorized(new { error = "Sign in to access management." });
        if (access.Status != AdminOperationsAccessStatus.Authorized || string.IsNullOrWhiteSpace(access.UserId)) return StatusCode(403, new { error = "Administrator access is required." });
        if (write && !AdminManagementRequestSecurity.AllowsWrite(Request, services.GetRequiredService<IOptions<AdminManagementOptions>>().Value))
            return StatusCode(403, new { code = "admin-write-origin-rejected", error = "Use the same-site admin portal to make this change." });
        HttpContext.Items[ActorKey] = access.UserId;
        try { return Ok(await action()); }
        catch (AdminManagementInputException exception) { return BadRequest(new { code = "invalid-management-change", error = exception.Message }); }
        catch (AdminManagementConflictException exception) { return Conflict(new { code = exception.Code, error = exception.Message }); }
    }
}

[ApiController]
[Route("api/catalog/visibility")]
[EnableRateLimiting(RateLimitPolicies.SlotReads)]
public sealed class CatalogVisibilityController(IServiceProvider services) : ControllerBase
{
    [HttpGet]
    public async Task<IActionResult> Get(CancellationToken cancellationToken)
    {
        Response.Headers.CacheControl = "no-store, max-age=0";
        return Ok(await services.GetRequiredService<AdminManagementService>().VisibilityAsync(cancellationToken));
    }
}
