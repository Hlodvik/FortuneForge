using FortuneForge.Games.Slots.Runtime;
using Xunit;

namespace FortuneForge.Games.RobotRevolution.Tests;

public sealed class FifthSunTotalityMathTests
{
    [Fact]
    public void QualifyPaidEclipseModes()
    {
        var modeSpins = ReadCount("FIFTH_SUN_TOTALITY_MODE_SPINS", 20_000);
        const int seed = 7_629_611;
        var samples = new List<Measurement>();
        foreach (var wager in new long[] { 12, 300 })
            foreach (var mode in new[] { FifthSunTotalityDefinitions.SolarMode, FifthSunTotalityDefinitions.LunarMode })
                samples.Add(MeasureMode(wager, mode, modeSpins, seed));
        WriteMeasurements($"totality-mode-math-{modeSpins}.json", seed, samples);
        foreach (var sample in samples)
        {
            Assert.InRange(sample.EstimatedReturn, 2.05, 2.25);
            Assert.True(sample.Approximate95Lower > 2.0,
                $"{sample.Mode} at wager {sample.WagerPoints} must qualify above 200%, including its lower confidence bound.");
            Assert.InRange(sample.Approximate95Upper - sample.Approximate95Lower, 0, 0.20);
        }
    }

    [Fact]
    public void QualifyCompletePaidCycles()
    {
        var baseCycles = ReadCount("FIFTH_SUN_TOTALITY_BASE_CYCLES", 50_000);
        const int seed = 7_629_612;
        var samples = new List<Measurement>();
        foreach (var wager in new long[] { 12, 300 })
            foreach (var useBoosts in new[] { false, true })
                samples.Add(MeasureCycles(wager, useBoosts, baseCycles, seed));
        WriteMeasurements($"totality-cycle-math-{baseCycles}.json", seed, samples);
        foreach (var sample in samples) Assert.InRange(sample.Approximate95Upper, 0, 0.95);
    }

    private static void WriteMeasurements(string name, int seed, IReadOnlyList<Measurement> samples) =>
        FifthSunTests.WriteArtifact(name, new
        {
            engineVersion = RobotRevolutionEngine.EngineVersion,
            ordinaryPayoutMultiplier = FifthSunTotalityDefinitions.OrdinaryPayoutMultiplier,
            eclipsePayoutMultiplier = FifthSunTotalityDefinitions.EclipsePayoutMultiplier,
            eclipseWheelScale = FifthSunTotalityDefinitions.EclipseWheelScale,
            totalityAwardMultiplier = FifthSunTotalityDefinitions.TotalityAwardMultiplier,
            eclipseRounds = FifthSunTotalityDefinitions.EclipseRounds,
            seed,
            qualification = "New native paid Eclipse modes and complete paid cycles only. All mode rounds count in wager turnover. Ordinary pity and earned boost balance continue across mode boundaries. Modes reject boosts and award no free games. Approximate 95% normal batch-mean intervals are local qualification evidence, not a certified theoretical RTP. Historical math suites were not repeated.",
            samples
        });

    private static Measurement MeasureMode(long wager, string mode, int spins, int seed)
    {
        var engine = new RobotRevolutionEngine(new FifthSunTests.StableRandom(seed));
        var statistics = new Statistics(wager);
        var batchPayout = 0L;
        var batchSize = Math.Max(1, spins / 100);
        for (var index = 0; index < spins; index++)
        {
            var outcome = engine.Spin(wager, "totality-mode-qualification", featureMode: mode,
                persistedPityMisses: 0, isFreeSpin: false);
            CheckOutcome(outcome);
            Assert.Equal(mode, outcome.Audit.FeatureMode);
            Assert.DoesNotContain(outcome.Reels.SelectMany(reel => reel), IsEclipse);
            statistics.Add(outcome, true);
            batchPayout += outcome.Payout.TotalPoints;
            if ((index + 1) % batchSize == 0)
            {
                statistics.Batch(batchPayout, batchSize);
                batchPayout = 0;
            }
        }
        if (spins % batchSize > 0) statistics.Batch(batchPayout, spins % batchSize);
        return statistics.Measure(mode, false, spins);
    }

    private static Measurement MeasureCycles(long wager, bool useBoosts, int cycles, int seed)
    {
        var engine = new RobotRevolutionEngine(new FifthSunTests.StableRandom(seed));
        var statistics = new Statistics(wager);
        var pity = 0;
        var points = 0;
        var boostCost = RobotRevolutionEngine.CreateGame().SpecialPoints!.ActivationCost;
        var batchPayout = 0L;
        var batchSpins = 0;
        var batchSize = Math.Max(1, cycles / 100);
        for (var cycle = 0; cycle < cycles; cycle++)
        {
            var boost = useBoosts && points >= boostCost;
            if (boost) { points -= boostCost; statistics.BoostedSpins++; }
            var ordinary = engine.Spin(wager, "totality-cycle-qualification", boost,
                persistedPityMisses: pity, isFreeSpin: false);
            Accept(ordinary, false);
            var eclipses = ordinary.Reels.SelectMany((reel, index) => reel.Where(IsEclipse)
                .Select(symbol => (Reel: index, Symbol: symbol))).ToArray();
            Assert.InRange(eclipses.Length, 0, 1);
            Assert.All(eclipses, symbol => Assert.Equal(2, symbol.Reel));
            var mode = FifthSunTotalityDefinitions.TriggerMode(ordinary.Reels);
            if (mode is not null)
            {
                statistics.Triggers++;
                for (var remaining = FifthSunTotalityDefinitions.EclipseRounds; remaining > 0; remaining--)
                    Accept(engine.Spin(wager, "totality-cycle-qualification", featureMode: mode,
                        persistedPityMisses: pity, isFreeSpin: false), true);
            }
            if ((cycle + 1) % batchSize == 0)
            {
                statistics.Batch(batchPayout, batchSpins);
                batchPayout = 0;
                batchSpins = 0;
            }
        }
        if (batchSpins > 0) statistics.Batch(batchPayout, batchSpins);
        return statistics.Measure(null, useBoosts, cycles);

        void Accept(ClassicSlotSpinOutcome outcome, bool eclipse)
        {
            CheckOutcome(outcome);
            pity = outcome.ConsecutiveFiveMisses;
            points = checked(points + outcome.SpecialPointsAwarded);
            statistics.Add(outcome, eclipse);
            batchPayout += outcome.Payout.TotalPoints;
            batchSpins++;
        }
    }

    private static void CheckOutcome(ClassicSlotSpinOutcome outcome)
    {
        Assert.Equal(RobotRevolutionEngine.EngineVersion, outcome.Audit.EngineVersion);
        Assert.False(outcome.IsFreeSpin);
        Assert.Equal(0, outcome.FreeSpinsAwarded);
        Assert.Equal(5, outcome.Reels.Count);
        Assert.All(outcome.Reels, reel => Assert.Equal(4, reel.Count));
        Assert.DoesNotContain(outcome.Reels.SelectMany(reel => reel), symbol =>
            symbol is "ECLIPSE" or "FREE" or "POWER" or "ACE" or "BANANA");
    }

    private static bool IsEclipse(string symbol) =>
        symbol is FifthSunTotalityDefinitions.SolarSymbol or FifthSunTotalityDefinitions.LunarSymbol;

    private static int ReadCount(string variable, int fallback) =>
        int.TryParse(Environment.GetEnvironmentVariable(variable), out var value) && value >= 1_000 ? value : fallback;

    private sealed class Statistics(long wager)
    {
        private readonly List<(long Payout, int Spins)> batches = [];
        private long payout;
        private long wheel;
        private int spins;
        private int modeSpins;
        private int wheelPayingSpins;
        private int totalitySpins;
        public int Triggers { get; set; }
        public int BoostedSpins { get; set; }

        public void Add(ClassicSlotSpinOutcome outcome, bool eclipse)
        {
            payout += outcome.Payout.TotalPoints;
            var wheelAward = outcome.Payout.Paylines.Where(line => line.PaylineId >= 1000).Sum(line => line.AmountPoints);
            wheel += wheelAward;
            if (wheelAward > 0) wheelPayingSpins++;
            if (outcome.Payout.Paylines.Any(line => line.PaylineId == 1010)) totalitySpins++;
            spins++;
            if (eclipse) modeSpins++;
        }

        public void Batch(long value, int count) => batches.Add((value, count));

        public Measurement Measure(string? mode, bool boosts, int cycles)
        {
            var estimate = payout / (double)(spins * wager);
            var totalBatchSpins = batches.Sum(batch => batch.Spins);
            var weightedSquares = batches.Sum(batch => Math.Pow(batch.Payout / (double)wager - estimate * batch.Spins, 2));
            var halfWidth = 1.96 * Math.Sqrt(batches.Count / (double)(batches.Count - 1) * weightedSquares) / totalBatchSpins;
            return new(mode, wager, boosts, cycles, spins, modeSpins, Triggers, BoostedSpins, wheelPayingSpins, totalitySpins, payout,
                wheel, estimate, wheel / (double)(spins * wager), estimate - halfWidth, estimate + halfWidth);
        }
    }

    private sealed record Measurement(string? Mode, long WagerPoints, bool SpendOrdinaryEarnedBoosts,
        int BaseCyclesOrModeSamples, int TotalPaidSpins, int EclipsePaidSpins, int Triggers, int BoostedSpins,
        int WheelPayingSpins, int TotalitySpins, long PayoutPoints, long WheelPoints, double EstimatedReturn, double WheelReturn,
        double Approximate95Lower, double Approximate95Upper);
}
