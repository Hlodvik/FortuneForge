using System.Text.Json;
using FortuneForge.Games.RobotRevolution;
using FortuneForge.Games.Slots;

internal static class SunstoneEclipseSimulation
{
    public static void Run(int paidCycles, int seed, decimal releaseRtpCeiling, string[] args)
    {
        var newOnly = args.Contains("--sunstone-new-only", StringComparer.Ordinal);
        var wagerOption = args.SingleOrDefault(argument => argument.StartsWith("--sunstone-wagers=", StringComparison.Ordinal));
        var wagers = wagerOption is null ? new long[] { 100 } : wagerOption["--sunstone-wagers=".Length..]
            .Split(',').Select(value => long.TryParse(value, out var wager) ? wager : throw new ArgumentException("Sunstone wagers must be integer points.")).ToArray();
        if (wagers.Length == 0 || wagers.Distinct().Count() != wagers.Length) throw new ArgumentException("Sunstone wagers must be a nonempty distinct list.");
        foreach (var wager in wagers) RobotRevolutionEngine.ValidateWager(wager, false);
        var policies = new[] { false, true };
        var results = new List<Sample>();
        foreach (var wager in wagers)
        foreach (var spendBoosts in policies)
        {
            if (!newOnly) results.Add(Simulate(RobotRevolutionEngine.LegacyEngineVersion, paidCycles, seed, spendBoosts, wager));
            results.Add(Simulate(RobotRevolutionEngine.EngineVersion, paidCycles, seed, spendBoosts, wager));
        }
        var newResults = results.Where(sample => sample.EngineVersion == RobotRevolutionEngine.EngineVersion).ToArray();
        var withinCeiling = newResults.All(sample => sample.EstimatedReturn <= releaseRtpCeiling);
        var result = new
        {
            mathVersion = SunstoneEclipse.MathVersion,
            seed,
            paidCycles,
            wagerPoints = wagers,
            pointValueInCents = RobotRevolutionEngine.WagerPointValueInCents,
            newBasePayoutMultiplier = RobotRevolutionEngine.Profile.PayoutMultiplier,
            historicalBasePayoutMultiplier = RobotRevolutionEngine.LegacyProfile.PayoutMultiplier,
            releaseRtpCeiling,
            observedWithinCeiling = withinCeiling,
            confidenceIntervalMethod = "Approximate normal 95% interval over contiguous batch means (up to 1000 paid cycles per batch), with all earned free spins and retriggers assigned to their triggering paid cycle; sequential pity/points state retained.",
            qualification = "Deterministic Monte Carlo qualification, not certified RTP. Two policies: never use a boost, and spend a boost as soon as eight earned special points are available. No deposits, extra paid plays or side wagers are simulated.",
            samples = results
        };
        var json = JsonSerializer.Serialize(result, new JsonSerializerOptions { WriteIndented = true, PropertyNamingPolicy = JsonNamingPolicy.CamelCase });
        Console.WriteLine(json);
        var output = args.SingleOrDefault(argument => argument.StartsWith("--sunstone-output=", StringComparison.Ordinal));
        if (output is not null)
        {
            var path = Path.GetFullPath(output["--sunstone-output=".Length..]);
            Directory.CreateDirectory(Path.GetDirectoryName(path)!);
            File.WriteAllText(path, json);
        }
        if (!withinCeiling) Environment.ExitCode = 1;
    }

    private static Sample Simulate(string version, int paidCycles, int seed, bool spendBoosts, long wager)
    {
        var engine = new RobotRevolutionEngine(new SeededSource(seed), version);
        var points = 0;
        var pity = 0;
        var spins = 0;
        var freeSpins = 0;
        var boostedSpins = 0;
        var featureHits = 0;
        var eclipseHits = 0;
        var totalPayout = 0L;
        var totalFeature = 0L;
        var maxFeature = 0m;
        var batchSize = Math.Min(1000, Math.Max(1, paidCycles / 100));
        var batchReturns = new List<double>();
        var batchPayout = 0L;
        var batchCycles = 0;
        var boostCost = RobotRevolutionEngine.CreateGame().SpecialPoints!.ActivationCost;
        for (var cycle = 0; cycle < paidCycles; cycle++)
        {
            var pending = 0;
            var cyclePayout = 0L;
            var first = true;
            do
            {
                var free = !first;
                first = false;
                if (free) { pending--; freeSpins++; }
                var boost = spendBoosts && points >= boostCost;
                if (boost) { points -= boostCost; boostedSpins++; }
                var spin = engine.Spin(wager, "sunstone-math", specialBoostApplied: boost,
                    featureMode: free ? version == RobotRevolutionEngine.LegacyEngineVersion
                        ? RobotRevolutionEngine.LegacyProfile.ScatterFeatureMode : RobotRevolutionEngine.Profile.ScatterFeatureMode
                        : null, persistedPityMisses: pity, isFreeSpin: free);
                pity = spin.ConsecutiveFiveMisses;
                points = checked(points + spin.SpecialPointsAwarded);
                pending = checked(pending + spin.FreeSpinsAwarded);
                spins++;
                if (spins > paidCycles * 20L) throw new InvalidOperationException("Sunstone free-spin qualification exceeded its continuation limit.");
                cyclePayout = checked(cyclePayout + spin.Payout.TotalPoints);
                if (version == RobotRevolutionEngine.LegacyEngineVersion) continue;
                var feature = SunstoneEclipse.Resolve(spin.Reels, wager, free);
                if (feature.TotalAwardPoints != spin.Payout.Paylines.Where(line => line.PaylineId is >= 1001 and <= 1010).Sum(line => line.AmountPoints) ||
                    feature.TotalAwardPoints > wager * feature.MaxAwardMultiplier)
                    throw new InvalidOperationException("The native Sunwheel award differs from its settled payout or exceeds its cap.");
                totalFeature = checked(totalFeature + feature.TotalAwardPoints);
                if (feature.TotalAwardPoints > 0) featureHits++;
                if (feature.EclipseTriggered) eclipseHits++;
                maxFeature = Math.Max(maxFeature, feature.TotalAwardPoints / (decimal)wager);
            } while (pending > 0);
            totalPayout = checked(totalPayout + cyclePayout);
            batchPayout = checked(batchPayout + cyclePayout);
            batchCycles++;
            if (batchCycles == batchSize || cycle == paidCycles - 1)
            {
                batchReturns.Add(batchPayout / (double)(batchCycles * wager));
                batchPayout = 0;
                batchCycles = 0;
            }
        }
        var mean = totalPayout / (double)(paidCycles * wager);
        var variance = batchReturns.Sum(value => (value - mean) * (value - mean)) / Math.Max(1, batchReturns.Count - 1);
        var interval = 1.96 * Math.Sqrt(variance / Math.Max(1, batchReturns.Count));
        return new(version, spendBoosts ? "spend-earned-boosts-as-available" : "never-use-boosts", wager, paidCycles, spins,
            freeSpins, boostedSpins, (long)paidCycles * wager, totalPayout, totalPayout / (decimal)(paidCycles * wager),
            (decimal)(mean - interval), (decimal)(mean + interval), totalFeature, totalFeature / (decimal)(paidCycles * wager),
            featureHits, featureHits / (decimal)spins, eclipseHits, maxFeature);
    }

    private sealed record Sample(string EngineVersion, string BoostPolicy, long WagerPoints, int PaidCycles, int TotalSpins,
        int FreeSpins, int BoostedSpins, long PaidWagerPoints, long PayoutPoints, decimal EstimatedReturn,
        decimal Confidence95Low, decimal Confidence95High, long FeatureAwardPoints, decimal FeatureReturn,
        int FeatureHitSpins, decimal FeatureHitRate, int EclipseSpins, decimal HighestFeatureMultiplier);

    private sealed class SeededSource(int seed) : IGameRandomSource
    {
        private readonly Random random = new(seed);
        public int Next(int maximumExclusive) => random.Next(maximumExclusive);
    }
}
