namespace FortuneForge.Games.CosmicFortune.RtpAnalysis;

public sealed record MathOptions(long PaidSpins, long[] Stakes, int BatchSize, ulong Seed,
    int Parallelism, string[] Strategies, string Output, string WagerPolicy, bool PairedStakes, bool UseEarnedBoosts)
{
    public static MathOptions? Parse(string[] args)
    {
        if (args.Contains("--help"))
        {
            Console.WriteLine("--paid-spins=10000 --stakes=2,12,300 --wager-policy=fixed --paired-stakes=false --use-earned-boosts=false --batch-size=10000 --seed=20261122 --parallel=2 --strategies=coins,keys,random,optimal --output=output/analysis/pharaohs-ascent/rtp-smoke.json");
            return null;
        }
        var values = new Dictionary<string, string>(StringComparer.Ordinal);
        foreach (var arg in args)
        {
            var split = arg.Split('=', 2);
            if (split.Length != 2 || !values.TryAdd(split[0], split[1])) throw new ArgumentException("Use unique --option=value arguments.");
        }
        string Read(string key, string fallback) => values.Remove(key, out var value) ? value : fallback;
        var paid = long.Parse(Read("--paid-spins", "10000"));
        var stakes = Read("--stakes", "2,12,300").Split(',').Select(long.Parse).Distinct().ToArray();
        var batch = int.Parse(Read("--batch-size", "10000"));
        var seed = ulong.Parse(Read("--seed", "20261122"));
        var parallel = int.Parse(Read("--parallel", "2"));
        var strategies = Read("--strategies", "coins,keys,random,optimal").Split(',').Distinct(StringComparer.Ordinal).ToArray();
        var output = Read("--output", "output/analysis/pharaohs-ascent/rtp-smoke.json");
        var wagerPolicy = Read("--wager-policy", "fixed");
        var pairedStakes = bool.Parse(Read("--paired-stakes", "false"));
        var useEarnedBoosts = bool.Parse(Read("--use-earned-boosts", "false"));
        if (values.Count != 0 || paid < 1 || batch < 10_000 || parallel is < 1 or > 2 || stakes.Length == 0 ||
            strategies.Length == 0 || strategies.Any(strategy => strategy is not ("coins" or "keys" or "random" or "optimal")) ||
            wagerPolicy is not ("fixed" or "sunfire-switch" or "sunfire-anticipate" or "pity-switch" or "progression-switch" or "alternating"))
            throw new ArgumentException("Unknown option or invalid sample/strategy/batch/parallel setting.");
        foreach (var stake in stakes) PharaohsAscentWagering.ValidateNewPaidWager(stake);
        return new(paid, stakes, batch, seed, parallel, strategies, output, wagerPolicy, pairedStakes, useEarnedBoosts);
    }
}
