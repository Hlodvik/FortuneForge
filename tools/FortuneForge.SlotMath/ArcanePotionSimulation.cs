// Edited: 2026-11-18T18:09:19-06:00 | Computer: Vinnabok
// Edited: 2026-11-18T18:01:37-06:00 | Computer: Vinnabok
using System.Numerics;
using FortuneForge.Games.ArcaneArchives;
using FortuneForge.Games.Slots;

internal static class ArcanePotionSimulation
{
    private static readonly HashSet<string> IngredientIds = ArcaneTome.IngredientIds.ToHashSet(StringComparer.Ordinal);

    private enum PotionUse
    {
        None,
        All,
        MandrakeOnly,
        StandardOnly,
        RareOnly
    }

    public static void Run(int paidSpins, int seed, decimal releaseRtpCeiling, string[] args)
    {
        var wagering = ArcaneArchivesEngine.CreateGame().Wagering;
        var maximum = wagering.MaximumWagerPoints
            ?? throw new InvalidOperationException("Arcane's maximum wager is required for verification.");
        var selectedWager = ReadWager(args, wagering, maximum);
        var use = ReadPotionUse(args);
        var switchWagers = args.Contains("--wager-switch", StringComparer.OrdinalIgnoreCase);
        var activeOnly = args.Contains("--rare-active", StringComparer.OrdinalIgnoreCase);

        Console.WriteLine($"Arcane Archives {ArcaneArchivesEngine.EngineVersion} math over {paidSpins:N0} paid spins per scenario, seed {seed}. Tome Pages are enabled.");
        if (activeOnly)
        {
            if (switchWagers || use != PotionUse.All)
                throw new ArgumentException("--rare-active runs one rare potion at a time at a fixed wager.");
            var wager = selectedWager ?? 12L;
            foreach (var stock in BrewRareVariants(wager))
            {
                var (_, report) = RunWager(wager, null, paidSpins, seed, PotionUse.RareOnly, stock);
                Console.WriteLine(report);
            }
            return;
        }

        long[] wagers = selectedWager is { } singleWager ? [singleWager]
            : new[] { wagering.MinimumWagerPoints, 12L, 100L, maximum }.Distinct().ToArray();
        PotionUse[] uses = selectedWager is null && use is (PotionUse.All or PotionUse.MandrakeOnly)
            ? [PotionUse.None, use] : [use];
        IEnumerable<(long Wager, long? PotionWager, PotionUse Mode)> scenarios = switchWagers
            ? [(selectedWager ?? wagering.MinimumWagerPoints, maximum, use)]
            : wagers.SelectMany(wager => uses.Select(mode => (wager, (long?)null, mode)));
        var results = scenarios.AsParallel().AsOrdered().Select(scenario =>
            RunWager(scenario.Wager, scenario.PotionWager, paidSpins, seed, scenario.Mode)).ToArray();
        foreach (var result in results)
            Console.WriteLine(result.Report);
        if (results.Any(result => result.Rtp >= releaseRtpCeiling))
            throw new InvalidOperationException($"An Arcane scenario met or exceeded the RTP ceiling of {releaseRtpCeiling:P0}.");
    }

    private static (decimal Rtp, string Report) RunWager(
        long baseWager, long? potionWager, int paidSpins, int seed, PotionUse use, ArcanePotionState? rareStock = null)
    {
        var engine = new ArcaneArchivesEngine(new SeededRandomIndexSource(seed));
        var tome = ArcaneTome.Empty;
        var pages = ArcaneTome.ToView(tome)!.Pages;
        var potions = ArcanePotions.Empty;
        var rareVariants = new SortedDictionary<string, RarePotionStatistics>(StringComparer.Ordinal);
        var payouts = new PayoutStatistics();
        var pityMisses = 0;
        var potionSpins = 0;
        var tomeActivations = 0;
        var wandSpins = 0;
        var ingredientSpins = new int[4];
        var ingredientlessPairs = 0;
        var previousSpinHadIngredient = true;
        long highestPotionWager = 0;
        for (var index = 0; index < paidSpins; index++)
        {
            if (tome is { ActivePage: null, GrimoireReady: true })
            {
                var page = pages[tomeActivations % pages.Count];
                tome = ArcaneTome.SelectPage(tome, page.Id, activate: true);
                tomeActivations++;
            }
            if (use != PotionUse.None && potions.Active is null)
            {
                potions = rareStock ?? potions;
                // Run the rare bottles at their first eligible spin, including when a standard bottle is waiting.
                var bottle = potions.Bottles.FirstOrDefault(candidate => candidate.VariantId is not null)
                    ?? potions.Bottles.FirstOrDefault();
                if (bottle is not null)
                {
                    potions = ArcanePotions.Use(potions, bottle.Id);
                    if (bottle.VariantId is { } variant)
                        GetStatistics(rareVariants, variant).Activations++;
                }
            }
            var potion = ArcanePotions.ForSpin(potions, isFreeSpin: false);
            if (potion is not null)
                potionSpins++;

            var requestedWager = potion is null ? baseWager : potionWager ?? baseWager;
            var wager = ArcanePotions.LockedWagerForSpin(potions, isFreeSpin: false) ?? requestedWager;
            if (potion is not null)
                highestPotionWager = Math.Max(highestPotionWager, wager);
            var spin = engine.SpinTome(wager, "arcane-potion-math", 0, pityMisses,
                false, tome.ActivePage is not null, potion);
            payouts.RecordSpin(spin.Payout, wager);
            if (use != PotionUse.None)
            {
                var units = rareStock is null ? ArcaneTome.OrderedIngredientUnits(tome, spin.Reels, isFreeSpin: false) : [];
                var nextBottleBeforeSpin = potions.NextBottleId;
                potions = ArcanePotions.CompleteSpin(potions, units, isFreeSpin: false, wagerPoints: wager);
                foreach (var bottle in potions.Bottles)
                    if (bottle.Id >= nextBottleBeforeSpin && bottle.VariantId is { } brewedVariant)
                        GetStatistics(rareVariants, brewedVariant).Brews++;
                if (potion?.VariantId is { } activeVariant)
                    GetStatistics(rareVariants, activeVariant).Payouts.RecordSpin(spin.Payout, wager);
                if (use != PotionUse.All && potions.Bottles.Any(bottle => !MayActivate(bottle, use)))
                    potions = potions with { Bottles = potions.Bottles.Where(bottle => MayActivate(bottle, use)).ToArray() };
            }
            tome = ArcaneTome.CompleteSpin(tome, spin.Reels, isFreeSpin: false).State;
            pityMisses = spin.ConsecutiveFiveMisses;

            var hasWand = false;
            var ingredients = 0;
            foreach (var reel in spin.Reels)
            foreach (var symbol in reel)
            {
                hasWand |= symbol == ArcaneWandCollector.WandSymbolId;
                if (IngredientIds.Contains(symbol)) ingredients++;
            }
            if (hasWand) wandSpins++;
            ingredientSpins[ingredients]++;
            if (ingredients == 0 && !previousSpinHadIngredient) ingredientlessPairs++;
            previousSpinHadIngredient = ingredients > 0;
        }

        decimal Rate(int count) => count / (decimal)paidSpins;
        var mode = rareStock is not null ? "Rare potion active for every spin" : use switch
        {
            PotionUse.None => "Base",
            PotionUse.MandrakeOnly => "Mandrake only",
            PotionUse.StandardOnly => "Standard potions auto-used",
            PotionUse.RareOnly => "Rare potions auto-used",
            _ => "All potions auto-used, rare bottles first"
        };
        var wagerLabel = potionWager is { } high
            ? $"{baseWager} points to earn ingredients / {high} points preferred after activation / {highestPotionWager} points max charged while active"
            : $"{baseWager} points";
        var report = $"{wagerLabel} | {mode} | RTP {payouts.Rtp:P3} | Payout SD {payouts.StandardDeviation:F3}× wager | " +
            $"Hit {Rate(paidSpins - payouts.ZeroWins):P3} | Zero {Rate(payouts.ZeroWins):P3} | Full return (≥1×) {Rate(payouts.FullReturns):P3} | Profit (>1×) {Rate(payouts.Profits):P3} | " +
            $"≥5× {Rate(payouts.FiveTimes):P3} | ≥10× {Rate(payouts.TenTimes):P3} | ≥25× {payouts.TwentyFiveTimes:N0} spins | Max {payouts.LargestMultiple:F3}×\n" +
            $"  Ingredient landings (0/1/2/3): {string.Join(" / ", ingredientSpins.Select(count => $"{Rate(count):P3}"))} | Wand visible {Rate(wandSpins):P3}";
        if (paidSpins > 1)
            report += $" | Two spins with no ingredients {ingredientlessPairs / (decimal)(paidSpins - 1):P3}";
        if (use != PotionUse.None)
            report += $" | Potion coverage {Rate(potionSpins):P3}";
        var rareBrews = rareVariants.Values.Sum(stats => stats.Brews);
        if (rareStock is null && use != PotionUse.None)
            report += $" | Bottles {potions.NextBottleId - 1:N0}\n  Rare brews combined: {rareBrews:N0} | {DescribeFrequency(rareBrews, paidSpins)}";
        foreach (var (variant, stats) in rareVariants)
            report += $"\n  {variant} | {stats.Describe(paidSpins)}";
        return (payouts.Rtp, report);
    }

    private static PotionUse ReadPotionUse(string[] args)
    {
        var choices = new[]
        {
            ("--base-only", PotionUse.None),
            ("--mandrake-only", PotionUse.MandrakeOnly),
            ("--standard-only", PotionUse.StandardOnly),
            ("--rare-only", PotionUse.RareOnly)
        };
        var selected = choices.Where(choice => args.Contains(choice.Item1, StringComparer.OrdinalIgnoreCase)).ToArray();
        if (selected.Length > 1)
            throw new ArgumentException("Choose one of --base-only, --mandrake-only, --standard-only, or --rare-only.");
        return selected.Length == 0 ? PotionUse.All : selected[0].Item2;
    }

    private static long? ReadWager(string[] args, GameWageringDefinition wagering, long maximum)
    {
        const string option = "--wager-points=";
        var argument = args.SingleOrDefault(arg => arg.StartsWith(option, StringComparison.OrdinalIgnoreCase));
        if (argument is null) return null;
        if (!long.TryParse(argument.AsSpan(option.Length), out var wager) ||
            wager < wagering.MinimumWagerPoints || wager > maximum ||
            (wager - wagering.MinimumWagerPoints) % (wagering.WagerIncrementPoints ?? 1) != 0 ||
            (wagering.AllowedWagerPoints.Count > 0 && !wagering.AllowedWagerPoints.Contains(wager)))
            throw new ArgumentException("Supply a legal Arcane wager as --wager-points=<points>.");
        return wager;
    }

    private static bool MayActivate(ArcanePotionBottle bottle, PotionUse use) => use switch
    {
        PotionUse.MandrakeOnly => bottle.VariantId is null && bottle.IngredientId == ArcaneTome.MandrakeRoot,
        PotionUse.StandardOnly => bottle.VariantId is null,
        PotionUse.RareOnly => bottle.VariantId is not null,
        _ => true
    };

    private static IEnumerable<ArcanePotionState> BrewRareVariants(long wager)
    {
        var ingredientIds = ArcaneTome.IngredientIds;
        var stockedVariants = new SortedDictionary<string, ArcanePotionState>(StringComparer.Ordinal);
        foreach (var mask in Enumerable.Range(0, 1 << ingredientIds.Count).Where(mask => BitOperations.PopCount((uint)mask) == 4))
        {
            var pairedUnits = ingredientIds.Where((_, index) => (mask & (1 << index)) != 0)
                .SelectMany(id => new[] { id, id }).ToArray();
            var brewed = ArcanePotions.CompleteSpin(ArcanePotions.Empty, pairedUnits, isFreeSpin: false, wagerPoints: wager);
            if (brewed.Bottles.Single().VariantId is { } variant)
                stockedVariants.TryAdd(variant, brewed);
        }
        if (stockedVariants.Count == 0)
            throw new InvalidOperationException("No rare variants brewed from the paired Arcane recipes.");
        return stockedVariants.Values;
    }

    private static string DescribeFrequency(int count, int paidSpins) => count == 0
        ? "no brews in this sample"
        : $"once per {paidSpins / (double)count:N1} spins";

    private static RarePotionStatistics GetStatistics(IDictionary<string, RarePotionStatistics> variants, string id)
    {
        if (variants.TryGetValue(id, out var stats)) return stats;
        return variants[id] = new();
    }

    private sealed class RarePotionStatistics
    {
        public int Brews { get; set; }
        public int Activations { get; set; }
        public PayoutStatistics Payouts { get; } = new();

        public string Describe(int paidSpins)
        {
            var brewing = Brews > 0 ? $"{Brews:N0} brews, {DescribeFrequency(Brews, paidSpins)} | " : string.Empty;
            var spins = Payouts.Spins;
            var activation = $"{Activations:N0} activations, {spins:N0} active spins";
            if (spins == 0) return brewing + activation;
            return brewing + activation +
                $" | Active RTP {Payouts.Rtp:P3} (wand {Payouts.WandRtp:P3}) | SD {Payouts.StandardDeviation:F3}× wager" +
                $" | ≥5× {Payouts.FiveTimes / (double)spins:P3} | ≥10× {Payouts.TenTimes / (double)spins:P3} | Max {Payouts.LargestMultiple:F3}×";
        }
    }

    private sealed class PayoutStatistics
    {
        private long paidPoints;
        private long wageredPoints;
        private long wandPoints;
        private double totalMultiples;
        private double squareMultiples;

        public int Spins { get; private set; }
        public int ZeroWins { get; private set; }
        public int FullReturns { get; private set; }
        public int Profits { get; private set; }
        public int FiveTimes { get; private set; }
        public int TenTimes { get; private set; }
        public int TwentyFiveTimes { get; private set; }
        public double LargestMultiple { get; private set; }
        public decimal Rtp => paidPoints / (decimal)wageredPoints;
        public decimal WandRtp => wandPoints / (decimal)wageredPoints;
        public double StandardDeviation => Math.Sqrt(Math.Max(0, squareMultiples / Spins - Math.Pow(totalMultiples / Spins, 2)));

        public void RecordSpin(SpinPayout payout, long wager)
        {
            var points = payout.TotalPoints;
            var multiple = points / (double)wager;
            Spins++;
            paidPoints = checked(paidPoints + points);
            wageredPoints = checked(wageredPoints + wager);
            wandPoints += payout.Paylines.FirstOrDefault(line => line.PaylineId == ArcaneWandCollector.PaylineId)?.AmountPoints ?? 0;
            totalMultiples += multiple;
            squareMultiples += multiple * multiple;
            LargestMultiple = Math.Max(LargestMultiple, multiple);
            if (points == 0) ZeroWins++;
            if (points >= wager) FullReturns++;
            if (points > wager) Profits++;
            if (points >= 5 * wager) FiveTimes++;
            if (points >= 10 * wager) TenTimes++;
            if (points >= 25 * wager) TwentyFiveTimes++;
        }
    }
}
