using FortuneForge.Games.Slots;

namespace FortuneForge.Games.Slots.Runtime;

public sealed partial class ClassicSlotEngine
{
    private static readonly string[] ReplacementSymbols = ["2", "3", "4", "5", "6"];
    private static readonly string[] CollectionSymbols =
        ["SEAL_SYNC", "SEAL_ROWS", "SEAL_PAW", "SEAL_RAND"];

    private ReelOutcome ApplyFeatureSymbols(ReelOutcome outcome, GameDefinition game,
        long energyBalance, string? featureMode)
    {
        var reels = outcome.VisibleReels.Select(reel => reel.ToArray()).ToArray();
        if (profile.HasFeatureMode(featureMode, "sync") && reels.Length > 1)
        {
            var source = Next(reels.Length);
            var destination = (source + 1 + Next(reels.Length - 1)) % reels.Length;
            reels[destination] = reels[source].ToArray();
        }

        if (profile.UsesDirectValueTokens && profile.HasFeatureMode(featureMode, "rand"))
        {
            var reel = reels[Next(reels.Length)];
            for (var row = 0; row < reel.Length; row++) reel[row] = PickValueSymbol();
        }
        else if (profile.UsesDirectValueTokens)
        {
            var roll = Next(100);
            var count = options.DirectValueCountFromRoll?.Invoke(roll) ?? roll switch
            {
                < 20 => 0, < 80 => 1, < 96 => 2, _ => 3
            };
            Inject(reels, Math.Max(0, count), PickValueSymbol);
        }
        else if (profile.HasFeatureMode(featureMode, "rand"))
        {
            var reel = reels[Next(reels.Length)];
            for (var row = 0; row < reel.Length; row++) reel[row] = "ACE";
        }

        if (profile.UsesDirectValueTokens || options.AlwaysUsesPawCollectors)
            Inject(reels, RollPawCount(featureMode), () => "PAW");
        else if (profile.HasFeatureMode(featureMode, "paw"))
            Inject(reels, 2, () => "ACE");

        Inject(reels, Next(100) switch { < 48 => 0, < 88 => 1, < 98 => 2, _ => 3 },
            () => "BANANA");
        if (profile.UsesCollections)
        {
            var chance = options.CollectionAppearanceChance?.Invoke(energyBalance) ?? energyBalance switch
            {
                >= 75 => 67, >= 50 => 50, >= 25 => 40, _ => 33
            };
            if (Next(100) < Math.Clamp(chance, 0, 100))
                Inject(reels, Next(25) == 0 ? 2 : 1,
                    () => CollectionSymbols[Next(CollectionSymbols.Length)]);
        }
        return outcome with
        {
            VisibleReels = reels.Select(reel => (IReadOnlyList<string>)reel).ToArray()
        };
    }

    private int RollPawCount(string? featureMode)
    {
        if (profile.HasFeatureMode(featureMode, "paw"))
        {
            if (options.HeavyPawFeature) return Next(4) + 2;
            return Next(6) switch { 0 => 2, 1 => 1, _ => 0 };
        }
        if (Next(777) == 0) return 2;
        return Next(Math.Max(1, options.SinglePawDivisor)) == 0 ? 1 : 0;
    }

    private void Inject(IReadOnlyList<string[]> reels, int count, Func<string> symbolFactory)
    {
        for (var index = 0; index < count; index++)
        {
            var candidates = reels.SelectMany((reel, reelIndex) =>
                    reel.Select((symbol, rowIndex) => (Symbol: symbol,
                        Position: new GridPosition(reelIndex, rowIndex))))
                .Where(value => ReplacementSymbols.Contains(value.Symbol, StringComparer.Ordinal))
                .Select(value => value.Position).ToArray();
            if (candidates.Length == 0) return;
            var position = candidates[Next(candidates.Length)];
            reels[position.Reel][position.Row] = symbolFactory();
        }
    }

    private string PickValueSymbol() => Next(100) switch
    {
        < 28 => "RAND_05", < 55 => "RAND_1", < 74 => "RAND_15", < 88 => "RAND_2",
        < 96 => "RAND_3", < 99 => "RAND_4", _ => "RAND_5"
    };

    private static ClassicFeaturePayout CalculateFeaturePayout(
        IReadOnlyList<IReadOnlyList<string>> reels, long wagerPoints,
        decimal payoutMultiplier)
    {
        var values = Positions(reels).Select(position => (Position: position,
                Multiplier: ValueMultiplier(reels[position.Reel][position.Row])))
            .Where(value => value.Multiplier > 0).ToArray();
        var paws = Positions(reels).Where(position =>
            reels[position.Reel][position.Row] == "PAW").ToArray();
        var collectorPoints = 0L;
        PaylinePayout? collector = null;
        if (paws.Length > 0 && values.Length > 0)
        {
            var multiplier = values.Sum(value => value.Multiplier) * Math.Min(paws.Length, 2);
            collectorPoints = Scale(wagerPoints, multiplier, payoutMultiplier);
            collector = new PaylinePayout(901, collectorPoints,
            [
                new PaidMatch(new SymbolMatch(901, "PAW", paws.Length + values.Length,
                    paws.Concat(values.Select(value => value.Position)).ToArray(), []),
                    checked((long)Math.Ceiling(multiplier)), collectorPoints)
            ]);
        }
        var patterns = CalculatePatternPayouts(reels, wagerPoints, payoutMultiplier);
        return new(paws.Length, collectorPoints, patterns.Sum(value => value.AmountPoints),
            collector is null ? patterns : [collector, .. patterns]);
    }

    public static IReadOnlyList<PaylinePayout> CalculatePatternPayouts(
        IReadOnlyList<IReadOnlyList<string>> reels, long wagerPoints,
        decimal payoutMultiplier, string symbolId = "BANANA", long patternMultiplier = 3)
    {
        var payouts = new List<PaylinePayout>();
        var seen = new HashSet<string>(StringComparer.Ordinal);
        var rows = reels.Count == 0 ? 0 : reels.Max(reel => reel.Count);
        var paylineId = 801;
        void Add(IReadOnlyList<GridPosition> positions)
        {
            if (positions.Any(position => position.Reel < 0 || position.Reel >= reels.Count ||
                position.Row < 0 || position.Row >= reels[position.Reel].Count ||
                reels[position.Reel][position.Row] != symbolId)) return;
            var key = string.Join('|', positions.OrderBy(position => position.Reel)
                .ThenBy(position => position.Row).Select(position => $"{position.Reel}.{position.Row}"));
            if (!seen.Add(key)) return;
            var amount = Scale(wagerPoints, patternMultiplier, payoutMultiplier);
            var id = paylineId++;
            payouts.Add(new(id, amount,
                [new PaidMatch(new SymbolMatch(id, symbolId, 3, positions.ToArray(), []),
                    patternMultiplier, amount)]));
        }
        for (var reel = 0; reel < reels.Count; reel++)
            for (var row = 0; row <= reels[reel].Count - 3; row++)
                Add([new(reel,row), new(reel,row + 1), new(reel,row + 2)]);
        for (var row = 0; row < rows; row++)
            for (var reel = 0; reel <= reels.Count - 3; reel++)
                Add([new(reel,row), new(reel + 1,row), new(reel + 2,row)]);
        for (var reel = 0; reel <= reels.Count - 3; reel++)
        {
            for (var row = 0; row <= rows - 3; row++)
                Add([new(reel,row), new(reel + 1,row + 1), new(reel + 2,row + 2)]);
            for (var row = 2; row < rows; row++)
                Add([new(reel,row), new(reel + 1,row - 1), new(reel + 2,row - 2)]);
        }
        return payouts;
    }

    private static SpinPayout AddFeaturePayout(SpinPayout payout, ClassicFeaturePayout feature)
    {
        if (feature.Paylines.Count == 0 && feature.OffPaylinePoints == 0) return payout;
        var paylines = payout.Paylines.Concat(feature.Paylines).ToArray();
        return new(checked(paylines.Sum(line => line.AmountPoints) + feature.OffPaylinePoints), paylines);
    }

    private static IReadOnlyList<GridPosition> Positions(
        IReadOnlyList<IReadOnlyList<string>> reels) => reels.SelectMany((reel, reelIndex) =>
        reel.Select((_, row) => new GridPosition(reelIndex, row))).ToArray();

    private static decimal ValueMultiplier(string symbol) => symbol switch
    {
        "RAND_05" => 0.5m, "RAND_1" => 1m, "RAND_15" => 1.5m,
        "RAND_2" => 2m, "RAND_3" => 3m, "RAND_4" => 4m, "RAND_5" => 5m, _ => 0m
    };

    public static long Scale(long wagerPoints, decimal multiplier,
        decimal payoutMultiplier = 1m) => checked((long)Math.Round(
        wagerPoints * multiplier * payoutMultiplier, MidpointRounding.AwayFromZero));

    private static Dictionary<string, int> CountCollections(
        IReadOnlyList<IReadOnlyList<string>> reels) => CollectionSymbols
        .Select(symbol => (Symbol: symbol, Count: CountSymbols(reels, symbol)))
        .Where(value => value.Count > 0)
        .ToDictionary(value => value.Symbol, value => value.Count, StringComparer.Ordinal);

    private static int CountSymbols(IReadOnlyList<IReadOnlyList<string>> reels,
        string? symbol) => symbol is null ? 0 : reels.Sum(reel => reel.Count(value => value == symbol));
}
