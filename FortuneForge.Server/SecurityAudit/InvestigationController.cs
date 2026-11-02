using FortuneForge.Server.Accounts.Security;
using FortuneForge.Server.Admin.Management;
using FortuneForge.Server.Admin.Operations;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;
using Microsoft.Extensions.Options;

namespace FortuneForge.Server.SecurityAudit;

public sealed record InvestigationResolutionRequest(string Outcome, string Reason);
public sealed record InvestigationPairResolutionRequest(string Outcome, string Reason, string? RiskIncidentId = null);

[ApiController]
[Route("api/admin/security-audit/investigations")]
[EnableRateLimiting(RateLimitPolicies.AdminOperationsReads)]
public sealed class InvestigationController(IServiceProvider services) : ControllerBase
{
    [HttpGet]
    public Task<IActionResult> List([FromQuery] int limit = 25, [FromQuery] string? cursor = null, CancellationToken ct = default) =>
        Execute("security-investigations:list", false, async _ => Ok(await Store.ListAsync(limit, cursor, ct)), ct);

    [HttpGet("{id}")]
    public Task<IActionResult> Read(string id, CancellationToken ct) => Execute("security-investigations:read", false, async _ =>
    {
        var value = await Store.ReadAsync(id, ct);
        return value is null ? NotFound(new { error = "The investigation was not found." }) : Ok(value);
    }, ct);

    [HttpPost("rounds/{auditId}/recheck")]
    [EnableRateLimiting(AdminManagementConfiguration.WriteRateLimitPolicy)]
    public Task<IActionResult> RecheckRound(string auditId, CancellationToken ct = default) =>
        Execute("security-investigations:recheck-round", true, async actor =>
        {
            await Store.RequeueAuditAsync(auditId, actor, ct);
            return Accepted(new { auditId, queuedVersion = GameSecurityIntegrityAuditor.Version });
        }, ct);

    [HttpGet("pair-analysis")]
    public Task<IActionResult> PairAnalysis([FromQuery] int limit = 25, [FromQuery] string? cursor = null, CancellationToken ct = default) =>
        Execute("security-investigations:private-pair-analysis", false, async _ => Ok(await Store.ReadPairQueueAsync(limit, cursor, ct)), ct);

    [HttpPost("pair-analysis/{id}/resolution")]
    [EnableRateLimiting(AdminManagementConfiguration.WriteRateLimitPolicy)]
    public Task<IActionResult> ResolvePairAnalysis(string id, InvestigationPairResolutionRequest request, CancellationToken ct = default) =>
        Execute("security-investigations:resolve-pair-analysis", true, async actor => Ok(await Store.ResolvePairQueueAsync(id, request, actor, ct)), ct);

    [HttpGet("{id}/instances")]
    public Task<IActionResult> Instances(string id, [FromQuery] int limit = 25, [FromQuery] string? cursor = null, CancellationToken ct = default) =>
        Execute("security-investigations:private-instance-manifests", false, async _ => Ok(await Store.ReadInstancesAsync(id, limit, cursor, ct)), ct);

    // Private cards, full command data and IP/geolocation context are restricted to this audited admin route.
    [HttpGet("{id}/instances/{auditId}/evidence")]
    public Task<IActionResult> Evidence(string id, string auditId, [FromQuery] int limit = 25, [FromQuery] string? cursor = null, CancellationToken ct = default) =>
        Execute("security-investigations:private-evidence", false, async _ => Ok(await Store.ReadEvidenceAsync(id, auditId, limit, cursor, ct)), ct);

    [HttpPost("{id}/resolution")]
    [EnableRateLimiting(AdminManagementConfiguration.WriteRateLimitPolicy)]
    public Task<IActionResult> Resolve(string id, InvestigationResolutionRequest request, CancellationToken ct = default) =>
        Execute("security-investigations:resolve", true, async actor => Ok(await Store.ResolveAsync(id, request.Outcome, request.Reason, actor, ct)), ct);

    private SecurityAuditInvestigationService Store => services.GetRequiredService<SecurityAuditInvestigationService>();
    private async Task<IActionResult> Execute(string operation, bool write, Func<string, Task<IActionResult>> action, CancellationToken ct)
    {
        var configuration = services.GetRequiredService<IConfiguration>();
        if (!AdminOperationsFeature.IsEnabled(configuration) || write && !AdminManagementFeature.Enabled(configuration))
            return StatusCode(503, new { error = "Admin operations are disabled." });
        var access = await services.GetRequiredService<IAdminOperationsAuthorizer>().AuthorizeAsync(Request, ct);
        if (access.Status == AdminOperationsAccessStatus.Unauthenticated) return Unauthorized(new { error = "Sign in to review investigations." });
        if (access.Status != AdminOperationsAccessStatus.Authorized) return StatusCode(403, new { error = "Administrator access is required." });
        if (write && !AdminManagementRequestSecurity.AllowsWrite(Request, services.GetRequiredService<IOptions<AdminManagementOptions>>().Value))
            return StatusCode(403, new { error = "The administrative request origin could not be verified." });
        Response.Headers.CacheControl = "no-store";
        await services.GetRequiredService<IAdminOperationsStore>().AppendAuthorizedAccessAuditAsync(access.UserId!, operation,
            services.GetRequiredService<TimeProvider>().GetUtcNow().UtcDateTime, ct);
        try { return await action(access.UserId!); }
        catch (KeyNotFoundException exception) { return NotFound(new { error = exception.Message }); }
        catch (Exception exception) when (exception is ArgumentException or AdminOperationsQueryException)
        { return BadRequest(new { error = exception.Message }); }
    }
}
