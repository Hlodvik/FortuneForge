using FortuneForge.Server.Accounts.Security;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;

namespace FortuneForge.Server.Admin.Operations;

[ApiController]
[Route("api/admin/operations")]
[EnableRateLimiting(RateLimitPolicies.AdminOperationsReads)]
public sealed class AdminOperationsController(IServiceProvider services) : ControllerBase
{
    private IConfiguration Configuration => services.GetRequiredService<IConfiguration>();
    private IAdminOperationsAuthorizer Authorizer => services.GetRequiredService<IAdminOperationsAuthorizer>();
    private AdminOperationsService Service => services.GetRequiredService<AdminOperationsService>();
    private TimeProvider Clock => services.GetRequiredService<TimeProvider>();
    [HttpGet("overview")]
    public Task<IActionResult> Overview(
        [FromQuery] DateTimeOffset? from = null,
        [FromQuery] DateTimeOffset? to = null,
        CancellationToken cancellationToken = default) =>
        ExecuteAsync("overview", from, to, null, null,
            (range, _, _) => Service.OverviewAsync(range, cancellationToken),
            cancellationToken);

    [HttpGet("activity")]
    public Task<IActionResult> Activity(
        [FromQuery] DateTimeOffset? from = null,
        [FromQuery] DateTimeOffset? to = null,
        [FromQuery] int limit = 50,
        [FromQuery] string? cursor = null,
        CancellationToken cancellationToken = default) =>
        ExecuteAsync("activity", from, to, limit, cursor,
            (range, safeLimit, safeCursor) => Service.ActivityAsync(
                range, safeLimit!.Value, safeCursor, cancellationToken),
            cancellationToken);

    [HttpGet("queues")]
    public Task<IActionResult> Queues(
        [FromQuery] DateTimeOffset? from = null,
        [FromQuery] DateTimeOffset? to = null,
        [FromQuery] int limit = 50,
        [FromQuery] string? cursor = null,
        CancellationToken cancellationToken = default) =>
        ExecuteAsync("queues", from, to, limit, cursor,
            (range, safeLimit, safeCursor) => Service.QueuesAsync(
                range, safeLimit!.Value, safeCursor, cancellationToken),
            cancellationToken);

    [HttpGet("matches")]
    public Task<IActionResult> Matches(
        [FromQuery] DateTimeOffset? from = null,
        [FromQuery] DateTimeOffset? to = null,
        [FromQuery] int limit = 50,
        [FromQuery] string? cursor = null,
        CancellationToken cancellationToken = default) =>
        ExecuteAsync("matches", from, to, limit, cursor,
            (range, safeLimit, safeCursor) => Service.MatchesAsync(
                range, safeLimit!.Value, safeCursor, cancellationToken),
            cancellationToken);

    [HttpGet("integrity")]
    public Task<IActionResult> Integrity(
        [FromQuery] DateTimeOffset? from = null,
        [FromQuery] DateTimeOffset? to = null,
        CancellationToken cancellationToken = default) =>
        ExecuteAsync("integrity", from, to, null, null,
            (range, _, _) => Service.IntegrityAsync(range, cancellationToken),
            cancellationToken);

    [HttpGet("bots")]
    public Task<IActionResult> Bots(
        [FromQuery] DateTimeOffset? from = null,
        [FromQuery] DateTimeOffset? to = null,
        CancellationToken cancellationToken = default) =>
        ExecuteAsync("bots", from, to, null, null,
            (range, _, _) => Service.BotsAsync(
                range, UtcNow(), cancellationToken),
            cancellationToken);

    private async Task<IActionResult> ExecuteAsync<T>(
        string operation,
        DateTimeOffset? from,
        DateTimeOffset? to,
        int? limit,
        string? cursor,
        Func<AdminOperationsRange, int?, string?, Task<T>> execute,
        CancellationToken cancellationToken)
    {
        if (!AdminOperationsFeature.IsEnabled(Configuration))
        {
            return StatusCode(StatusCodes.Status503ServiceUnavailable, new
            {
                code = "admin-operations-disabled",
                error = "The admin operations surface is disabled."
            });
        }

        var access = await Authorizer.AuthorizeAsync(Request, cancellationToken);
        if (access.Status == AdminOperationsAccessStatus.Unauthenticated)
            return Unauthorized(new { error = "Sign in to access operations." });
        if (access.Status == AdminOperationsAccessStatus.Forbidden)
            return StatusCode(StatusCodes.Status403Forbidden, new { error = "Administrator access is required." });

        await Service.AuditAsync(access.UserId!, operation, UtcNow(), cancellationToken);
        try
        {
            var range = Service.ValidateRange(from, to, UtcNow());
            var safeLimit = limit is null
                ? (int?)null
                : AdminOperationsService.ValidateLimit(limit.Value);
            return Ok(await execute(range, safeLimit, cursor));
        }
        catch (AdminOperationsQueryException exception)
        {
            return BadRequest(new { code = "invalid-admin-query", error = exception.Message });
        }
    }

    private DateTime UtcNow() => Clock.GetUtcNow().UtcDateTime;
}
