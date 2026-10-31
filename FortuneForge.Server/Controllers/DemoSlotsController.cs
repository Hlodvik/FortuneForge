using FortuneForge.Server.Accounts.Security;
using FortuneForge.Server.Slots.Bonuses;
using FortuneForge.Server.Slots.Models;
using FortuneForge.Server.Slots.Spins;
using FortuneForge.Server.RoundAudit;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;

namespace FortuneForge.Server.Controllers;

public sealed record DemoSpinRequest(
    string GameId,
    long WagerPoints,
    bool UseFreeSpin,
    int FreeSpinsRemaining,
    long? FreeSpinWagerPoints,
    long EnergyBalance,
    IReadOnlyList<SlotSealCollection>? SealCollections = null,
    string? FreeSpinFeatureMode = null);

[ApiController]
[Route("api/slots/demo")]
public sealed class DemoSlotsController(
    SpinService spinService,
    IDemoSlotAuditStore auditStore,
    ILogger<DemoSlotsController> logger) : ControllerBase
{
    private const string DemoPlayerId = "public-demo";

    [HttpGet("status")]
    public IActionResult Status([FromQuery] string gameId)
    {
        try
        {
            spinService.ValidateGame(gameId);
            return NoContent();
        }
        catch (KeyNotFoundException exception)
        {
            return NotFound(new { error = exception.Message });
        }
        catch (ArgumentException exception)
        {
            return BadRequest(new { error = exception.Message });
        }
    }

    [HttpPost("spins")]
    [EnableRateLimiting(RateLimitPolicies.SlotSpins)]
    public async Task<ActionResult> Spin(DemoSpinRequest request, CancellationToken cancellationToken)
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
            SlotSpecialRoundProfiles.TryGet(request.GameId, out var profile);
            var usesEnergy = profile?.UsesEnergy != false;
            var currentEnergy = usesEnergy ? request.EnergyBalance : 0;
            var startedAtUtc = DateTime.UtcNow;
            var engineResult = spinService.Spin(
                request.GameId,
                request.WagerPoints,
                DemoPlayerId,
                specialBoostApplied: false,
                currentEnergy,
                request.UseFreeSpin && SlotSpecialRoundProfiles.IsFeatureMode(request.FreeSpinFeatureMode)
                    ? request.FreeSpinFeatureMode
                    : null);
            var energy = EnergyBonus.Settle(
                currentEnergy,
                engineResult.EnergyAwarded,
                engineResult.Payout);
            var specialRounds = profile is null
                ? new SlotSpecialRoundProgress(0, null, [])
                : SlotSpecialRoundProfiles.SettleDemo(
                    profile,
                    request.SealCollections ?? [],
                    engineResult.SealsAwarded,
                    engineResult.WagerPoints,
                    energy.MultiplierApplied,
                    engineResult.SpinId);
            var freeSpinsRemaining = checked(
                request.FreeSpinsRemaining - (request.UseFreeSpin ? 1 : 0) +
                engineResult.FreeSpinsAwarded + specialRounds.FreeSpinsAwarded);
            long? freeSpinWagerPoints = freeSpinsRemaining > 0
                ? request.FreeSpinWagerPoints ?? request.WagerPoints
                : null;

            var responseResult = engineResult with
            {
                SlotsCreditsBalance = null,
                Payout = energy.Payout,
                FreeSpinsAwarded = engineResult.GameId == SlotSpecialRoundProfiles.PiratesFortuneGameId
                    ? checked(engineResult.FreeSpinsAwarded + specialRounds.FreeSpinsAwarded)
                    : engineResult.FreeSpinsAwarded,
                CollectionFreeSpinsAwarded = engineResult.GameId == SlotSpecialRoundProfiles.PiratesFortuneGameId
                    ? specialRounds.FreeSpinsAwarded : 0,
                IsFreeSpin = request.UseFreeSpin,
                FreeSpinsRemaining = freeSpinsRemaining,
                FreeSpinWagerPoints = freeSpinWagerPoints,
                EnergyBalance = energy.FinalEnergyBalance,
                EnergyMultiplierApplied = energy.MultiplierApplied,
                PayoutMultiplier = energy.PayoutMultiplier,
                SealCollections = specialRounds.Collections,
                FreeSpinFeatureMode = freeSpinsRemaining > 0
                    ? specialRounds.FeatureMode ??
                        (engineResult.FreeSpinsAwarded > 0 ? profile?.ScatterFeatureMode : null) ??
                        (request.UseFreeSpin ? request.FreeSpinFeatureMode : null)
                    : null
            };
            await auditStore.RecordAsync(new DemoSlotAuditEntry(request, engineResult,
                responseResult, startedAtUtc, DateTime.UtcNow), cancellationToken);
            return Ok(responseResult);
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
