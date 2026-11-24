using System.Text.Json;
using FortuneForge.Games.CrookedRoad;
using FortuneForge.Games.Slots;

var cycles = ReadInt("--cycles=", 100_000);
var seed = ReadInt("--seed=", 20261122);
var wagers = args.FirstOrDefault(argument => argument.StartsWith("--wagers="))?["--wagers=".Length..]
    .Split(',').Select(long.Parse).ToArray() ?? [100L, 1000L, 5000L];
var samples = new List<object>();
foreach (var wager in wagers)
foreach (var destination in CrookedRoadMap.Destinations)
{
    CrookedRoadRules.ValidateWager(wager);
    var random = new Seeded(seed);
    var state = CrookedRoadState.Empty;
    long paid = 0, awards = 0, bonusAwards = 0, bonusSteps = 0, tickets = 0;
    long batchPaid = 0, batchAwards = 0;
    var batches = new List<double>();
    for (var cycle = 0; cycle < cycles; cycle++)
    {
        var spin = CrookedRoadGame.Spin(state, wager, false, Guid.NewGuid(), DateTime.UnixEpoch, random);
        state = spin.After;
        paid += wager; batchPaid += wager; awards += spin.Payout.TotalPoints; batchAwards += spin.Payout.TotalPoints;
        while (state.Tickets.Length > 0)
        {
            // Ideal routing redeems every funded ticket immediately. The map can delay payment, never increase it.
            // No tickets, stakes or monetary awards are created by this measurement shortcut.
            state = state with { ActiveBonus = CrookedRoadGame.Begin(state.Tickets[0], destination, random), Tickets = state.Tickets.Skip(1).ToArray() };
            tickets++;
            while (state.ActiveBonus is not null)
            {
                if (CrookedRoadViews.ActionRequired(state))
                    state = CrookedRoadMap.Act(state, new(state.Revision, destination == "well" ? "choose-spring" : "choose-door", Choice: cycle % 3),
                        (_, _) => throw new InvalidOperationException());
                var bonus = CrookedRoadGame.Spin(state, state.ActiveBonus!.FundedWagerPoints, true, Guid.NewGuid(), DateTime.UnixEpoch, random);
                state = bonus.After;
                awards += bonus.Payout.TotalPoints; bonusAwards += bonus.Payout.TotalPoints;
                batchAwards += bonus.Payout.TotalPoints; bonusSteps++;
            }
        }
        if ((cycle + 1) % 1000 == 0 || cycle == cycles - 1)
        {
            batches.Add(batchAwards / (double)batchPaid);
            batchPaid = batchAwards = 0;
        }
    }
    var mean = awards / (double)paid;
    var deviation = batches.Count <= 1 ? 0 : Math.Sqrt(batches.Sum(value => (value - mean) * (value - mean)) / (batches.Count - 1) / batches.Count);
    samples.Add(new { destination, wagerPoints = wager, paidSpins = cycles, paidWagerPoints = paid, payoutPoints = awards,
        observedRtp = mean, approximate95PercentBatchInterval = new[] { mean - 1.96 * deviation, mean + 1.96 * deviation },
        bonusAwardPoints = bonusAwards, ticketsRedeemed = tickets, bonusSteps });
}
var upper = CrookedRoadMath.FullReturnUpperBound;
var qualified = upper.Numerator * 100 < upper.Denominator * 95;
var report = new
{
    modelId = CrookedRoadRules.Data.ModelId, seed, exactBaseReturn = CrookedRoadMath.BaseReturn.Value,
    exactScatterProbability = CrookedRoadMath.ScatterProbability.Value, analyticRtpUpperBound = upper.Value,
    analyticUpperNumerator = upper.Numerator.ToString(), analyticUpperDenominator = upper.Denominator.ToString(),
    releaseCeiling = .95, qualifiedByAnalyticBound = qualified,
    methodology = "Exact independent-line enumeration, exact finite Fort recurrence, exact symmetric Hill door expectation, and separately normalized Well spring policies. All awarded funded/scatter tickets redeemed; base awards, funding averages and feature payouts round down. Ideal map routing is an upper-return policy; road placements cannot add value. Monte Carlo uses the production domain engine and is a regression check, not certified RTP. Batch means retain funding state; no bonus retriggers or bonus funding.",
    samples
};
var json = JsonSerializer.Serialize(report, new JsonSerializerOptions { WriteIndented = true });
Console.WriteLine(json);
if (args.FirstOrDefault(argument => argument.StartsWith("--output=")) is { } output)
{
    var path = Path.GetFullPath(output["--output=".Length..]);
    Directory.CreateDirectory(Path.GetDirectoryName(path)!);
    File.WriteAllText(path, json);
}
if (!qualified) Environment.ExitCode = 1;

int ReadInt(string option, int fallback)
{
    var argument = args.FirstOrDefault(argument => argument.StartsWith(option));
    if (argument is null) return fallback;
    if (!int.TryParse(argument[option.Length..], out var value) || value <= 0) throw new ArgumentException($"Provide a positive integer for {option}.");
    return value;
}
sealed class Seeded(int seed) : IGameRandomSource
{
    private readonly Random random = new(seed);
    public int Next(int maximumExclusive) => random.Next(maximumExclusive);
}
