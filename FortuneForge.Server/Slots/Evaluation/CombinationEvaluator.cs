using FortuneForge.Server.Slots.Models;

namespace FortuneForge.Server.Slots.Evaluation;

public sealed class CombinationEvaluator : ICombinationEvaluator
{
    public IReadOnlyList<PaylineEvaluation> Evaluate(
        IReadOnlyList<IReadOnlyList<string>> reels,
        GameDefinition game,
        SymbolSetDefinition symbolSet)
    {
        ValidateGrid(reels, game);
        var wildId = game.Symbols.WildSymbolId;
        var regularIds = symbolSet.Symbols
            .Where(symbol => !string.Equals(symbol.Id, wildId, StringComparison.Ordinal))
            .Select(symbol => symbol.Id)
            .ToArray();
        var nativeWildMatchLengths = game.Symbols.NativeWildMatchLengths.ToHashSet();

        return game.Paylines
            .Select((rows, index) => EvaluatePayline(
                index + 1,
                rows,
                reels,
                game,
                wildId,
                regularIds,
                nativeWildMatchLengths))
            .ToArray();
    }

    private static PaylineEvaluation EvaluatePayline(
        int paylineId,
        IReadOnlyList<int> rows,
        IReadOnlyList<IReadOnlyList<string>> reels,
        GameDefinition game,
        string wildId,
        IReadOnlyList<string> regularIds,
        IReadOnlySet<int> nativeWildMatchLengths)
    {
        var original = rows.Select((row, reel) => reels[reel][row]).ToArray();
        var assignments = new string[game.Layout.ReelCount];
        var candidates = new Dictionary<string, MatchCandidate>(StringComparer.Ordinal);

        ExploreAssignments(0);
        return new PaylineEvaluation(paylineId, candidates.Values.ToArray());

        void ExploreAssignments(int position)
        {
            if (position == assignments.Length)
            {
                AddCandidate();
                return;
            }

            if (!string.Equals(original[position], wildId, StringComparison.Ordinal))
            {
                assignments[position] = original[position];
                ExploreAssignments(position + 1);
                return;
            }

            assignments[position] = wildId;
            ExploreAssignments(position + 1);
            foreach (var regularId in regularIds)
            {
                assignments[position] = regularId;
                ExploreAssignments(position + 1);
            }
        }

        void AddCandidate()
        {
            var matches = new List<SymbolMatch>();
            var start = 0;
            while (start < assignments.Length)
            {
                var end = start + 1;
                while (end < assignments.Length && assignments[end] == assignments[start])
                {
                    end++;
                }

                var length = end - start;
                var isNativeWild = string.Equals(assignments[start], wildId, StringComparison.Ordinal);
                if (length >= game.Matching.MinimumRunLength &&
                    (!isNativeWild || nativeWildMatchLengths.Contains(length)))
                {
                    var positions = Enumerable.Range(start, length)
                        .Select(reel => new GridPosition(reel, rows[reel]))
                        .ToArray();
                    var wildPositions = Enumerable.Range(start, length)
                        .Where(reel => original[reel] == wildId && assignments[reel] != wildId)
                        .Select(reel => new GridPosition(reel, rows[reel]))
                        .ToArray();

                    matches.Add(new SymbolMatch(
                        paylineId,
                        assignments[start],
                        length,
                        positions,
                        wildPositions));
                }

                start = end;
            }

            if (matches.Count == 0)
            {
                return;
            }

            if (game.Matching.AllowMultipleRunsPerPayline)
            {
                AddMatchCandidate(matches);
                return;
            }

            foreach (var match in matches)
            {
                AddMatchCandidate([match]);
            }
        }

        void AddMatchCandidate(IReadOnlyList<SymbolMatch> matches)
        {
            var key = string.Join('|', matches.Select(match =>
                $"{match.SymbolId}:{match.MatchLength}:{match.Positions[0].Reel}"));
            candidates.TryAdd(key, new MatchCandidate(matches));
        }
    }

    private static void ValidateGrid(
        IReadOnlyList<IReadOnlyList<string>> reels,
        GameDefinition game)
    {
        if (reels.Count != game.Layout.ReelCount ||
            reels.Any(reel => reel.Count != game.Layout.VisibleRows))
        {
            throw new ArgumentException(
                $"The slot grid must contain {game.Layout.ReelCount} reels and " +
                $"{game.Layout.VisibleRows} rows per reel.",
                nameof(reels));
        }
    }
}
