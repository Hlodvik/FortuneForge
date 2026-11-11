using FortuneForge.Games.JadeDragon;

internal static class JadeDragonCascadeSimulation
{
    private const long WagerPoints = 20; // R5, the default cabinet wager.
    private const decimal MinimumRtp = .89m;

    internal static void Run(int paidSpinCount, int seed, decimal ceiling)
    {
        var random = new Random(seed);
        long totalPayoutCents = 0, freeSpinCount = 0, initialHits = 0, cycleHits = 0;
        for (var cycle = 0; cycle < paidSpinCount; cycle++)
        {
            var paid = JadeDragonCascadeRules.Resolve(WagerPoints, false, random.Next);
            if (paid.TotalPoints > 0) initialHits++;
            var cyclePayout = paid.TotalPoints;
            var freeSpinsRemaining = paid.FreeSpinsAwarded;
            while (freeSpinsRemaining > 0)
            {
                var free = JadeDragonCascadeRules.Resolve(WagerPoints, true, random.Next);
                cyclePayout = checked(cyclePayout + free.TotalPoints);
                freeSpinsRemaining = checked(freeSpinsRemaining - 1 + free.FreeSpinsAwarded);
                freeSpinCount++;
            }
            if (cyclePayout > 0) cycleHits++;
            totalPayoutCents = checked(totalPayoutCents + cyclePayout);
        }

        var wageredCents = (decimal)paidSpinCount * WagerPoints * JadeDragonCascadeRules.WagerPointValueInCents;
        var rtp = totalPayoutCents / wageredCents;
        Console.WriteLine($"Jade Dragon {JadeDragonCascadeRules.Version}: {paidSpinCount:N0} paid rounds, " +
            $"{freeSpinCount:N0} free spins, seed {seed}, RTP {rtp:P4}, " +
            $"paid hit rate {initialHits / (decimal)paidSpinCount:P4}, full-cycle hit rate {cycleHits / (decimal)paidSpinCount:P4}.");
        if (rtp < MinimumRtp || rtp >= ceiling)
            throw new InvalidOperationException($"Jade Dragon measured {rtp:P4}. Expected at least {MinimumRtp:P0} and below {ceiling:P0}.");
    }
}
