using System.Diagnostics;
using System.Text.Json;
using FortuneForge.Games.PharaohsAscent;
using FortuneForge.Games.PharaohsAscent.RtpAnalysis;

var options = MathOptions.Parse(args);
if (options is null) return;
var started = DateTimeOffset.UtcNow;
var watch = Stopwatch.StartNew();
var results = new List<ProfileResult>();
var gate = new object();
_ = TombStrategies.OptimalExpectedMultiplier;
Parallel.ForEach(options.Stakes, new ParallelOptions { MaxDegreeOfParallelism = options.Parallelism }, stake =>
{
    var result = Simulation.Run(options, stake);
    lock (gate) results.Add(result);
});
var report = new
{
    engineVersion = PharaohsAscentEngine.EngineVersion,
    gameId = PharaohsAscentEngine.GameId,
    wagering = PharaohsAscentWagering.Current,
    pricing = new { PharaohsAscentEngine.Profile.PayoutMultiplier, paytable = PharaohsAscentEngine.CreatePaytable() },
    generatedAtUtc = DateTimeOffset.UtcNow,
    startedAtUtc = started,
    elapsedSeconds = watch.Elapsed.TotalSeconds,
    options,
    methodology = "Exact current production PharaohsAscentEngine and PharaohsAscentRoundPolicy.Advance/SpendsLegacy; every paid cycle drains all earned carry and redeems every funded tomb ticket through SealedTombRules. One continuous state per selected stake, retained between nonoverlapping paid-spin blocks. The wager policy selects only legal new paid wagers; earned spins always use their actual locked funded wager. Strategies share only the within-stake reel/funding trace and use separate unbiased RNG streams for actual tomb outcomes. Main state retains the completed coins branch; tomb decisions do not affect reel rules or funding. Per-symbol contributions reconcile to settled reel returns and are diagnostic selected-match exposures, not an alternate payout model. Earned boosts are off by default; the explicit option spends only the actual saved special-point balance at the production activation cost on paid or earned spins. No live wallet/database/network access. Confidence intervals use actual paid-wager block ratios, not individual reel/action observations. Residual partial resources are reported and are not artificially paid out.",
    tombExpectations = new { coins = 20.9m, keys = 26.35m, optimal = TombStrategies.OptimalExpectedMultiplier },
    profiles = results.OrderBy(result => result.StakePoints).ToArray(),
};
var json = JsonSerializer.Serialize(report, new JsonSerializerOptions(JsonSerializerDefaults.Web) { WriteIndented = true });
var output = Path.GetFullPath(options.Output);
Directory.CreateDirectory(Path.GetDirectoryName(output)!);
File.WriteAllText(output, json + Environment.NewLine);
Console.WriteLine(json);
Console.Error.WriteLine($"Saved {output}; elapsed {watch.Elapsed.TotalSeconds:F2}s.");
