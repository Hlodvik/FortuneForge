using FortuneForge.Server.Slots.Bonuses;

internal static class OlympusTumbleSimulation
{
    public static void Run(int spinCount, int seed, decimal releaseRtpCeiling)
    {
        Console.WriteLine($"Olympus demo {OlympusTumbleDemo.EngineVersion}, {spinCount:N0} random spins per wager, seed {seed}.");
        foreach (var wager in new long[] { 2, 100, 2000 })
        {
            var random = new Random(seed);
            Func<int, int> nextIndex = random.Next;
            var hits = 0;
            var multiTumbles = 0;
            var threeOrMore = 0;
            var profitSpins = 0;
            var tenTimesSpins = 0;
            var maxReturn = 0d;
            var sum = 0d;
            var sumSquares = 0d;

            for (var spin = 0; spin < spinCount; spin++)
            {
                var outcome = OlympusTumbleDemo.Resolve(wager, false, nextIndex);
                var payout = (double)outcome.TotalPoints / wager;
                sum += payout;
                sumSquares += payout * payout;
                maxReturn = Math.Max(maxReturn, payout);
                hits += payout > 0 ? 1 : 0;
                multiTumbles += outcome.Steps.Count >= 2 ? 1 : 0;
                threeOrMore += outcome.Steps.Count >= 3 ? 1 : 0;
                profitSpins += payout > 1 ? 1 : 0;
                tenTimesSpins += payout >= 10 ? 1 : 0;
            }

            var rtp = sum / spinCount;
            var variance = Math.Max(0, sumSquares / spinCount - rtp * rtp);
            var confidenceHalfWidth = 1.96 * Math.Sqrt(variance / spinCount);
            Console.WriteLine(
                $"{wager,4} points | RTP {rtp:P2} ± {100 * confidenceHalfWidth:F2} pp | " +
                $"Hit {Rate(hits):P2} | 2+ tumbles {Rate(multiTumbles):P2} | 3+ tumbles {Rate(threeOrMore):P2} | " +
                $"Profit {Rate(profitSpins):P2} | 10x+ {Rate(tenTimesSpins):P2} | Max {maxReturn:F1}x");
            if (rtp >= (double)releaseRtpCeiling)
                throw new InvalidOperationException(
                    $"Olympus RTP must remain below {releaseRtpCeiling:P0}, but measured {rtp:P3} at {wager} points.");

            double Rate(int count) => (double)count / spinCount;
        }
    }
}
