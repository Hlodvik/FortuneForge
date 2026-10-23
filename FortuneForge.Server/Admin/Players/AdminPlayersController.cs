using FortuneForge.Server.Accounts.Security;
using FortuneForge.Server.Admin.Operations;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;

namespace FortuneForge.Server.Admin.Players;

[ApiController]
[Route("api/admin/players")]
[EnableRateLimiting(RateLimitPolicies.AdminOperationsReads)]
public sealed class AdminPlayersController(IServiceProvider services) : ControllerBase
{
    [HttpGet]
    public Task<IActionResult> List([FromQuery] string? query = null, [FromQuery] int limit = 25,
        [FromQuery] string? cursor = null, CancellationToken cancellationToken = default) =>
        Execute("players:list", async store => Ok(await store.ListAsync(query, limit, cursor, cancellationToken)), cancellationToken);

    [HttpGet("{userId}")]
    public Task<IActionResult> Detail(string userId, CancellationToken cancellationToken) =>
        Execute("players:detail", async store =>
        {
            ValidateUserId(userId);
            var detail = await store.DetailAsync(userId, cancellationToken);
            return detail is null ? NotFound(new { code = "player-not-found", error = "The player was not found." }) : Ok(detail);
        }, cancellationToken);

    [HttpGet("{userId}/purchases")]
    public Task<IActionResult> Purchases(string userId, [FromQuery] int limit = 25, [FromQuery] string? cursor = null,
        CancellationToken cancellationToken = default) => Execute("players:purchases", async store =>
        { ValidateUserId(userId); return Ok(await store.PurchasesAsync(userId, limit, cursor, cancellationToken)); }, cancellationToken);

    [HttpGet("{userId}/gaming")]
    public Task<IActionResult> Gaming(string userId, [FromQuery] int limit = 25, [FromQuery] string? cursor = null,
        CancellationToken cancellationToken = default) => Execute("players:gaming", async store =>
        { ValidateUserId(userId); return Ok(await store.GamingAsync(userId, limit, cursor, cancellationToken)); }, cancellationToken);

    [HttpGet("{userId}/matches")]
    public Task<IActionResult> Matches(string userId, [FromQuery] int limit = 25, [FromQuery] string? cursor = null,
        CancellationToken cancellationToken = default) => Execute("players:matches", async store =>
        { ValidateUserId(userId); return Ok(await store.MatchesAsync(userId, limit, cursor, cancellationToken)); }, cancellationToken);

    private async Task<IActionResult> Execute(string operation, Func<IAdminPlayersStore, Task<IActionResult>> action, CancellationToken ct)
    {
        if (!AdminOperationsFeature.IsEnabled(services.GetRequiredService<IConfiguration>()))
            return StatusCode(503, new { code = "admin-operations-disabled", error = "The admin operations surface is disabled." });
        var access = await services.GetRequiredService<IAdminOperationsAuthorizer>().AuthorizeAsync(Request, ct);
        if (access.Status == AdminOperationsAccessStatus.Unauthenticated)
            return Unauthorized(new { error = "Sign in to access players." });
        if (access.Status != AdminOperationsAccessStatus.Authorized)
            return StatusCode(403, new { error = "Administrator access is required." });
        await services.GetRequiredService<IAdminOperationsStore>().AppendAuthorizedAccessAuditAsync(
            access.UserId!, operation, services.GetRequiredService<TimeProvider>().GetUtcNow().UtcDateTime, ct);
        Response.Headers.CacheControl = "no-store";
        try { return await action(services.GetRequiredService<IAdminPlayersStore>()); }
        catch (AdminOperationsQueryException exception)
        { return BadRequest(new { code = "invalid-admin-query", error = exception.Message }); }
    }

    internal static void ValidateUserId(string userId)
    {
        if (string.IsNullOrWhiteSpace(userId) || userId.Length > 128 || userId.Contains('/') || userId.Any(char.IsControl))
            throw new AdminOperationsQueryException("Enter a valid immutable user ID.");
    }
}

public static class AdminPlayersConfiguration
{
    public static IServiceCollection AddAdminPlayers(this IServiceCollection services, IConfiguration configuration)
    {
        // AddAdminOperations owns shared authorization, feature gate and validated cursor options.
        services.AddSingleton<IAdminPlayersStore, FirestoreAdminPlayersStore>();
        return services;
    }
}
