using FortuneForge.Server.Accounts.Security;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;

namespace FortuneForge.Server.Catalog;

[ApiController]
[Route("api/catalog/popularity")]
[EnableRateLimiting(RateLimitPolicies.SlotReads)]
public sealed class CatalogPopularityController(IServiceProvider services, ILogger<CatalogPopularityController> logger) : ControllerBase
{
    [HttpGet]
    public async Task<IActionResult> Get(CancellationToken cancellationToken)
    {
        Response.Headers.CacheControl = "no-store, max-age=0";
        try
        {
            return Ok(await services.GetRequiredService<CatalogPopularityService>().GetAsync(cancellationToken));
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested) { throw; }
        catch (Exception exception)
        {
            logger.LogWarning(exception, "Could not read catalogue popularity.");
            return StatusCode(StatusCodes.Status503ServiceUnavailable,
                new { message = "Game popularity is temporarily unavailable." });
        }
    }
}
