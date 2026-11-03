using System.Security.Cryptography;
using System.Text;
using FortuneForge.MidnightAudit;
using FortuneForge.Server.Slots.Configuration;
using FortuneForge.Server.Slots.Models;
using FortuneForge.Server.Slots.Payouts;

// Calibration only. Every evaluator alternative is repriced before geometry
// deduplication; a histogram of already selected wins would not be sufficient.
internal sealed class IntegerCandidateSweep : IPayoutCalculator
{
    private readonly PayoutCalculator calculator = new();
    private readonly Candidate[] candidates;
    private readonly IncrementalHash observed = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
    private readonly int batchSize;
    private readonly int expectedCycles;
    private long actualNative;
    private long calls;
    private long cycles;
    private long paidWager;
    private long currentBatchWager;
    private bool pendingSpin;

    public IntegerCandidateSweep(ISlotsDefinitionProvider provider, int cycleCount)
    {
        var game = provider.GetGame(Definitions.GameId)!;
        var table = provider.GetPaytable(game.Math.PaytableId)!;
        if (game.Math.PayoutMultiplier != 1 || table.Id != MidnightVaultSlotDefinitions.PaytableId ||
            game.Math.PaylinePayoutSteps.Any(value => value != 0) || table.Rules.Any(rule => rule.Multiplier <= 0))
            throw new InvalidOperationException("Integer search requires scalar1, zero steps and unchanged positive native support.");
        expectedCycles = cycleCount;
        batchSize = Math.Max(1, cycleCount / Math.Min(50, cycleCount));
        var basis = provider.GetPaytable(MidnightVaultSlotDefinitions.PreviousPaytableId)
            ?? throw new InvalidOperationException("Frozen v2 calibration basis is missing.");
        var list = new Dictionary<string, Candidate>(StringComparer.Ordinal);
        // Approved, predeclared factor family: round each frozen v2 native rate
        // once, positive AwayFromZero, then floor at1. Deduplicate whole tables.
        // Feature777 remains3x and is added outside this native calculator.
        for (var numerator = 25; numerator <= 75; numerator++)
        {
            var factor = numerator / 100m;
            var candidateTable = new PaytableDefinition
            {
                Id = table.Id, SymbolSetId = table.SymbolSetId,
                Rules = basis.Rules.Select(rule => new PayoutRule { SymbolId = rule.SymbolId, MatchLength = rule.MatchLength,
                    Multiplier = Math.Max(1, checked((long)Math.Round(rule.Multiplier * factor, MidpointRounding.AwayFromZero))) }).ToList()
            };
            var key = string.Join('|', candidateTable.Rules.OrderBy(rule => rule.SymbolId, StringComparer.Ordinal).ThenBy(rule => rule.MatchLength)
                .Select(rule => $"{rule.SymbolId}:{rule.MatchLength}:{rule.Multiplier}"));
            if (list.TryGetValue(key, out var equivalent)) { equivalent.Factors.Add(factor); continue; }
            var rates = candidateTable.Rules.GroupBy(rule => rule.SymbolId).ToDictionary(group => group.Key,
                group => (Three: group.Single(rule => rule.MatchLength == 3).Multiplier,
                    Five: group.Single(rule => rule.MatchLength == 5).Multiplier), StringComparer.Ordinal);
            var changed = candidateTable.Rules.Count(rule => basis.Rules.Single(original =>
                original.SymbolId == rule.SymbolId && original.MatchLength == rule.MatchLength).Multiplier != rule.Multiplier);
            list.Add(key, new Candidate($"native-f{numerator:000}", rates, candidateTable, changed, key, factor));
        }
        candidates = list.Values.OrderBy(candidate => candidate.Id, StringComparer.Ordinal).ToArray();
    }

    public void BeginCycle()
    {
        if (pendingSpin) throw new InvalidOperationException("Unrecorded calibration spin.");
        foreach (var candidate in candidates) candidate.CyclePayout = 0;
    }

    public SpinPayout Calculate(IReadOnlyList<PaylineEvaluation> evaluations, GameDefinition game, PaytableDefinition paytable, long wagerPoints)
    {
        if (pendingSpin) throw new InvalidOperationException("Native calculation was not followed by its completed spin.");
        if (game.Math.PayoutMultiplier != 1 || paytable.Id != MidnightVaultSlotDefinitions.PaytableId)
            throw new InvalidOperationException("A calibration stream unexpectedly entered legacy/fractional math.");
        var actual = calculator.Calculate(evaluations, game, paytable, wagerPoints);
        actualNative = actual.TotalPoints; calls++; pendingSpin = true;
        // The digest identifies the complete observed alternatives, including
        // losing candidates and shared short-match geometry, in stream order.
        var text = new StringBuilder().Append(game.Layout.VisibleRows).Append('/').Append(wagerPoints);
        foreach (var line in evaluations)
        {
            text.Append('|').Append(line.PaylineId);
            foreach (var alternative in line.Candidates)
            {
                text.Append('[');
                foreach (var match in alternative.Matches)
                    text.Append(IntegerPayoutOracle.MatchKey(line.PaylineId, match, 0)).Append(';');
                text.Append(']');
            }
        }
        observed.AppendData(Encoding.UTF8.GetBytes(text.Append('\n').ToString()));
        var actualKeys = SelectionKeys(actual);
        foreach (var candidate in candidates)
        {
            var oracle = IntegerPayoutOracle.Native(evaluations, candidate.Rates);
            // Repricing at one native point avoids reference-wager rounding.
            var priced = calculator.Calculate(evaluations, game, candidate.Table, 1);
            if (priced.TotalPoints != oracle.Coefficient ||
                !IntegerPayoutOracle.EngineNativeKeys(priced).SequenceEqual(IntegerPayoutOracle.OracleKeys(oracle)))
                throw new InvalidOperationException("Candidate pricing disagreed with independent integer/geometry oracle: " + candidate.Id);
            candidate.LastNative = checked(oracle.Coefficient * wagerPoints);
            if (!actualKeys.SequenceEqual(SelectionKeys(priced))) candidate.ChangedNativeSelections++;
        }
        return actual;
    }

    private static string[] SelectionKeys(SpinPayout payout) => payout.Paylines.SelectMany(line => line.Matches
        .Select(match => IntegerPayoutOracle.MatchKey(line.PaylineId, match.Match, 0))).Order(StringComparer.Ordinal).ToArray();

    public void CompleteSpin(SpinResult result)
    {
        if (!pendingSpin) throw new InvalidOperationException("No native candidate record for completed spin.");
        var feature = checked(result.Payout.TotalPoints - actualNative);
        if (feature != result.BananaBonusPoints || result.EnergyAwarded != 0 || result.MoneyGrabPoints != 0)
            throw new InvalidOperationException("Candidate replay encountered an unaccounted payout path.");
        foreach (var candidate in candidates)
        {
            var amount = checked(candidate.LastNative + feature);
            candidate.CyclePayout = checked(candidate.CyclePayout + amount);
            if (amount > 0) candidate.Hits++;
        }
        pendingSpin = false;
    }

    public void CompleteCycle(long wager)
    {
        if (pendingSpin) throw new InvalidOperationException("Unrecorded final spin.");
        cycles++; paidWager = checked(paidWager + wager); currentBatchWager = checked(currentBatchWager + wager);
        foreach (var candidate in candidates)
        {
            candidate.TotalPayout = checked(candidate.TotalPayout + candidate.CyclePayout);
            candidate.BatchPayout = checked(candidate.BatchPayout + candidate.CyclePayout);
            var ratio = candidate.CyclePayout / (double)wager;
            candidate.ReturnSum += ratio; candidate.ReturnSquares += ratio * ratio;
            candidate.MaxCyclePayout = Math.Max(candidate.MaxCyclePayout, candidate.CyclePayout);
            if (candidate.CyclePayout == 0) candidate.ZeroCycles++;
        }
        if (cycles % batchSize != 0 && cycles != expectedCycles) return;
        foreach (var candidate in candidates)
        {
            candidate.BatchRtps.Add(candidate.BatchPayout / (double)currentBatchWager);
            candidate.BatchPayout = 0;
        }
        currentBatchWager = 0;
    }

    public IntegerSearchReport Report()
    {
        if (cycles != expectedCycles || pendingSpin) throw new InvalidOperationException("Candidate sweep did not finish every complete cycle.");
        var digest = Convert.ToHexStringLower(observed.GetHashAndReset());
        observed.Dispose();
        return new IntegerSearchReport(
            "All complete evaluator alternatives repriced in memory with production calculator and independent integer/geometry oracle; unchanged positive support, zero steps, scalar1. Feature777 amounts copied exactly from the same complete seeded queue stream. No selected-match histogram approximation.",
            "Frozen v2 native rates *factor0.25..0.75 step0.01, integer AwayFromZero once, floor1; identical tables deduplicated. Select after both predeclared calibration seeds by nearest paid-amount-pooled90%, then fewer changed native rules vs v2, then canonical ID ordinal. Require pooled88..92%; no family widening. Search intervals descriptive, not multiplicity-adjusted.",
            digest, calls, cycles, candidates.Select(candidate =>
            {
                var mean = candidate.BatchRtps.Average();
                var standardError = candidate.BatchRtps.Count > 1 ? Math.Sqrt(candidate.BatchRtps.Sum(value => (value - mean) * (value - mean)) /
                    (candidate.BatchRtps.Count - 1) / candidate.BatchRtps.Count) : 0;
                return new IntegerCandidateResult(candidate.Id, candidate.Factors[0], candidate.Factors.ToArray(),
                    candidate.ChangedNativeRules, candidate.CanonicalTable, candidate.Table.Rules, candidate.TotalPayout,
                    candidate.TotalPayout / (double)paidWager, candidate.Hits / (double)calls,
                    [mean - 2 * standardError, mean + 2 * standardError],
                    Math.Sqrt(Math.Max(0, candidate.ReturnSquares / cycles - Math.Pow(candidate.ReturnSum / cycles, 2))),
                    candidate.MaxCyclePayout, candidate.ZeroCycles / (double)cycles, candidate.ChangedNativeSelections);
            }).ToArray());
    }

    private sealed class Candidate(string id, IReadOnlyDictionary<string, (long Three, long Five)> rates, PaytableDefinition table,
        int changedNativeRules, string canonicalTable, decimal factor)
    {
        public string Id { get; } = id;
        public IReadOnlyDictionary<string, (long Three, long Five)> Rates { get; } = rates;
        public PaytableDefinition Table { get; } = table;
        public int ChangedNativeRules { get; } = changedNativeRules;
        public string CanonicalTable { get; } = canonicalTable;
        public List<decimal> Factors { get; } = [factor];
        public long LastNative, CyclePayout, TotalPayout, BatchPayout, Hits, ChangedNativeSelections, MaxCyclePayout, ZeroCycles;
        public double ReturnSum, ReturnSquares;
        public List<double> BatchRtps { get; } = [];
    }
}
