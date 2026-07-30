using FortuneForge.Server.Accounts;
using FortuneForge.Server.Accounts.Models;
using FortuneForge.Server.Accounts.Security;
using FortuneForge.Server.Slots.Spins;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;

namespace FortuneForge.Server.Controllers;

public sealed record SpinRequest(
    string GameId,
    long WagerPoints,
    bool UseFreeSpin,
    bool UseSpecialBoost);

[ApiController]
[Route("api/slots")]
public sealed class SlotsController(
    SpinService spinService,
    AccountService accountService,
    ILogger<SlotsController> logger) : ControllerBase
{
    private static readonly TimeSpan SpinCooldown = TimeSpan.FromMilliseconds(500);

    [HttpGet("state")]
    [EnableRateLimiting(RateLimitPolicies.SlotReads)]
    public async Task<ActionResult> State(
        [FromQuery] string gameId,
        CancellationToken cancellationToken)
    {
        try
        {
            spinService.ValidateGame(gameId);
            var pointValueInCents = spinService.GetPointValueInCents(gameId);
            var accountResult = await accountService.GetProfileAsync(SessionToken(), cancellationToken);
            if (accountResult.Value is null)
            {
                return Unauthorized(new { error = "Sign in to play Fortune Slots." });
            }

            return Ok(await accountService.GetSlotStateAsync(
                accountResult.Value.UserId,
                gameId,
                pointValueInCents,
                cancellationToken));
        }
        catch (KeyNotFoundException exception)
        {
            return NotFound(new { error = exception.Message });
        }
        catch (ArgumentException exception)
        {
            logger.LogWarning(
                "Rejected malformed slot state request for game {GameId}; trace {TraceIdentifier}.",
                gameId,
                HttpContext.TraceIdentifier);
            return BadRequest(new { error = exception.Message });
        }
    }

    [HttpPost("spins")]
    [EnableRateLimiting(RateLimitPolicies.SlotSpins)]
    public async Task<ActionResult> Spin(
        SpinRequest request,
        CancellationToken cancellationToken)
    {
        try
        {
            var accountResult = await accountService.GetProfileAsync(
                SessionToken(),
                cancellationToken);
            if (accountResult.Value is null)
            {
                return Unauthorized(new
                {
                    error = "Sign in to play Fortune Slots."
                });
            }

            var account = accountResult.Value;
            spinService.ValidateRequest(request.GameId, request.WagerPoints);
            var pointValueInCents = spinService.GetPointValueInCents(request.GameId);
            var specialBoostCost = request.UseSpecialBoost
                ? spinService.GetSpecialBoostCost(request.GameId)
                : 0;
            var admission = await accountService.BeginSlotSpinAsync(
                account.UserId,
                request.GameId,
                request.WagerPoints,
                pointValueInCents,
                request.UseFreeSpin,
                request.UseSpecialBoost,
                specialBoostCost,
                DateTime.UtcNow,
                SpinCooldown,
                cancellationToken);
            if (admission.CooldownRemaining is { } remaining)
            {
                var retryAfterMilliseconds = Math.Max(1, (int)Math.Ceiling(remaining.TotalMilliseconds));
                Response.Headers.RetryAfter = "1";
                return StatusCode(StatusCodes.Status429TooManyRequests, new
                {
                    error = "Wait half a second before starting another spin.",
                    retryAfterMilliseconds
                });
            }

            var result = spinService.Spin(
                request.GameId,
                admission.WagerPoints,
                account.UserId,
                admission.SpecialBoostApplied,
                admission.EnergyBalance,
                admission.FreeSpinFeatureMode);
            var settlement = await accountService.RecordSlotSpinAsync(
                account.UserId,
                result,
                admission,
                cancellationToken);
            result = result with
            {
                SlotsCreditsBalance = settlement.SlotsCreditsBalance,
                Payout = settlement.Payout,
                IsFreeSpin = admission.IsFreeSpin,
                FreeSpinsRemaining = settlement.FreeSpinsRemaining,
                FreeSpinWagerPoints = settlement.FreeSpinWagerPoints,
                SpecialPointsBalance = settlement.SpecialPointsBalance,
                EnergyBalance = settlement.EnergyBalance,
                EnergyMultiplierApplied = settlement.EnergyMultiplierApplied,
                PayoutMultiplier = settlement.PayoutMultiplier,
                SealCollections = settlement.SealCollections,
                FreeSpinFeatureMode = settlement.FreeSpinFeatureMode
            };
            return Ok(result);
        }
        catch (KeyNotFoundException exception)
        {
            return NotFound(new { error = exception.Message });
        }
        catch (InvalidOperationException exception)
        {
            return Problem(exception.Message, statusCode: StatusCodes.Status500InternalServerError);
        }
        catch (InsufficientSlotCreditsException exception)
        {
            return Conflict(new
            {
                code = "insufficient-slot-credits",
                error = exception.Message,
                available = exception.Available,
                required = exception.Required
            });
        }
        catch (NoFreeSpinsException exception)
        {
            return Conflict(new
            {
                code = "free-spins-unavailable",
                error = exception.Message,
                freeSpinsRemaining = 0
            });
        }
        catch (InsufficientSpecialPointsException exception)
        {
            return Conflict(new
            {
                code = "insufficient-special-points",
                error = exception.Message,
                available = exception.Available,
                required = exception.Required
            });
        }
        catch (ArgumentOutOfRangeException exception)
        {
            logger.LogWarning(
                "Rejected slot wager {WagerPoints} for game {GameId}; trace {TraceIdentifier}.",
                request.WagerPoints,
                request.GameId,
                HttpContext.TraceIdentifier);
            return BadRequest(new { error = exception.Message });
        }
        catch (ArgumentException exception)
        {
            logger.LogWarning(
                "Rejected malformed slot request for game {GameId}; trace {TraceIdentifier}.",
                request.GameId,
                HttpContext.TraceIdentifier);
            return BadRequest(new { error = exception.Message });
        }
    }

    private string? SessionToken() => AccountSessionCookie.Read(Request);
}
