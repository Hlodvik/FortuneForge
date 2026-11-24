// Edited: 2026-11-18T18:14:04-06:00 | Computer: Vinnabok
using System.Text.Json;
using FortuneForge.Games.GodsOfOlympus;
using FortuneForge.Games.GoldFoundry.Server;
using FortuneForge.Games.MidnightVault.Server;
using FortuneForge.Games.JadeDragon;
using FortuneForge.Games.JadeDragon.Server;
using FortuneForge.Games.NeonNights;
using FortuneForge.Games.Slots;
using FortuneForge.Games.Slots.Runtime;
using FortuneForge.Server.Accounts.Models;
using FortuneForge.Server.GameWorkers;
using FortuneForge.Server.RoundAudit;
using FortuneForge.Server.Slots.Bonuses;
using FortuneForge.Server.Slots.Games;
using FortuneForge.Server.Slots.Models;
using HostJadeEngine = FortuneForge.Server.Slots.Games.JadeDragon.JadeDragonEngine;

namespace FortuneForge.Server.Slots.Spins;

internal static class PaidSlotWorker
{
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);

    internal static async Task<SpinResult> ResolveAsync(GameWorkerClient worker, string gameId, string playerId,
        SlotSpinAdmission admission, CancellationToken cancellationToken)
    {
        if (gameId == FortuneForge.Games.FrontierFortune.FrontierFortuneEngine.GameId ||
            gameId == FortuneForge.Games.ArcaneArchives.ArcaneArchivesEngine.GameId)
            throw new GameWorkerUnavailableException(gameId);
        var isNeon = gameId == NeonArcadeSlotDefinitions.GameId;
        if (admission.IsNeonDiscoPlay && !isNeon)
            throw new GameWorkerUnavailableException(gameId);
        var request = new PaidSlotOutcomeRequest(admission.AdmissionId, gameId, playerId, admission.WagerPoints,
            admission.IsFreeSpin, admission.SpecialBoostApplied, admission.EnergyBalance,
            admission.FreeSpinFeatureMode, admission.PityMissesBefore, admission.MathModelId)
        {
            FreeSpinsRemaining = admission.FreeSpinsRemaining
        };
        if (isNeon)
            request = new PaidNeonSpinOptions(admission.IsNeonDiscoPlay,
                admission.IsNeonDiscoPlay ? admission.NeonDiscoMultiplier : null).WriteTo(request);

        var response = await worker.InvokeAsync<PaidSlotOutcomeRequest, PaidSlotOutcomeResponse>(gameId, playerId,
            PaidSlotOutcomeProtocol.Route, request, cancellationToken);
        if (response.AdmissionId != admission.AdmissionId || response.GameId != gameId)
            throw new GameWorkerUnavailableException(gameId);

        try
        {
            T Read<T>() where T : class => response.Outcome.Deserialize<T>(Json)
                ?? throw new JsonException("The game worker outcome is missing.");

            SpinResult ReadJadeLegacy()
            {
                var frozen = Read<JadeLegacyOutcome>();
                if (frozen.Result is null || frozen.Audit is null)
                    throw new JsonException("The legacy Jade outcome is missing its replay audit.");
                return HostJadeEngine.MapOutcome(frozen.Result with { EngineAudit = frozen.Audit }, admission.IsFreeSpin);
            }

            var result = response.OutcomeKind switch
            {
                JadeDragonCascadeRules.Version when gameId == JadeDragonEngine.GameId && admission.MathModelId == JadeDragonCascadeRules.Version =>
                    Games.JadeDragon.JadeDragonCascadeAdapter.MapOutcome(Read<JadeDragonCascadeSpin>(), admission),
                JadeDragonEngine.Version when gameId == JadeDragonEngine.GameId && admission.MathModelId is null && admission.IsFreeSpin =>
                    ReadJadeLegacy(),
                "neon-nights" when gameId == SlotSpecialRoundProfiles.NeonNightsGameId && admission.MathModelId is null =>
                    Games.NeonNights.NeonNightsEngine.MapOutcome(Read<NeonSpinOutcome>()),
                MidnightVaultPaidSlotOutcomes.OutcomeKind when gameId == SlotSpecialRoundProfiles.DragonHoardGameId =>
                    Games.MidnightVault.MidnightVaultEngine.MapOutcome(Read<FortuneForge.Games.MidnightVault.MidnightVaultSpinOutcome>()),
                GoldFoundryPaidSlotOutcomes.OutcomeKind when gameId == FortuneForge.Games.GoldFoundry.GoldFoundryEngine.GameId && admission.MathModelId is null =>
                    Games.GoldFoundry.GoldFoundryEngine.MapOutcome(Read<FortuneForge.Games.GoldFoundry.GoldFoundrySpinOutcome>()),
                PaidSlotOutcomeProtocol.OlympusTumbleKind when gameId == SlotSpecialRoundProfiles.GodsOfOlympusGameId &&
                    OlympusTumbleDemo.SupportsPaidEngineVersion(admission.MathModelId) =>
                    StandaloneSlotPackageRegistry.MapTumble(Read<GodsOfOlympusTumbleSpinOutcome>(), showcase: false,
                        admission.PityMissesBefore, admission.EnergyBalance),
                PaidSlotOutcomeProtocol.ClassicSlotKind when
                    StandaloneSlotPackageRegistry.Owns(gameId) && admission.MathModelId is null =>
                    StandaloneSlotPackageRegistry.MapOutcome(Read<ClassicSlotSpinOutcome>()),
                _ => throw new JsonException("The game worker returned an unexpected outcome kind.")
            };

            if (result.EngineAudit is not { } audit || result.GameId != gameId || result.SpinId == Guid.Empty ||
                result.WagerPoints != admission.WagerPoints || result.IsFreeSpin != admission.IsFreeSpin ||
                result.SpecialBoostApplied != admission.SpecialBoostApplied || result.Payout.TotalPoints < 0 ||
                audit.PityMissesBefore != admission.PityMissesBefore || audit.EnergyBalanceBefore != admission.EnergyBalance ||
                audit.GeneratedAtUtc < admission.StartedAtUtc || audit.GeneratedAtUtc > DateTime.UtcNow ||
                admission.MathModelId is { } model && model != audit.EngineVersion && model != result.PaytableId)
                throw new JsonException("The worker outcome differs from the admitted play.");

            var isCarPlay = isNeon && NeonCarRoundState.IsCarPlay(admission.IsFreeSpin, admission.FreeSpinFeatureMode);
            if (isNeon && audit.FreeSpinFeatureMode != admission.FreeSpinFeatureMode)
                throw new JsonException("The Neon worker outcome differs from the admitted feature mode.");

            if (isNeon && result.NeonNitroWild?.RequiresChoice == true &&
                (result.Payout.TotalPoints != 0 ||
                 NeonOutcomeReplay.Check(Games.NeonNights.NeonNightsEngine.ToPackage(result, audit), result.Payout).Count != 0))
                throw new JsonException("The pending Nitro outcome could not be replayed.");

            if (result.NeonNitroWild?.RequiresChoice != true &&
                (result.JadeDragonCascade is not null || result.OlympusTumble is not null || admission.IsNeonDiscoPlay ||
                 isCarPlay || result.NeonNitroWild is not null) &&
                SlotRoundAudit.Check(result, result.Payout, isPaid: true).Any(finding => finding.Severity == "error"))
                throw new JsonException("The paid feature outcome could not be replayed.");

            return result with { ChargedWagerCents = admission.ChargedWagerCents };
        }
        catch (Exception error) when (error is JsonException or ArgumentException or InvalidOperationException or OverflowException)
        {
            throw new GameWorkerUnavailableException(gameId, error);
        }
    }
}
