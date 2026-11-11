using FortuneForge.Games.Slots;

namespace FortuneForge.Games.GoldFoundry;

public static class GoldFoundryCollector
{
    public static (int CollectorCount, long PayoutPoints) Calculate(
        IReadOnlyList<IReadOnlyList<string>> reels, long wagerPoints)
    {
        ArgumentNullException.ThrowIfNull(reels);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(wagerPoints);
        var collectorCount = 0;
        var ingotMultiplier = 0m;
        foreach (var symbol in reels.SelectMany(reel => reel))
        {
            if (symbol == "PAW") collectorCount++;
            else ingotMultiplier += IngotMultiplier(symbol);
        }
        var points = collectorCount == 0 ? 0 : checked((long)Math.Round(
            wagerPoints * ingotMultiplier * Math.Min(collectorCount, 2),
            MidpointRounding.AwayFromZero));
        return (collectorCount, points);
    }

    public static decimal IngotMultiplier(string symbolId) => symbolId switch
    {
        "SEAL_ROWS" => 0.5m,
        "SEAL_SYNC" => 1m,
        "SEAL_PAW" => 1.5m,
        "SEAL_RAND" => 2m,
        _ => 0m
    };

    public static IReadOnlyList<PaylinePayout> CalculateHammerPayouts(
        IReadOnlyList<IReadOnlyList<string>> reels, long wagerPoints)
    {
        ArgumentNullException.ThrowIfNull(reels);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(wagerPoints);
        var payouts = new List<PaylinePayout>();
        var seen = new HashSet<string>(StringComparer.Ordinal);
        var rows = reels.Count == 0 ? 0 : reels.Max(reel => reel.Count);
        var paylineId = 801;

        void Add(IReadOnlyList<GridPosition> positions)
        {
            if (positions.Any(position => position.Reel < 0 || position.Reel >= reels.Count ||
                position.Row < 0 || position.Row >= reels[position.Reel].Count ||
                reels[position.Reel][position.Row] != "BANANA")) return;
            var key = string.Join('|', positions.OrderBy(position => position.Reel)
                .ThenBy(position => position.Row).Select(position => $"{position.Reel}.{position.Row}"));
            if (!seen.Add(key)) return;
            var amount = checked(wagerPoints * 3);
            var current = paylineId++;
            payouts.Add(new(current, amount,
                [new PaidMatch(new SymbolMatch(current, "BANANA", 3, positions.ToArray(), []), 3, amount)]));
        }

        for (var reel = 0; reel < reels.Count; reel++)
            for (var row = 0; row <= reels[reel].Count - 3; row++)
                Add([new(reel, row), new(reel, row + 1), new(reel, row + 2)]);
        for (var row = 0; row < rows; row++)
            for (var reel = 0; reel <= reels.Count - 3; reel++)
                Add([new(reel, row), new(reel + 1, row), new(reel + 2, row)]);
        for (var reel = 0; reel <= reels.Count - 3; reel++)
        {
            for (var row = 0; row <= rows - 3; row++)
                Add([new(reel, row), new(reel + 1, row + 1), new(reel + 2, row + 2)]);
            for (var row = 2; row < rows; row++)
                Add([new(reel, row), new(reel + 1, row - 1), new(reel + 2, row - 2)]);
        }
        return payouts;
    }
}

internal sealed class GoldFoundryReelGenerator(IGameRandomSource random)
{
    internal ReelOutcome Generate(GameDefinition game, ReelSetDefinition reelSet, SymbolSetDefinition symbolSet)
    {
        if (reelSet.SymbolSetId != symbolSet.Id)
            throw new InvalidOperationException($"Reel set '{reelSet.Id}' does not use symbol set '{symbolSet.Id}'.");
        if (reelSet.Reels.Count != game.Layout.ReelCount)
            throw new InvalidOperationException($"Reel set '{reelSet.Id}' has an invalid strip count.");
        var known = symbolSet.Symbols.Select(symbol => symbol.Id).ToHashSet(StringComparer.Ordinal);
        var stops = new int[game.Layout.ReelCount];
        var reels = new List<IReadOnlyList<string>>(game.Layout.ReelCount);
        for (var reel = 0; reel < game.Layout.ReelCount; reel++)
        {
            var strip = reelSet.Reels[reel];
            if (strip.Count < game.Layout.VisibleRows || strip.Any(symbol => !known.Contains(symbol)))
                throw new InvalidOperationException($"Strip {reel + 1} in reel set '{reelSet.Id}' is invalid.");
            var stop = random.Next(strip.Count);
            if (stop < 0 || stop >= strip.Count)
                throw new InvalidOperationException("The random source returned an invalid reel stop.");
            stops[reel] = stop;
            reels.Add(Enumerable.Range(0, game.Layout.VisibleRows)
                .Select(row => strip[(stop + row) % strip.Count]).ToArray());
        }
        return new(stops, reels);
    }
}

internal sealed class GoldFoundryCombinationEvaluator
{
    internal IReadOnlyList<PaylineEvaluation> Evaluate(
        IReadOnlyList<IReadOnlyList<string>> reels, GameDefinition game, SymbolSetDefinition symbolSet)
    {
        if (reels.Count != game.Layout.ReelCount || reels.Any(reel => reel.Count != game.Layout.VisibleRows))
            throw new ArgumentException("The Gold Foundry grid shape is invalid.", nameof(reels));
        var wildId = game.Symbols.WildSymbolId;
        var paylineIds = game.Symbols.PaylineSymbolIds?.ToHashSet(StringComparer.Ordinal)
            ?? throw new InvalidOperationException("Gold Foundry must declare its paying symbols.");
        var regularIds = symbolSet.Symbols
            .Where(symbol => symbol.Id != wildId && paylineIds.Contains(symbol.Id))
            .Select(symbol => symbol.Id).ToArray();
        var nativeWildLengths = game.Symbols.NativeWildMatchLengths.ToHashSet();
        var substituteLengths = game.Symbols.WildSubstitutionMatchLengths.ToHashSet();
        return game.Paylines.Select((rows, index) => EvaluatePayline(index + 1, rows, reels, game,
            wildId, regularIds, nativeWildLengths, substituteLengths, paylineIds)).ToArray();
    }

    private static PaylineEvaluation EvaluatePayline(int paylineId, IReadOnlyList<int> rows,
        IReadOnlyList<IReadOnlyList<string>> reels, GameDefinition game, string wildId,
        IReadOnlyList<string> regularIds, IReadOnlySet<int> nativeWildLengths,
        IReadOnlySet<int> substituteLengths, IReadOnlySet<string> paylineIds)
    {
        var original = rows.Select((row, reel) => reels[reel][row]).ToArray();
        var assignments = new string[game.Layout.ReelCount];
        var candidates = new Dictionary<string, MatchCandidate>(StringComparer.Ordinal);
        Explore(0);
        return new(paylineId, candidates.Values.ToArray());

        void Explore(int position)
        {
            if (position == assignments.Length) { AddCandidate(); return; }
            if (original[position] != wildId)
            {
                assignments[position] = original[position];
                Explore(position + 1);
                return;
            }
            assignments[position] = wildId;
            Explore(position + 1);
            foreach (var regularId in regularIds)
            {
                assignments[position] = regularId;
                Explore(position + 1);
            }
        }

        void AddCandidate()
        {
            var matches = new List<SymbolMatch>();
            var start = 0;
            while (start < assignments.Length)
            {
                var end = start + 1;
                while (end < assignments.Length && assignments[end] == assignments[start]) end++;
                var length = end - start;
                var nativeWild = assignments[start] == wildId;
                var substituted = Enumerable.Range(start, length).Any(reel =>
                    original[reel] == wildId && assignments[reel] != wildId);
                if (paylineIds.Contains(assignments[start]) && length >= game.Matching.MinimumRunLength &&
                    IsAllowedRunShape(rows, start, length, game.Layout.ReelCount) &&
                    (!substituted || substituteLengths.Contains(length)) &&
                    (!nativeWild || nativeWildLengths.Contains(length)))
                {
                    var positions = Enumerable.Range(start, length)
                        .Select(reel => new GridPosition(reel, rows[reel])).ToArray();
                    var wilds = Enumerable.Range(start, length)
                        .Where(reel => original[reel] == wildId && assignments[reel] != wildId)
                        .Select(reel => new GridPosition(reel, rows[reel])).ToArray();
                    matches.Add(new(paylineId, assignments[start], length, positions, wilds));
                }
                start = end;
            }
            if (matches.Count == 0) return;
            var hasLeftThree = matches.Any(match => match.MatchLength == 3 && match.Positions[0].Reel == 0);
            matches.RemoveAll(match => match.MatchLength == 3 &&
                match.Positions[0].Reel == game.Layout.ReelCount - 3 && !hasLeftThree);
            if (matches.Count == 0) return;
            if (game.Matching.AllowMultipleRunsPerPayline) Add(matches);
            else foreach (var match in matches) Add([match]);
        }

        void Add(IReadOnlyList<SymbolMatch> matches)
        {
            var key = string.Join('|', matches.Select(match =>
                $"{match.SymbolId}:{match.MatchLength}:{match.Positions[0].Reel}"));
            candidates.TryAdd(key, new MatchCandidate(matches));
        }
    }

    private static bool IsAllowedRunShape(IReadOnlyList<int> rows, int start, int length, int fullLength)
    {
        if (length == fullLength) return true;
        if (length is not (3 or 4) || start != 0 && start != fullLength - length) return false;
        if (length == 4) return true;
        var first = rows[start + 1] - rows[start];
        var second = rows[start + 2] - rows[start + 1];
        return first == second && Math.Abs(first) <= 1;
    }
}

internal sealed class GoldFoundryPayoutCalculator
{
    internal SpinPayout Calculate(IReadOnlyList<PaylineEvaluation> evaluations, GameDefinition game,
        PaytableDefinition paytable, long wagerPoints)
    {
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(wagerPoints);
        var rules = paytable.Rules.ToDictionary(rule => (rule.SymbolId, rule.MatchLength), rule => rule.Multiplier);
        var priced = new List<PaylinePayout>();
        foreach (var evaluation in evaluations)
        {
            var step = game.Math.PaylinePayoutSteps[evaluation.PaylineId - 1];
            var best = evaluation.Candidates.Select(candidate => PriceCandidate(evaluation.PaylineId, candidate,
                    rules, wagerPoints, game.Layout.ReelCount, step, game.Math.PayoutMultiplier))
                .Where(candidate => candidate.AmountPoints > 0)
                .OrderByDescending(candidate => candidate.AmountPoints)
                .ThenBy(CandidateKey, StringComparer.Ordinal).FirstOrDefault();
            if (best is not null) priced.Add(best);
        }
        var payouts = priced.Where(payout => payout.Matches.Any(match =>
            match.Match.MatchLength == game.Layout.ReelCount)).ToList();
        var claimed = new HashSet<string>(StringComparer.Ordinal);
        foreach (var candidate in priced.Where(payout => payout.Matches.All(match =>
                     match.Match.MatchLength < game.Layout.ReelCount))
                 .OrderByDescending(payout => payout.Matches.Count)
                 .ThenByDescending(payout => payout.AmountPoints)
                 .ThenBy(CandidateKey, StringComparer.Ordinal))
        {
            var keys = candidate.Matches.Select(match => MatchGeometryKey(match.Match)).ToArray();
            if (keys.Any(claimed.Contains)) continue;
            payouts.Add(candidate);
            foreach (var key in keys) claimed.Add(key);
        }
        return new(payouts.Sum(payout => payout.AmountPoints), payouts);
    }

    private static PaylinePayout PriceCandidate(int paylineId, MatchCandidate candidate,
        IReadOnlyDictionary<(string SymbolId, int MatchLength), long> rules, long wagerPoints,
        int fullLength, int paylineStep, decimal payoutMultiplier)
    {
        var paid = candidate.Matches.Select(match =>
        {
            var baseMultiplier = GetBaseMultiplier(rules, match.SymbolId, match.MatchLength);
            var multiplier = match.MatchLength == fullLength && baseMultiplier > 0
                ? checked(baseMultiplier + paylineStep) : baseMultiplier;
            var amount = checked((long)Math.Round(checked(wagerPoints * multiplier) * payoutMultiplier,
                MidpointRounding.AwayFromZero));
            return new PaidMatch(match, multiplier, amount);
        }).Where(match => match.AmountPoints > 0).ToArray();
        return new(paylineId, paid.Sum(match => match.AmountPoints), paid);
    }

    private static long GetBaseMultiplier(
        IReadOnlyDictionary<(string SymbolId, int MatchLength), long> rules, string symbolId, int matchLength)
    {
        for (var length = matchLength; length > 0; length--)
            if (rules.TryGetValue((symbolId, length), out var multiplier)) return multiplier;
        return 0;
    }

    private static string CandidateKey(PaylinePayout payout) => string.Join('|', payout.Matches.Select(match =>
        $"{match.Match.SymbolId}:{match.Match.MatchLength}:{match.Match.Positions[0].Reel}"));
    private static string MatchGeometryKey(SymbolMatch match) =>
        $"{match.MatchLength}:" + string.Join(',', match.Positions.Select(position =>
            $"{position.Reel}.{position.Row}"));
}
