using System.Collections.Concurrent;
using FortuneForge.Server.Slots.Bonuses;
using FortuneForge.Server.Slots.Configuration;
using FortuneForge.Server.Slots.Evaluation;
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
        string? freeSpinFeatureMode = null)
    {
        var game = definitions.GetGame(gameId)
            ?? throw new KeyNotFoundException($"Game '{gameId}' was not found.");
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
        var effectiveGame = CreateEffectiveSpinGame(game, freeSpinFeatureMode);
        var pityTriggered = game.Math.FiveMatchPityMissLimit is { } pityLimit &&
            pityState.ConsecutiveFiveMisses >= pityLimit;
        var maximumAttempts = pityTriggered ? MaximumPityGenerationAttempts : 1;
        ReelOutcome? outcome = null;
        IReadOnlyList<PaylineEvaluation>? evaluations = null;

        for (var attempt = 0; attempt < maximumAttempts; attempt++)
        {
            outcome = reelGenerator.Generate(effectiveGame, reelSet, symbolSet);
            outcome = ApplyFeatureSymbols(outcome, effectiveGame, currentEnergyBalance, freeSpinFeatureMode);
            evaluations = combinationEvaluator.Evaluate(outcome.VisibleReels, effectiveGame, symbolSet);
            if (!pityTriggered || HasPayingFullMatch(evaluations, effectiveGame, paytable))
            {
                break;
            }
        }

        if (outcome is null || evaluations is null ||
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
        var featurePayout = CalculateFeaturePayout(outcome.VisibleReels, wagerPoints);
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
            MonkeyPawCount = featurePayout.MonkeyPawCount,
            MoneyGrabPoints = featurePayout.MoneyGrabPoints,
            BananaBonusPoints = featurePayout.BananaBonusPoints,
            SealsAwarded = CountSealSymbols(outcome.VisibleReels)
        };
    }
}
