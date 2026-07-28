using FortuneForge.Server.Slots.Models;

namespace FortuneForge.Server.Slots.Bonuses;

public static class SpecialPointBonus
{
    public static int CalculateAward(
        IReadOnlyList<PaylineEvaluation> evaluations,
        GameDefinition game)
    {
        var rules = game.SpecialPoints;
        if (rules is null)
        {
            return 0;
        }

        var exactMatches = evaluations
            .SelectMany(evaluation => evaluation.Candidates)
            .SelectMany(candidate => candidate.Matches)
            .Where(match =>
                string.Equals(match.SymbolId, rules.SymbolId, StringComparison.Ordinal) &&
                match.WildPositions.Count == 0 &&
                (match.MatchLength == 3 || match.MatchLength == game.Layout.ReelCount))
            .GroupBy(MatchGeometryKey, StringComparer.Ordinal)
            .Select(group => group.First());

        return exactMatches.Sum(match =>
            match.MatchLength == game.Layout.ReelCount
                ? rules.FiveMatchPoints
                : rules.ThreeMatchPoints);
    }

    public static ReelSetDefinition CreateBoostedReelSet(
        GameDefinition game,
        ReelSetDefinition reelSet)
    {
        var rules = game.SpecialPoints
            ?? throw new InvalidOperationException($"Game '{game.Id}' does not define a special-point boost.");
        var commonIds = rules.CommonSymbolIds.ToHashSet(StringComparer.Ordinal);

        return new ReelSetDefinition
        {
            Id = $"{reelSet.Id}-power-boost",
            SymbolSetId = reelSet.SymbolSetId,
            Reels = reelSet.Reels.Select(strip => ReduceCommonSymbols(strip, commonIds)).ToList()
        };
    }

    private static List<string> ReduceCommonSymbols(
        IReadOnlyList<string> strip,
        IReadOnlySet<string> commonIds)
    {
        var targets = strip
            .Where(commonIds.Contains)
            .GroupBy(symbol => symbol, StringComparer.Ordinal)
            .ToDictionary(group => group.Key, group => group.Count() / 2, StringComparer.Ordinal);
        var seen = new Dictionary<string, int>(StringComparer.Ordinal);
        var kept = new Dictionary<string, int>(StringComparer.Ordinal);
        var reduced = new List<string>(strip.Count);

        foreach (var symbol in strip)
        {
            if (!commonIds.Contains(symbol))
            {
                reduced.Add(symbol);
                continue;
            }

            var ordinal = seen.GetValueOrDefault(symbol);
            seen[symbol] = ordinal + 1;
            if (ordinal % 2 == 0 && kept.GetValueOrDefault(symbol) < targets[symbol])
            {
                reduced.Add(symbol);
                kept[symbol] = kept.GetValueOrDefault(symbol) + 1;
            }
        }

        return reduced;
    }

    private static string MatchGeometryKey(SymbolMatch match) =>
        $"{match.MatchLength}:" + string.Join(',', match.Positions.Select(position =>
            $"{position.Reel}.{position.Row}"));
}
