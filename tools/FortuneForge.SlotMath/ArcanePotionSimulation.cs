// Edited: 2026-11-18T18:09:19-06:00 | Computer: Vinnabok
// Edited: 2026-11-18T18:01:37-06:00 | Computer: Vinnabok
using FortuneForge.Games.ArcaneArchives;

internal static class ArcanePotionSimulation
{
    private static readonly HashSet<string> IngredientIds = ArcaneTome.IngredientIds.ToHashSet(StringComparer.Ordinal);

    public static void Run(int paidSpins, int seed, decimal releaseRtpCeiling, bool switchWagers)
    {
        (long Wager, long? PotionWager, bool UsePotions)[] scenarios =
            [(12, null, false), (12, null, true), (100, null, false), (100, null, true)];
        if (switchWagers)
        {
            var wagering = ArcaneArchivesEngine.CreateGame().Wagering;
            var max = wagering.MaximumWagerPoints
                ?? throw new InvalidOperationException("A maximum wager is required for the Arcane wager-switch test.");
            scenarios = [(wagering.MinimumWagerPoints, max, true)];
        }
        Console.WriteLine($"Arcane Archives math over {paidSpins:N0} paid spins per scenario, seed {seed}. Tome Pages are enabled.");
        var results = scenarios.AsParallel().AsOrdered().Select(scenario =>
            RunWager(scenario.Wager, scenario.PotionWager, paidSpins, seed, scenario.UsePotions)).ToArray();
        foreach (var result in results)
            Console.WriteLine(result.Report);
        if (results.Any(result => result.Rtp >= releaseRtpCeiling))
            throw new InvalidOperationException($"An Arcane scenario met or exceeded the RTP ceiling of {releaseRtpCeiling:P0}.");
    }

    private static (decimal Rtp, string Report) RunWager(
        long baseWager, long? potionWager, int paidSpins, int seed, bool usePotions)
    {
        var engine = new ArcaneArchivesEngine(new SeededRandomIndexSource(seed));
        var tome = ArcaneTome.Empty;
        var pages = ArcaneTome.ToView(tome)!.Pages;
        var potions = ArcanePotions.Empty;
        var pityMisses = 0;
        var potionSpins = 0;
        var tomeActivations = 0;
        var zeroWins = 0;
        var fullReturns = 0;
        var profits = 0;
        var fiveTimes = 0;
        var tenTimes = 0;
        var twentyFiveTimes = 0;
        var wandSpins = 0;
        var ingredientSpins = new int[4];
        long paidPoints = 0;
        long wageredPoints = 0;
        double largestPayoutMultiple = 0;
        double sumMultiples = 0;
        double sumSquaredMultiples = 0;
        for (var index = 0; index < paidSpins; index++)
        {
            if (tome is { ActivePage: null, GrimoireReady: true })
            {
                var page = pages[tomeActivations % pages.Count];
                tome = ArcaneTome.SelectPage(tome, page.Id, activate: true);
                tomeActivations++;
            }
            if (usePotions && potions.Active is null && potions.Bottles.Count > 0)
                potions = ArcanePotions.Use(potions, potions.Bottles[0].Id);
            var potion = ArcanePotions.ForSpin(potions, isFreeSpin: false);
            if (potion is not null)
                potionSpins++;

            var wager = potion is null ? baseWager : potionWager ?? baseWager;
            var spin = engine.SpinTome(wager, "arcane-potion-math", 0, pityMisses,
                false, tome.ActivePage is not null, potion);
            if (usePotions)
            {
                var units = ArcaneTome.OrderedIngredientUnits(tome, spin.Reels, isFreeSpin: false);
                potions = ArcanePotions.CompleteSpin(potions, units, isFreeSpin: false);
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

            var payout = spin.Payout.TotalPoints;
            var multiple = payout / (double)wager;
            paidPoints = checked(paidPoints + payout);
            wageredPoints = checked(wageredPoints + wager);
            sumMultiples += multiple;
            sumSquaredMultiples += multiple * multiple;
            largestPayoutMultiple = Math.Max(largestPayoutMultiple, multiple);
            if (payout == 0) zeroWins++;
            if (payout >= wager) fullReturns++;
            if (payout > wager) profits++;
            if (payout >= 5 * wager) fiveTimes++;
            if (payout >= 10 * wager) tenTimes++;
            if (payout >= 25 * wager) twentyFiveTimes++;
        }

        var rtp = paidPoints / (decimal)wageredPoints;
        var meanMultiple = sumMultiples / paidSpins;
        var deviation = Math.Sqrt(Math.Max(0, sumSquaredMultiples / paidSpins - meanMultiple * meanMultiple));
        decimal Rate(int count) => count / (decimal)paidSpins;
        var mode = usePotions ? "Potions auto-used" : "Base";
        var wagerLabel = potionWager is { } high ? $"{baseWager} points between potions / {high} points while active" : $"{baseWager} points";
        var report = $"{wagerLabel} | {mode} | RTP {rtp:P3} | Payout SD {deviation:F3}× wager | " +
            $"Hit {Rate(paidSpins - zeroWins):P3} | Zero {Rate(zeroWins):P3} | Full return (≥1×) {Rate(fullReturns):P3} | Profit (>1×) {Rate(profits):P3} | " +
            $"≥5× {Rate(fiveTimes):P3} | ≥10× {Rate(tenTimes):P3} | ≥25× {twentyFiveTimes:N0} spins | Max {largestPayoutMultiple:F3}×\n" +
            $"  Ingredient landings (0/1/2/3): {string.Join(" / ", ingredientSpins.Select(count => $"{Rate(count):P3}"))} | Wand visible {Rate(wandSpins):P3}";
        if (usePotions)
            report += $" | Potion coverage {Rate(potionSpins):P3} | Bottles {potions.NextBottleId - 1:N0}";
        return (rtp, report);
    }
}
