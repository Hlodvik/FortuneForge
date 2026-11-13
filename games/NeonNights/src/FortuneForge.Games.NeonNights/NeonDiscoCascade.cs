using FortuneForge.Games.Slots;

namespace FortuneForge.Games.NeonNights;

// A Disco play is one paid spin. All cascades that follow its initial landing
// belong to that spin and never draw another wager or award another feature.
public static class NeonDiscoCascade
{
    public const int InitialMultiplier = 1;
    public const int MaximumWinningSteps = 16;

    public static bool IsValidMultiplier(long multiplier) => multiplier is 1 or 2 or 3 or 5;

    private static int TierIndex(int multiplier) => multiplier switch
    {
        1 => 0, 2 => 1, 3 => 2, 5 => 3,
        _ => throw new ArgumentOutOfRangeException(nameof(multiplier))
    };

    internal static int MultiplierAfterSpin(int startingMultiplier, int wins)
    {
        var tier = TierIndex(startingMultiplier);
        return MultiplierForStep(wins == 0 ? Math.Max(0, tier - 1) : tier + wins);
    }

    public static int MultiplierForStep(int winningStep) => winningStep switch
    {
        < 0 => throw new ArgumentOutOfRangeException(nameof(winningStep)),
        0 => 1, 1 => 2, 2 => 3, _ => 5
    };

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
        out long sevenPoints,
        int startingMultiplier = InitialMultiplier)
    {
        if (symbols.Id != NeonDiscoDefinitions.SymbolSetId)
            return ResolveLegacy(initialReels, game, reelSet, symbols, paytable,
                wagerPoints, evaluator, payoutCalculator, draw, out sevenPoints);

        if (initialReels.Count != 5 || initialReels.Any(reel => reel.Count != 4) ||
            reelSet.Reels.Count != 5 || game.Layout.ReelCount != 5 || game.Layout.VisibleRows != 4 ||
            game.Symbols.SymbolSetId != symbols.Id || reelSet.SymbolSetId != symbols.Id || paytable.SymbolSetId != symbols.Id)
            throw new ArgumentException("The Disco board does not match its captured cluster rules.", nameof(initialReels));
        var allowed = NeonDiscoDefinitions.SymbolIds.ToHashSet(StringComparer.Ordinal);
        if (!allowed.SetEquals(symbols.Symbols.Select(symbol => symbol.Id)) ||
            initialReels.SelectMany(reel => reel).Any(symbol => !allowed.Contains(symbol)) ||
            reelSet.Reels.Any(reel => reel.Count == 0 || reel.Any(symbol => !allowed.Contains(symbol))))
            throw new ArgumentException("Disco uses only its seven ordinary symbols.", nameof(symbols));

        var startingTier = TierIndex(startingMultiplier);
        var pools = reelSet.Reels.Select(reel => reel.ToArray()).ToArray();
        var board = initialReels.Select(reel => reel.ToArray()).ToArray();
        var steps = new List<NeonDiscoCascadeStep>();
        var total = 0L;
        sevenPoints = 0;
        for (var stepIndex = 0; stepIndex < MaximumWinningSteps; stepIndex++)
        {
            var multiplier = MultiplierForStep(startingTier + stepIndex);
            var payout = EvaluateClusters(board, game, paytable, wagerPoints, multiplier);
            if (payout.TotalPoints == 0) break;
            var cleared = payout.Paylines.SelectMany(line => line.Matches)
                .SelectMany(match => match.Match.Positions)
                .OrderBy(position => position.Reel).ThenBy(position => position.Row).ToArray();
            var (next, dropRows) = Refill(board, cleared, pools, draw);
            total = checked(total + payout.TotalPoints);
            steps.Add(new(cleared, next, dropRows, payout) { Multiplier = multiplier });
            board = next;
        }
        if (steps.Count == MaximumWinningSteps && EvaluateClusters(board, game, paytable,
                wagerPoints, MultiplierForStep(startingTier + steps.Count)).TotalPoints > 0)
            throw new InvalidOperationException("The Disco cascade exceeded its settlement limit.");
        return new(initialReels, steps, total);
    }

    public static SpinPayout EvaluateClusters(
        IReadOnlyList<IReadOnlyList<string>> board,
        GameDefinition game,
        PaytableDefinition paytable,
        long wagerPoints,
        int multiplier)
    {
        if (!IsValidMultiplier(multiplier))
            throw new ArgumentOutOfRangeException(nameof(multiplier));
        var seen = new HashSet<GridPosition>();
        var paylines = new List<PaylinePayout>();
        var rules = paytable.Rules.ToDictionary(rule => (rule.SymbolId, rule.MatchLength));
        for (var reel = 0; reel < board.Count; reel++)
        for (var row = 0; row < board[reel].Count; row++)
        {
            var start = new GridPosition(reel, row);
            if (!seen.Add(start)) continue;
            var symbol = board[reel][row];
            var cluster = new List<GridPosition>();
            var pending = new Queue<GridPosition>();
            pending.Enqueue(start);
            while (pending.TryDequeue(out var cell))
            {
                cluster.Add(cell);
                foreach (var adjacent in new GridPosition[]
                {
                    new(cell.Reel - 1, cell.Row), new(cell.Reel + 1, cell.Row),
                    new(cell.Reel, cell.Row - 1), new(cell.Reel, cell.Row + 1)
                })
                {
                    if (adjacent.Reel >= 0 && adjacent.Reel < board.Count &&
                        adjacent.Row >= 0 && adjacent.Row < board[adjacent.Reel].Count &&
                        board[adjacent.Reel][adjacent.Row] == symbol && seen.Add(adjacent))
                        pending.Enqueue(adjacent);
                }
            }
            if (cluster.Count < NeonDiscoDefinitions.MinimumClusterSize) continue;
            if (!rules.TryGetValue((symbol, cluster.Count), out var rule))
                throw new InvalidOperationException("A Disco cluster is missing its captured paytable award.");
            var effectiveMultiplier = checked(rule.Multiplier * multiplier);
            var amount = checked((long)Math.Round(wagerPoints * (decimal)effectiveMultiplier *
                game.Math.PayoutMultiplier, MidpointRounding.AwayFromZero));
            if (amount == 0) continue;
            var id = 1001 + paylines.Count;
            var positions = cluster.OrderBy(cell => cell.Reel).ThenBy(cell => cell.Row).ToArray();
            paylines.Add(new(id, amount,
                [new(new(id, symbol, positions.Length, positions, []), effectiveMultiplier, amount)]));
        }
        return new(paylines.Sum(line => line.AmountPoints), paylines);
    }

    private static NeonDiscoCascadeOutcome ResolveLegacy(
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
            var evaluations = evaluator.Evaluate(board, game, symbols, NeonArcadeSlotDefinitions.PreviousCascadeEngineVersion);
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

            var (next, dropRows) = Refill(board, cleared, pools, draw);

            total = checked(total + stepPayout.TotalPoints);
            sevenPoints = checked(sevenPoints + sevens.Sum(line => line.AmountPoints));
            board = next;
            steps.Add(new NeonDiscoCascadeStep(cleared, next, dropRows, stepPayout));
        }

        return new NeonDiscoCascadeOutcome(initialReels, steps, total);
    }

    private static (string[][] Reels, int[][] DropRows) Refill(string[][] board,
        IReadOnlyList<GridPosition> cleared, string[][] pools, Func<int, int> draw)
    {
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
        return (next, dropRows);
    }
}
