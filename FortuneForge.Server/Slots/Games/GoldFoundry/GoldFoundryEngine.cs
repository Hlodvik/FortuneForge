using FortuneForge.Server.Slots.Bonuses;
using FortuneForge.Games.GoldFoundry;
using FortuneForge.Server.Slots.Configuration;
using FortuneForge.Server.Slots.Models;
using FortuneForge.Server.Slots.Reels;
using PackageEngine = FortuneForge.Games.GoldFoundry.GoldFoundryEngine;

namespace FortuneForge.Server.Slots.Games.GoldFoundry;

// Host compatibility boundary only. Game rules and outcome replay live in the
// independent Gold Foundry package; this adapter maps to the host envelope.
internal sealed class GoldFoundryEngine(ISlotsDefinitionProvider definitions, IRandomIndexSource random)
{
    private readonly PackageEngine engine = new(definitions, random);

    internal static SlotSpecialRoundProfile Profile { get; } = ToHostProfile(PackageEngine.Profile);

    internal void ValidateRequest(long wagerPoints, bool usesAwardedPlay) =>
        engine.ValidateRequest(wagerPoints, usesAwardedPlay);

    internal SpinResult Spin(long wagerPoints, string playerId, bool specialBoostApplied,
        long currentEnergyBalance, string? featureMode, int? persistedPityMisses, bool isFreeSpin)
    {
        var outcome = engine.Spin(wagerPoints, playerId, specialBoostApplied, currentEnergyBalance,
            featureMode, persistedPityMisses, isFreeSpin);
        var audit = outcome.EngineAudit;
        return new(outcome.SpinId, outcome.GameId, outcome.ReelSetId, outcome.SymbolSetId,
            outcome.PaytableId, outcome.WagerPoints, outcome.PointValueInCents, outcome.ReelStops,
            outcome.Reels, outcome.Payout, outcome.ConsecutiveFiveMisses,
            outcome.FiveMatchPityTriggered, outcome.FreeSpinsAwarded, outcome.SpecialPointsAwarded,
            outcome.EnergyAwarded, outcome.SpecialBoostApplied)
        {
            IsFreeSpin = outcome.IsFreeSpin,
            MonkeyPawCount = outcome.CollectorCount,
            MoneyGrabPoints = outcome.CollectorPayoutPoints,
            BananaBonusPoints = outcome.HammerBonusPoints,
            SealsAwarded = outcome.SealsAwarded,
            EngineAudit = new SlotSpinEngineAudit(audit.EngineVersion, audit.GeneratedAtUtc,
                audit.BaseGame, audit.EffectiveGame, audit.ReelSet, audit.SymbolSet, audit.Paytable,
                ToHostProfile(audit.FeatureProfile), audit.PityMissesBefore, audit.EnergyBalanceBefore,
                audit.FreeSpinFeatureMode, audit.GenerationAttempts, audit.RawGeneratedReels,
                audit.ReelsAfterFreeGameFrequency)
            {
                IsDerivedFreeSpin = audit.IsDerivedFreeSpin,
                CrucibleRuleVersion = audit.CrucibleRuleVersion
            }
        };
    }

    internal static GoldFoundrySpinOutcome ToPackage(SpinResult result, SlotSpinEngineAudit audit) =>
        new(result.SpinId, result.GameId, result.ReelSetId, result.SymbolSetId, result.PaytableId,
            result.WagerPoints, result.PointValueInCents, result.ReelStops, result.Reels, result.Payout,
            result.ConsecutiveFiveMisses, result.FiveMatchPityTriggered, result.FreeSpinsAwarded,
            result.SpecialPointsAwarded, result.EnergyAwarded, result.SpecialBoostApplied,
            result.IsFreeSpin, result.MonkeyPawCount, result.MoneyGrabPoints, result.BananaBonusPoints,
            result.SealsAwarded, new GoldFoundryEngineAudit(audit.EngineVersion, audit.GeneratedAtUtc,
                audit.BaseGame, audit.EffectiveGame, audit.ReelSet, audit.SymbolSet, audit.Paytable,
                ToPackageProfile(audit.FeatureProfile), audit.PityMissesBefore, audit.EnergyBalanceBefore,
                audit.FreeSpinFeatureMode, audit.GenerationAttempts, audit.RawGeneratedReels,
                audit.ReelsAfterFreeGameFrequency, audit.IsDerivedFreeSpin,
                audit.CrucibleRuleVersion ?? string.Empty));

    private static SlotSpecialRoundProfile ToHostProfile(GoldFoundryProfile profile) => new(
        profile.GameId, profile.ScatterRequiredSymbols, profile.ScatterAwardedSpins,
        profile.CollectionTarget, profile.CollectionAwardedSpins, profile.UsesCollections,
        profile.UsesEnergy, profile.UsesDirectValueTokens, profile.ScatterFeatureMode,
        profile.CollectionFeatureMode, profile.BaseReelSetId, profile.TargetHitRate,
        profile.PaylinePatternIds, profile.PayoutMultiplier);

    private static GoldFoundryProfile ToPackageProfile(SlotSpecialRoundProfile? profile) => profile is null
        ? throw new InvalidOperationException("Current Gold Foundry evidence requires a captured feature profile.")
        : new(profile.GameId, profile.ScatterRequiredSymbols, profile.ScatterAwardedSpins,
            profile.CollectionTarget, profile.CollectionAwardedSpins, profile.UsesCollections,
            profile.UsesEnergy, profile.UsesDirectValueTokens, profile.ScatterFeatureMode,
            profile.CollectionFeatureMode, profile.BaseReelSetId, profile.TargetHitRate,
            profile.PaylinePatternIds ?? [], profile.PayoutMultiplier);
}
