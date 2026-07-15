using FortuneForge.Server.Slots.Configuration;
using FortuneForge.Server.Slots.Evaluation;
using FortuneForge.Server.Slots.Models;
using FortuneForge.Server.Slots.Payouts;
using FortuneForge.Server.Slots.Reels;

namespace FortuneForge.Server.Slots.Spins;

public sealed class SpinService(
    ISlotsDefinitionProvider definitions,
    IReelGenerator reelGenerator,
    ICombinationEvaluator combinationEvaluator,
    IPayoutCalculator payoutCalculator)
{
    public SpinResult Spin(string gameId, long wagerPoints)
    {
        var game = definitions.GetGame(gameId)
            ?? throw new KeyNotFoundException($"Game '{gameId}' was not found.");
        ValidateWager(game, wagerPoints);

        var symbolSet = definitions.GetSymbolSet(game.Symbols.SymbolSetId)
            ?? throw new KeyNotFoundException($"Symbol set '{game.Symbols.SymbolSetId}' was not found.");
        var reelSet = definitions.GetReelSet(game.Math.ReelSetId)
            ?? throw new KeyNotFoundException($"Reel set '{game.Math.ReelSetId}' was not found.");
        var paytable = definitions.GetPaytable(game.Math.PaytableId)
            ?? throw new KeyNotFoundException($"Paytable '{game.Math.PaytableId}' was not found.");

        if (!string.Equals(paytable.SymbolSetId, symbolSet.Id, StringComparison.Ordinal))
        {
            throw new InvalidOperationException(
                $"Paytable '{paytable.Id}' is not compatible with symbol set '{symbolSet.Id}'.");
        }

        var outcome = reelGenerator.Generate(game, reelSet, symbolSet);
        var evaluations = combinationEvaluator.Evaluate(outcome.VisibleReels, game, symbolSet);
        var payout = payoutCalculator.Calculate(evaluations, paytable, wagerPoints);

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
            payout);
    }

    private static void ValidateWager(GameDefinition game, long wagerPoints)
    {
        if (wagerPoints < game.Wagering.MinimumWagerPoints)
        {
            throw new ArgumentOutOfRangeException(
                nameof(wagerPoints),
                $"Game '{game.Id}' requires at least {game.Wagering.MinimumWagerPoints} points per spin.");
        }

        if (game.Wagering.MaximumWagerPoints is { } maximum && wagerPoints > maximum)
        {
            throw new ArgumentOutOfRangeException(
                nameof(wagerPoints),
                $"Game '{game.Id}' allows at most {maximum} points per spin.");
        }
    }
}
