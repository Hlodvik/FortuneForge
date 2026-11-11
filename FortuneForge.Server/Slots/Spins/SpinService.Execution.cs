using System.Collections.Concurrent;
using FortuneForge.Server.Accounts.Models;
using FortuneForge.Server.Slots.Bonuses;
using FortuneForge.Server.Slots.Configuration;
using FortuneForge.Server.Slots.Evaluation;
using FortuneForge.Server.Slots.Games.GoldFoundry;
using FortuneForge.Server.Slots.Models;
using FortuneForge.Server.Slots.Payouts;
using FortuneForge.Server.Slots.Reels;

namespace FortuneForge.Server.Slots.Spins;

public sealed partial class SpinService
{
    public SpinResult Spin(
        string gameId,
        long wagerPoints,
        string playerId,
        bool specialBoostApplied,
        long currentEnergyBalance = 0,
        string? freeSpinFeatureMode = null,
        int? persistedPityMisses = null,
        bool isFreeSpin = false,
        bool isNeonDiscoPlay = false,
        int neonDiscoMultiplier = NeonDiscoCascade.InitialMultiplier)
    {
        if (gameId == SlotSpecialRoundProfiles.NeonNightsGameId)
            return neonNightsEngine.Spin(wagerPoints, playerId, specialBoostApplied,
                currentEnergyBalance, freeSpinFeatureMode, persistedPityMisses, isFreeSpin,
                isNeonDiscoPlay, neonDiscoMultiplier);
        if (isNeonDiscoPlay || neonDiscoMultiplier != NeonDiscoCascade.InitialMultiplier)
            throw new ArgumentException("Only Neon Nights supports Disco cascades.", nameof(isNeonDiscoPlay));
        if (gameId == SlotSpecialRoundProfiles.JadeDragonGameId)
            return jadeDragonEngine.Spin(wagerPoints, playerId, specialBoostApplied,
                currentEnergyBalance, freeSpinFeatureMode, persistedPityMisses, isFreeSpin);
        if (gameId == SlotSpecialRoundProfiles.DragonHoardGameId)
        {
            var model = definitions.GetGame(gameId)?.Math.PaytableId
                ?? MidnightVaultSlotDefinitions.PaytableId;
            return midnightVaultEngine.Spin(wagerPoints, playerId, specialBoostApplied,
                currentEnergyBalance, freeSpinFeatureMode, persistedPityMisses, isFreeSpin,
                model);
        }
        if (gameId == GoldFoundrySlotDefinitions.GameId)
            return goldFoundryEngine.Spin(wagerPoints, playerId, specialBoostApplied,
                currentEnergyBalance, freeSpinFeatureMode, persistedPityMisses, isFreeSpin);
        if (useStandaloneSlotPackages && standaloneSlotPackages.TrySpin(gameId, wagerPoints, playerId,
                specialBoostApplied, currentEnergyBalance, freeSpinFeatureMode,
                persistedPityMisses, isFreeSpin, out var packaged))
            return packaged;
        return SpinCore(gameId, wagerPoints, playerId,
            specialBoostApplied, currentEnergyBalance, freeSpinFeatureMode, persistedPityMisses, false,
            isFreeSpin: isFreeSpin);
    }

    public SpinResult SpinAdmitted(string gameId, SlotSpinAdmission admission, string playerId)
    {
        if (admission.AdmissionId == Guid.Empty || admission.CooldownRemaining is not null)
            throw new ArgumentException("An accepted slot admission is required.", nameof(admission));
        if (gameId == SlotSpecialRoundProfiles.JadeDragonGameId)
        {
            if (admission.MathModelId is not null || admission.IsNeonDiscoPlay)
                throw new ArgumentException("Jade Dragon does not accept a foreign math model.", nameof(admission));
            return jadeDragonEngine.Spin(admission.WagerPoints, playerId,
                admission.SpecialBoostApplied, admission.EnergyBalance,
                admission.FreeSpinFeatureMode, admission.PityMissesBefore, admission.IsFreeSpin);
        }
        if (admission.MathModelId is { } olympusVersion && OlympusTumbleDemo.SupportsEngineVersion(olympusVersion))
        {
            ArgumentException.ThrowIfNullOrWhiteSpace(playerId);
            if (!OlympusTumbleDemo.SupportsPaidEngineVersion(olympusVersion) ||
                gameId != SlotSpecialRoundProfiles.GodsOfOlympusGameId || admission.IsFreeSpin ||
                admission.SpecialBoostApplied || admission.FreeSpinsRemaining != 0 ||
                admission.FreeSpinFeatureMode is not null)
                throw new ArgumentException("A base Olympus admission is required for a tumble spin.", nameof(admission));
            return SpinOlympusTumble(olympusVersion, admission.WagerPoints, showcase: false,
                admission.PityMissesBefore, admission.EnergyBalance);
        }
        if (gameId == SlotSpecialRoundProfiles.NeonNightsGameId)
        {
            if (admission.MathModelId is not null)
                throw new ArgumentException("Neon Nights does not accept a foreign math model.", nameof(admission));
            var neon = neonNightsEngine.Spin(admission.WagerPoints, playerId,
                admission.SpecialBoostApplied, admission.EnergyBalance,
                admission.FreeSpinFeatureMode, admission.PityMissesBefore, admission.IsFreeSpin,
                admission.IsNeonDiscoPlay, admission.IsNeonDiscoPlay
                    ? admission.NeonDiscoMultiplier : NeonDiscoCascade.InitialMultiplier);
            return neon with { ChargedWagerCents = admission.ChargedWagerCents };
        }
        if (gameId == SlotSpecialRoundProfiles.DragonHoardGameId)
        {
            var midnight = midnightVaultEngine.Spin(admission.WagerPoints, playerId,
                admission.SpecialBoostApplied, admission.EnergyBalance, admission.FreeSpinFeatureMode,
                admission.PityMissesBefore, admission.IsFreeSpin, admission.MathModelId);
            return midnight with { ChargedWagerCents = admission.ChargedWagerCents };
        }
        if (gameId == GoldFoundrySlotDefinitions.GameId)
        {
            if (admission.MathModelId is not null || admission.IsNeonDiscoPlay)
                throw new ArgumentException("Gold Foundry does not accept a foreign math model.", nameof(admission));
            var foundry = goldFoundryEngine.Spin(admission.WagerPoints, playerId,
                admission.SpecialBoostApplied, admission.EnergyBalance, admission.FreeSpinFeatureMode,
                admission.PityMissesBefore, admission.IsFreeSpin);
            return foundry with { ChargedWagerCents = admission.ChargedWagerCents };
        }
        if (useStandaloneSlotPackages && standaloneSlotPackages.TrySpin(gameId,
                admission.WagerPoints, playerId, admission.SpecialBoostApplied,
                admission.EnergyBalance, admission.FreeSpinFeatureMode,
                admission.PityMissesBefore, admission.IsFreeSpin, out var packaged))
            return packaged with { ChargedWagerCents = admission.ChargedWagerCents };
        var result = SpinCore(gameId, admission.WagerPoints, playerId, admission.SpecialBoostApplied,
            admission.EnergyBalance, admission.FreeSpinFeatureMode, admission.PityMissesBefore,
            false, isFreeSpin: admission.IsFreeSpin);
        return result with
        {
            IsFreeSpin = admission.IsFreeSpin,
            EngineAudit = result.EngineAudit! with { IsDerivedFreeSpin = false }
        };
    }

    internal SpinResult SpinMidnightDemo(long wagerPoints, string playerId, MidnightVaultBonusRound? round)
    {
        return midnightVaultEngine.Spin(wagerPoints, playerId, false, 0, round?.FeatureMode,
            null, round is not null, round?.MathModelId ?? MidnightVaultSlotDefinitions.PaytableId);
    }

    private SpinResult SpinCore(string gameId, long wagerPoints, string playerId,
        bool specialBoostApplied, long currentEnergyBalance, string? freeSpinFeatureMode,
        int? persistedPityMisses, bool derivedFreeSpin,
        WukongPawPrototypeEvidence? pawPrototype = null, bool isFreeSpin = false)
    {
        var game = definitions.GetGame(gameId)
            ?? throw new KeyNotFoundException($"Game '{gameId}' was not found.");
        if (derivedFreeSpin && ArcaneLightningCollector.IsModel(game))
            ArcaneLightningCollector.ValidateCarryWager(game, wagerPoints);
        else
            ValidateWager(game, wagerPoints);
        if (string.IsNullOrWhiteSpace(playerId))
        {
            throw new ArgumentException("An authenticated player id is required.", nameof(playerId));
        }

        var symbolSet = definitions.GetSymbolSet(game.Symbols.SymbolSetId)
            ?? throw new KeyNotFoundException($"Symbol set '{game.Symbols.SymbolSetId}' was not found.");
        var reelSet = definitions.GetReelSet(game.Math.ReelSetId)
            ?? throw new KeyNotFoundException($"Reel set '{game.Math.ReelSetId}' was not found.");
        var paytable = definitions.GetPaytable(game.Math.PaytableId)
            ?? throw new KeyNotFoundException($"Paytable '{game.Math.PaytableId}' was not found.");
        if (pawPrototype is not null)
        {
            ValidateWukongPawPrototype(game, wagerPoints, pawPrototype);
            if (pawPrototype.InjectEarnSymbol)
                symbolSet = new SymbolSetDefinition
                {
                    Id = symbolSet.Id,
                    Symbols = [.. symbolSet.Symbols, new SymbolDefinition { Id = WukongPawPrototypeEvidence.EarnSymbolId }]
                };
        }
        var effectiveReelSet = specialBoostApplied
            ? SpecialPointBonus.CreateBoostedReelSet(game, reelSet)
            : reelSet;

        if (!string.Equals(paytable.SymbolSetId, symbolSet.Id, StringComparison.Ordinal))
        {
            throw new InvalidOperationException(
                $"Paytable '{paytable.Id}' is not compatible with symbol set '{symbolSet.Id}'.");
        }

        var pityState = _pityStates.GetOrAdd(
            $"{game.Id}\u001f{playerId}",
            static _ => new PityState());

        lock (pityState.SyncRoot)
        {
            if (persistedPityMisses is { } persisted)
            {
                if (persisted < 0)
                    throw new ArgumentOutOfRangeException(nameof(persistedPityMisses));
                pityState.ConsecutiveFiveMisses = persisted;
            }
            return SpinWithPity(
                game,
                effectiveReelSet,
                symbolSet,
                paytable,
                wagerPoints,
                pityState,
                specialBoostApplied,
                currentEnergyBalance,
                freeSpinFeatureMode,
                isFreeSpin,
                pawPrototype);
        }
    }

    private SpinResult SpinWithPity(
        GameDefinition game,
        ReelSetDefinition reelSet,
        SymbolSetDefinition symbolSet,
        PaytableDefinition paytable,
        long wagerPoints,
        PityState pityState,
        bool specialBoostApplied,
        long currentEnergyBalance,
        string? freeSpinFeatureMode,
        bool isFreeSpin,
        WukongPawPrototypeEvidence? pawPrototype = null)
    {
        var rainbowHarvest = RainbowRealmSlotDefinitions.IsModel(game);
        var arcaneLightning = ArcaneLightningCollector.IsModel(game);
        var effectiveGame = CreateEffectiveSpinGame(game, freeSpinFeatureMode);
        var pityTriggered = string.IsNullOrWhiteSpace(freeSpinFeatureMode) &&
            game.Math.FiveMatchPityMissLimit is { } pityLimit &&
            pityState.ConsecutiveFiveMisses >= pityLimit;
        var maximumAttempts = pityTriggered ? MaximumPityGenerationAttempts : 1;
        var pityMissesBefore = pityState.ConsecutiveFiveMisses;
        SlotSpecialRoundProfiles.TryGet(game.Id, out var featureProfile);
        ReelOutcome? outcome = null;
        ReelOutcome? rawOutcome = null;
        ReelOutcome? frequencyOutcome = null;
        IReadOnlyList<PaylineEvaluation>? evaluations = null;
        var generationAttempts = 0;

        for (var attempt = 0; attempt < maximumAttempts; attempt++)
        {
            generationAttempts++;
            rawOutcome = reelGenerator.Generate(effectiveGame, reelSet, symbolSet);
            frequencyOutcome = FreeGameFrequency.Apply(rawOutcome, effectiveGame, random);
            outcome = frequencyOutcome;
            outcome = ApplyFeatureSymbols(outcome, effectiveGame, arcaneLightning ? 0 : currentEnergyBalance, freeSpinFeatureMode);
            if (pawPrototype is not null)
                outcome = ApplyWukongPawPrototype(outcome, pawPrototype);
            evaluations = combinationEvaluator.Evaluate(outcome.VisibleReels, effectiveGame, symbolSet);
            if (!pityTriggered || HasPayingFullMatch(evaluations, effectiveGame, paytable))
            {
                break;
            }
        }

        if (outcome is null || rawOutcome is null || frequencyOutcome is null || evaluations is null ||
            (pityTriggered && !HasPayingFullMatch(evaluations, effectiveGame, paytable)))
        {
            throw new InvalidOperationException(
                $"Game '{game.Id}' could not generate a five-match pity outcome.");
        }

        var hasFiveMatch = HasPayingFullMatch(evaluations, effectiveGame, paytable);
        pityState.ConsecutiveFiveMisses = hasFiveMatch
            ? 0
            : checked(pityState.ConsecutiveFiveMisses + 1);
        var payout = payoutCalculator.Calculate(evaluations, effectiveGame, paytable, wagerPoints);
        var featurePayout = rainbowHarvest
            ? CalculateRainbowFeaturePayout(outcome.VisibleReels, wagerPoints, game.Math.PayoutMultiplier)
            : CalculateFeaturePayout(
            outcome.VisibleReels,
            wagerPoints,
            game.Math.PayoutMultiplier,
            collectWithEveryPaw: pawPrototype is not null);
        var lightningCollection = arcaneLightning
            ? ArcaneLightningCollector.Calculate(outcome.VisibleReels, wagerPoints) : null;
        if (lightningCollection is not null)
            featurePayout = featurePayout with
            {
                Paylines = [.. featurePayout.Paylines, ArcaneLightningCollector.CreatePayout(lightningCollection, wagerPoints)]
            };
        payout = AddFeaturePayout(payout, featurePayout);
        var freeSpinsAwarded = CountFreeGameSymbols(outcome.VisibleReels, effectiveGame) >=
            (effectiveGame.FreeGames?.RequiredSymbols ?? int.MaxValue)
                ? effectiveGame.FreeGames?.AwardedSpins ?? 0
                : 0;
        var specialPointsAwarded = SpecialPointBonus.CalculateAward(evaluations, effectiveGame);
        var energyAwarded = CountEnergySymbols(outcome.VisibleReels, effectiveGame) *
            (effectiveGame.Energy?.PointsPerVisibleSymbol ?? 0);

        return new SpinResult(
            Guid.NewGuid(),
            game.Id,
            reelSet.Id,
            symbolSet.Id,
            paytable.Id,
            wagerPoints,
            game.Wagering.PointValueInCents,
            outcome.StopIndexes,
            outcome.VisibleReels,
            payout,
            pityState.ConsecutiveFiveMisses,
            pityTriggered,
            freeSpinsAwarded,
            specialPointsAwarded,
            energyAwarded,
            specialBoostApplied)
        {
            IsFreeSpin = isFreeSpin,
            EngineAudit = new SlotSpinEngineAudit(
                rainbowHarvest ? RainbowRealmSlotDefinitions.EngineVersion :
                    arcaneLightning ? ArcaneLightningCollector.EngineVersion :
                    pawPrototype is null ? AuditEngineVersion : WukongPawPrototypeEvidence.EngineVersion,
                DateTime.UtcNow, game, effectiveGame, reelSet,
                symbolSet, paytable, featureProfile, pityMissesBefore,
                currentEnergyBalance, freeSpinFeatureMode, generationAttempts,
                rawOutcome.VisibleReels, frequencyOutcome.VisibleReels)
            {
                WukongPawPrototype = pawPrototype
            },
            MonkeyPawCount = featurePayout.MonkeyPawCount,
            MoneyGrabPoints = featurePayout.MoneyGrabPoints,
            BananaBonusPoints = featurePayout.BananaBonusPoints,
            LightningCollection = lightningCollection,
            SealsAwarded = CountSealSymbols(outcome.VisibleReels)
        };
    }

}
