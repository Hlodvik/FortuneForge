using FortuneForge.Server.Slots.Bonuses;
using FortuneForge.Server.Slots.Configuration;
using FortuneForge.Server.Slots.Models;
using FortuneForge.Server.Slots.Reels;
using GameEngine = FortuneForge.Games.NeonNights.NeonNightsEngine;

namespace FortuneForge.Server.Slots.Games.NeonNights;

// Host compatibility boundary only. All game rules execute in the independent
// Neon package; this maps its result into the existing wallet/audit envelope.
internal sealed class NeonNightsEngine(ISlotsDefinitionProvider definitions, IRandomIndexSource random)
{
    private readonly GameEngine engine = new(definitions, random);

    internal void ValidateRequest(long wagerPoints, bool usesAwardedPlay) =>
        engine.ValidateRequest(wagerPoints, usesAwardedPlay);

    internal static NeonSpinOutcome ToPackage(SpinResult result, SlotSpinEngineAudit audit) =>
        new(result.SpinId, result.GameId, result.ReelSetId, result.SymbolSetId, result.PaytableId,
            result.WagerPoints, result.PointValueInCents, result.ReelStops, result.Reels, result.Payout,
            result.ConsecutiveFiveMisses, result.FiveMatchPityTriggered, result.FreeSpinsAwarded,
            result.SpecialPointsAwarded, result.EnergyAwarded, result.SpecialBoostApplied)
        {
            IsFreeSpin = result.IsFreeSpin,
            BananaBonusPoints = result.BananaBonusPoints,
            NeonArcade = result.NeonArcade!,
            NeonDiscoCascade = result.NeonDiscoCascade,
            EngineAudit = new(audit.EngineVersion, audit.GeneratedAtUtc, audit.BaseGame,
                audit.EffectiveGame, audit.ReelSet, audit.SymbolSet, audit.Paytable,
                audit.PityMissesBefore, audit.EnergyBalanceBefore, audit.FreeSpinFeatureMode,
                audit.GenerationAttempts, audit.RawGeneratedReels, audit.ReelsAfterFreeGameFrequency,
                audit.NeonArcade!, audit.NeonDiscoCascade)
        };

    internal SpinResult Spin(long wagerPoints, string playerId, bool specialBoostApplied,
        long currentEnergyBalance, string? featureMode, int? persistedPityMisses,
        bool isFreeSpin, bool isNeonDiscoPlay = false)
    {
        var result = engine.Spin(wagerPoints, playerId, specialBoostApplied,
            currentEnergyBalance, featureMode, persistedPityMisses, isFreeSpin, isNeonDiscoPlay);
        var audit = result.EngineAudit;
        SlotSpecialRoundProfiles.TryGet(result.GameId, out var profile);
        return new SpinResult(result.SpinId, result.GameId, result.ReelSetId, result.SymbolSetId,
            result.PaytableId, result.WagerPoints, result.PointValueInCents, result.ReelStops,
            result.Reels, result.Payout, result.ConsecutiveFiveMisses, result.FiveMatchPityTriggered,
            result.FreeSpinsAwarded, result.SpecialPointsAwarded, result.EnergyAwarded, result.SpecialBoostApplied)
        {
            IsFreeSpin = result.IsFreeSpin,
            BananaBonusPoints = result.BananaBonusPoints,
            NeonArcade = result.NeonArcade,
            NeonDiscoCascade = result.NeonDiscoCascade,
            EngineAudit = new SlotSpinEngineAudit(audit.EngineVersion, audit.GeneratedAtUtc,
                audit.BaseGame, audit.EffectiveGame, audit.ReelSet, audit.SymbolSet, audit.Paytable,
                profile, audit.PityMissesBefore, audit.EnergyBalanceBefore, audit.FreeSpinFeatureMode,
                audit.GenerationAttempts, audit.RawGeneratedReels, audit.ReelsAfterFreeGameFrequency)
            {
                NeonArcade = audit.NeonArcade,
                NeonDiscoCascade = audit.NeonDiscoCascade
            }
        };
    }
}
