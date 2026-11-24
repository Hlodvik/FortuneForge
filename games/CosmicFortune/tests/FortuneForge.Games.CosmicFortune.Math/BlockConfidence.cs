namespace FortuneForge.Games.CosmicFortune.RtpAnalysis;

internal static class BlockConfidence
{
    internal static object? Calculate(ReturnBlock[] blocks, string strategy)
    {
        if (blocks.Length < 2) return null;
        var wager = blocks.Sum(block => (double)block.PaidWagerCents);
        var payouts = blocks.Select(block => (double)block.PaidReelCents + block.EarnedReelCents + block.TombCents[strategy]).ToArray();
        var ratio = payouts.Sum() / wager;
        var residuals = blocks.Select((block, index) => payouts[index] - ratio * block.PaidWagerCents).ToArray();
        var se = System.Math.Sqrt(blocks.Length / (double)(blocks.Length - 1) * residuals.Sum(value => value * value)) / wager;
        var critical = TCritical(blocks.Length - 1);
        var mean = payouts.Select((payout, index) => payout / blocks[index].PaidWagerCents).Average();
        var ratios = payouts.Select((payout, index) => payout / blocks[index].PaidWagerCents).ToArray();
        var denominator = ratios.Sum(value => (value - mean) * (value - mean));
        var lag1 = denominator == 0 ? 0 : Enumerable.Range(1, ratios.Length - 1)
            .Sum(index => (ratios[index] - mean) * (ratios[index - 1] - mean)) / denominator;
        return new { lower = ratio - critical * se, upper = ratio + critical * se, standardError = se,
            halfWidth = critical * se, degreesOfFreedom = blocks.Length - 1, tCritical = critical,
            blockLagOneCorrelation = lag1 };
    }
    private static double TCritical(int df)
    {
        double[] small = [0, 12.7062047, 4.3026527, 3.1824463, 2.7764451, 2.5705818, 2.4469119, 2.3646243,
            2.3060041, 2.2621572, 2.2281389, 2.2009852, 2.1788128, 2.1603687, 2.1447867, 2.1314495,
            2.1199053, 2.1098156, 2.1009220, 2.0930241, 2.0859634, 2.0796138, 2.0738731, 2.0686576,
            2.0638986, 2.0595386, 2.0555294, 2.0518305, 2.0484071, 2.0452296, 2.0422725];
        if (df < small.Length) return small[df];
        const double z = 1.959963984540054;
        return z + (z * z * z + z) / (4 * df) +
            (5 * System.Math.Pow(z, 5) + 16 * z * z * z + 3 * z) / (96 * df * df) +
            (3 * System.Math.Pow(z, 7) + 19 * System.Math.Pow(z, 5) + 17 * z * z * z - 15 * z) / (384 * df * df * df);
    }
}
