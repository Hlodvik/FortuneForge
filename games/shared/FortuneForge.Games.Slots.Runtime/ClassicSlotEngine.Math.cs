using FortuneForge.Games.Slots;

namespace FortuneForge.Games.Slots.Runtime;

public sealed partial class ClassicSlotEngine
{
    private static readonly HashSet<string> FeatureOnlySymbols = new(StringComparer.Ordinal)
    {
        "BANANA", "PAW", "RAND_05", "RAND_1", "RAND_15", "RAND_2", "RAND_3",
        "RAND_4", "RAND_5", "SEAL_SYNC", "SEAL_ROWS", "SEAL_PAW", "SEAL_RAND"
    };

    private static GameDefinition CreateEffectiveGame(GameDefinition game, string? featureMode)
    {
        if (!SlotProfile.IsFeatureMode(featureMode) ||
            !featureMode!.Split('-', StringSplitOptions.RemoveEmptyEntries).Contains("rows", StringComparer.Ordinal))
            return game;
        var clone = SlotDefinitionCloner.Clone(game);
        return new GameDefinition
        {
            Id = clone.Id,
            Layout = new GameLayoutDefinition
            {
                ReelCount = clone.Layout.ReelCount,
                VisibleRows = checked(clone.Layout.VisibleRows + 2),
                PaylineCount = clone.Layout.PaylineCount
            },
            Symbols = clone.Symbols,
            Matching = clone.Matching,
            Math = clone.Math,
            Wagering = clone.Wagering,
            FreeGames = clone.FreeGames,
            SpecialPoints = clone.SpecialPoints,
            Energy = clone.Energy,
            Paylines = clone.Paylines
        };
    }

    private static ReelSetDefinition CreateBoostedReels(GameDefinition game,
        ReelSetDefinition source)
    {
        var common = game.SpecialPoints?.CommonSymbolIds.ToHashSet(StringComparer.Ordinal)
            ?? throw new InvalidOperationException("The game does not support a special-point boost.");
        return new ReelSetDefinition
        {
            Id = source.Id + "-power-boost",
            SymbolSetId = source.SymbolSetId,
            Reels = source.Reels.Select(strip =>
            {
                var targets = strip.Where(common.Contains).GroupBy(value => value, StringComparer.Ordinal)
                    .ToDictionary(group => group.Key, group => group.Count() / 2, StringComparer.Ordinal);
                var seen = new Dictionary<string, int>(StringComparer.Ordinal);
                var kept = new Dictionary<string, int>(StringComparer.Ordinal);
                return strip.Where(symbol =>
                {
                    if (!common.Contains(symbol)) return true;
                    var ordinal = seen.GetValueOrDefault(symbol);
                    seen[symbol] = ordinal + 1;
                    if (ordinal % 2 != 0 || kept.GetValueOrDefault(symbol) >= targets[symbol]) return false;
                    kept[symbol] = kept.GetValueOrDefault(symbol) + 1;
                    return true;
                }).ToList();
            }).ToList()
        };
    }

    public static IReadOnlyList<PaylineEvaluation> Evaluate(
        IReadOnlyList<IReadOnlyList<string>> reels, GameDefinition game,
        SymbolSetDefinition symbolSet)
    {
        if (reels.Count != game.Layout.ReelCount ||
            reels.Any(reel => reel.Count != game.Layout.VisibleRows))
            throw new ArgumentException("The visible slot grid has the wrong shape.", nameof(reels));
        var wild = game.Symbols.WildSymbolId;
        var free = game.FreeGames?.SymbolId;
        var explicitPaying = game.Symbols.PaylineSymbolIds?.ToHashSet(StringComparer.Ordinal);
        var regular = symbolSet.Symbols.Where(symbol => symbol.Id != wild && symbol.Id != free &&
                (explicitPaying?.Contains(symbol.Id) ?? !FeatureOnlySymbols.Contains(symbol.Id)))
            .Select(symbol => symbol.Id).ToArray();
        var nativeWild = game.Symbols.NativeWildMatchLengths.ToHashSet();
        var substitutes = game.Symbols.WildSubstitutionMatchLengths.ToHashSet();
        return game.Paylines.Select((rows, index) => EvaluatePayline(index + 1, rows,
            reels, game, wild, free, regular, nativeWild, substitutes, explicitPaying)).ToArray();
    }

    private static PaylineEvaluation EvaluatePayline(int paylineId, IReadOnlyList<int> rows,
        IReadOnlyList<IReadOnlyList<string>> reels, GameDefinition game, string wild,
        string? free, IReadOnlyList<string> regular, IReadOnlySet<int> nativeWild,
        IReadOnlySet<int> substitutes, IReadOnlySet<string>? explicitPaying)
    {
        var original = rows.Select((row, reel) => reels[reel][row]).ToArray();
        var assignments = new string[game.Layout.ReelCount];
        var candidates = new Dictionary<string, MatchCandidate>(StringComparer.Ordinal);
        Explore(0);
        return new(paylineId, candidates.Values.ToArray());

        void Explore(int position)
        {
            if (position == assignments.Length) { AddCandidate(); return; }
            if (original[position] != wild)
            {
                assignments[position] = original[position];
                Explore(position + 1);
                return;
            }
            assignments[position] = wild;
            Explore(position + 1);
            foreach (var symbol in regular)
            {
                assignments[position] = symbol;
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
                var native = assignments[start] == wild;
                var isFree = assignments[start] == free;
                var substituted = Enumerable.Range(start, length).Any(reel =>
                    original[reel] == wild && assignments[reel] != wild);
                if (!isFree && (explicitPaying?.Contains(assignments[start]) ?? true) &&
                    length >= game.Matching.MinimumRunLength &&
                    IsAllowedRunShape(rows, start, length, game.Layout.ReelCount) &&
                    (!substituted || substitutes.Contains(length)) &&
                    (!native || nativeWild.Contains(length)))
                {
                    var positions = Enumerable.Range(start, length)
                        .Select(reel => new GridPosition(reel, rows[reel])).ToArray();
                    var wilds = Enumerable.Range(start, length)
                        .Where(reel => original[reel] == wild && assignments[reel] != wild)
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

    private static bool IsAllowedRunShape(IReadOnlyList<int> rows, int start,
        int length, int fullLength)
    {
        if (length == fullLength) return true;
        if (length is not (3 or 4) || start != 0 && start != fullLength - length) return false;
        if (length == 4) return true;
        var first = rows[start + 1] - rows[start];
        var second = rows[start + 2] - rows[start + 1];
        return first == second && Math.Abs(first) <= 1;
    }

    public static SpinPayout CalculatePayout(IReadOnlyList<PaylineEvaluation> evaluations,
        GameDefinition game, PaytableDefinition paytable, long wagerPoints)
    {
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(wagerPoints);
        var rules = paytable.Rules.ToDictionary(rule => (rule.SymbolId, rule.MatchLength),
            rule => rule.Multiplier);
        var priced = new List<PaylinePayout>();
        foreach (var evaluation in evaluations)
        {
            var step = game.Math.PaylinePayoutSteps[evaluation.PaylineId - 1];
            var best = evaluation.Candidates.Select(candidate => PriceCandidate(
                    evaluation.PaylineId, candidate, rules, wagerPoints,
                    game.Layout.ReelCount, step, game.Math.PayoutMultiplier))
                .Where(candidate => candidate.AmountPoints > 0)
                .OrderByDescending(candidate => candidate.AmountPoints)
                .ThenBy(CandidateKey, StringComparer.Ordinal).FirstOrDefault();
            if (best is not null) priced.Add(best);
        }
        var payouts = priced.Where(line => line.Matches.Any(match =>
            match.Match.MatchLength == game.Layout.ReelCount)).ToList();
        var claimed = new HashSet<string>(StringComparer.Ordinal);
        foreach (var candidate in priced.Where(line => line.Matches.All(match =>
                     match.Match.MatchLength < game.Layout.ReelCount))
                 .OrderByDescending(line => line.Matches.Count)
                 .ThenByDescending(line => line.AmountPoints)
                 .ThenBy(CandidateKey, StringComparer.Ordinal))
        {
            var keys = candidate.Matches.Select(match => GeometryKey(match.Match)).ToArray();
            if (keys.Any(claimed.Contains)) continue;
            payouts.Add(candidate);
            foreach (var key in keys) claimed.Add(key);
        }
        return new(payouts.Sum(line => line.AmountPoints), payouts);
    }

    private static PaylinePayout PriceCandidate(int paylineId, MatchCandidate candidate,
        IReadOnlyDictionary<(string SymbolId, int MatchLength), long> rules,
        long wagerPoints, int fullLength, int paylineStep, decimal payoutMultiplier)
    {
        var paid = candidate.Matches.Select(match =>
        {
            var baseMultiplier = GetBaseMultiplier(rules, match.SymbolId, match.MatchLength);
            var multiplier = match.MatchLength == fullLength && baseMultiplier > 0
                ? checked(baseMultiplier + paylineStep) : baseMultiplier;
            var amount = Scale(wagerPoints, multiplier, payoutMultiplier);
            return new PaidMatch(match, multiplier, amount);
        }).Where(match => match.AmountPoints > 0).ToArray();
        return new(paylineId, paid.Sum(match => match.AmountPoints), paid);
    }

    private static long GetBaseMultiplier(
        IReadOnlyDictionary<(string SymbolId, int MatchLength), long> rules,
        string symbolId, int matchLength)
    {
        for (var length = matchLength; length > 0; length--)
            if (rules.TryGetValue((symbolId, length), out var value)) return value;
        return 0;
    }

    private static bool HasPayingFullMatch(IReadOnlyList<PaylineEvaluation> evaluations,
        GameDefinition game, PaytableDefinition paytable) => evaluations.Any(evaluation =>
        evaluation.Candidates.Any(candidate => candidate.Matches.Any(match =>
            match.MatchLength == game.Layout.ReelCount && paytable.Rules.Any(rule =>
                rule.SymbolId == match.SymbolId && rule.MatchLength == match.MatchLength &&
                rule.Multiplier > 0))));

    private static int CalculateSpecialPoints(IReadOnlyList<PaylineEvaluation> evaluations,
        GameDefinition game)
    {
        var rules = game.SpecialPoints;
        if (rules is null) return 0;
        return evaluations.SelectMany(value => value.Candidates).SelectMany(value => value.Matches)
            .Where(match => match.SymbolId == rules.SymbolId && match.WildPositions.Count == 0 &&
                (match.MatchLength == 3 || match.MatchLength == game.Layout.ReelCount))
            .GroupBy(GeometryKey, StringComparer.Ordinal).Select(group => group.First())
            .Sum(match => match.MatchLength == game.Layout.ReelCount
                ? rules.FiveMatchPoints : rules.ThreeMatchPoints);
    }

    private static string CandidateKey(PaylinePayout payout) => string.Join('|',
        payout.Matches.Select(match =>
            $"{match.Match.SymbolId}:{match.Match.MatchLength}:{match.Match.Positions[0].Reel}"));
    private static string GeometryKey(SymbolMatch match) => $"{match.MatchLength}:" +
        string.Join(',', match.Positions.Select(position => $"{position.Reel}.{position.Row}"));
}
