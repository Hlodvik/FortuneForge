using FortuneForge.Games.EmptySlots.Bonuses;
using FortuneForge.Games.EmptySlots.Models;

namespace FortuneForge.Games.EmptySlots.Spins;

internal sealed partial class SpinService
{
    public SpinResult Spin(string gameId, long wagerPoints, string playerId,
        bool specialBoostApplied, long currentEnergyBalance = 0,
        string? freeSpinFeatureMode = null, int? persistedPityMisses = null, bool isFreeSpin = false)
    {
        var game = definitions.GetGame(gameId)
            ?? throw new KeyNotFoundException($"Game '{gameId}' was not found.");
        ValidateWager(game, wagerPoints);
        ArgumentException.ThrowIfNullOrWhiteSpace(playerId);
        var pityMisses = persistedPityMisses ?? 0;
        ArgumentOutOfRangeException.ThrowIfNegative(pityMisses);
        var symbolSet = definitions.GetSymbolSet(game.Symbols.SymbolSetId)
            ?? throw new InvalidOperationException("Empty Slots Game's symbol set is missing.");
        var reelSet = definitions.GetReelSet(game.Math.ReelSetId)
            ?? throw new InvalidOperationException("Empty Slots Game's reel set is missing.");
        var paytable = definitions.GetPaytable(game.Math.PaytableId)
            ?? throw new InvalidOperationException("Empty Slots Game's paytable is missing.");
        var effectiveReelSet = specialBoostApplied
            ? SpecialPointBonus.CreateBoostedReelSet(game, reelSet) : reelSet;
        var effectiveGame = CreateEffectiveSpinGame(game, freeSpinFeatureMode);
        var pityTriggered = string.IsNullOrWhiteSpace(freeSpinFeatureMode) &&
            game.Math.FiveMatchPityMissLimit is { } limit && pityMisses >= limit;
        var maximumAttempts = pityTriggered ? MaximumPityGenerationAttempts : 1;
        ReelOutcome? rawOutcome = null;
        ReelOutcome? frequencyOutcome = null;
        ReelOutcome? outcome = null;
        IReadOnlyList<PaylineEvaluation>? evaluations = null;
        var attempts = 0;
        for (var attempt = 0; attempt < maximumAttempts; attempt++)
        {
            attempts++;
            rawOutcome = reelGenerator.Generate(effectiveGame, effectiveReelSet, symbolSet);
            frequencyOutcome = FreeGameFrequency.Apply(rawOutcome, effectiveGame, random);
            outcome = ApplyFeatureSymbols(frequencyOutcome, freeSpinFeatureMode, currentEnergyBalance);
            evaluations = combinationEvaluator.Evaluate(outcome.VisibleReels, effectiveGame, symbolSet);
            if (!pityTriggered || HasPayingFullMatch(evaluations, effectiveGame, paytable)) break;
        }
        if (outcome is null || rawOutcome is null || frequencyOutcome is null || evaluations is null ||
            pityTriggered && !HasPayingFullMatch(evaluations, effectiveGame, paytable))
            throw new InvalidOperationException("Empty Slots Game could not generate a five-match pity outcome.");

        var nextMisses = HasPayingFullMatch(evaluations, effectiveGame, paytable)
            ? 0 : checked(pityMisses + 1);
        var payout = payoutCalculator.Calculate(evaluations, game, paytable, wagerPoints);
        var featurePayout = CalculateFeaturePayout(outcome.VisibleReels, wagerPoints, game.Math.PayoutMultiplier);
        payout = AddFeaturePayout(payout, featurePayout);
        var freeSpinsAwarded = CountFreeGameSymbols(outcome.VisibleReels, effectiveGame) >=
            (effectiveGame.FreeGames?.RequiredSymbols ?? int.MaxValue)
            ? effectiveGame.FreeGames?.AwardedSpins ?? 0 : 0;
        var specialPointsAwarded = SpecialPointBonus.CalculateAward(evaluations, effectiveGame);
        EmptySlotsRules.TryGet(game.Id, out var profile);
        return new SpinResult(Guid.NewGuid(), game.Id, effectiveReelSet.Id, symbolSet.Id, paytable.Id,
            wagerPoints, game.Wagering.PointValueInCents, outcome.StopIndexes, outcome.VisibleReels,
            payout, nextMisses, pityTriggered, freeSpinsAwarded, specialPointsAwarded, 0, specialBoostApplied)
        {
            IsFreeSpin = isFreeSpin,
            EngineAudit = new SlotSpinEngineAudit(AuditEngineVersion, DateTime.UtcNow,
                game, effectiveGame, effectiveReelSet, symbolSet, paytable, profile, pityMisses,
                currentEnergyBalance, freeSpinFeatureMode, attempts, rawOutcome.VisibleReels, frequencyOutcome.VisibleReels),
            CollectorCount = featurePayout.CollectorCount,
            CollectorPayoutPoints = featurePayout.CollectorPayoutPoints,
            PatternPayoutPoints = featurePayout.PatternPayoutPoints,
            CollectionSymbolsAwarded = CountCollectionSymbols(outcome.VisibleReels)
        };
    }
}
