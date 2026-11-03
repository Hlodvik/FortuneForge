using FortuneForge.Server.Accounts.Models;
using FortuneForge.Server.Slots.Bonuses;
using FortuneForge.Server.Slots.Configuration;
using FortuneForge.Server.Slots.Evaluation;
using FortuneForge.Server.Slots.Models;
using FortuneForge.Server.Slots.Payouts;
using FortuneForge.Server.Slots.Reels;
using FortuneForge.Server.Slots.Spins;

internal static class CalibrationRunner
{
    public static CalibrationMeasurement Run(ISlotsDefinitionProvider provider, SlotSpecialRoundProfile profile,
        long[] schedule, int cycles, int seed, bool autoBoost, bool searchInteger = false)
    {
        var game = provider.GetGame(Definitions.GameId)!;
        if (profile.UsesEnergy || profile.UsesDirectValueTokens || game.Energy is not null)
            throw new InvalidOperationException("This focused harness requires Midnight's no-energy/no-direct-token profile.");
        var random = new SeededRandom(seed);
        var sweep = searchInteger ? new IntegerCandidateSweep(provider, cycles) : null;
        IPayoutCalculator calculator = sweep is null ? new PayoutCalculator() : sweep;
        var engine = new SpinService(provider, new CryptoReelGenerator(random), new CombinationEvaluator(), calculator, random);
        foreach (var wager in schedule) engine.ValidateRequest(game.Id, wager);
        var checks = ValidateFixtures(provider, game, profile, schedule);
        IReadOnlyList<MidnightVaultBonusRound> rounds = [];
        IReadOnlyDictionary<string, long> counts = new Dictionary<string, long>();
        IReadOnlyDictionary<string, long> wagerTotals = new Dictionary<string, long>();
        var specialPoints = 0; var pityMisses = 0;
        long paidWager = 0, totalPayout = 0, paidPayout = 0, spins = 0, hits = 0, boosts = 0, pity = 0, scatterSpins = 0, collectionSpins = 0;
        long specialAwarded = 0, specialSpent = 0, maxCycle = 0, maxSpin = 0, zeroCycles = 0;
        var symbols = new Dictionary<string, long>(StringComparer.Ordinal);
        var symbolsByMode = new Dictionary<string, Dictionary<string, long>>(StringComparer.Ordinal);
        var spinsByMode = new Dictionary<string, long>(StringComparer.Ordinal);
        var contribution = new Dictionary<string, long>(StringComparer.Ordinal);
        var featureModes = new Dictionary<string, long>(StringComparer.Ordinal);
        var cycleReturns = new double[cycles]; var payouts = new long[cycles];
        var batches = new List<double>(); long batchPayout = 0, batchWager = 0;
        var batchSize = Math.Max(1, cycles / Math.Min(50, cycles));
        long Resolve(bool free, long wager)
        {
            var boosted = autoBoost && specialPoints >= game.SpecialPoints!.ActivationCost;
            if (boosted) { specialPoints -= game.SpecialPoints!.ActivationCost; specialSpent += game.SpecialPoints.ActivationCost; boosts++; }
            var head = free ? rounds[0] : null;
            if (free) rounds = MidnightVaultBonusRounds.Consume(rounds);
            var beforeMode = head?.FeatureMode;
            var model = head?.MathModelId ?? game.Math.PaytableId;
            if (head is not null && head.WagerPoints != wager) throw new InvalidOperationException("Queued bonus wager changed.");
            var admission = new SlotSpinAdmission(wager, free ? 0 : checked(wager * 25), free,
                rounds.Sum(round => round.RemainingSpins), boosted, specialPoints, null, 0, beforeMode)
                { AdmissionId = Guid.NewGuid(), PityMissesBefore = pityMisses, MathModelId = model };
            var result = engine.SpinAdmitted(game.Id, admission, "midnight-calibration");
            pityMisses = result.ConsecutiveFiveMisses;
            if (result.EnergyAwarded != 0 || result.MoneyGrabPoints != 0) throw new InvalidOperationException("Unexpected disabled energy/direct-token value.");
            var settled = MidnightVaultBonusRounds.Advance(rounds, counts, wagerTotals, result.SealsAwarded,
                wager, result.FreeSpinsAwarded, result.EngineAudit!.FeatureProfile!.ScatterFeatureMode, model, result.SpinId);
            rounds = settled.Rounds; counts = settled.CollectionCounts; wagerTotals = settled.CollectionWagerPoints;
            specialPoints = checked(specialPoints + result.SpecialPointsAwarded); specialAwarded += result.SpecialPointsAwarded;
            scatterSpins += result.FreeSpinsAwarded;
            collectionSpins += settled.AwardedRounds.Sum(round => round.TotalSpins) - result.FreeSpinsAwarded;
            sweep?.CompleteSpin(result);
            spins++; if (result.Payout.TotalPoints > 0) hits++; if (result.FiveMatchPityTriggered) pity++;
            maxSpin = Math.Max(maxSpin, result.Payout.TotalPoints);
            if (free) featureModes[beforeMode ?? "scatter-base"] = featureModes.GetValueOrDefault(beforeMode ?? "scatter-base") + 1;
            var frequencyMode = (free ? "free:" + (beforeMode ?? "scatter-base") : "paid") + (boosted ? ":power-boosted" : ":ordinary");
            if (!symbolsByMode.TryGetValue(frequencyMode, out var modeSymbols))
                symbolsByMode[frequencyMode] = modeSymbols = new Dictionary<string, long>(StringComparer.Ordinal);
            spinsByMode[frequencyMode] = spinsByMode.GetValueOrDefault(frequencyMode) + 1;
            foreach (var symbol in result.Reels.SelectMany(reel => reel))
            {
                symbols[symbol] = symbols.GetValueOrDefault(symbol) + 1;
                modeSymbols[symbol] = modeSymbols.GetValueOrDefault(symbol) + 1;
            }
            foreach (var match in result.Payout.Paylines.SelectMany(line => line.Matches))
                contribution[match.Match.SymbolId] = checked(contribution.GetValueOrDefault(match.Match.SymbolId) + match.AmountPoints);
            return result.Payout.TotalPoints;
        }
        for (var paid = 0; paid < cycles; paid++)
        {
            sweep?.BeginCycle();
            var wager = schedule[paid % schedule.Length]; paidWager += wager;
            var cyclePayout = Resolve(false, wager); paidPayout += cyclePayout;
            var safety = 1000;
            while (rounds.Count > 0)
            {
                if (--safety == 0) throw new InvalidOperationException("Unbounded free-spin cycle.");
                cyclePayout = checked(cyclePayout + Resolve(true, rounds[0].WagerPoints));
            }
            sweep?.CompleteCycle(wager);
            payouts[paid] = cyclePayout; cycleReturns[paid] = cyclePayout / (double)wager;
            totalPayout += cyclePayout; batchPayout += cyclePayout; batchWager += wager;
            maxCycle = Math.Max(maxCycle, cyclePayout); if (cyclePayout == 0) zeroCycles++;
            if ((paid + 1) % batchSize == 0 || paid + 1 == cycles)
            { batches.Add(batchPayout / (double)batchWager); batchPayout = 0; batchWager = 0; }
        }
        if (specialAwarded - specialSpent != specialPoints) throw new InvalidOperationException("Special-point carry failed conservation.");
        var rtp = totalPayout / (double)paidWager;
        var hit = hits / (double)spins;
        var mean = batches.Average(); var se = batches.Count > 1 ? Math.Sqrt(batches.Sum(value => Math.Pow(value - mean, 2)) / (batches.Count - 1) / batches.Count) : 0;
        var target = (double)(game.Math.Targets.Rtp ?? 0.9m); var targetHit = (double)(game.Math.Targets.HitRate ?? 0.3m);
        var interval = new[] { mean - 2 * se, mean + 2 * se };
        var intervalInsidePolicy = interval[0] >= target - .02 && interval[1] <= target + .02;
        var averageCycleReturn = cycleReturns.Average();
        var ruin = EstimateRuin(payouts, schedule, seed + 1);
        var lettersTopFour = symbols.OrderByDescending(pair => pair.Value).Take(4).Select(pair => pair.Key)
            .ToHashSet(StringComparer.Ordinal).SetEquals(["2", "3", "4", "5"]);
        var letterShare = symbols.Where(pair => pair.Key is "2" or "3" or "4" or "5").Sum(pair => pair.Value) /
            (double)symbols.Values.Sum();
        var letterSharePass = letterShare >= .37 && letterShare <= .41;
        return new CalibrationMeasurement(schedule, cycles, seed, autoBoost, game.Math.PayoutMultiplier, paidWager, totalPayout,
            rtp, paidPayout / (double)paidWager, hit, Math.Abs(rtp - target) <= .02 && rtp < 1 && Math.Abs(hit - targetHit) <= .02 && (ruin is null || ruin > .5) && letterSharePass,
            interval, spins, scatterSpins, collectionSpins, boosts, specialAwarded, specialSpent,
            specialPoints, pity, maxSpin, maxCycle, zeroCycles / (double)cycles,
            Math.Sqrt(cycleReturns.Sum(value => Math.Pow(value - averageCycleReturn, 2)) / cycles),
            ruin, counts.Select(pair => new SlotSealCollection(pair.Key, checked((int)pair.Value), pair.Value == 0 ? 0 :
                checked((long)Math.Round(wagerTotals[pair.Key] / (decimal)pair.Value, MidpointRounding.AwayFromZero)), profile.CollectionTarget)).ToArray(),
            symbols, contribution, featureModes, checks, lettersTopFour, symbolsByMode, spinsByMode, counts, wagerTotals, sweep?.Report(), intervalInsidePolicy,
            letterShare, letterSharePass);
    }

    private static string[] ValidateFixtures(ISlotsDefinitionProvider provider, GameDefinition game, SlotSpecialRoundProfile profile, long[] wagers)
    {
        var paytable = provider.GetPaytable(game.Math.PaytableId)!; var calculator = new PayoutCalculator();
        foreach (var wager in wagers)
        foreach (var rule in paytable.Rules)
        {
            var match = new SymbolMatch(1, rule.SymbolId, rule.MatchLength, Enumerable.Range(0, rule.MatchLength).Select(reel => new GridPosition(reel, 0)).ToArray(), []);
            var multiplier = rule.Multiplier + (rule.MatchLength == game.Layout.ReelCount ? game.Math.PaylinePayoutSteps[0] : 0);
            var exact = wager * multiplier * game.Math.PayoutMultiplier;
            var expected = checked((long)decimal.Truncate(exact + .5m));
            var actual = calculator.Calculate([new PaylineEvaluation(1, [new MatchCandidate([match])])], game, paytable, wager).TotalPoints;
            if (actual != expected) throw new InvalidOperationException("Per-match positive AwayFromZero rounding mismatch.");
        }
        var source = provider.GetReelSet(game.Math.ReelSetId)!; var boost = SpecialPointBonus.CreateBoostedReelSet(game, source);
        for (var reel = 0; reel < source.Reels.Count; reel++)
        {
            foreach (var common in game.SpecialPoints!.CommonSymbolIds)
                if (boost.Reels[reel].Count(id => id == common) != source.Reels[reel].Count(id => id == common) / 2)
                    throw new InvalidOperationException("Boost common-symbol halving mismatch.");
            if (boost.Reels[reel].Count(id => id == "8") != source.Reels[reel].Count(id => id == "8"))
                throw new InvalidOperationException("Boost changed protected new 8 frequency.");
        }
        var carry = MidnightVaultBonusRounds.Advance([], new Dictionary<string, long> { ["rows"] = 29 },
            new Dictionary<string, long> { ["rows"] = 58 }, new Dictionary<string, int> { ["SEAL_ROWS"] = 2 },
            6, 0, null, game.Math.PaytableId, Guid.NewGuid());
        if (carry.AwardedRounds.Single().TotalSpins != profile.CollectionAwardedSpins || carry.AwardedRounds.Single().FeatureMode != profile.CollectionFeatureMode ||
            carry.AwardedRounds.Single().WagerPoints != 2 || carry.CollectionCounts["rows"] != 1 || carry.CollectionWagerPoints["rows"] != 6)
            throw new InvalidOperationException("Collection completion/overflow fixture failed.");
        var power = new SymbolMatch(1, game.SpecialPoints!.SymbolId, 3,
            [new GridPosition(0, 0), new GridPosition(1, 0), new GridPosition(2, 0)], []);
        if (SpecialPointBonus.CalculateAward([new PaylineEvaluation(1, [new MatchCandidate([power]), new MatchCandidate([power])])], game) != game.SpecialPoints.ThreeMatchPoints ||
            SpecialPointBonus.CalculateAward([new PaylineEvaluation(1, [new MatchCandidate([power with { WildPositions = [new GridPosition(1, 0)] }])])], game) != 0)
            throw new InvalidOperationException("Special-point exact-match/deduplication fixture failed.");
        return ["Every native paytable rule checked at each tested wager against per-match positive half-up arithmetic.",
            "Power boost halves inherited common stops and preserves new 8 stops.", "Collection target crossing awards the configured spins/mode and retains one overflow gem.",
            "Special points deduplicate identical geometry and exclude wild-substituted matches.",
            "Every simulation conserves special-point balance and drains real FIFO rounds with locked wager/mode/model and exact gem wager totals."];
    }

    private static double? EstimateRuin(long[] payouts, long[] schedule, int seed)
    {
        if (schedule.Length != 1) return null;
        var random = new Random(seed); var ruined = 0;
        for (var trial = 0; trial < 10000; trial++)
        {
            var balance = schedule[0] * 100;
            for (var spin = 0; spin < 1000; spin++)
            { if (balance < schedule[0]) { ruined++; break; } balance += payouts[random.Next(payouts.Length)] - schedule[0]; }
        }
        return ruined / 10000d;
    }
    private sealed class SeededRandom(int seed) : IRandomIndexSource
    { private readonly Random random = new(seed); public int Next(int maximumExclusive) => random.Next(maximumExclusive); }
}

internal sealed record CalibrationMeasurement(long[] WagerSchedule, int PaidCycles, int Seed, bool AutoBoost, decimal Multiplier,
    long PaidWager, long TotalPayout, double Rtp, double PaidOnlyRtp, double HitRate, bool PolicyPass, double[] Approximate95PercentBatchInterval,
    long TotalSpins, long ScatterFreeSpins, long CollectionFreeSpins, long BoostedSpins, long SpecialPointsAwarded,
    long SpecialPointsSpent, int SpecialPointsRemaining, long PitySpins, long MaxSpinPayout, long MaxCyclePayout, double ZeroCycleRate,
    double CycleReturnStdDev, double? ResampledBankrollRuinRate, IReadOnlyList<SlotSealCollection> FinalCollections,
    Dictionary<string, long> VisibleSymbolCounts, Dictionary<string, long> PayoutBySymbol, Dictionary<string, long> FreeSpinModes, string[] Checks,
    bool LettersAreOverallTopFour, Dictionary<string, Dictionary<string, long>> VisibleSymbolCountsByMode, Dictionary<string, long> SpinsByMode,
    IReadOnlyDictionary<string, long> FinalCollectionCounts, IReadOnlyDictionary<string, long> FinalCollectionWagerPoints, IntegerSearchReport? CandidateSearch,
    bool RtpIntervalInsidePolicy, double CombinedLetterVisibleShare, bool LetterVisibleSharePolicyPass);
