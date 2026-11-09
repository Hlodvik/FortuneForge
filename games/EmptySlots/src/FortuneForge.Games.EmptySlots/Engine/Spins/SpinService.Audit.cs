using FortuneForge.Games.EmptySlots.Bonuses;

namespace FortuneForge.Games.EmptySlots.Spins;

internal sealed partial class SpinService
{
    public EmptySlotsEvaluation EvaluateOutcome(
        IReadOnlyList<IReadOnlyList<string>> reels, long wagerPoints, string? featureMode)
    {
        var game = definitions.GetGame(EmptySlotsEngine.GameId)!;
        ValidateWager(game, wagerPoints);
        var effectiveGame = CreateEffectiveSpinGame(game, featureMode);
        var symbols = definitions.GetSymbolSet(game.Symbols.SymbolSetId)!;
        var paytable = definitions.GetPaytable(game.Math.PaytableId)!;
        var known = symbols.Symbols.Select(symbol => symbol.Id).ToHashSet(StringComparer.Ordinal);
        if (reels.SelectMany(reel => reel).Any(symbol => !known.Contains(symbol)))
            throw new ArgumentException("The Empty Slots Game grid contains an unknown symbol.", nameof(reels));
        var evaluations = combinationEvaluator.Evaluate(reels, effectiveGame, symbols);
        var featurePayout = CalculateFeaturePayout(reels, wagerPoints, game.Math.PayoutMultiplier);
        var payout = AddFeaturePayout(
            payoutCalculator.Calculate(evaluations, game, paytable, wagerPoints), featurePayout);
        var freeSpins = CountFreeGameSymbols(reels, effectiveGame) >=
            (effectiveGame.FreeGames?.RequiredSymbols ?? int.MaxValue)
            ? effectiveGame.FreeGames?.AwardedSpins ?? 0 : 0;
        return new EmptySlotsEvaluation(evaluations, payout, freeSpins,
            SpecialPointBonus.CalculateAward(evaluations, effectiveGame),
            featurePayout.PatternPayoutPoints, CountCollectionSymbols(reels),
            HasPayingFullMatch(evaluations, effectiveGame, paytable));
    }
}
