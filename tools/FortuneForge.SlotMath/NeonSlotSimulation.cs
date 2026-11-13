internal static class NeonSlotSimulation
{
    public static void Run(int baseCycles, int seed, decimal releaseRtpCeiling, string[] args)
    {
        var wager = ReadOption(args, "--neon-wager", 100);
        var isolatedRounds = ReadOption(args, "--neon-car-rounds", 3_000);
        var compareVisibleRespin = args.Contains("--neon-compare-respin", StringComparer.Ordinal);
        var carOnly = args.Contains("--neon-car-only", StringComparer.Ordinal);
        var naturalOnly = args.Contains("--neon-natural-only", StringComparer.Ordinal);
        if (carOnly && naturalOnly)
            throw new ArgumentException("Choose either the isolated Car sample or the natural progression sample.");
        var definitions = new CachedNeonDefinitions();
        Console.WriteLine($"Neon Nights {NeonArcadeSlotDefinitions.EngineVersion}; wager {wager} cents; seed {seed}");
        Console.WriteLine("Gross hit = payout > 0; net hit = payout > wager. A free respin is credited to its original paid spin.");
        Console.WriteLine("For a Nitro reel choice, select the highest-paying visible option.");
        Console.WriteLine("Respin policy: use the leftmost eligible reel after each losing paid Car spin.");
        if (compareVisibleRespin)
            Console.WriteLine("Deliberate policy: prefer an eligible Nitro target, then a two-777 setup, then the most one-away paylines.");
        Console.WriteLine();
        if (!naturalOnly)
            RunIsolatedCar(definitions, seed, wager, isolatedRounds, compareVisibleRespin, releaseRtpCeiling);
        if (!carOnly)
            RunNaturalProgression(definitions, seed, wager, baseCycles, compareVisibleRespin, releaseRtpCeiling);
    }

    private static void RunIsolatedCar(ISlotDefinitionSource definitions, int seed, long wager, int isolatedRounds,
        bool compareVisibleRespin, decimal releaseRtpCeiling)
    {
        var isolated = new Simulation(definitions, seed, wager);
        for (var round = 0; round < isolatedRounds; round++)
        {
            var state = new PlayState
            {
                Car = new(NeonArcadeSlotDefinitions.SpecialPlaySpins, wager, NeonArcadeBonus.PaidCarFeatureMode)
            };
            isolated.PlayCarRound(state, compareVisibleRespin);
        }
        Console.WriteLine($"Isolated Car Plays ({isolatedRounds:N0} rounds, including retriggers)");
        isolated.Car.PaidSpins.Print("Car with respin policy");
        isolated.Car.WithoutRespin.Print("Car without respins");
        isolated.Car.PrintRounds();
        isolated.Car.PrintEstimatedLineScale(releaseRtpCeiling);
        if (compareVisibleRespin)
        {
            isolated.DeliberateCar.PaidSpins.Print("Car with deliberate respin policy");
            isolated.DeliberateCar.PrintRounds();
        }
        Console.WriteLine();
    }

    private static void RunNaturalProgression(ISlotDefinitionSource definitions, int seed, long wager, int baseCycles,
        bool compareVisibleRespin, decimal releaseRtpCeiling)
    {
        var overall = new Simulation(definitions, seed, wager);
        for (var cycle = 0; cycle < baseCycles; cycle++)
            overall.PlayBaseCycle(compareVisibleRespin);

        Console.WriteLine($"Natural progression ({baseCycles:N0} base cycles, with Car and Disco played through)");
        overall.Base.Print("Base");
        overall.Car.PaidSpins.Print("Car");
        overall.Disco.Print("Disco");
        overall.Overall.Print("Overall");
        Console.WriteLine($"Overall RTP contributions: base {Share(overall.Base.Payout, overall.Overall.Wagers):P3}; " +
            $"Car {Share(overall.Car.PaidSpins.Payout, overall.Overall.Wagers):P3}; " +
            $"Disco {Share(overall.Disco.Payout, overall.Overall.Wagers):P3}.");
        overall.BaseAwards.Print("Base", overall.Base.Wagers);
        overall.Car.PrintRounds();
        Console.WriteLine($"Disco rounds {overall.DiscoRounds.Returns.Count:N0}; ahead {overall.DiscoRounds.Ahead:P3}; " +
            $"round return SD {overall.DiscoRounds.Returns.Deviation:F3}x.");
        Console.WriteLine($"Overall cycle ahead {overall.Cycles.Ahead:P3}; cycle return SD {overall.Cycles.Returns.Deviation:F3}x.");
        Console.WriteLine($"Overall RTP excluding respins on the same spins: " +
            $"{(overall.Overall.Payout - overall.Car.RespinImprovement) / (decimal)overall.Overall.Wagers:P3}");
        if (compareVisibleRespin)
        {
            var deliberatePayout = checked(overall.Overall.Payout - overall.Car.RespinImprovement +
                overall.DeliberateCar.RespinImprovement);
            var deliberateRtp = Share(deliberatePayout, overall.Overall.Wagers);
            overall.DeliberateCar.PaidSpins.Print("Car with deliberate respin policy");
            overall.DeliberateCar.PrintRounds();
            Console.WriteLine($"Overall RTP with deliberate respins on the same spins: {deliberateRtp:P3}");
            if (deliberateRtp >= releaseRtpCeiling) Environment.ExitCode = 1;
        }
        var bankroll = overall.Bankroll.EstimateBankrollDepletion(wager, unchecked(seed + 1));
        Console.WriteLine($"Bankroll trial (100 starting bets, up to 1,000 base cycles): {bankroll.RuinRate:P2} ran out; " +
            $"median ending balance {bankroll.MedianEndingBalance / (decimal)wager:N1} bets.");
        Console.WriteLine($"Observed overall RTP {(overall.Overall.Rtp < releaseRtpCeiling ? "below" : "at or above")} " +
            $"the {releaseRtpCeiling:P0} release ceiling.");
        if (bankroll.RuinRate <= 0.5m)
            Console.WriteLine("Bankroll depletion falls short of the existing majority requirement.");
        if (overall.Overall.Rtp >= releaseRtpCeiling || bankroll.RuinRate <= 0.5m) Environment.ExitCode = 1;
    }

    private static int ReadOption(string[] args, string name, int fallback)
    {
        var prefix = name + "=";
        var option = args.SingleOrDefault(arg => arg.StartsWith(prefix, StringComparison.Ordinal));
        if (option is null) return fallback;
        if (int.TryParse(option.AsSpan(prefix.Length), out var parsed) && parsed > 0) return parsed;
        throw new ArgumentException($"Provide a positive integer as {prefix}<number>.");
    }

    private sealed class Simulation(ISlotDefinitionSource definitions, int seed, long wager)
    {
        private readonly NeonNightsEngine engine = new(definitions, new SeededRandom(seed));
        private readonly NeonCarRespin respin = new(new SeededRandom(unchecked(seed + 1)));
        private readonly NeonCarRespin deliberateRespin = new(new SeededRandom(unchecked(seed + 1)));
        private readonly PlayState ongoing = new();
        public PaidMetrics Base { get; } = new();
        public PayoutComponents BaseAwards { get; } = new();
        public PaidMetrics Disco { get; } = new();
        public PaidMetrics Overall { get; } = new();
        public CarMetrics Car { get; } = new();
        public CarMetrics DeliberateCar { get; } = new();
        public RoundMetrics Cycles { get; } = new();
        public RoundMetrics DiscoRounds { get; } = new();
        public Statistics Bankroll { get; } = new([]);

        public void PlayBaseCycle(bool compareVisibleRespin)
        {
            var wagersBefore = Overall.Wagers;
            var payoutBefore = Overall.Payout;
            Resolve(ongoing, Base);
            while (ongoing.Car.Remaining > 0 || ongoing.DiscoRemaining > 0)
            {
                if (ongoing.Car.Remaining > 0) PlayCarRound(ongoing, compareVisibleRespin);
                else PlayDiscoRound(ongoing);
            }
            var cycleWager = Overall.Wagers - wagersBefore;
            var cyclePayout = Overall.Payout - payoutBefore;
            Cycles.Record(cycleWager, cyclePayout);
            Bankroll.RecordCycle(cyclePayout, cycleWager);
        }

        private void PlayDiscoRound(PlayState state)
        {
            var wagerBefore = Disco.Wagers;
            var payoutBefore = Disco.Payout;
            while (state.DiscoRemaining > 0 && state.Car.Remaining == 0)
                Resolve(state, Disco, disco: true);
            DiscoRounds.Record(Disco.Wagers - wagerBefore, Disco.Payout - payoutBefore);
        }

        public void PlayCarRound(PlayState state, bool compareVisibleRespin = false)
        {
            var roundWager = 0L;
            var payout = 0L;
            var improvement = 0L;
            var deliberateImprovement = 0L;
            var remainingSafetyPlays = 2_000;
            while (state.Car.Remaining > 0)
            {
                if (--remainingSafetyPlays < 0)
                    throw new InvalidOperationException("The Car round did not terminate.");
                var result = Resolve(state, null, car: true);
                var added = 0L;
                var eligible = NeonCarRespin.OfferReels(result);
                if (eligible.Count > 0)
                {
                    var outcome = ResolveRespin(respin, result, eligible[0]);
                    added = outcome.ImprovementPoints;
                    Car.RecordRespin(outcome);
                }
                if (compareVisibleRespin)
                {
                    var deliberateAdded = 0L;
                    if (eligible.Count > 0)
                    {
                        var selected = ChooseVisibleReel(result, eligible);
                        var outcome = ResolveRespin(deliberateRespin, result, selected);
                        deliberateAdded = outcome.ImprovementPoints;
                        DeliberateCar.RecordRespin(outcome);
                    }
                    DeliberateCar.RecordSpin(result, deliberateAdded);
                    deliberateImprovement = checked(deliberateImprovement + deliberateAdded);
                }
                Car.RecordSpin(result, added);
                Overall.Record(result.WagerPoints, checked(result.Payout.TotalPoints + added));
                roundWager = checked(roundWager + result.WagerPoints);
                payout = checked(payout + result.Payout.TotalPoints);
                improvement = checked(improvement + added);
            }
            Car.WithoutRespinRounds.Record(roundWager, payout);
            Car.Rounds.Record(roundWager, checked(payout + improvement));
            if (compareVisibleRespin)
            {
                DeliberateCar.WithoutRespinRounds.Record(roundWager, payout);
                DeliberateCar.Rounds.Record(roundWager, checked(payout + deliberateImprovement));
            }
        }

        private static NeonCarRespinOutcome ResolveRespin(NeonCarRespin engine, NeonSpinOutcome original, int reel)
        {
            var result = engine.Resolve(original, reel);
            return result.NeonNitroWild?.RequiresChoice == true
                ? Enumerable.Range(0, result.Reels.Count)
                    .Select(target => NeonCarRespin.ResolveChoice(original, result, target))
                    .MaxBy(candidate => candidate.ImprovementPoints)!
                : result;
        }

        private NeonSpinOutcome Resolve(PlayState state, PaidMetrics? metrics, bool car = false, bool disco = false)
        {
            var feature = car ? state.Car.FeatureMode : null;
            var stake = car ? state.Car.LockedWagerPoints!.Value : wager;
            if (!NeonPlayPolicy.ChargesWager(car, feature))
                throw new InvalidOperationException("Current Neon special plays must charge their admitted wager.");
            var result = engine.Spin(stake, "neon-slot-math", false, 0, feature, state.PityMisses, car, disco,
                disco ? state.DiscoMultiplier : NeonDiscoCascade.InitialMultiplier);
            if (result.NeonNitroWild?.RequiresChoice == true)
            {
                var pending = result;
                result = Enumerable.Range(0, pending.Reels.Count)
                    .Select(reel => NeonNightsEngine.ResolveNitroChoice(pending, reel))
                    .MaxBy(candidate => candidate.Payout.TotalPoints)!;
            }
            state.PityMisses = result.ConsecutiveFiveMisses;
            state.Car = NeonPlayPolicy.AdvanceAwardedPlays(state.Car.Remaining - (car ? 1 : 0), car,
                feature, result.FreeSpinsAwarded, stake, result.NeonArcade);
            state.DiscoRemaining = NeonPlayPolicy.NextDiscoSpins(state.DiscoRemaining, disco, result.NeonArcade);
            state.DiscoMultiplier = NeonPlayPolicy.NextDiscoMultiplier(state.DiscoMultiplier, state.DiscoRemaining,
                result.NeonDiscoCascade, result.NeonArcade);
            if (metrics is not null)
            {
                metrics.Record(stake, result.Payout.TotalPoints);
                Overall.Record(stake, result.Payout.TotalPoints);
                if (!disco) BaseAwards.Record(result);
            }
            return result;
        }
    }

    private static int ChooseVisibleReel(NeonSpinOutcome spin, IReadOnlyList<int> eligible)
    {
        if (spin.NeonNitroWild is { ExpandedReel: null, EligibleReel: var target } && eligible.Contains(target))
            return target;

        var landed = spin.NeonNitroWild?.InitialReels ?? spin.Reels;
        var sevenChances = eligible.Select(reel => (Reel: reel, Chances: CountTwoSevenPaths(landed, reel)))
            .OrderByDescending(candidate => candidate.Chances).First();
        if (sevenChances.Chances > 0) return sevenChances.Reel;
        return eligible.MaxBy(reel => CountOneAwayPaylines(spin, reel));
    }

    private static int CountTwoSevenPaths(IReadOnlyList<IReadOnlyList<string>> landed, int selected)
    {
        var chances = 0;
        for (var start = Math.Max(0, selected - 2); start <= Math.Min(selected, landed.Count - 3); start++)
        {
            for (var row = 0; row < landed[start].Count; row++)
            {
                for (var step = -1; step <= 1; step++)
                {
                    if (Enumerable.Range(0, 3).All(offset =>
                    {
                        var reel = start + offset;
                        var targetRow = row + offset * step;
                        return targetRow >= 0 && targetRow < landed[reel].Count &&
                            (reel == selected || landed[reel][targetRow] == "BANANA");
                    })) chances++;
                }
            }
        }
        return chances;
    }

    private static int CountOneAwayPaylines(NeonSpinOutcome spin, int selected)
    {
        var game = spin.EngineAudit.EffectiveGame;
        var shortPaths = new HashSet<(int, int, int)>();
        var fullPaths = 0;
        foreach (var rows in game.Paylines)
        {
            if (CanComplete(rows, rows.Count)) fullPaths++;
            if (selected < 3 && rows[1] - rows[0] == rows[2] - rows[1] && Math.Abs(rows[1] - rows[0]) <= 1 &&
                CanComplete(rows, 3)) shortPaths.Add((rows[0], rows[1], rows[2]));
        }
        return shortPaths.Count + fullPaths;

        bool CanComplete(List<int> rows, int length)
        {
            string? natural = null;
            for (var reel = 0; reel < length; reel++)
            {
                if (reel == selected) continue;
                var symbol = spin.Reels[reel][rows[reel]];
                if (symbol == game.Symbols.WildSymbolId) continue;
                if (!game.Symbols.PaylineSymbolIds!.Contains(symbol) || natural is not null && natural != symbol)
                    return false;
                natural = symbol;
            }
            return true;
        }
    }

    private sealed class PlayState
    {
        public NeonAwardedPlayState Car { get; set; } = new(0, null, null);
        public int DiscoRemaining { get; set; }
        public int DiscoMultiplier { get; set; } = NeonDiscoCascade.InitialMultiplier;
        public int PityMisses { get; set; }
    }

    private sealed class CarMetrics
    {
        private long armed;
        private long expanded;
        private long retriggers;
        private long respins;
        private long improvingRespins;
        private long respinExpansions;
        private readonly PayoutComponents awards = new();
        private decimal lineScale;
        public long RespinImprovement { get; private set; }
        public PaidMetrics PaidSpins { get; } = new();
        public PaidMetrics WithoutRespin { get; } = new();
        public RoundMetrics Rounds { get; } = new();
        public RoundMetrics WithoutRespinRounds { get; } = new();

        public void RecordSpin(NeonSpinOutcome spin, long improvement)
        {
            WithoutRespin.Record(spin.WagerPoints, spin.Payout.TotalPoints);
            PaidSpins.Record(spin.WagerPoints, checked(spin.Payout.TotalPoints + improvement));
            if (spin.NeonNitroWild is not null) armed++;
            if (spin.NeonNitroWild?.ExpandedReel is not null) expanded++;
            if (spin.FreeSpinsAwarded > 0) retriggers++;
            lineScale = spin.EngineAudit.EffectiveGame.Math.PayoutMultiplier;
            awards.Record(spin);
        }

        public void RecordRespin(NeonCarRespinOutcome result)
        {
            respins++;
            if (result.ImprovementPoints > 0) improvingRespins++;
            if (result.NeonNitroWild?.ExpandedReel is not null) respinExpansions++;
            RespinImprovement += result.ImprovementPoints;
        }

        public void PrintRounds()
        {
            Console.WriteLine($"Car rounds {Rounds.Returns.Count:N0}; ahead {Rounds.Ahead:P3} with respin, " +
                $"{WithoutRespinRounds.Ahead:P3} without; round return SD {Rounds.Returns.Deviation:F3}x; " +
                $"retriggers {Share(retriggers, PaidSpins.Spins):P3} per paid Car spin.");
            Console.WriteLine($"Car Nitro armed {Share(armed, PaidSpins.Spins):P3}; " +
                $"expanded {Share(expanded, PaidSpins.Spins):P3} of paid Car spins.");
            Console.WriteLine($"Car respins {respins:N0} ({Share(respins, Rounds.Returns.Count):P3} of rounds); " +
                $"improved {Share(improvingRespins, respins):P3}; expanded {Share(respinExpansions, respins):P3} of respins.");
            awards.Print("Car", PaidSpins.Wagers, RespinImprovement);
        }

        public void PrintEstimatedLineScale(decimal target)
        {
            var scaledPayout = checked(awards.LinePayout + awards.BoostPayout);
            if (scaledPayout == 0) return;
            var estimate = lineScale * Math.Max(0m, target * PaidSpins.Wagers - awards.SevenPayout) / scaledPayout;
            Console.WriteLine($"Indicative Car line scale for {target:P0} Car RTP without respins: {estimate:F4} " +
                $"(current {lineScale:F4}; fixed 777 awards, ignores cent rounding).");
        }
    }

    private sealed class PayoutComponents
    {
        public long LinePayout { get; private set; }
        public long BoostPayout { get; private set; }
        public long SevenPayout { get; private set; }

        public void Record(NeonSpinOutcome spin)
        {
            LinePayout += spin.NeonArcade.UnboostedPaylinePoints;
            BoostPayout += spin.NeonArcade.PaylineBoostPoints;
            SevenPayout += spin.BananaBonusPoints;
        }

        public void Print(string mode, long wagers, long? respin = null)
        {
            var respinShare = respin is { } amount ? $"; respin improvement {Share(amount, wagers):P3}" : string.Empty;
            Console.WriteLine($"{mode} RTP contributions: natural lines {Share(LinePayout, wagers):P3}; " +
                $"BONUS boost {Share(BoostPayout, wagers):P3}; 777 {Share(SevenPayout, wagers):P3}{respinShare}.");
        }
    }

    private sealed class PaidMetrics
    {
        private long hits;
        private long ahead;
        private long even;
        private readonly Distribution returns = new();
        public long Spins => returns.Count;
        public long Wagers { get; private set; }
        public long Payout { get; private set; }
        public decimal Rtp => Share(Payout, Wagers);

        public void Record(long wager, long payout)
        {
            Wagers = checked(Wagers + wager);
            Payout = checked(Payout + payout);
            if (payout > 0) hits++;
            if (payout > wager) ahead++;
            if (payout == wager) even++;
            returns.Record(payout / (double)wager);
        }

        public void Print(string label) => Console.WriteLine($"{label}: {Spins:N0} paid spins; RTP {Rtp:P3}; " +
            $"gross hit {Share(hits, Spins):P3}; net hit {Share(ahead, Spins):P3}; " +
            $"break even {Share(even, Spins):P3}; spin return SD {returns.Deviation:F3}x.");
    }

    private sealed class RoundMetrics
    {
        private long profitable;
        public Distribution Returns { get; } = new();
        public decimal Ahead => Share(profitable, Returns.Count);

        public void Record(long wager, long payout)
        {
            if (payout > wager) profitable++;
            Returns.Record(payout / (double)wager);
        }
    }

    private sealed class Distribution
    {
        private double mean;
        private double squaredDelta;
        public long Count { get; private set; }
        public double Deviation => Count > 1 ? Math.Sqrt(squaredDelta / (Count - 1)) : 0;

        public void Record(double value)
        {
            Count++;
            var delta = value - mean;
            mean += delta / Count;
            squaredDelta += delta * (value - mean);
        }
    }

    private static decimal Share(long count, long total) => total == 0 ? 0 : count / (decimal)total;

    private sealed class SeededRandom(int seed) : IGameRandomSource
    {
        private readonly Random random = new(seed);
        public int Next(int maximumExclusive) => random.Next(maximumExclusive);
    }

    private sealed class CachedNeonDefinitions : ISlotDefinitionSource
    {
        private readonly NeonDefinitionSource source = new();
        private readonly Dictionary<string, GameDefinition?> games = new(StringComparer.Ordinal);
        private readonly Dictionary<string, SymbolSetDefinition?> symbols = new(StringComparer.Ordinal);
        private readonly Dictionary<string, ReelSetDefinition?> reels = new(StringComparer.Ordinal);
        private readonly Dictionary<string, PaytableDefinition?> paytables = new(StringComparer.Ordinal);

        public GameDefinition? GetGame(string id) => Find(games, id, source.GetGame);
        public SymbolSetDefinition? GetSymbolSet(string id) => Find(symbols, id, source.GetSymbolSet);
        public ReelSetDefinition? GetReelSet(string id) => Find(reels, id, source.GetReelSet);
        public PaytableDefinition? GetPaytable(string id) => Find(paytables, id, source.GetPaytable);

        private static T? Find<T>(Dictionary<string, T?> cache, string id, Func<string, T?> get) where T : class
        {
            if (!cache.TryGetValue(id, out var value)) cache.Add(id, value = get(id));
            return value;
        }
    }
}
