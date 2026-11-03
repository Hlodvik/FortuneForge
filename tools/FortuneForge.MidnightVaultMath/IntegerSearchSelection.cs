using FortuneForge.Server.Slots.Models;

internal sealed record IntegerCandidateResult(string Id, decimal RepresentativeFactor, decimal[] EquivalentFactors,
    int ChangedNativeRules, string CanonicalTable, IReadOnlyList<PayoutRule> Rates, long TotalPayout,
    double Rtp, double HitRate, double[] Approximate95PercentBatchInterval, double CycleReturnStdDev,
    long MaxCyclePayout, double ZeroCycleRate, long ChangedNativeSelections);

internal sealed record IntegerSearchReport(string Method, string SelectionRule, string CompleteEvaluationStreamSha256,
    long EvaluatedSpins, long CompleteCycles, IntegerCandidateResult[] Candidates);

internal sealed record IntegerCandidateSeedResult(int Seed, int PaidCycles, long PaidWager, long TotalPayout,
    double Rtp, double HitRate, double[] Approximate95PercentBatchInterval, long ChangedNativeSelections);

internal sealed record PooledIntegerCandidate(string Id, decimal RepresentativeFactor, decimal[] EquivalentFactors,
    int ChangedNativeRules, string CanonicalTable, IReadOnlyList<PayoutRule> Rates, long PaidWager, long TotalPayout,
    double Rtp, double[] Approximate95PercentIndependentBatchInterval, bool PooledRtpInsidePolicy,
    IntegerCandidateSeedResult[] PerSeed)
{
    public decimal DistanceFrom90 => Math.Abs((decimal)TotalPayout / PaidWager - .9m);
}

internal static class IntegerSearchSelection
{
    public static object Create(IReadOnlyList<CalibrationMeasurement> measurements)
    {
        if (measurements.Count == 0 || measurements.Any(measurement => measurement.CandidateSearch is null))
            throw new InvalidOperationException("Complete native candidate records are required for pooling.");
        var approvedWindow = measurements.Count == 2 && measurements.Select(value => value.Seed).Order().SequenceEqual(new[] { 20261112, 20261113 }) &&
            measurements.All(value => value.PaidCycles == 50000 && value.WagerSchedule.SequenceEqual(new long[] { 100 }) && value.AutoBoost);
        var pooled = measurements[0].CandidateSearch!.Candidates.Select(candidate =>
        {
            var samples = measurements.Select(measurement =>
            {
                var current = measurement.CandidateSearch!.Candidates.Single(value => value.Id == candidate.Id);
                if (current.CanonicalTable != candidate.CanonicalTable)
                    throw new InvalidOperationException("Candidate tables changed between calibration seeds.");
                return new IntegerCandidateSeedResult(measurement.Seed, measurement.PaidCycles, measurement.PaidWager,
                    current.TotalPayout, current.Rtp, current.HitRate, current.Approximate95PercentBatchInterval, current.ChangedNativeSelections);
            }).ToArray();
            var paid = samples.Sum(sample => sample.PaidWager);
            var payout = samples.Sum(sample => sample.TotalPayout);
            var rtp = payout / (double)paid;
            var variance = samples.Sum(sample =>
            {
                var weight = sample.PaidWager / (double)paid;
                var standardError = (sample.Approximate95PercentBatchInterval[1] - sample.Approximate95PercentBatchInterval[0]) / 4;
                return weight * weight * standardError * standardError;
            });
            var half = 2 * Math.Sqrt(variance);
            var exactRtp = (decimal)payout / paid;
            return new PooledIntegerCandidate(candidate.Id, candidate.RepresentativeFactor, candidate.EquivalentFactors,
                candidate.ChangedNativeRules, candidate.CanonicalTable, candidate.Rates, paid, payout, rtp,
                [rtp - half, rtp + half], exactRtp >= .88m && exactRtp <= .92m, samples);
        }).OrderBy(candidate => candidate.DistanceFrom90).ThenBy(candidate => candidate.ChangedNativeRules)
            .ThenBy(candidate => candidate.Id, StringComparer.Ordinal).ToArray();
        var selected = approvedWindow ? pooled.FirstOrDefault(candidate => candidate.PooledRtpInsidePolicy) : null;
        return new
        {
            predeclaredCalibration = new { seeds = new[] { 20261112, 20261113 }, cyclesPerSeed = 50000, wagerPoints = 100, autoBoost = true },
            family = "Native v2 rates *decimal factors0.25..0.75 inclusive step0.01, integer AwayFromZero once, floor1; identical full tables deduplicated;777 remains3x",
            selectionRule = "Nearest paid-amount-weighted pooled90%, ties fewer changed native rules versus frozenv2, then canonical candidate ID ordinal; pooled RTP must be88..92%; all candidates retained; no widening",
            uncertainty = "Approximate independent per-seed batch standard errors combined with paid-amount weights; descriptive and not adjusted for candidate search. Final validation uses independent holdout seeds.",
            approvedCalibrationWindowComplete = approvedWindow,
            selectionStatus = !approvedWindow ? "Not selected: both predeclared complete calibration runs are required" :
                selected is null ? "No candidate qualifies; report before widening" : "Candidate selected for owner integration and independent final-source validation",
            selected, candidates = pooled
        };
    }
}
