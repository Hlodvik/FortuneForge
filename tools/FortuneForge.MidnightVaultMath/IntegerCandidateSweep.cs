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
        var basis = table.Rules.GroupBy(rule => rule.SymbolId).ToDictionary(group => group.Key,
            group => (Three: group.Single(rule => rule.MatchLength == 3).Multiplier,
                Five: group.Single(rule => rule.MatchLength == 5).Multiplier), StringComparer.Ordinal);
        // Predeclared search: the original integer table (J-five2), plus each
        // single five-match rule shifted by +/-1 or +/-2. No reel/feature/RNG or
        // positive-paying support changes. A selected table needs a holdout run.
        basis["3"] = (basis["3"].Three, 2);
        // Preserve the original predeclared lattice after production selects
        // A-five7. Historical calibration candidates must remain identical.
        basis["5"] = (basis["5"].Three, 6);
        var list = new List<Candidate> { Create("integer-base-j2", basis, table) };
        foreach (var id in basis.Keys.Order(StringComparer.Ordinal))
        foreach (var delta in new[] { -2, -1, 1, 2 })
        {
            var value = basis[id].Five + delta;
            if (value is < 1 or > 18) continue;
            var rates = basis.ToDictionary(pair => pair.Key, pair => pair.Value, StringComparer.Ordinal);
            rates[id] = (rates[id].Three, value);
            list.Add(Create($"{id}-five-{value}", rates, table));
        }
        candidates = list.ToArray();
    }

    private static Candidate Create(string id, IReadOnlyDictionary<string, (long Three, long Five)> rates, PaytableDefinition source) =>
        new(id, rates, new PaytableDefinition
        {
            Id = source.Id, SymbolSetId = source.SymbolSetId,
            Rules = source.Rules.Select(rule => new PayoutRule { SymbolId = rule.SymbolId, MatchLength = rule.MatchLength,
                Multiplier = rule.MatchLength == 3 ? rates[rule.SymbolId].Three : rates[rule.SymbolId].Five }).ToList()
        });

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

    public object Report()
    {
        if (cycles != expectedCycles || pendingSpin) throw new InvalidOperationException("Candidate sweep did not finish every complete cycle.");
        var digest = Convert.ToHexStringLower(observed.GetHashAndReset());
        observed.Dispose();
        return new
        {
            method = "All complete evaluator alternatives repriced in memory with production calculator and independent integer/geometry oracle; unchanged positive support, zero steps, scalar1. Feature amounts copied exactly from the same complete seeded queue stream. No selected-match histogram approximation.",
            selectionRule = "Predeclared original integer table plus one five-match rate shifted +/-1 or +/-2, bounded1..18. Select using multiple calibration seeds and minimum changed rules; independently validate the final source on a holdout seed. Search intervals are descriptive, not multiplicity-adjusted.",
            completeEvaluationStreamSha256 = digest, evaluatedSpins = calls, completeCycles = cycles,
            candidates = candidates.Select(candidate =>
            {
                var mean = candidate.BatchRtps.Average();
                var standardError = candidate.BatchRtps.Count > 1 ? Math.Sqrt(candidate.BatchRtps.Sum(value => (value - mean) * (value - mean)) /
                    (candidate.BatchRtps.Count - 1) / candidate.BatchRtps.Count) : 0;
                return new
                {
                    candidate.Id, rates = candidate.Table.Rules, candidate.TotalPayout,
                    rtp = candidate.TotalPayout / (double)paidWager, hitRate = candidate.Hits / (double)calls,
                    approximate95PercentBatchInterval = new[] { mean - 2 * standardError, mean + 2 * standardError },
                    cycleReturnStdDev = Math.Sqrt(Math.Max(0, candidate.ReturnSquares / cycles - Math.Pow(candidate.ReturnSum / cycles, 2))),
                    candidate.MaxCyclePayout, zeroCycleRate = candidate.ZeroCycles / (double)cycles,
                    candidate.ChangedNativeSelections
                };
            }).ToArray()
        };
    }

    private sealed class Candidate(string id, IReadOnlyDictionary<string, (long Three, long Five)> rates, PaytableDefinition table)
    {
        public string Id { get; } = id;
        public IReadOnlyDictionary<string, (long Three, long Five)> Rates { get; } = rates;
        public PaytableDefinition Table { get; } = table;
        public long LastNative, CyclePayout, TotalPayout, BatchPayout, Hits, ChangedNativeSelections, MaxCyclePayout, ZeroCycles;
        public double ReturnSum, ReturnSquares;
        public List<double> BatchRtps { get; } = [];
    }
}
