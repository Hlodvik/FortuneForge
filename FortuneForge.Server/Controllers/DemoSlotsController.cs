using FortuneForge.Server.Accounts.Security;
using FortuneForge.Server.Slots.Bonuses;
using FortuneForge.Server.Slots.Spins;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;

namespace FortuneForge.Server.Controllers;

public sealed record DemoSpinRequest(
    string GameId,
    long WagerPoints,
    bool UseFreeSpin,
    int FreeSpinsRemaining,
    long? FreeSpinWagerPoints,
    long EnergyBalance);

[ApiController]
[Route("api/slots/demo")]
public sealed class DemoSlotsController(
    SpinService spinService,
    ILogger<DemoSlotsController> logger) : ControllerBase
{
    private const string DemoPlayerId = "public-demo";

    [HttpPost("spins")]
    [EnableRateLimiting(RateLimitPolicies.SlotSpins)]
    public ActionResult Spin(DemoSpinRequest request)
    {
        try
        {
            if (request.FreeSpinsRemaining < 0 || request.FreeSpinsRemaining > 1_000)
            {
                throw new ArgumentOutOfRangeException(
                    nameof(request.FreeSpinsRemaining),
                    "The demo free-spin balance is outside the allowed range.");
            }
            if (request.EnergyBalance < 0 || request.EnergyBalance > EnergyBonus.MeterCapacityPoints)
            {
                throw new ArgumentOutOfRangeException(
                    nameof(request.EnergyBalance),
                    "The demo energy balance is outside the allowed range.");
            }
            if (request.UseFreeSpin && request.FreeSpinsRemaining == 0)
            {
                throw new ArgumentException("No demo free spins are available.", nameof(request));
            }

            spinService.ValidateRequest(request.GameId, request.WagerPoints);
            var result = spinService.Spin(
                request.GameId,
                request.WagerPoints,
                DemoPlayerId,
                specialBoostApplied: false,
                request.EnergyBalance);
            var energy = EnergyBonus.Settle(
                request.EnergyBalance,
                result.EnergyAwarded,
                result.Payout);
            var freeSpinsRemaining = checked(
                request.FreeSpinsRemaining - (request.UseFreeSpin ? 1 : 0) + result.FreeSpinsAwarded);
            long? freeSpinWagerPoints = freeSpinsRemaining > 0
                ? request.FreeSpinWagerPoints ?? request.WagerPoints
                : null;

            return Ok(result with
            {
                SlotsCreditsBalance = null,
                Payout = energy.Payout,
                IsFreeSpin = request.UseFreeSpin,
                FreeSpinsRemaining = freeSpinsRemaining,
                FreeSpinWagerPoints = freeSpinWagerPoints,
                EnergyBalance = energy.FinalEnergyBalance,
                EnergyMultiplierApplied = energy.MultiplierApplied,
                PayoutMultiplier = energy.PayoutMultiplier
            });
        }
        catch (KeyNotFoundException exception)
        {
            return NotFound(new { error = exception.Message });
        }
        catch (ArgumentOutOfRangeException exception)
        {
            logger.LogWarning(
                "Rejected demo slot wager {WagerPoints} for game {GameId}; trace {TraceIdentifier}.",
                request.WagerPoints,
                request.GameId,
                HttpContext.TraceIdentifier);
            return BadRequest(new { error = exception.Message });
        }
        catch (ArgumentException exception)
        {
            logger.LogWarning(
                "Rejected malformed demo slot request for game {GameId}; trace {TraceIdentifier}.",
                request.GameId,
                HttpContext.TraceIdentifier);
            return BadRequest(new { error = exception.Message });
        }
    }
}
