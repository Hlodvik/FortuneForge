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
        int? persistedPityMisses = null) => SpinCore(gameId, wagerPoints, playerId,
            specialBoostApplied, currentEnergyBalance, freeSpinFeatureMode, persistedPityMisses, false, null);

    public SpinResult SpinAdmitted(string gameId, SlotSpinAdmission admission, string playerId)
    {
        if (admission.AdmissionId == Guid.Empty || admission.CooldownRemaining is not null)
            throw new ArgumentException("An accepted slot admission is required.", nameof(admission));
        if (gameId == GoldFoundrySlotDefinitions.GameId)
            GoldFoundrySlotDefinitions.ValidateSpinRequest(admission.IsFreeSpin);
        var derived = gameId == SlotSpecialRoundProfiles.DragonHoardGameId && admission.IsFreeSpin;
        var result = SpinCore(gameId, admission.WagerPoints, playerId, admission.SpecialBoostApplied,
            admission.EnergyBalance, admission.FreeSpinFeatureMode, admission.PityMissesBefore,
            derived, admission.MathModelId);
        return result with
        {
            IsFreeSpin = admission.IsFreeSpin,
            EngineAudit = result.EngineAudit! with { IsDerivedFreeSpin = derived }
        };
    }

    internal SpinResult SpinMidnightDemo(long wagerPoints, string playerId, MidnightVaultBonusRound? round)
    {
        var result = SpinCore(SlotSpecialRoundProfiles.DragonHoardGameId, wagerPoints, playerId,
            false, 0, round?.FeatureMode, null, round is not null,
            round?.MathModelId ?? MidnightVaultSlotDefinitions.PaytableId);
        return result with
        {
            IsFreeSpin = round is not null,
            EngineAudit = result.EngineAudit! with { IsDerivedFreeSpin = round is not null }
        };
    }

    private SpinResult SpinCore(string gameId, long wagerPoints, string playerId,
        bool specialBoostApplied, long currentEnergyBalance, string? freeSpinFeatureMode,
        int? persistedPityMisses, bool derivedFreeSpin, string? mathModelId)
    {
        var game = definitions.GetGame(gameId)
            ?? throw new KeyNotFoundException($"Game '{gameId}' was not found.");
        if (mathModelId is not null)
            game = MidnightVaultSlotDefinitions.CreateGameForPaytable(game, mathModelId);
        if (derivedFreeSpin)
            MidnightVaultBonusPolicy.ValidateDerivedWager(wagerPoints);
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
                freeSpinFeatureMode);
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
        string? freeSpinFeatureMode)
    {
        var foundry = GoldFoundrySlotDefinitions.IsModel(game);
        if (foundry)
            freeSpinFeatureMode = null;
        var effectiveGame = CreateEffectiveSpinGame(game, freeSpinFeatureMode);
        var pityTriggered = string.IsNullOrWhiteSpace(freeSpinFeatureMode) &&
            game.Math.FiveMatchPityMissLimit is { } pityLimit &&
            pityState.ConsecutiveFiveMisses >= pityLimit;
        var maximumAttempts = pityTriggered ? MaximumPityGenerationAttempts : 1;
        var pityMissesBefore = pityState.ConsecutiveFiveMisses;
        SlotSpecialRoundProfiles.TryGet(game.Id, out var featureProfile);
        if (game.Id == SlotSpecialRoundProfiles.DragonHoardGameId && featureProfile is not null)
            featureProfile = featureProfile with
            {
                PayoutMultiplier = game.Math.PayoutMultiplier,
                ScatterAwardedSpins = game.FreeGames!.AwardedSpins,
                CollectionAwardedSpins = game.Math.PaytableId == MidnightVaultSlotDefinitions.LegacyPaytableId
                    ? MidnightVaultSlotDefinitions.LegacyCollectionAwardedSpins
                    : MidnightVaultSlotDefinitions.BonusRoundSpins
            };
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
            if (!foundry)
                outcome = ApplyFeatureSymbols(outcome, effectiveGame, currentEnergyBalance, freeSpinFeatureMode);
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
        var payout = payoutCalculator.Calculate(evaluations, game, paytable, wagerPoints);
        var rainbowHarvest = RainbowRealmSlotDefinitions.IsModel(game);
        var featurePayout = foundry
            ? CalculateFoundryFeaturePayout(outcome.VisibleReels, wagerPoints)
            : rainbowHarvest
            ? CalculateRainbowFeaturePayout(outcome.VisibleReels, wagerPoints, game.Math.PayoutMultiplier)
            : CalculateFeaturePayout(outcome.VisibleReels, wagerPoints, game.Math.PayoutMultiplier);
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
            EngineAudit = new SlotSpinEngineAudit(
                foundry ? GoldFoundryReelCollector.EngineVersion :
                    rainbowHarvest ? RainbowRealmSlotDefinitions.EngineVersion : AuditEngineVersion,
                DateTime.UtcNow, game, effectiveGame, reelSet,
                symbolSet, paytable, featureProfile, pityMissesBefore,
                currentEnergyBalance, freeSpinFeatureMode, generationAttempts,
                rawOutcome.VisibleReels, frequencyOutcome.VisibleReels)
            {
                CrucibleRuleVersion = foundry ? GoldFoundryCrucible.RuleVersion : null
            },
            MonkeyPawCount = featurePayout.MonkeyPawCount,
            MoneyGrabPoints = featurePayout.MoneyGrabPoints,
            BananaBonusPoints = featurePayout.BananaBonusPoints,
            SealsAwarded = foundry ? new Dictionary<string, int>() : CountSealSymbols(outcome.VisibleReels)
        };
    }

}
