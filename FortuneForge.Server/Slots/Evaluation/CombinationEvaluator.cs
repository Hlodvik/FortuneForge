using FortuneForge.Server.Slots.Models;

namespace FortuneForge.Server.Slots.Evaluation;

public sealed class CombinationEvaluator : ICombinationEvaluator
{
    private static readonly HashSet<string> FeatureOnlySymbolIds = new(StringComparer.Ordinal)
    {
        "BANANA",
        "PAW",
        "RAND_05",
        "RAND_1",
        "RAND_15",
        "RAND_2",
        "RAND_3",
        "RAND_4",
        "RAND_5",
        "SEAL_SYNC",
        "SEAL_ROWS",
        "SEAL_PAW",
        "SEAL_RAND"
    };

    public IReadOnlyList<PaylineEvaluation> Evaluate(
        IReadOnlyList<IReadOnlyList<string>> reels,
        GameDefinition game,
        SymbolSetDefinition symbolSet)
    {
        ValidateGrid(reels, game);
        var wildId = game.Symbols.WildSymbolId;
        var freeGameSymbolId = game.FreeGames?.SymbolId;
        var regularIds = symbolSet.Symbols
            .Where(symbol =>
                !string.Equals(symbol.Id, wildId, StringComparison.Ordinal) &&
                !string.Equals(symbol.Id, freeGameSymbolId, StringComparison.Ordinal) &&
                !FeatureOnlySymbolIds.Contains(symbol.Id))
            .Select(symbol => symbol.Id)
            .ToArray();
        var nativeWildMatchLengths = game.Symbols.NativeWildMatchLengths.ToHashSet();
        var wildSubstitutionMatchLengths = game.Symbols.WildSubstitutionMatchLengths.ToHashSet();

        return game.Paylines
            .Select((rows, index) => EvaluatePayline(
                index + 1,
                rows,
                reels,
                game,
                wildId,
                freeGameSymbolId,
                regularIds,
                nativeWildMatchLengths,
                wildSubstitutionMatchLengths))
            .ToArray();
    }

    private static PaylineEvaluation EvaluatePayline(
        int paylineId,
        IReadOnlyList<int> rows,
        IReadOnlyList<IReadOnlyList<string>> reels,
        GameDefinition game,
        string wildId,
        string? freeGameSymbolId,
        IReadOnlyList<string> regularIds,
        IReadOnlySet<int> nativeWildMatchLengths,
        IReadOnlySet<int> wildSubstitutionMatchLengths)
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
                var isFreeGameSymbol = string.Equals(
                    assignments[start],
                    freeGameSymbolId,
                    StringComparison.Ordinal);
                var usesWildSubstitution = Enumerable.Range(start, length).Any(reel =>
                    original[reel] == wildId && assignments[reel] != wildId);
                if (!isFreeGameSymbol &&
                    length >= game.Matching.MinimumRunLength &&
                    IsAllowedRunShape(rows, start, length, game.Layout.ReelCount) &&
                    (!usesWildSubstitution || wildSubstitutionMatchLengths.Contains(length)) &&
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

            var hasLeftThreeMatch = matches.Any(match =>
                match.MatchLength == 3 && match.Positions[0].Reel == 0);
            matches.RemoveAll(match =>
                match.MatchLength == 3 &&
                match.Positions[0].Reel == game.Layout.ReelCount - 3 &&
                !hasLeftThreeMatch);

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

    private static bool IsAllowedRunShape(
        IReadOnlyList<int> paylineRows,
        int start,
        int length,
        int fullMatchLength)
    {
        if (length == fullMatchLength)
        {
            return true;
        }

        if (length == 3)
        {
            if (start != 0 && start != fullMatchLength - length)
            {
                return false;
            }

            var firstStep = paylineRows[start + 1] - paylineRows[start];
            var secondStep = paylineRows[start + 2] - paylineRows[start + 1];
            return firstStep == secondStep && Math.Abs(firstStep) <= 1;
        }

        return false;
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
