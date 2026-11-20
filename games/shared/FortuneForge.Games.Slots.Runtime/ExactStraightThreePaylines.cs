using FortuneForge.Games.Slots;

namespace FortuneForge.Games.Slots.Runtime;

internal static class ExactStraightThreePaylines
{
    public static IReadOnlyList<PaylineEvaluation> Evaluate(
        IReadOnlyList<IReadOnlyList<string>> reels, GameDefinition game)
    {
        var payingSymbols = game.Symbols.PaylineSymbolIds?.ToHashSet(StringComparer.Ordinal)
            ?? throw new InvalidOperationException("Exact straight three matching requires explicit paying symbols.");
        var evaluations = new List<PaylineEvaluation>();
        var coveredTriples = new HashSet<(int Reel, int Row, int Step)>();
        for (var index = 0; index < game.Paylines.Count; index++)
        {
            var rows = game.Paylines[index];
            var symbol = reels[0][rows[0]];
            if (!payingSymbols.Contains(symbol) || rows.Where((row, reel) => reels[reel][row] != symbol).Any())
                continue;

            var positions = rows.Select((row, reel) => new GridPosition(reel, row)).ToArray();
            evaluations.Add(Match(index + 1, symbol, positions));
            for (var start = 0; start + 2 < rows.Count; start++)
            {
                var step = rows[start + 1] - rows[start];
                if (Math.Abs(step) <= 1 && rows[start + 2] - rows[start + 1] == step)
                    coveredTriples.Add((start, rows[start], step));
            }
        }

        // Short paths have stable IDs above the full routes. In particular, the
        // middle-reel diagonals are not segments of any of the active full routes.
        var shortPathId = game.Layout.PaylineCount;
        for (var start = 0; start + 2 < reels.Count; start++)
        for (var row = 0; row < game.Layout.VisibleRows; row++)
        for (var step = -1; step <= 1; step++)
        {
            var lastRow = row + 2 * step;
            if ((uint)lastRow >= (uint)game.Layout.VisibleRows) continue;
            shortPathId++;
            var symbol = reels[start][row];
            if (!payingSymbols.Contains(symbol) ||
                reels[start + 1][row + step] != symbol ||
                reels[start + 2][lastRow] != symbol ||
                SameSymbolAt(start - 1, row - step, symbol) ||
                SameSymbolAt(start + 3, lastRow + step, symbol) ||
                coveredTriples.Contains((start, row, step)))
                continue;

            evaluations.Add(Match(shortPathId, symbol,
                [new(start, row), new(start + 1, row + step), new(start + 2, lastRow)]));
        }
        return evaluations;

        bool SameSymbolAt(int reel, int row, string symbol) =>
            (uint)reel < (uint)reels.Count && (uint)row < (uint)game.Layout.VisibleRows && reels[reel][row] == symbol;
    }

    private static PaylineEvaluation Match(int id, string symbol, IReadOnlyList<GridPosition> positions) =>
        new(id, [new([new(id, symbol, positions.Count, positions, [])])]);
}
