using FortuneForge.Games.Slots;

namespace FortuneForge.Games.MidnightVault;

internal sealed class MidnightVaultReelGenerator(IGameRandomSource random) : IMidnightVaultReelSource
{
    public ReelOutcome Generate(GameDefinition game, ReelSetDefinition reelSet, SymbolSetDefinition symbolSet)
    {
        if (reelSet.SymbolSetId != symbolSet.Id || reelSet.Reels.Count != game.Layout.ReelCount)
            throw new InvalidOperationException("The Midnight Vault reel set does not match its rules.");
        var known = symbolSet.Symbols.Select(symbol => symbol.Id).ToHashSet(StringComparer.Ordinal);
        var stops = new int[game.Layout.ReelCount];
        var reels = new List<IReadOnlyList<string>>(game.Layout.ReelCount);
        for (var reel = 0; reel < game.Layout.ReelCount; reel++)
        {
            var strip = reelSet.Reels[reel];
            if (strip.Count < game.Layout.VisibleRows || strip.Any(symbol => !known.Contains(symbol)))
                throw new InvalidOperationException("A Midnight Vault reel strip is invalid.");
            var stop = random.Next(strip.Count);
            stops[reel] = stop;
            reels.Add(Enumerable.Range(0, game.Layout.VisibleRows)
                .Select(row => strip[(stop + row) % strip.Count]).ToArray());
        }
        return new(stops, reels);
    }
}

internal static class MidnightVaultFreeGameFrequency
{
    internal static ReelOutcome Apply(ReelOutcome outcome, GameDefinition game, IGameRandomSource random)
    {
        var rules = game.FreeGames;
        if (rules is null || rules.VisibleFrequencyDivisor <= 1) return outcome;
        var replacements = game.SpecialPoints?.CommonSymbolIds;
        if (replacements is null || replacements.Count == 0)
            throw new InvalidOperationException("Midnight Vault needs replacement symbols for FREE thinning.");
        var reels = outcome.VisibleReels.Select(reel => reel.Select(symbol =>
            symbol == rules.SymbolId && random.Next(rules.VisibleFrequencyDivisor) != 0
                ? replacements[random.Next(replacements.Count)] : symbol).ToArray())
            .Select(reel => (IReadOnlyList<string>)reel).ToArray();
        return outcome with { VisibleReels = reels };
    }
}

internal static class MidnightVaultSpecialPoints
{
    internal static int CalculateAward(IReadOnlyList<PaylineEvaluation> evaluations, GameDefinition game)
    {
        var rules = game.SpecialPoints;
        if (rules is null) return 0;
        var exact = evaluations.SelectMany(evaluation => evaluation.Candidates)
            .SelectMany(candidate => candidate.Matches)
            .Where(match => match.SymbolId == rules.SymbolId && match.WildPositions.Count == 0 &&
                (match.MatchLength == 3 || match.MatchLength == game.Layout.ReelCount))
            .GroupBy(MatchGeometryKey, StringComparer.Ordinal).Select(group => group.First());
        return exact.Sum(match => match.MatchLength == game.Layout.ReelCount
            ? rules.FiveMatchPoints : rules.ThreeMatchPoints);
    }

    internal static ReelSetDefinition CreateBoostedReelSet(GameDefinition game, ReelSetDefinition reelSet)
    {
        var rules = game.SpecialPoints ?? throw new InvalidOperationException("Midnight Vault has no boost rules.");
        var common = rules.CommonSymbolIds.ToHashSet(StringComparer.Ordinal);
        return new ReelSetDefinition
        {
            Id = $"{reelSet.Id}-power-boost",
            SymbolSetId = reelSet.SymbolSetId,
            Reels = reelSet.Reels.Select(strip => ReduceCommonSymbols(strip, common)).ToList()
        };
    }

    private static List<string> ReduceCommonSymbols(IReadOnlyList<string> strip, IReadOnlySet<string> common)
    {
        var targets = strip.Where(common.Contains).GroupBy(symbol => symbol, StringComparer.Ordinal)
            .ToDictionary(group => group.Key, group => group.Count() / 2, StringComparer.Ordinal);
        var seen = new Dictionary<string, int>(StringComparer.Ordinal);
        var kept = new Dictionary<string, int>(StringComparer.Ordinal);
        var reduced = new List<string>(strip.Count);
        foreach (var symbol in strip)
        {
            if (!common.Contains(symbol)) { reduced.Add(symbol); continue; }
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
        $"{match.MatchLength}:" + string.Join(',', match.Positions.Select(position => $"{position.Reel}.{position.Row}"));
}

internal sealed class MidnightVaultCombinationEvaluator
{
    private static readonly HashSet<string> FeatureOnlySymbolIds = new(StringComparer.Ordinal)
    { "BANANA", "SEAL_SYNC", "SEAL_ROWS", "SEAL_PAW", "SEAL_RAND" };

    internal IReadOnlyList<PaylineEvaluation> Evaluate(IReadOnlyList<IReadOnlyList<string>> reels,
        GameDefinition game, SymbolSetDefinition symbolSet)
    {
        if (reels.Count != game.Layout.ReelCount || reels.Any(reel => reel.Count != game.Layout.VisibleRows))
            throw new ArgumentException("The Midnight Vault grid shape is invalid.", nameof(reels));
        var wildId = game.Symbols.WildSymbolId;
        var freeId = game.FreeGames?.SymbolId;
        var regularIds = symbolSet.Symbols.Where(symbol => symbol.Id != wildId && symbol.Id != freeId &&
                !FeatureOnlySymbolIds.Contains(symbol.Id)).Select(symbol => symbol.Id).ToArray();
        var nativeWildLengths = game.Symbols.NativeWildMatchLengths.ToHashSet();
        var substituteLengths = game.Symbols.WildSubstitutionMatchLengths.ToHashSet();
        return game.Paylines.Select((rows, index) => EvaluatePayline(index + 1, rows, reels, game,
            wildId, freeId, regularIds, nativeWildLengths, substituteLengths)).ToArray();
    }

    private static PaylineEvaluation EvaluatePayline(int paylineId, IReadOnlyList<int> rows,
        IReadOnlyList<IReadOnlyList<string>> reels, GameDefinition game, string wildId, string? freeId,
        IReadOnlyList<string> regularIds, IReadOnlySet<int> nativeWildLengths, IReadOnlySet<int> substituteLengths)
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
                var free = assignments[start] == freeId;
                var substituted = Enumerable.Range(start, length).Any(reel =>
                    original[reel] == wildId && assignments[reel] != wildId);
                if (!free && length >= game.Matching.MinimumRunLength &&
                    IsAllowedRunShape(rows, start, length, game.Layout.ReelCount) &&
                    (!substituted || substituteLengths.Contains(length)) &&
                    (!nativeWild || nativeWildLengths.Contains(length)))
                {
                    var positions = Enumerable.Range(start, length)
                        .Select(reel => new GridPosition(reel, rows[reel])).ToArray();
                    var wilds = Enumerable.Range(start, length).Where(reel =>
                            original[reel] == wildId && assignments[reel] != wildId)
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

internal sealed class MidnightVaultPayoutCalculator
{
    internal SpinPayout Calculate(IReadOnlyList<PaylineEvaluation> evaluations, GameDefinition game,
        PaytableDefinition paytable, long wagerPoints)
    {
        if (wagerPoints <= 0) throw new ArgumentOutOfRangeException(nameof(wagerPoints));
        var rules = paytable.Rules.ToDictionary(rule => (rule.SymbolId, rule.MatchLength), rule => rule.Multiplier);
        var priced = new List<PaylinePayout>();
        foreach (var evaluation in evaluations)
        {
            var step = game.Math.PaylinePayoutSteps[evaluation.PaylineId - 1];
            var best = evaluation.Candidates.Select(candidate => PriceCandidate(evaluation.PaylineId, candidate,
                    rules, wagerPoints, game.Layout.ReelCount, step, game.Math.PayoutMultiplier))
                .Where(candidate => candidate.AmountPoints > 0).OrderByDescending(candidate => candidate.AmountPoints)
                .ThenBy(CandidateKey, StringComparer.Ordinal).FirstOrDefault();
            if (best is not null) priced.Add(best);
        }
        var payouts = priced.Where(payout => payout.Matches.Any(match =>
            match.Match.MatchLength == game.Layout.ReelCount)).ToList();
        var claimed = new HashSet<string>(StringComparer.Ordinal);
        foreach (var candidate in priced.Where(payout => payout.Matches.All(match =>
                     match.Match.MatchLength < game.Layout.ReelCount)).OrderByDescending(payout => payout.Matches.Count)
                 .ThenByDescending(payout => payout.AmountPoints).ThenBy(CandidateKey, StringComparer.Ordinal))
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

    private static long GetBaseMultiplier(IReadOnlyDictionary<(string SymbolId, int MatchLength), long> rules,
        string symbolId, int matchLength)
    {
        for (var length = matchLength; length > 0; length--)
            if (rules.TryGetValue((symbolId, length), out var multiplier)) return multiplier;
        return 0;
    }

    private static string CandidateKey(PaylinePayout payout) => string.Join('|', payout.Matches.Select(match =>
        $"{match.Match.SymbolId}:{match.Match.MatchLength}:{match.Match.Positions[0].Reel}"));
    private static string MatchGeometryKey(SymbolMatch match) =>
        $"{match.MatchLength}:" + string.Join(',', match.Positions.Select(position => $"{position.Reel}.{position.Row}"));
}
