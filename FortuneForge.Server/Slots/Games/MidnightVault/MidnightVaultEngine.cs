using FortuneForge.Server.Slots.Bonuses;
using FortuneForge.Server.Slots.Models;
using FortuneForge.Server.Slots.Reels;
using PackageEngine = FortuneForge.Games.MidnightVault.MidnightVaultEngine;

namespace FortuneForge.Server.Slots.Games.MidnightVault;

// Host compatibility boundary only. Rules, generation, matching, payouts and
// progression all live in the independent Midnight Vault package.
internal sealed class MidnightVaultEngine(IRandomIndexSource random, IReelGenerator reelGenerator)
{
    private readonly PackageEngine engine = new(random, new HostReelSource(reelGenerator));

    internal static SlotSpecialRoundProfile Profile { get; } = ToHostProfile(PackageEngine.Profile);

    internal void ValidateRequest(long wagerPoints, bool usesAwardedPlay) =>
        engine.ValidateRequest(wagerPoints, usesAwardedPlay);

    internal SpinResult Spin(long wagerPoints, string playerId, bool specialBoostApplied,
        long currentEnergyBalance, string? featureMode, int? persistedPityMisses,
        bool isFreeSpin, string? mathModelId)
    {
        return MapOutcome(engine.Spin(wagerPoints, playerId, specialBoostApplied, currentEnergyBalance,
            featureMode, persistedPityMisses, isFreeSpin, mathModelId));
    }

    internal static SpinResult MapOutcome(MidnightVaultSpinOutcome outcome)
    {
        var audit = outcome.EngineAudit;
        return new SpinResult(outcome.SpinId, outcome.GameId, outcome.ReelSetId, outcome.SymbolSetId,
            outcome.PaytableId, outcome.WagerPoints, outcome.PointValueInCents, outcome.ReelStops,
            outcome.Reels, outcome.Payout, outcome.ConsecutiveFiveMisses, outcome.FiveMatchPityTriggered,
            outcome.FreeSpinsAwarded, outcome.SpecialPointsAwarded, outcome.EnergyAwarded,
            outcome.SpecialBoostApplied)
        {
            IsFreeSpin = outcome.IsFreeSpin,
            BananaBonusPoints = outcome.BananaBonusPoints,
            SealsAwarded = outcome.SealsAwarded,
            EngineAudit = new SlotSpinEngineAudit(audit.EngineVersion, audit.GeneratedAtUtc,
                audit.BaseGame, audit.EffectiveGame, audit.ReelSet, audit.SymbolSet, audit.Paytable,
                ToHostProfile(audit.FeatureProfile), audit.PityMissesBefore, audit.EnergyBalanceBefore,
                audit.FreeSpinFeatureMode, audit.GenerationAttempts, audit.RawGeneratedReels,
                audit.ReelsAfterFreeGameFrequency)
            {
                IsDerivedFreeSpin = audit.IsDerivedFreeSpin
            }
        };
    }

    internal static MidnightVaultSpinOutcome ToPackage(SpinResult result, SlotSpinEngineAudit audit) =>
        new(result.SpinId, result.GameId, result.ReelSetId, result.SymbolSetId, result.PaytableId,
            result.WagerPoints, result.PointValueInCents, result.ReelStops, result.Reels, result.Payout,
            result.ConsecutiveFiveMisses, result.FiveMatchPityTriggered, result.FreeSpinsAwarded,
            result.SpecialPointsAwarded, result.EnergyAwarded, result.SpecialBoostApplied,
            result.IsFreeSpin, result.BananaBonusPoints, result.SealsAwarded,
            new MidnightVaultEngineAudit(audit.EngineVersion, audit.GeneratedAtUtc, audit.BaseGame,
                audit.EffectiveGame, audit.ReelSet, audit.SymbolSet, audit.Paytable,
                ToPackageProfile(audit.FeatureProfile), audit.PityMissesBefore,
                audit.EnergyBalanceBefore, audit.FreeSpinFeatureMode, audit.GenerationAttempts,
                audit.RawGeneratedReels, audit.ReelsAfterFreeGameFrequency, audit.IsDerivedFreeSpin));

    private static SlotSpecialRoundProfile ToHostProfile(MidnightVaultProfile profile) => new(
        profile.GameId, profile.ScatterRequiredSymbols, profile.ScatterAwardedSpins,
        profile.CollectionTarget, profile.CollectionAwardedSpins, profile.UsesCollections,
        profile.UsesEnergy, profile.UsesDirectValueTokens, profile.ScatterFeatureMode,
        profile.CollectionFeatureMode, profile.BaseReelSetId, profile.TargetHitRate,
        PayoutMultiplier: profile.PayoutMultiplier);

    private static MidnightVaultProfile ToPackageProfile(SlotSpecialRoundProfile? profile) => profile is null
        ? new(string.Empty, 0, 0, 0, 0, false, false, false, null, null, string.Empty, null, 0m)
        : new(profile.GameId, profile.ScatterRequiredSymbols, profile.ScatterAwardedSpins,
            profile.CollectionTarget, profile.CollectionAwardedSpins, profile.UsesCollections,
            profile.UsesEnergy, profile.UsesDirectValueTokens, profile.ScatterFeatureMode,
            profile.CollectionFeatureMode, profile.BaseReelSetId ?? string.Empty,
            profile.TargetHitRate, profile.PayoutMultiplier);

    private sealed class HostReelSource(IReelGenerator source) : IMidnightVaultReelSource
    {
        public ReelOutcome Generate(GameDefinition game, ReelSetDefinition reelSet,
            SymbolSetDefinition symbolSet) => source.Generate(game, reelSet, symbolSet);
    }
}
