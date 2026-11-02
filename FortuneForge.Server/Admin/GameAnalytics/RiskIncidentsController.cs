using FortuneForge.Server.Accounts.Security;
using FortuneForge.Server.Admin.Management;
using FortuneForge.Server.Admin.Operations;
using FortuneForge.Server.Admin.Players;
using FortuneForge.Server.Risk;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;
using Microsoft.Extensions.Options;

namespace FortuneForge.Server.Admin.GameAnalytics;

public sealed record CreateRiskIncidentRequest(string UserId, string GameId, string RoundId, string Severity,
    string SourceIncidentId, string RuleId, string RuleVersion, string AttributionBasis, IReadOnlyList<string> EvidenceIds);
public sealed record ResolveRiskIncidentRequest(string Outcome, string Reason, string? Severity = null);

[ApiController]
[Route("api/admin/risk-incidents")]
[EnableRateLimiting(RateLimitPolicies.AdminOperationsReads)]
public sealed class RiskIncidentsController(IServiceProvider services) : ControllerBase
{
    [HttpGet]
    public Task<IActionResult> List([FromQuery] string? userId = null, [FromQuery] int limit = 100, CancellationToken ct = default) =>
        Execute("risk-incidents:list", false, async _ =>
        {
            if (userId is not null) AdminPlayersController.ValidateUserId(userId);
            var store = services.GetRequiredService<FirestoreRiskIncidentStore>();
            return Ok(new { incidents = await store.ListAsync(userId, limit, ct), state = userId is null ? null : await store.GetStateAsync(userId, ct) });
        }, ct);

    [HttpPost]
    [EnableRateLimiting(AdminManagementConfiguration.WriteRateLimitPolicy)]
    public Task<IActionResult> Create(CreateRiskIncidentRequest request, CancellationToken ct = default) =>
        Execute("risk-incidents:create", true, async actor =>
        {
            AdminPlayersController.ValidateUserId(request.UserId);
            return Ok(await services.GetRequiredService<FirestoreRiskIncidentStore>().CreateAsync(new RiskIncidentDraft(
                request.UserId, request.GameId, request.RoundId, request.Severity, "admin-review", request.SourceIncidentId,
                request.RuleId, request.RuleVersion, request.AttributionBasis, request.EvidenceIds, actor), ct));
        }, ct);

    [HttpPost("{id}/resolution")]
    [EnableRateLimiting(AdminManagementConfiguration.WriteRateLimitPolicy)]
    public Task<IActionResult> Resolve(string id, ResolveRiskIncidentRequest request, CancellationToken ct = default) =>
        Execute("risk-incidents:resolve", true, async actor => Ok(await services.GetRequiredService<FirestoreRiskIncidentStore>()
            .ResolveAsync(id, actor, request.Outcome, request.Reason, ct, request.Severity)), ct);

    private async Task<IActionResult> Execute(string operation, bool write, Func<string, Task<IActionResult>> action, CancellationToken ct)
    {
        var configuration = services.GetRequiredService<IConfiguration>();
        if (!AdminOperationsFeature.IsEnabled(configuration) || write && !AdminManagementFeature.Enabled(configuration))
            return StatusCode(503, new { error = "Admin operations are disabled." });
        var access = await services.GetRequiredService<IAdminOperationsAuthorizer>().AuthorizeAsync(Request, ct);
        if (access.Status == AdminOperationsAccessStatus.Unauthenticated) return Unauthorized(new { error = "Sign in to review incidents." });
        if (access.Status != AdminOperationsAccessStatus.Authorized) return StatusCode(403, new { error = "Administrator access is required." });
        if (write && !AdminManagementRequestSecurity.AllowsWrite(Request, services.GetRequiredService<IOptions<AdminManagementOptions>>().Value))
            return StatusCode(403, new { error = "The administrative request origin could not be verified." });
        Response.Headers.CacheControl = "no-store";
        await services.GetRequiredService<IAdminOperationsStore>().AppendAuthorizedAccessAuditAsync(access.UserId!, operation,
            services.GetRequiredService<TimeProvider>().GetUtcNow().UtcDateTime, ct);
        try { return await action(access.UserId!); }
        catch (RiskIncidentConflictException exception) { return Conflict(new { error = exception.Message }); }
        catch (KeyNotFoundException exception) { return NotFound(new { error = exception.Message }); }
        catch (Exception exception) when (exception is ArgumentException or AdminOperationsQueryException)
        { return BadRequest(new { error = exception.Message }); }
    }
}
