using System.Text.Json.Serialization;
using FortuneForge.Server.Accounts.Security;
using FortuneForge.Server.Accounts.Models;
using FortuneForge.Server.Slots.Bonuses;
using FortuneForge.Server.Slots.Configuration;
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
    string? FreeSpinFeatureMode = null,
    MidnightVaultDemoState? MidnightBonusState = null,
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)] long CrucibleWageredCents = 0,
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] bool? OlympusTumbleShowcase = null);

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
            if (request.OlympusTumbleShowcase is { } showcase)
                return await SpinOlympusTumble(request, showcase, cancellationToken);

            var isGoldFoundry = GoldFoundryCrucible.IsGame(request.GameId);
            var isNeon = request.GameId == SlotSpecialRoundProfiles.NeonNightsGameId;
            if (isGoldFoundry && request.UseFreeSpin)
                throw new ArgumentException("Gold Foundry does not support demo free spins.", nameof(request));
            if (request.FreeSpinsRemaining < 0 || !isNeon && request.FreeSpinsRemaining > 1_000)
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
            if (isNeon && !request.UseFreeSpin && request.FreeSpinsRemaining > 0)
                throw new ArgumentException("Finish your awarded Neon spins before placing another bet.", nameof(request));
            if (isNeon && request.UseFreeSpin && request.WagerPoints != request.FreeSpinWagerPoints)
                throw new ArgumentException("Special Plays use the wager from the triggering spin.", nameof(request));

            if (request.GameId == SlotSpecialRoundProfiles.DragonHoardGameId)
                return await SpinMidnight(request, cancellationToken);
            if (isGoldFoundry && request.CrucibleWageredCents is < 0 or > GoldFoundryCrucible.MaximumDemoWageredCents)
                throw new ArgumentOutOfRangeException(nameof(request.CrucibleWageredCents),
                    "The demo crucible total is outside the allowed range.");
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
                    : null,
                isFreeSpin: isNeon && request.UseFreeSpin);
            var crucible = isGoldFoundry
                ? GoldFoundryCrucible.Settle(currentEnergy, engineResult.EnergyAwarded,
                    request.CrucibleWageredCents, request.UseFreeSpin ? 0 :
                        RandMoney.PointsToCents(engineResult.WagerPoints, engineResult.PointValueInCents))
                : null;
            var energy = crucible?.AsEnergySettlement(engineResult.Payout) ?? EnergyBonus.Settle(
                currentEnergy,
                engineResult.EnergyAwarded,
                engineResult.Payout);
            var specialRounds = isGoldFoundry || profile is null
                ? new SlotSpecialRoundProgress(0, null, [])
                : SlotSpecialRoundProfiles.SettleDemo(
                    profile,
                    request.SealCollections ?? [],
                    engineResult.SealsAwarded,
                    engineResult.WagerPoints,
                    energy.MultiplierApplied,
                    engineResult.SpinId);
            var nextFreeSpins = isGoldFoundry ? 0L :
                (long)request.FreeSpinsRemaining - (request.UseFreeSpin ? 1 : 0) +
                engineResult.FreeSpinsAwarded + specialRounds.FreeSpinsAwarded;
            if (nextFreeSpins > int.MaxValue)
                throw new ArgumentOutOfRangeException(nameof(request.FreeSpinsRemaining),
                    "The next demo free-spin balance exceeds the supported range.");
            var freeSpinsRemaining = checked((int)nextFreeSpins);
            long? freeSpinWagerPoints = freeSpinsRemaining > 0
                ? isNeon ? engineResult.WagerPoints : request.FreeSpinWagerPoints ?? request.WagerPoints
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
                CrucibleWageredCents = crucible?.WageredCents,
                CruciblePourValueCents = crucible?.PourValueCents,
                CruciblePoured = crucible?.Poured,
                CruciblePayoutCents = crucible?.PayoutCents,
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

    private async Task<ActionResult> SpinOlympusTumble(
        DemoSpinRequest request, bool showcase, CancellationToken cancellationToken)
    {
        if (request.GameId != SlotSpecialRoundProfiles.GodsOfOlympusGameId ||
            request.UseFreeSpin || request.FreeSpinsRemaining != 0 || request.FreeSpinWagerPoints is not null ||
            request.FreeSpinFeatureMode is not null || request.EnergyBalance != 0 || request.SealCollections is { Count: > 0 } ||
            request.MidnightBonusState is not null || request.CrucibleWageredCents != 0)
            throw new ArgumentException("The Olympus tumble demo requires its own game and a base spin.", nameof(request));

        var startedAt = DateTime.UtcNow;
        var result = spinService.SpinOlympusTumbleDemo(request.WagerPoints, showcase);
        await auditStore.RecordAsync(new DemoSlotAuditEntry(request, result, result, startedAt, DateTime.UtcNow), cancellationToken);
        return Ok(result);
    }

    private async Task<ActionResult> SpinMidnight(DemoSpinRequest request, CancellationToken cancellationToken)
    {
        var state = CreateMidnightState(request);
        MidnightVaultBonusRounds.Validate(state.Rounds);
        if (state.Rounds.Sum(round => (long)round.RemainingSpins) > 1_000)
            throw new ArgumentException("The demo bonus queue exceeds its allowed size.");
        var head = state.Rounds.FirstOrDefault();
        if (request.FreeSpinsRemaining != (head?.RemainingSpins ?? 0) ||
            request.UseFreeSpin != (head is not null))
            throw new ArgumentException("Finish the active demo bonus round before placing another bet.");
        var wager = head?.WagerPoints ?? request.WagerPoints;
        if (request.WagerPoints != wager)
            throw new ArgumentException("The bonus round uses its locked wager.");
        spinService.ValidateRequest(request.GameId, wager, head is not null);
        var startedAt = DateTime.UtcNow;
        var engine = spinService.SpinMidnightDemo(wager, DemoPlayerId, head);
        var progress = MidnightVaultBonusRounds.Advance(
            head is null ? state.Rounds : MidnightVaultBonusRounds.Consume(state.Rounds),
            state.CollectionCounts, state.CollectionWagerPoints, engine.SealsAwarded,
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
            CollectionFreeSpinsAwarded = progress.AwardedRounds.Where(round => round.FeatureMode == "rows-paw").Sum(round => round.TotalSpins),
            SealCollections = CreateMidnightCollections(progress.CollectionCounts, progress.CollectionWagerPoints),
            MidnightBonusState = new(progress.Rounds, progress.CollectionCounts, progress.CollectionWagerPoints)
        };
        await auditStore.RecordAsync(new DemoSlotAuditEntry(request, engine, response, startedAt, DateTime.UtcNow), cancellationToken);
        return Ok(response);
    }

    internal static MidnightVaultDemoState CreateMidnightState(DemoSpinRequest request, string? capturedMathModelId = null)
    {
        if (request.MidnightBonusState is { } state) return state;
        // Compatibility for an older local demo client. All newly returned state
        // carries exact totals and the complete ordered round queue. Missing live
        // state retains the previous v2 contract; replay supplies its captured model.
        var mathModelId = capturedMathModelId ?? MidnightVaultSlotDefinitions.PreviousPaytableId;
        if (mathModelId is not (MidnightVaultSlotDefinitions.LegacyPaytableId or
            MidnightVaultSlotDefinitions.PreviousPaytableId or MidnightVaultSlotDefinitions.PaytableId))
            throw new ArgumentException("The demo carry has an unsupported Midnight Vault payout model.", nameof(capturedMathModelId));
        // A v1 aggregate does not identify its original six/seven-spin rounds.
        // Preserve old generic receipt replay; never fabricate ten-spin v1 rounds.
        if (request.FreeSpinsRemaining > 0 && mathModelId == MidnightVaultSlotDefinitions.LegacyPaytableId)
            throw new ArgumentException("A legacy v1 demo bonus requires its original round state.", nameof(capturedMathModelId));
        var rounds = new List<MidnightVaultBonusRound>();
        var remaining = request.FreeSpinsRemaining;
        for (var index = 0; remaining > 0; index++)
        {
            var spins = Math.Min(10, remaining);
            rounds.Add(new($"demo-carry:{index}", 10, spins,
                request.FreeSpinWagerPoints ?? request.WagerPoints, request.FreeSpinFeatureMode,
                mathModelId));
            remaining -= spins;
        }
        var collections = request.SealCollections ?? [];
        var counts = collections.ToDictionary(entry => entry.SealId, entry => (long)entry.Count, StringComparer.Ordinal);
        var totals = collections.ToDictionary(entry => entry.SealId,
            entry => checked(entry.Count * (entry.AverageWagerPoints > 0 ? entry.AverageWagerPoints : request.WagerPoints)), StringComparer.Ordinal);
        return new(rounds, counts, totals);
    }

    internal static IReadOnlyList<SlotSealCollection> CreateMidnightCollections(
        IReadOnlyDictionary<string, long> counts, IReadOnlyDictionary<string, long> totals) =>
        new[] { "sync", "rows", "paw", "rand" }.Select(mode =>
        {
            var count = counts.GetValueOrDefault(mode);
            return new SlotSealCollection(mode, checked((int)count),
                count > 0 ? checked((totals.GetValueOrDefault(mode) + count / 2) / count) : 0, 30);
        }).ToArray();
}
