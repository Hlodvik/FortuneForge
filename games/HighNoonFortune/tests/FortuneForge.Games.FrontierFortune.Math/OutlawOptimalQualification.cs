using System.Diagnostics;
using System.Numerics;
using System.Security.Cryptography;
using System.Text.Json;
using FortuneForge.Games.FrontierFortune;

/// <summary>Offline exact Bellman enumeration. This never admits a wager or calls a wallet.</summary>
internal static class OutlawOptimalQualification
{
    private const int RankCount = 5;
    private const int HandSize = 5;
    private const int CodeSpace = 7776; // Six possible counts in each of five rank positions.
    private static readonly int[] RankPowers = [1, 6, 36, 216, 1296];
    private static readonly int[] Factorials = [1, 1, 2, 6, 24, 120];

    public static void Run(string[] args)
    {
        if (args.Length > 1) throw new ArgumentException("Supply at most one output JSON path.");
        var timer = Stopwatch.StartNew();
        var baseRules = FrontierRules.Definition;
        var outlawRules = OutlawRules.Definition;
        if (baseRules.Columns != HandSize || baseRules.Rows != 3 || outlawRules.Hand.Symbols.Length != RankCount || outlawRules.Hand.Deals != 3)
            throw new InvalidOperationException("This solver requires the current five-reel, three-row, three-deal contract.");
        var semanticHash = Convert.ToHexStringLower(SHA256.HashData(JsonSerializer.SerializeToUtf8Bytes(new { baseRules, outlawRules }, FrontierRules.Json)));
        var destination = args.Length == 1 ? Path.GetFullPath(args[0]) : Path.Combine(RepositoryRoot(), "output", "analysis", "frontier-outlaw-optimal-rtp.json");
        var vectors = Enumerable.Range(0, HandSize + 1).Select(Vectors).ToArray();
        var fullHands = vectors[HandSize];
        var partialHands = vectors.SelectMany(group => group).ToArray();
        if (fullHands.Length != 126 || partialHands.Length != 252) throw new InvalidOperationException("Unexpected state-space size.");
        var fullIndex = Enumerable.Repeat(-1, CodeSpace).ToArray();
        for (var index = 0; index < fullHands.Length; index++) fullIndex[Encode(fullHands[index])] = index;
        var partialIndex = partialHands.Select((counts, index) => (Code: Encode(counts), Index: index)).ToDictionary(pair => pair.Code, pair => pair.Index);
        var zeroIndex = partialIndex[0];
        var masks = AvailabilityMasks();
        var maskWeightTotal = masks.Aggregate(BigInteger.Zero, (sum, mask) => sum + mask.Weight);
        var expectedMaskTotal = BigInteger.Pow(outlawRules.Hand.Symbols.Sum(symbol => symbol.Weight), baseRules.Rows);
        if (masks.Length != 25 || maskWeightTotal != expectedMaskTotal) throw new InvalidOperationException("Reel-mask distribution does not normalize.");
        Console.Error.WriteLine($"Exact Outlaw solver: {fullHands.Length} full states, {partialHands.Length} hold states, {masks.Length} availability masks.");
        var terminal = fullHands.Select(counts => new BigInteger(OutlawHand.Classify(Symbols(counts)).Multiplier)).ToArray();
        var nativeChecks = VerifyNativeStates(fullHands, terminal);
        var grids = new Grid[HandSize + 1][];
        long hallChecks = 0;
        for (var unheld = 0; unheld <= HandSize; unheld++)
        {
            grids[unheld] = EnumerateGrids(unheld, masks, vectors[unheld], ref hallChecks);
            var normalization = grids[unheld].Aggregate(BigInteger.Zero, (sum, grid) => sum + grid.Weight);
            if (normalization != BigInteger.Pow(maskWeightTotal, unheld) || grids[unheld].Length != (int)EconomicsMath.Choose(24 + unheld, unheld))
                throw new InvalidOperationException("Grid multiset weights do not normalize.");
            Console.Error.WriteLine($"Availability grids m={unheld}: {grids[unheld].Length:N0}; exact weights and Hall feasibility verified ({timer.Elapsed.TotalSeconds:F1}s).");
        }
        var firstDealProbabilities = VerifyFirstDealProbabilities(grids[HandSize], fullHands, maskWeightTotal);
        var maps = partialHands.Select(held => vectors[HandSize - held.Sum()]
            .Select(completion => fullIndex[Encode(Add(held, completion))]).ToArray()).ToArray();
        var legalHolds = fullHands.Select(hand => partialHands.Select((held, index) => (held, index))
            .Where(pair => Enumerable.Range(0, RankCount).All(rank => pair.held[rank] <= hand[rank])).Select(pair => pair.index).ToArray()).ToArray();
        if (maps.Any(map => map.Any(index => index < 0)) || legalHolds.Any(holds => holds.Length is < 1 or > 32))
            throw new InvalidOperationException("The count-state transition graph is invalid.");

        var previous = terminal;
        var denominator = BigInteger.One;
        var dealDenominator = BigInteger.Pow(maskWeightTotal, HandSize);
        var horizons = new List<object>();
        BigInteger featureNumerator = 0;
        long gridExpectations = 0;
        for (var horizon = 1; horizon <= outlawRules.Hand.Deals; horizon++)
        {
            var expectations = new BigInteger[partialHands.Length];
            for (var heldIndex = 0; heldIndex < partialHands.Length; heldIndex++)
            {
                var unheld = HandSize - partialHands[heldIndex].Sum();
                var completionMap = maps[heldIndex];
                // Sorting is outside the expectation. The first feasible completion is exactly the maximum.
                var rankedCompletions = Enumerable.Range(0, completionMap.Length)
                    .OrderByDescending(index => previous[completionMap[index]]).ThenBy(index => index).ToArray();
                BigInteger weighted = 0;
                foreach (var grid in grids[unheld])
                {
                    var best = -1;
                    foreach (var candidate in rankedCompletions)
                        if (grid.Contains(candidate)) { best = completionMap[candidate]; break; }
                    if (best < 0) throw new InvalidOperationException("A grid has no feasible completion.");
                    weighted += grid.Weight * previous[best];
                    gridExpectations++;
                }
                expectations[heldIndex] = weighted * BigInteger.Pow(maskWeightTotal, HandSize - unheld);
            }
            denominator *= dealDenominator;
            var values = legalHolds.Select(holds => holds.Select(index => expectations[index]).Max()).ToArray();
            foreach (var index in Enumerable.Range(0, fullHands.Length))
                if (values[index] < previous[index] * dealDenominator || values[index] > outlawRules.Hand.Awards.Values.Max() * denominator)
                    throw new InvalidOperationException("Bellman monotonicity or the maximum award bound failed.");
            VerifyWildIdentities(fullHands, values, denominator, horizon);
            featureNumerator = expectations[zeroIndex];
            horizons.Add(new { deals = horizon, startMeanExact = Fraction(featureNumerator, denominator), startMeanDecimal = Decimal(featureNumerator, denominator),
                stateValues = fullHands.Select((counts, index) => new { counts, valueExact = Fraction(values[index], denominator) }).ToArray() });
            Console.Error.WriteLine($"Bellman horizon {horizon}/{outlawRules.Hand.Deals}: initial mean {Decimal(featureNumerator, denominator, 18)}x ({timer.Elapsed.TotalSeconds:F1}s).");
            previous = values;
        }

        Console.Error.WriteLine("Evaluating the native base return and exact ante rate...");
        var exactBase = EconomicsMath.EvaluateBase();
        var baseWeight = baseRules.Symbols.Sum(symbol => symbol.Weight);
        var wantedWeight = baseRules.Symbols.Single(symbol => symbol.Id == baseRules.ScatterSymbol).Weight;
        var cells = baseRules.Columns * baseRules.Rows;
        var anteDenominator = BigInteger.Pow(baseWeight, cells);
        BigInteger oneChip = 0, twoChips = 0;
        for (var count = outlawRules.Ante.WantedForOneChip; count <= cells; count++)
        {
            var weight = EconomicsMath.Choose(cells, count) * BigInteger.Pow(wantedWeight, count) * BigInteger.Pow(baseWeight - wantedWeight, cells - count);
            if (count >= outlawRules.Ante.WantedForTwoChips) twoChips += weight; else oneChip += weight;
        }
        var chipNumerator = oneChip + 2 * twoChips;
        var featureRateDenominator = anteDenominator * outlawRules.Ante.RequiredChips;
        var totalNumerator = exactBase.Numerator * featureRateDenominator * denominator + exactBase.Denominator * chipNumerator * featureNumerator;
        var totalDenominator = exactBase.Denominator * featureRateDenominator * denominator;
        var ordinaryCeiling = outlawRules.Hand.Awards.Where(award => award.Key != "grand").Max(award => award.Value);
        var totalHandWeight = outlawRules.Hand.Symbols.Sum(symbol => symbol.Weight);
        var wildWeight = outlawRules.Hand.Symbols.Single(symbol => symbol.Id == baseRules.WildSymbol).Weight;
        var observedCardDenominator = BigInteger.Pow(totalHandWeight, baseRules.Rows * outlawRules.Hand.Deals);
        var wildSeenNumerator = observedCardDenominator - BigInteger.Pow(totalHandWeight - wildWeight, baseRules.Rows * outlawRules.Hand.Deals);
        var grandBoundNumerator = BigInteger.Pow(wildSeenNumerator, HandSize);
        var grandBoundDenominator = BigInteger.Pow(observedCardDenominator, HandSize);
        var featureBoundNumerator = ordinaryCeiling * grandBoundDenominator + (outlawRules.Hand.Awards["grand"] - ordinaryCeiling) * grandBoundNumerator;
        if (featureNumerator * grandBoundDenominator > featureBoundNumerator * denominator) throw new InvalidOperationException("The independent Grand-availability upper bound failed.");
        var report = new
        {
            engineVersion = outlawRules.EngineVersion, baseMathVersion = outlawRules.BaseMathVersion, semanticRulesSha256 = semanticHash,
            classificationAssemblySha256 = Convert.ToHexStringLower(SHA256.HashData(File.ReadAllBytes(typeof(OutlawHand).Assembly.Location))),
            method = "Exact finite-horizon Bellman dynamic programming with BigInteger rational arithmetic; 25 weighted reel-availability masks, unordered iid mask-multisets, exact multinomial weights, and feasible rank-count completions.",
            arithmetic = "Integer weights and values only. All expectation sums, comparisons, Bellman decisions, invariants, and reported fractions are exact. Decimal strings are truncated displays; approximate doubles are presentation only.",
            recurrence = new[] { "V0(c) = native final-hand multiplier.", "Et(h) = sum over unseen grids of probability(grid) * max over feasible completions u of V(t-1)(h+u).",
                "Vt(c) = max over precommitted held subvectors h<=c of Et(h). Initial three-deal value = E3(empty), which uses V2 after the first reveal.",
                "Each horizon's full-hand stateValues table describes that many additional future deals from an already selected hand. The horizon3 full-hand table is auxiliary, not an extra fourth deal in the reported feature mean." },
            strategyAssumptions = new[]
            {
                "The player jointly chooses one actual visible row per unheld reel after all five reels reveal their three independent weighted cards.",
                "The player commits any legal hold subset before the next deal, without seeing future cards. Unheld cards must be replaced, even if the old rank is absent.",
                "Five reel positions are exchangeable, but the five actual rank identities remain separate. A has weight24; J/Q/K each25; actual WILD weight1 is retained for Grand.",
                "Three included deals; no extra wagers, early cash-out, suit/straight awards, or ordinary feature-line payouts. Only the final native hand classifier awards.",
                "Full-game return assumes a constant legal wager, completed earned hands, and an indefinitely long renewal horizon. Excess ante chips carry forward. Initial/terminal unfunded ante liability and mixed-wager rounding are excluded."
            },
            stateSpace = new { rankTypes = RankCount, fullHands = fullHands.Length, heldVectors = partialHands.Length,
                availabilityMasks = masks.Length, reelMaskDenominator = maskWeightTotal.ToString(), maskMultisetCounts = grids.Select(group => group.Length),
                feasibleCompletionsCheckedByHall = hallChecks, nativeInvariantChecks = nativeChecks, gridExpectations },
            firstDealProbabilities,
            feature = new { optimalMeanAwardMultiplierExact = Fraction(featureNumerator, denominator),
                optimalMeanAwardMultiplierDecimal = Decimal(featureNumerator, denominator),
                optimalMeanAwardMultiplier = (double)featureNumerator / (double)denominator,
                independentAnyStrategyUpperBoundExact = Fraction(featureBoundNumerator, grandBoundDenominator), horizons },
            baseGame = new { returnExact = exactBase.Fraction, returnDecimal = Decimal(exactBase.Numerator, exactBase.Denominator), returnApproximate = exactBase.Rate, nativeSequencesEvaluated = exactBase.Sequences },
            ante = new { oneChipProbabilityExact = Fraction(oneChip, anteDenominator), twoChipProbabilityExact = Fraction(twoChips, anteDenominator),
                expectedChipsPerPaidSpinExact = Fraction(chipNumerator, anteDenominator), steadyStateFeaturesPerPaidSpinExact = Fraction(chipNumerator, featureRateDenominator) },
            fullGame = new { optimalFixedWagerReturnExact = Fraction(totalNumerator, totalDenominator), optimalFixedWagerReturnDecimal = Decimal(totalNumerator, totalDenominator),
                optimalFixedWagerRtpPercentDecimal = Decimal(totalNumerator * 100, totalDenominator), optimalFixedWagerReturnApproximate = (double)totalNumerator / (double)totalDenominator },
            verification = new[] { "All grid weights normalize exactly for every unheld count0..5.", "All feasible-completion sets agree between direct per-reel assignment enumeration and all31 Hall rank-subset inequalities.",
                "All126 terminal multipliers invoke the production OutlawHand.Classify; every hand also completes through native Start/Deal/Confirm/Validate.",
                "All32 positional hold masks per full hand preserve native held/selected-row semantics; invalid held selections are rejected.",
                "First-deal Five/Grand feasibility probabilities agree exactly with independent row-level inclusion-exclusion formulas.",
                "Exact all-WILD, four-WILD, monotonicity, payout-ceiling and independent Grand-availability bounds pass." },
            certification = "Not independently certified. This is exact under the stated production semantics, with internal independent feasibility cross-checks and analytic invariants; an independent end-to-end solver review is still appropriate.",
            elapsedSeconds = timer.Elapsed.TotalSeconds
        };
        var json = JsonSerializer.Serialize(report, new JsonSerializerOptions(JsonSerializerDefaults.Web) { WriteIndented = true });
        Directory.CreateDirectory(Path.GetDirectoryName(destination)!); File.WriteAllText(destination, json + Environment.NewLine);
        Console.WriteLine($"Optimal feature mean: {Decimal(featureNumerator, denominator, 24)}x");
        Console.WriteLine($"Optimal fixed-wager full-game RTP: {Decimal(totalNumerator * 100, totalDenominator, 24)}%");
        Console.WriteLine($"Report: {destination}");
    }

    private static Mask[] AvailabilityMasks()
    {
        var weights = OutlawRules.Definition.Hand.Symbols.Select(symbol => symbol.Weight).ToArray();
        var masks = new Dictionary<int, BigInteger>();
        for (var a = 0; a < RankCount; a++) for (var b = 0; b < RankCount; b++) for (var c = 0; c < RankCount; c++)
        {
            var bits = (1 << a) | (1 << b) | (1 << c);
            masks[bits] = masks.GetValueOrDefault(bits) + (BigInteger)weights[a] * weights[b] * weights[c];
        }
        return masks.OrderBy(pair => pair.Key).Select(pair => new Mask(pair.Key, pair.Value)).ToArray();
    }

    private static Grid[] EnumerateGrids(int count, Mask[] masks, int[][] completions, ref long totalHallChecks)
    {
        var result = new List<Grid>();
        var selected = new int[count];
        var completionIndex = Enumerable.Repeat(-1, CodeSpace).ToArray();
        for (var index = 0; index < completions.Length; index++) completionIndex[Encode(completions[index])] = index;
        var demands = completions.Select(vector => Enumerable.Range(1, 31).Select(subset => Enumerable.Range(0, RankCount).Where(rank => (subset & (1 << rank)) != 0).Sum(rank => vector[rank])).ToArray()).ToArray();
        var marks = new int[CodeSpace]; var stamp = 0; long hallChecks = 0;
        void Enumerate(int depth, int minimum)
        {
            if (depth < count)
            {
                for (var index = minimum; index < masks.Length; index++) { selected[depth] = index; Enumerate(depth + 1, index); }
                return;
            }
            BigInteger product = Factorials[count];
            for (var start = 0; start < count;)
            {
                var end = start + 1; while (end < count && selected[end] == selected[start]) end++;
                product /= Factorials[end - start]; start = end;
            }
            foreach (var index in selected) product *= masks[index].Weight;
            Span<int> current = stackalloc int[126]; Span<int> next = stackalloc int[126];
            current[0] = 0; var currentCount = 1;
            foreach (var index in selected)
            {
                var nextCount = 0; stamp++;
                for (var previous = 0; previous < currentCount; previous++)
                    for (var rank = 0; rank < RankCount; rank++)
                        if ((masks[index].Bits & (1 << rank)) != 0)
                        {
                            var code = current[previous] + RankPowers[rank];
                            if (marks[code] == stamp) continue;
                            marks[code] = stamp; next[nextCount++] = code;
                        }
                next[..nextCount].CopyTo(current); currentCount = nextCount;
            }
            ulong low = 0, high = 0;
            for (var index = 0; index < currentCount; index++)
            {
                var bit = completionIndex[current[index]];
                if (bit < 0) throw new InvalidOperationException("Unknown completion vector.");
                if (bit < 64) low |= 1UL << bit; else high |= 1UL << (bit - 64);
            }
            Span<int> capacities = stackalloc int[31];
            capacities.Clear();
            for (var subset = 1; subset <= 31; subset++) foreach (var index in selected)
                if ((masks[index].Bits & subset) != 0) capacities[subset - 1]++;
            for (var index = 0; index < completions.Length; index++)
            {
                var feasible = true;
                for (var subset = 0; subset < 31; subset++) if (demands[index][subset] > capacities[subset]) { feasible = false; break; }
                var assigned = index < 64 ? (low & (1UL << index)) != 0 : (high & (1UL << (index - 64))) != 0;
                if (feasible != assigned) throw new InvalidOperationException("Hall and direct assignment disagree.");
                hallChecks++;
            }
            result.Add(new(low, high, product));
        }
        Enumerate(0, 0); totalHallChecks += hallChecks; return result.ToArray();
    }

    private static int[][] Vectors(int sum)
    {
        var result = new List<int[]>(); var counts = new int[RankCount];
        void Generate(int rank, int remaining)
        {
            if (rank == RankCount - 1) { counts[rank] = remaining; result.Add(counts.ToArray()); return; }
            for (var value = 0; value <= remaining; value++) { counts[rank] = value; Generate(rank + 1, remaining - value); }
        }
        Generate(0, sum); return result.ToArray();
    }
    private static int Encode(int[] counts) => counts.Select((count, rank) => count * RankPowers[rank]).Sum();
    private static int[] Add(int[] left, int[] right) => left.Select((value, rank) => value + right[rank]).ToArray();
    private static string[] Symbols(int[] counts) => counts.SelectMany((count, rank) => Enumerable.Repeat(OutlawRules.Definition.Hand.Symbols[rank].Id, count)).ToArray();

    private static object VerifyFirstDealProbabilities(Grid[] grids, int[][] hands, BigInteger maskWeightTotal)
    {
        var rules = OutlawRules.Definition;
        var ranks = rules.Hand.Symbols;
        var wild = Array.FindIndex(ranks, rank => rank.Id == FrontierRules.Definition.WildSymbol);
        var ordinary = Enumerable.Range(0, ranks.Length).Where(rank => rank != wild).ToArray();
        var total = ranks.Sum(rank => rank.Weight);
        var rows = FrontierRules.Definition.Rows;
        var reelDenominator = BigInteger.Pow(total, rows);
        var wildPresent = reelDenominator - BigInteger.Pow(total - ranks[wild].Weight, rows);
        BigInteger analyticFive = 0;
        for (var subset = 1; subset < (1 << ordinary.Length); subset++)
        {
            // For every rank in S to be available with substitution, a reel
            // must contain WILD or all ranks in S. Derive the latter by IE on
            // raw row weights, independently of the mask catalogue.
            BigInteger allRequiredWithoutWild = 0;
            for (var missing = subset;; missing = (missing - 1) & subset)
            {
                var remaining = total - ranks[wild].Weight;
                for (var index = 0; index < ordinary.Length; index++) if ((missing & (1 << index)) != 0) remaining -= ranks[ordinary[index]].Weight;
                var term = BigInteger.Pow(remaining, rows);
                allRequiredWithoutWild += BitOperations.PopCount((uint)missing) % 2 == 0 ? term : -term;
                if (missing == 0) break;
            }
            var intersection = BigInteger.Pow(wildPresent + allRequiredWithoutWild, HandSize);
            analyticFive += BitOperations.PopCount((uint)subset) % 2 == 1 ? intersection : -intersection;
        }
        var fiveIndices = hands.Select((counts, index) => (index, category: OutlawHand.Classify(Symbols(counts)).Category))
            .Where(pair => pair.category is "five" or "grand").Select(pair => pair.index).ToArray();
        var grandIndex = Array.FindIndex(hands, counts => counts[wild] == HandSize);
        BigInteger catalogFive = 0, catalogGrand = 0;
        foreach (var grid in grids)
        {
            if (fiveIndices.Any(grid.Contains)) catalogFive += grid.Weight;
            if (grid.Contains(grandIndex)) catalogGrand += grid.Weight;
        }
        var analyticGrand = BigInteger.Pow(wildPresent, HandSize);
        if (analyticFive != catalogFive || analyticGrand != catalogGrand) throw new InvalidOperationException("First-deal inclusion-exclusion cross-check failed.");
        var denominator = BigInteger.Pow(maskWeightTotal, HandSize);
        return new { fiveOrGrandFeasibleExact = Fraction(catalogFive, denominator), fiveOrGrandFeasibleDecimal = Decimal(catalogFive, denominator),
            grandFeasibleExact = Fraction(catalogGrand, denominator), grandFeasibleDecimal = Decimal(catalogGrand, denominator),
            independentCheck = "Exact equality with row-weight inclusion-exclusion, independent of mask-multiset enumeration." };
    }

    private static long VerifyNativeStates(int[][] hands, BigInteger[] terminal)
    {
        long checks = 0; var wager = FrontierRules.Definition.MinWagerPoints;
        foreach (var (counts, index) in hands.Select((counts, index) => (counts, index)))
        {
            var symbols = Symbols(counts); var grid = symbols.Select(symbol => new[] { symbol, symbol, symbol }).ToArray();
            var first = OutlawHand.Confirm(OutlawHand.Start("11111111111111111111111111111111", wager), grid, new int?[] { 0, 0, 0, 0, 0 });
            for (var mask = 0; mask < 32; mask++)
            {
                var held = Enumerable.Range(0, HandSize).Select(column => (mask & (1 << column)) != 0).ToArray();
                var dealt = OutlawHand.Deal(first, held);
                var rows = held.Select(value => value ? (int?)null : 0).ToArray();
                var confirmed = OutlawHand.Confirm(dealt, grid, rows); OutlawHand.Validate(confirmed);
                if (!confirmed.Hand.Select(card => card.Symbol).SequenceEqual(symbols)) throw new InvalidOperationException("Native hold preservation failed.");
                if (held.Any(value => value))
                {
                    var invalid = rows.ToArray(); invalid[Array.FindIndex(held, value => value)] = 0;
                    try { OutlawHand.Confirm(dealt, grid, invalid); throw new InvalidOperationException("A selected held position was accepted."); }
                    catch (ArgumentException) { }
                }
                checks++;
            }
            var second = OutlawHand.Confirm(OutlawHand.Deal(first, new bool[HandSize]), grid, new int?[] { 0, 0, 0, 0, 0 });
            var final = OutlawHand.Confirm(OutlawHand.Deal(second, new bool[HandSize]), grid, new int?[] { 0, 0, 0, 0, 0 });
            OutlawHand.Validate(final);
            if (final.AwardPoints != terminal[index] * wager) throw new InvalidOperationException("Native terminal award disagrees with classification.");
            checks++;
        }
        return checks;
    }

    private static void VerifyWildIdentities(int[][] hands, BigInteger[] values, BigInteger denominator, int horizon)
    {
        var rules = OutlawRules.Definition; var wild = Array.FindIndex(rules.Hand.Symbols, symbol => symbol.Id == FrontierRules.Definition.WildSymbol);
        var ordinaryAward = rules.Hand.Awards["five"]; var grandAward = rules.Hand.Awards["grand"];
        var total = rules.Hand.Symbols.Sum(symbol => symbol.Weight); var wildWeight = rules.Hand.Symbols[wild].Weight;
        foreach (var index in Enumerable.Range(0, hands.Length))
        {
            if (hands[index][wild] == HandSize && values[index] != grandAward * denominator) throw new InvalidOperationException("Five-WILD invariant failed.");
            if (hands[index][wild] != HandSize - 1) continue;
            var opportunities = FrontierRules.Definition.Rows * horizon;
            var probabilityDenominator = BigInteger.Pow(total, opportunities);
            var chance = probabilityDenominator - BigInteger.Pow(total - wildWeight, opportunities);
            var expected = (ordinaryAward * probabilityDenominator + (grandAward - ordinaryAward) * chance) * denominator / probabilityDenominator;
            if (values[index] != expected) throw new InvalidOperationException($"Four-WILD horizon{horizon} identity failed.");
        }
    }
    private static string Fraction(BigInteger numerator, BigInteger denominator)
    {
        var gcd = BigInteger.GreatestCommonDivisor(numerator, denominator); return $"{numerator / gcd}/{denominator / gcd}";
    }
    private static string Decimal(BigInteger numerator, BigInteger denominator, int places = 35)
    {
        var whole = BigInteger.DivRem(numerator, denominator, out var remainder);
        return $"{whole}.{(remainder * BigInteger.Pow(10, places) / denominator).ToString().PadLeft(places, '0')}";
    }
    private static string RepositoryRoot()
    {
        for (var directory = new DirectoryInfo(Environment.CurrentDirectory); directory is not null; directory = directory.Parent)
            if (File.Exists(Path.Combine(directory.FullName, "Directory.Build.props")) && Directory.Exists(Path.Combine(directory.FullName, "games", "HighNoonFortune"))) return directory.FullName;
        throw new InvalidOperationException("Run from the supplied repository checkout or provide an output path.");
    }
    private sealed record Mask(int Bits, BigInteger Weight);
    private sealed record Grid(ulong Low, ulong High, BigInteger Weight)
    {
        public bool Contains(int index) => index < 64 ? (Low & (1UL << index)) != 0 : (High & (1UL << (index - 64))) != 0;
    }
}
