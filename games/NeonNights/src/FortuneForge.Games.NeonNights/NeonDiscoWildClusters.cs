using FortuneForge.Games.Slots;

namespace FortuneForge.Games.NeonNights;

// Each WILD belongs to one paid cluster in a cascade. Re-evaluating after the
// highest award claims its cells keeps competing groups connected correctly.
internal static class NeonDiscoWildClusters
{
    private sealed record Candidate(string Symbol, GridPosition[] Positions,
        GridPosition[] WildPositions, long Multiplier, long Amount);

    public static SpinPayout Evaluate(IReadOnlyList<IReadOnlyList<string>> board,
        GameDefinition game, PaytableDefinition paytable, long wagerPoints, int multiplier)
    {
        var rules = paytable.Rules.ToDictionary(rule => (rule.SymbolId, rule.MatchLength));
        var consumed = new HashSet<GridPosition>();
        var paylines = new List<PaylinePayout>();
        while (true)
        {
            var winner = Candidates(board, game, rules, wagerPoints, multiplier, consumed)
                .OrderByDescending(candidate => candidate.Amount)
                .ThenByDescending(candidate => candidate.Positions.Length)
                .ThenBy(candidate => candidate.Symbol, StringComparer.Ordinal)
                .ThenBy(candidate => candidate.Positions[0].Reel)
                .ThenBy(candidate => candidate.Positions[0].Row)
                .FirstOrDefault();
            if (winner is null) break;
            consumed.UnionWith(winner.Positions);
            var id = 1001 + paylines.Count;
            paylines.Add(new(id, winner.Amount,
                [new(new(id, winner.Symbol, winner.Positions.Length, winner.Positions, winner.WildPositions),
                    winner.Multiplier, winner.Amount)]));
        }
        return new(paylines.Sum(line => line.AmountPoints), paylines);
    }

    private static IEnumerable<Candidate> Candidates(IReadOnlyList<IReadOnlyList<string>> board,
        GameDefinition game, Dictionary<(string SymbolId, int MatchLength), PayoutRule> rules,
        long wagerPoints, int multiplier, HashSet<GridPosition> consumed)
    {
        foreach (var symbol in NeonDiscoDefinitions.SymbolIds)
        {
            var seen = new HashSet<GridPosition>();
            for (var reel = 0; reel < board.Count; reel++)
            for (var row = 0; row < board[reel].Count; row++)
            {
                var start = new GridPosition(reel, row);
                // Starting from a paying symbol also excludes all-WILD groups.
                if (board[reel][row] != symbol || consumed.Contains(start) || !seen.Add(start)) continue;
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
                        if (adjacent.Reel < 0 || adjacent.Reel >= board.Count ||
                            adjacent.Row < 0 || adjacent.Row >= board[adjacent.Reel].Count ||
                            consumed.Contains(adjacent)) continue;
                        var adjacentSymbol = board[adjacent.Reel][adjacent.Row];
                        if ((adjacentSymbol == symbol || adjacentSymbol == NeonDiscoDefinitions.WildSymbolId) &&
                            seen.Add(adjacent)) pending.Enqueue(adjacent);
                    }
                }
                if (cluster.Count < NeonDiscoDefinitions.MinimumClusterSize) continue;
                if (!rules.TryGetValue((symbol, cluster.Count), out var rule))
                    throw new InvalidOperationException("A Disco cluster is missing its captured paytable award.");
                var effectiveMultiplier = checked(rule.Multiplier * multiplier);
                var amount = checked((long)Math.Round(wagerPoints * (decimal)effectiveMultiplier *
                    game.Math.PayoutMultiplier, MidpointRounding.AwayFromZero));
                if (amount == 0) continue;
                var positions = cluster.OrderBy(cell => cell.Reel).ThenBy(cell => cell.Row).ToArray();
                var wilds = positions.Where(cell =>
                    board[cell.Reel][cell.Row] == NeonDiscoDefinitions.WildSymbolId).ToArray();
                yield return new(symbol, positions, wilds, effectiveMultiplier, amount);
            }
        }
    }
}
