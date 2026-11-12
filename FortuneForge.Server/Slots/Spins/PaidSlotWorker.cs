using System.Text.Json;
using FortuneForge.Games.GodsOfOlympus;
using FortuneForge.Games.JadeDragon;
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
using PackageJadeResult = FortuneForge.Games.JadeDragon.Models.SpinResult;

namespace FortuneForge.Server.Slots.Spins;

internal static class PaidSlotWorker
{
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);

    internal static async Task<SpinResult> ResolveAsync(GameWorkerClient worker, string gameId, string playerId,
        SlotSpinAdmission admission, CancellationToken cancellationToken)
    {
        var request = new PaidSlotOutcomeRequest(admission.AdmissionId, gameId, playerId, admission.WagerPoints,
            admission.IsFreeSpin, admission.SpecialBoostApplied, admission.EnergyBalance,
            admission.FreeSpinFeatureMode, admission.PityMissesBefore, admission.MathModelId)
        {
            FreeSpinsRemaining = admission.FreeSpinsRemaining,
            IsNeonDiscoPlay = admission.IsNeonDiscoPlay,
            NeonDiscoMultiplier = admission.IsNeonDiscoPlay ? admission.NeonDiscoMultiplier : null
        };
        var response = await worker.InvokeAsync<PaidSlotOutcomeRequest, PaidSlotOutcomeResponse>(gameId, playerId,
            PaidSlotOutcomeProtocol.Route, request, cancellationToken);
        if (response.AdmissionId != admission.AdmissionId || response.GameId != gameId)
            throw new GameWorkerUnavailableException(gameId);

        try
        {
            T Read<T>() where T : class => response.Outcome.Deserialize<T>(Json)
                ?? throw new JsonException("The game worker outcome is missing.");

            var result = response.OutcomeKind switch
            {
                JadeDragonCascadeRules.Version when gameId == JadeDragonEngine.GameId && admission.MathModelId == JadeDragonCascadeRules.Version =>
                    Games.JadeDragon.JadeDragonCascadeAdapter.MapOutcome(Read<JadeDragonCascadeSpin>(), admission),
                JadeDragonEngine.Version when gameId == JadeDragonEngine.GameId && admission.MathModelId is null && admission.IsFreeSpin =>
                    HostJadeEngine.MapOutcome(Read<PackageJadeResult>(), admission.IsFreeSpin),
                "neon-nights" when gameId == SlotSpecialRoundProfiles.NeonNightsGameId && admission.MathModelId is null =>
                    Games.NeonNights.NeonNightsEngine.MapOutcome(Read<NeonSpinOutcome>()),
                "midnight-vault" when gameId == SlotSpecialRoundProfiles.DragonHoardGameId =>
                    Games.MidnightVault.MidnightVaultEngine.MapOutcome(Read<FortuneForge.Games.MidnightVault.MidnightVaultSpinOutcome>()),
                "gold-foundry" when gameId == FortuneForge.Games.GoldFoundry.GoldFoundryEngine.GameId && admission.MathModelId is null =>
                    Games.GoldFoundry.GoldFoundryEngine.MapOutcome(Read<FortuneForge.Games.GoldFoundry.GoldFoundrySpinOutcome>()),
                PaidSlotOutcomeProtocol.OlympusTumbleKind when gameId == SlotSpecialRoundProfiles.GodsOfOlympusGameId &&
                    OlympusTumbleDemo.SupportsPaidEngineVersion(admission.MathModelId) =>
                    StandaloneSlotPackageRegistry.MapTumble(Read<GodsOfOlympusTumbleSpinOutcome>(), showcase: false,
                        admission.PityMissesBefore, admission.EnergyBalance),
                PaidSlotOutcomeProtocol.ClassicSlotKind when StandaloneSlotPackageRegistry.Owns(gameId) && admission.MathModelId is null =>
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

            if ((result.JadeDragonCascade is not null || result.OlympusTumble is not null || admission.IsNeonDiscoPlay) &&
                SlotRoundAudit.Check(result, result.Payout, isPaid: true).Any(finding => finding.Severity == "error"))
                throw new JsonException("The paid cascade outcome could not be replayed.");

            return result with { ChargedWagerCents = admission.ChargedWagerCents };
        }
        catch (Exception error) when (error is JsonException or ArgumentException or InvalidOperationException or OverflowException)
        {
            throw new GameWorkerUnavailableException(gameId, error);
        }
    }
}
