using System.Numerics;
using System.Security.Cryptography;
using System.Text.Json;
using FortuneForge.Games.FrontierFortune;

// An isolated automated economics tool. No deterministic controls are exported to players.
if (args.FirstOrDefault() == "outlaw-optimal")
{
    OutlawOptimalQualification.Run(args.Skip(1).ToArray());
    return;
}
if (args.FirstOrDefault() == "outlaw")
{
    OutlawQualification.Run(args.Skip(1).ToArray());
    return;
}
var samples = args.Length > 0 ? int.Parse(args[0]) : 100;
var beamWidth = args.Length > 1 ? int.Parse(args[1]) : 500;
if (samples is < 1 or > 1000 || beamWidth is < 1 or > 5000)
    throw new ArgumentOutOfRangeException(nameof(args));
var rules = FrontierRules.Definition;
var wager = rules.MinWagerPoints;
const int seed = 20261122;
var rulesHash = Convert.ToHexStringLower(SHA256.HashData(JsonSerializer.SerializeToUtf8Bytes(rules, FrontierRules.Json)));
var weight = rules.Symbols.Sum(symbol => symbol.Weight);
Console.Error.WriteLine("Enumerating base outcomes through the native evaluator...");
var exactBase = EconomicsMath.EvaluateBase();
var exactNumerator = exactBase.Numerator;
var exactDenominator = exactBase.Denominator;
var sequences = exactBase.Sequences;
var divisor = BigInteger.GreatestCommonDivisor(exactNumerator, exactDenominator);
var baseReturn = (double)exactNumerator / (double)exactDenominator;
var cells = rules.Columns * rules.Rows;
var scatterWeight = rules.Symbols.Single(symbol => symbol.Id == rules.ScatterSymbol).Weight;
var triggerNumerator = BigInteger.Zero;
for (var count = rules.ScatterTrigger; count <= cells; count++)
    triggerNumerator += EconomicsMath.Choose(cells, count) * BigInteger.Pow(scatterWeight, count) * BigInteger.Pow(weight - scatterWeight, cells - count);
var triggerDenominator = BigInteger.Pow(weight, cells);
var trigger = (double)triggerNumerator / (double)triggerDenominator;
var ceiling = rules.Bonus.Targets.Sum(target => target.Count * target.RewardMultiplier);
var template = rules.Bonus.Targets.SelectMany(target => Enumerable.Repeat(target.Kind, target.Count)).ToArray();
var results = new double[samples];
var baselines = new double[samples];
var searchNodes = new long[samples];

for (var sample = 0; sample < samples; sample++)
{
    var random = new Random(seed + sample);
    var kinds = template.ToArray();
    for (var index = kinds.Length - 1; index > 0; index--)
    {
        var other = random.Next(index + 1);
        (kinds[index], kinds[other]) = (kinds[other], kinds[index]);
    }
    var idBytes = new byte[16];
    BitConverter.GetBytes(sample + 1).CopyTo(idBytes, 0);
    var board = FrontierBounties.Create(new Guid(idBytes).ToString("N"), wager, kinds);
    baselines[sample] = GuaranteedStrategy(board).CollectedPoints / (double)wager;
    var (best, nodes) = Search(board, beamWidth);
    results[sample] = Math.Max(best, baselines[sample] * wager) / wager;
    searchNodes[sample] = nodes;
    if ((sample + 1) % 10 == 0 || sample == samples - 1)
        Console.Error.WriteLine($"Boards {sample + 1}/{samples}: mean achievable {results.Take(sample + 1).Average():F4}x, ceiling {ceiling}x");
}

var average = results.Average();
var variance = results.Sum(value => Math.Pow(value - average, 2)) / Math.Max(1, samples - 1);
var margin = 1.96 * Math.Sqrt(variance / samples);
var report = new
{
    engineVersion = rules.EngineVersion, semanticRulesSha256 = rulesHash, symbols = rules.Symbols.Length,
    baseOutcomeSequences = sequences, baseReturnExact = $"{exactNumerator / divisor}/{exactDenominator / divisor}",
    baseReturn, minimumWagerPoints = wager, maximumWagerPoints = rules.MaxWagerPoints,
    legalWagersHaveNoPayoutFlooring = rules.MinWagerPoints % rules.PayoutDivisor == 0 && rules.StepPoints % rules.PayoutDivisor == 0,
    scatterProbability = scatterWeight / (double)weight, bonusTriggerProbability = trigger,
    maximumBoardWagerMultiplier = ceiling, baselineWagerMultiplier = baselines.Average(),
    baselineTotalReturn = baseReturn + trigger * baselines.Average(), absoluteTotalReturnUpperBound = baseReturn + trigger * ceiling,
    search = new
    {
        method = "Native FrontierBounties preview/transition beam search; achievable strategy estimate, not certified optimal play",
        seed, samples, beamWidth, nativeTransitionCalls = searchNodes.Sum(),
        meanBonusWagerMultiplier = average, minimumBonusWagerMultiplier = results.Min(), maximumBonusWagerMultiplier = results.Max(),
        achievedCeilingBoards = results.Count(value => value == ceiling), mean95PercentSamplingInterval = new[] { average - margin, average + margin },
        estimatedAchievableStrategyTotalReturn = baseReturn + trigger * average,
        totalReturn95PercentSamplingInterval = new[] { baseReturn + trigger * (average - margin), baseReturn + trigger * (average + margin) },
        boardWagerMultipliers = results
    }
};
var json = JsonSerializer.Serialize(report, new JsonSerializerOptions(JsonSerializerDefaults.Web) { WriteIndented = true });
Console.WriteLine(json);
if (args.Length > 2)
{
    var output = Path.GetFullPath(args[2]);
    Directory.CreateDirectory(Path.GetDirectoryName(output)!);
    File.WriteAllText(output, json + Environment.NewLine);
}

static FrontierBonus GuaranteedStrategy(FrontierBonus original)
{
    var board = original;
    var supplies = board.Cells.Count(cell => cell.Kind == "supplies");
    // Spend a shot before opening each crate so the shot cap wastes no resource.
    for (var index = 0; index < supplies; index++)
    {
        var reward = board.Cells.First(cell => cell.Kind == "bounty" && cell.HitPoints > 0);
        board = FrontierBounties.Shoot(board, reward.Index).Bonus;
        board = FrontierBounties.Shoot(board, board.Cells.First(cell => cell.Kind == "supplies" && cell.HitPoints > 0).Index).Bonus;
    }
    while (!board.Completed)
    {
        var target = board.Cells.Where(cell => cell.HitPoints > 0 && cell.Kind is "bounty" or "strongbox")
            .OrderByDescending(cell => cell.Kind == "bounty").ThenBy(cell => cell.Index).FirstOrDefault();
        if (target is null) return FrontierBounties.Bank(board).Bonus;
        board = FrontierBounties.Shoot(board, target.Index).Bonus;
    }
    return board;
}

static (double Best, long Nodes) Search(FrontierBonus initial, int width)
{
    var rules = FrontierRules.Definition;
    var depthLimit = rules.Bonus.StartingShots + rules.Bonus.Targets.Where(target => target.Kind == "supplies").Sum(target => target.Count) * rules.Bonus.SupplyShots;
    var beam = new List<FrontierBonus> { initial };
    long best = 0, nodes = 0;
    for (var depth = 0; depth < depthLimit && beam.Count > 0; depth++)
    {
        var candidates = new Dictionary<string, Candidate>(StringComparer.Ordinal);
        foreach (var board in beam)
        foreach (var preview in board.Previews)
        {
            var earned = board.CollectedPoints + preview.AwardPoints;
            if (earned > best)
            {
                var verified = FrontierBounties.Shoot(board, preview.TargetIndex).Bonus;
                nodes++;
                best = verified.Completed ? verified.CollectedPoints : FrontierBounties.Bank(verified).PayoutPoints;
            }
            if (preview.FinishesBonus) continue;
            // Projected HP is a cache key derived solely from the native preview.
            // Rewards, chains, resource changes and every retained outcome come from native APIs.
            var hit = preview.HitIndices.ToHashSet();
            var hp = board.Cells.Select(cell => cell.HitPoints - (hit.Contains(cell.Index) ? 1 : 0)).ToArray();
            var key = $"{preview.ShotsAfter}:" + string.Concat(hp.Select(value => (char)('0' + value)));
            if (candidates.ContainsKey(key)) continue;
            var capacity = preview.ShotsAfter + board.Cells.Count(cell => hp[cell.Index] > 0 && cell.Kind == "supplies") * (rules.Bonus.SupplyShots - 1)
                + 2.0 * board.Cells.Count(cell => hp[cell.Index] > 0 && cell.Kind == "ricochet");
            double future = 0;
            foreach (var cell in board.Cells.Where(cell => hp[cell.Index] > 0 && cell.RewardPoints > 0)
                .OrderByDescending(cell => cell.RewardPoints / (double)hp[cell.Index]))
            {
                var spending = Math.Min(capacity, hp[cell.Index]);
                future += cell.RewardPoints * spending / hp[cell.Index];
                capacity -= spending;
            }
            candidates.Add(key, new(board, preview.TargetIndex, earned + future * .97 + preview.ShotsAfter * .001));
        }
        beam = candidates.Values.OrderByDescending(candidate => candidate.Score).Take(width).Select(candidate =>
        {
            nodes++;
            return FrontierBounties.Shoot(candidate.Board, candidate.Target).Bonus;
        }).ToList();
    }
    return (best, nodes);
}

internal sealed record Candidate(FrontierBonus Board, int Target, double Score);
