using System.Text.Json.Serialization;
using FortuneForge.Server.Accounts.Security;
using FortuneForge.Server.Accounts.Models;
using FortuneForge.Server.Slots.Bonuses;
using FortuneForge.Server.Slots.Configuration;
using FortuneForge.Server.Slots.Games.GoldFoundry;
using FortuneForge.Server.Slots.Models;
using FortuneForge.Server.Slots.Spins;
using FortuneForge.Server.RoundAudit;
using Microsoft.AspNetCore.Mvc;

namespace FortuneForge.Server.Controllers;

public sealed record DemoSpinRequest(
    string GameId,
    long WagerPoints,
    bool UseFreeSpin,
    int FreeSpinsRemaining,
    long? FreeSpinWagerPoints,
    long EnergyBalance,
    IReadOnlyList<SlotSealCollection>? SealCollections = null,
    string? FreeSpinFeatureMode = null,
    MidnightVaultDemoState? MidnightBonusState = null,
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)] long CrucibleWageredCents = 0,
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] bool? OlympusTumbleShowcase = null,
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)] int NeonDiscoSpinsRemaining = 0,
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] int? NeonDiscoMultiplier = null,
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] long? NeonDiscoWagerPoints = null);

[NonController]
public sealed class DemoSlotsController(
    SpinService spinService,
    IDemoSlotAuditStore auditStore,
    ILogger<DemoSlotsController> logger) : ControllerBase
{
    private const string DemoPlayerId = "public-demo";

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

    public async Task<ActionResult> Spin(DemoSpinRequest request, CancellationToken cancellationToken)
    {
        try
        {
            var isNeon = request.GameId == SlotSpecialRoundProfiles.NeonNightsGameId;
            if (!isNeon && request.NeonDiscoMultiplier is not null)
                throw new ArgumentException("Only Neon Nights supports a Disco multiplier.", nameof(request.NeonDiscoMultiplier));
            if (!isNeon && request.NeonDiscoWagerPoints is not null)
                throw new ArgumentException("Only Neon Nights supports a Disco wager lock.", nameof(request.NeonDiscoWagerPoints));
            var isGoldFoundry = request.GameId == GoldFoundrySlotDefinitions.GameId;
            if (isGoldFoundry)
                GoldFoundryDemoBoundary.Validate(request);
            if (request.OlympusTumbleShowcase is { } showcase)
                return await SpinOlympusTumble(request, showcase, cancellationToken);
            if (isNeon)
                return await SpinNeon(request, cancellationToken);

            if (request.NeonDiscoSpinsRemaining != 0)
                throw new ArgumentOutOfRangeException(nameof(request.NeonDiscoSpinsRemaining),
                    "The demo Disco play balance is outside the allowed range.");
            if (request.FreeSpinsRemaining is < 0 or > 1_000)
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
            if (request.GameId == SlotSpecialRoundProfiles.DragonHoardGameId)
                return await SpinMidnight(request, cancellationToken);
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
            if (isGoldFoundry)
            {
                var settled = GoldFoundryCrucible.SettleDemo(engineResult, currentEnergy, request.CrucibleWageredCents);
                await auditStore.RecordAsync(new DemoSlotAuditEntry(request, engineResult,
                    settled, startedAtUtc, DateTime.UtcNow), cancellationToken);
                return Ok(settled);
            }

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
            var nextFreeSpins = (long)request.FreeSpinsRemaining - (request.UseFreeSpin ? 1 : 0) +
                engineResult.FreeSpinsAwarded + specialRounds.FreeSpinsAwarded;
            if (nextFreeSpins > int.MaxValue)
                throw new ArgumentOutOfRangeException(nameof(request.FreeSpinsRemaining),
                    "The next demo free-spin balance exceeds the supported range.");
            var freeSpinsRemaining = checked((int)nextFreeSpins);
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
                ChargedWagerCents = !request.UseFreeSpin
                    ? RandMoney.PointsToCents(engineResult.WagerPoints, engineResult.PointValueInCents)
                    : 0,
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

    private async Task<ActionResult> SpinNeon(DemoSpinRequest request, CancellationToken cancellationToken)
    {
        var policy = new NeonDemoPolicy(new(request.WagerPoints, request.UseFreeSpin, request.FreeSpinsRemaining,
            request.FreeSpinWagerPoints, request.FreeSpinFeatureMode, request.NeonDiscoSpinsRemaining,
            request.NeonDiscoWagerPoints, request.NeonDiscoMultiplier, request.EnergyBalance), EnergyBonus.MeterCapacityPoints);
        var play = policy.Engine;
        spinService.ValidateRequest(request.GameId, play.WagerPoints);
        var startedAtUtc = DateTime.UtcNow;
        var engineResult = spinService.Spin(request.GameId, play.WagerPoints, DemoPlayerId, specialBoostApplied: false,
            play.EnergyBalance, play.FeatureMode, play.PityMissesBefore, play.IsFreeSpin, play.IsDiscoPlay, play.DiscoMultiplier);
        var progress = policy.Complete(engineResult.WagerPoints, engineResult.FreeSpinsAwarded,
            engineResult.NeonArcade, engineResult.NeonDiscoCascade);
        var response = engineResult with
        {
            SlotsCreditsBalance = null,
            ChargedWagerCents = policy.ChargesWager
                ? RandMoney.PointsToCents(engineResult.WagerPoints, engineResult.PointValueInCents) : 0,
            FreeSpinsRemaining = progress.FreeSpinsRemaining,
            FreeSpinWagerPoints = progress.FreeSpinWagerPoints,
            FreeSpinFeatureMode = progress.FreeSpinFeatureMode,
            NeonDiscoSpinsRemaining = progress.NeonDiscoSpinsRemaining,
            NeonDiscoWagerPoints = progress.NeonDiscoWagerPoints,
            NeonDiscoMultiplier = progress.NeonDiscoMultiplier,
            EnergyBalance = progress.EnergyBalance,
            EnergyMultiplierApplied = progress.EnergyMultiplierApplied,
            PayoutMultiplier = progress.PayoutMultiplier
        };
        await auditStore.RecordAsync(new DemoSlotAuditEntry(request, engineResult, response, startedAtUtc, DateTime.UtcNow), cancellationToken);
        return Ok(response);
    }

    private async Task<ActionResult> SpinOlympusTumble(
        DemoSpinRequest request, bool showcase, CancellationToken cancellationToken)
    {
        if (request.GameId != SlotSpecialRoundProfiles.GodsOfOlympusGameId ||
            request.UseFreeSpin || request.FreeSpinsRemaining != 0 || request.FreeSpinWagerPoints is not null ||
            request.FreeSpinFeatureMode is not null || request.EnergyBalance != 0 || request.SealCollections is { Count: > 0 } ||
            request.MidnightBonusState is not null || request.CrucibleWageredCents != 0 ||
            request.NeonDiscoSpinsRemaining != 0)
            throw new ArgumentException("The Olympus tumble demo requires its own game and a base spin.", nameof(request));

        var startedAt = DateTime.UtcNow;
        var result = spinService.SpinOlympusTumbleDemo(request.WagerPoints, showcase);
        await auditStore.RecordAsync(new DemoSlotAuditEntry(request, result, result, startedAt, DateTime.UtcNow), cancellationToken);
        return Ok(result);
    }

    private async Task<ActionResult> SpinMidnight(DemoSpinRequest request, CancellationToken cancellationToken)
    {
        var state = CreateMidnightState(request);
        var (head, wager) = MidnightVaultDemoPolicy.Admit(state.Rounds, request.UseFreeSpin,
            request.FreeSpinsRemaining, request.WagerPoints);
        var startedAt = DateTime.UtcNow;
        var engine = spinService.SpinMidnightDemo(wager, DemoPlayerId, head);
        var progress = MidnightVaultDemoPolicy.Advance(state.Rounds, state.CollectionCounts, state.CollectionWagerPoints,
            request.UseFreeSpin, engine.SealsAwarded,
            wager, engine.FreeSpinsAwarded, engine.EngineAudit!.FeatureProfile?.ScatterFeatureMode,
            engine.PaytableId, engine.SpinId);
        var next = progress.Rounds.FirstOrDefault();
        var response = engine with
        {
            FreeSpinsRemaining = next?.RemainingSpins ?? 0,
            FreeSpinWagerPoints = next?.WagerPoints,
            FreeSpinFeatureMode = next?.FeatureMode,
            BonusRound = next,
            QueuedBonusRounds = progress.Rounds.Skip(1).ToArray(),
            AwardedBonusRounds = progress.AwardedRounds,
            CollectionFreeSpinsAwarded = MidnightVaultDemoPolicy.CollectionSpinsAwarded(progress),
            SealCollections = CreateMidnightCollections(progress.CollectionCounts, progress.CollectionWagerPoints),
            MidnightBonusState = new(progress.Rounds, progress.CollectionCounts, progress.CollectionWagerPoints)
        };
        await auditStore.RecordAsync(new DemoSlotAuditEntry(request, engine, response, startedAt, DateTime.UtcNow), cancellationToken);
        return Ok(response);
    }

    internal static MidnightVaultDemoState CreateMidnightState(DemoSpinRequest request, string? capturedMathModelId = null)
    {
        if (request.MidnightBonusState is { } state) return state;
        var carry = MidnightVaultDemoPolicy.ReadLegacyCarry(request.WagerPoints, request.FreeSpinsRemaining,
            request.FreeSpinWagerPoints, request.FreeSpinFeatureMode, (request.SealCollections ?? [])
                .Select(entry => (entry.SealId, entry.Count, entry.AverageWagerPoints)), capturedMathModelId);
        return new(carry.Rounds, carry.CollectionCounts, carry.CollectionWagerPoints);
    }

    internal static IReadOnlyList<SlotSealCollection> CreateMidnightCollections(
        IReadOnlyDictionary<string, long> counts, IReadOnlyDictionary<string, long> totals) =>
        MidnightVaultDemoPolicy.DescribeCollections(counts, totals).Select(entry =>
            new SlotSealCollection(entry.SealId, entry.Count, entry.AverageWagerPoints, entry.RequiredCount)).ToArray();
}
