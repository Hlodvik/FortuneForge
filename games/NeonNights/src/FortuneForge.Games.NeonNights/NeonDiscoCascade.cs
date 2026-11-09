using FortuneForge.Games.Slots;

namespace FortuneForge.Games.NeonNights;

// A Disco play is one paid spin. All cascades that follow its initial landing
// belong to that spin and never draw another wager or award another feature.
public static class NeonDiscoCascade
{
    public const int MaximumWinningSteps = 16;

    public static NeonDiscoCascadeOutcome Resolve(
        IReadOnlyList<IReadOnlyList<string>> initialReels,
        GameDefinition game,
        ReelSetDefinition reelSet,
        SymbolSetDefinition symbols,
        PaytableDefinition paytable,
        long wagerPoints,
        NeonCombinationEvaluator evaluator,
        NeonPayoutCalculator payoutCalculator,
        Func<int, int> draw,
        out long sevenPoints)
    {
        if (initialReels.Count != game.Layout.ReelCount ||
            initialReels.Any(reel => reel.Count != game.Layout.VisibleRows) ||
            reelSet.Reels.Count != game.Layout.ReelCount)
            throw new ArgumentException("The Disco board does not match its captured rules.", nameof(initialReels));

        var pools = reelSet.Reels.Select(reel => reel.Where(symbol =>
            symbol is not ("6" or "POWER" or "SEAL_RAND")).ToArray()).ToArray();
        if (pools.Any(pool => pool.Length == 0))
            throw new ArgumentException("A Disco refill reel has no ordinary symbols.", nameof(reelSet));

        var board = initialReels.Select(reel => reel.ToArray()).ToArray();
        var steps = new List<NeonDiscoCascadeStep>();
        var total = 0L;
        sevenPoints = 0;

        for (var stepIndex = 0; stepIndex < MaximumWinningSteps; stepIndex++)
        {
            var evaluations = evaluator.Evaluate(board, game, symbols);
            var linePayout = payoutCalculator.Calculate(evaluations, game, paytable, wagerPoints);
            var boosted = NeonArcadeBonus.Apply(linePayout, board).Payout;
            var sevens = NeonNightsEngine.CalculateTripleSevenPayouts(board, wagerPoints);
            var stepPayout = sevens.Count == 0 ? boosted : boosted with
            {
                Paylines = [.. boosted.Paylines, .. sevens],
                TotalPoints = checked(boosted.TotalPoints + sevens.Sum(line => line.AmountPoints))
            };
            if (stepPayout.TotalPoints == 0) break;

            var cleared = stepPayout.Paylines.SelectMany(line => line.Matches)
                .SelectMany(match => match.Match.Positions)
                .Distinct().OrderBy(position => position.Reel).ThenBy(position => position.Row)
                .ToArray();
            if (cleared.Length == 0)
                throw new InvalidOperationException("A paying Disco step must clear symbols.");

            var clearSet = cleared.ToHashSet();
            var next = new string[board.Length][];
            var dropRows = new int[board.Length][];
            for (var reel = 0; reel < board.Length; reel++)
            {
                var survivors = Enumerable.Range(0, board[reel].Length)
                    .Where(row => !clearSet.Contains(new GridPosition(reel, row)))
                    .Select(row => (Symbol: board[reel][row], Row: row)).ToArray();
                var gapCount = board[reel].Length - survivors.Length;
                var nextReel = new string[board[reel].Length];
                var reelDropRows = new int[board[reel].Length];
                for (var row = 0; row < gapCount; row++)
                {
                    var index = draw(pools[reel].Length);
                    if (index < 0 || index >= pools[reel].Length)
                        throw new ArgumentOutOfRangeException(nameof(draw), "A Disco draw is outside its refill pool.");
                    nextReel[row] = pools[reel][index];
                    reelDropRows[row] = gapCount;
                }
                for (var index = 0; index < survivors.Length; index++)
                {
                    var row = gapCount + index;
                    nextReel[row] = survivors[index].Symbol;
                    reelDropRows[row] = row - survivors[index].Row;
                }
                next[reel] = nextReel;
                dropRows[reel] = reelDropRows;
            }

            total = checked(total + stepPayout.TotalPoints);
            sevenPoints = checked(sevenPoints + sevens.Sum(line => line.AmountPoints));
            board = next;
            steps.Add(new NeonDiscoCascadeStep(cleared, next, dropRows, stepPayout));
        }

        return new NeonDiscoCascadeOutcome(initialReels, steps, total);
    }
}
