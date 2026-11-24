using System.Numerics;
using System.Security.Cryptography;
using System.Text.Json;
using FortuneForge.Games.FrontierFortune;
using FortuneForge.Games.Slots;

internal static class OutlawQualification
{
    public static void Run(string[] args)
    {
        var samples = args.Length > 0 ? int.Parse(args[0]) : 20_000;
        if (samples is < 10_000 or > 1_000_000) throw new ArgumentOutOfRangeException(nameof(args));
        const int seed = 20261123;
        var baseRules = FrontierRules.Definition;
        var rules = OutlawRules.Definition;
        var semanticHash = Convert.ToHexStringLower(SHA256.HashData(JsonSerializer.SerializeToUtf8Bytes(
            new { baseRules, outlawRules = rules }, FrontierRules.Json)));
        Console.Error.WriteLine("Enumerating the frozen base model through native evaluation...");
        var exactBase = EconomicsMath.EvaluateBase();
        var totalWeight = baseRules.Symbols.Sum(symbol => symbol.Weight);
        var wantedWeight = baseRules.Symbols.Single(symbol => symbol.Id == baseRules.ScatterSymbol).Weight;
        var cells = baseRules.Columns * baseRules.Rows;
        var probabilityDenominator = BigInteger.Pow(totalWeight, cells);
        var oneChipNumerator = BigInteger.Zero;
        var twoChipNumerator = BigInteger.Zero;
        for (var count = rules.Ante.WantedForOneChip; count <= cells; count++)
        {
            var outcomeWeight = EconomicsMath.Choose(cells, count) * BigInteger.Pow(wantedWeight, count)
                * BigInteger.Pow(totalWeight - wantedWeight, cells - count);
            if (count >= rules.Ante.WantedForTwoChips) twoChipNumerator += outcomeWeight;
            else oneChipNumerator += outcomeWeight;
        }
        var expectedChips = (double)(oneChipNumerator + 2 * twoChipNumerator) / (double)probabilityDenominator;
        var bonusRate = expectedChips / rules.Ante.RequiredChips;
        var handWeight = rules.Hand.Symbols.Sum(symbol => symbol.Weight);
        var wildProbability = rules.Hand.Symbols.Single(symbol => symbol.Id == baseRules.WildSymbol).Weight / (double)handWeight;
        var cellsSeenPerReel = rules.Hand.Deals * baseRules.Rows;
        var grandAvailabilityUpperBound = Math.Pow(1 - Math.Pow(1 - wildProbability, cellsSeenPerReel), baseRules.Columns);
        var ordinaryCeiling = rules.Hand.Awards.Where(award => award.Key != "grand").Max(award => award.Value);
        var grandAward = rules.Hand.Awards["grand"];
        var featureMeanUpperBound = ordinaryCeiling + (grandAward - ordinaryCeiling) * grandAvailabilityUpperBound;
        var random = new SeededDraws(seed);
        var categories = new Dictionary<string, int>(StringComparer.Ordinal);
        var values = new double[samples];
        long classifications = 0;
        for (var sample = 0; sample < samples; sample++)
        {
            var id = new byte[16];
            BitConverter.GetBytes(sample + 1).CopyTo(id, 0);
            var round = OutlawHand.Start(new Guid(id).ToString("N"), baseRules.MinWagerPoints);
            while (round.Phase != "complete")
            {
                if (round.Phase == "hold") round = OutlawHand.Deal(round, ChooseHolds(round));
                var grid = OutlawReels.Draw(random, handOnly: true).Reels;
                var choice = ChooseVisible(round, grid);
                classifications += choice.Classifications;
                round = OutlawHand.Confirm(round, grid, choice.Rows);
            }
            OutlawHand.Validate(round);
            values[sample] = round.AwardPoints / (double)round.WagerPoints;
            categories[round.Category!] = categories.GetValueOrDefault(round.Category!) + 1;
            if ((sample + 1) % 2000 == 0)
                Console.Error.WriteLine($"Native hands {sample + 1}/{samples}: mean {values.Take(sample + 1).Average():F5}x");
        }
        var mean = values.Average();
        var variance = values.Sum(value => Math.Pow(value - mean, 2)) / (samples - 1);
        var standardError = Math.Sqrt(variance / samples);
        var margin = 1.96 * standardError;
        var report = new
        {
            engineVersion = rules.EngineVersion, baseMathVersion = rules.BaseMathVersion, semanticRulesSha256 = semanticHash,
            baseReturnExact = exactBase.Fraction, baseReturn = exactBase.Rate, baseSequencesEvaluated = exactBase.Sequences,
            ante = new
            {
                wantedProbability = wantedWeight / (double)totalWeight,
                oneChipProbability = (double)oneChipNumerator / (double)probabilityDenominator,
                twoChipProbability = (double)twoChipNumerator / (double)probabilityDenominator,
                expectedChipsPerPaidSpin = expectedChips, requiredChips = rules.Ante.RequiredChips,
                steadyStateBonusesPerPaidSpin = bonusRate, steadyStatePaidSpinsPerBonus = 1 / bonusRate
            },
            rigorousBounds = new
            {
                wildProbability, cellsSeenPerReel, grandActualWildAvailabilityUpperBound = grandAvailabilityUpperBound,
                ordinaryAwardCeiling = ordinaryCeiling, actualWildGrandAward = grandAward,
                anyStrategyMeanFeatureAwardUpperBound = featureMeanUpperBound,
                anyStrategyFullGameReturnUpperBound = exactBase.Rate + bonusRate * featureMeanUpperBound,
                explanation = "A Grand needs an actual WILD in each reel's at-most-nine observed cards. All other categories pay no more than the ordinary ceiling."
            },
            strategy = new
            {
                seed, samples, nativeRandomDraws = random.Calls, nativeClassifications = classifications,
                method = "Enumerate native classifications over every visible unheld-reel row combination (at most 3^5). Prefer current award, then actual WILDs; hold WILDs and the largest same-rank group, using native card weights to break ties. All deals, confirms and final awards use production APIs.",
                meanFeatureAwardMultiplier = mean, standardDeviation = Math.Sqrt(variance), standardError,
                meanFeatureAward95PercentSamplingInterval = new[] { mean - margin, mean + margin },
                estimatedFixedWagerFullGameReturn = exactBase.Rate + bonusRate * mean,
                fullGameReturn95PercentSamplingInterval = new[] { exactBase.Rate + bonusRate * (mean - margin), exactBase.Rate + bonusRate * (mean + margin) },
                finalCategoryCounts = categories.OrderBy(category => category.Key).ToDictionary(category => category.Key, category => category.Value),
                observedMinimumAward = values.Min(), observedMaximumAward = values.Max(),
                optimality = "Achievable heuristic strategy estimate, not certified optimal play. Sampling intervals exclude the unknown strategy optimality gap."
            },
            assumptions = new[]
            {
                "A fixed legal wager is used throughout the ante cycle; excess ante chips carry forward. Included deals charge no additional wager.",
                "The test-only seeded random source drives native weighted draws without changing any probabilities or player-facing behavior.",
                "The Grand bound is analytic; an unobserved rare Grand in a finite sample is not assigned zero probability.",
                "The long-run budget uses expected earned chips divided by required chips; finite initial/final incomplete ante cycles are excluded."
            }
        };
        var json = JsonSerializer.Serialize(report, new JsonSerializerOptions(JsonSerializerDefaults.Web) { WriteIndented = true });
        Console.WriteLine(json);
        if (args.Length > 1)
        {
            var destination = Path.GetFullPath(args[1]);
            Directory.CreateDirectory(Path.GetDirectoryName(destination)!);
            File.WriteAllText(destination, json + Environment.NewLine);
        }
    }

    private static (int?[] Rows, long Classifications) ChooseVisible(OutlawHandRound round, string[][] grid)
    {
        var symbols = new string[round.Hand.Length];
        var rows = new int?[round.Hand.Length];
        var bestRows = new int?[round.Hand.Length];
        var bestAward = -1;
        var bestWilds = -1;
        double bestFuture = -1;
        long classifications = 0;
        void Choose(int column)
        {
            if (column == round.Hand.Length)
            {
                classifications++;
                var award = OutlawHand.Classify(symbols).Multiplier;
                var wilds = symbols.Count(symbol => symbol == FrontierRules.Definition.WildSymbol);
                var future = OutlawRules.Definition.Hand.Symbols.Where(symbol => symbol.Id != FrontierRules.Definition.WildSymbol)
                    .Max(symbol => symbols.Count(card => card == symbol.Id) + wilds + symbol.Weight / 100_000.0);
                if (award > bestAward || award == bestAward && (wilds > bestWilds || wilds == bestWilds && future > bestFuture))
                { bestAward = award; bestWilds = wilds; bestFuture = future; bestRows = rows.ToArray(); }
                return;
            }
            if (round.Hand[column].Held)
            {
                symbols[column] = round.Hand[column].Symbol!;
                rows[column] = null;
                Choose(column + 1);
                return;
            }
            foreach (var row in Enumerable.Range(0, grid[column].Length).DistinctBy(row => grid[column][row]))
            {
                symbols[column] = grid[column][row];
                rows[column] = row;
                Choose(column + 1);
            }
        }
        Choose(0);
        return (bestRows, classifications);
    }

    private static bool[] ChooseHolds(OutlawHandRound round)
    {
        var wild = FrontierRules.Definition.WildSymbol;
        var rank = OutlawRules.Definition.Hand.Symbols.Where(symbol => symbol.Id != wild)
            .OrderByDescending(symbol => round.Hand.Count(card => card.Symbol == symbol.Id))
            .ThenByDescending(symbol => symbol.Weight).First().Id;
        return round.Hand.Select(card => card.Symbol == wild || card.Symbol == rank).ToArray();
    }

    private sealed class SeededDraws(int seed) : IGameRandomSource
    {
        private readonly Random random = new(seed);
        public long Calls { get; private set; }
        public int Next(int maximumExclusive) { Calls++; return random.Next(maximumExclusive); }
    }
}
