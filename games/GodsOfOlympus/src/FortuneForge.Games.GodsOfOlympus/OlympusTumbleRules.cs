using FortuneForge.Games.Slots;

namespace FortuneForge.Games.GodsOfOlympus;

public sealed record OlympusTumbleAward(string Symbol, long AmountPoints,
    IReadOnlyList<GridPosition> Positions);
public sealed record OlympusTumbleStep(IReadOnlyList<OlympusTumbleAward> Awards,
    IReadOnlyList<GridPosition> Cleared, IReadOnlyList<IReadOnlyList<string>> Reels,
    IReadOnlyList<IReadOnlyList<int>> DropRows, long AmountPoints);
public sealed record OlympusTumbleOutcome(IReadOnlyList<IReadOnlyList<string>> InitialReels,
    IReadOnlyList<OlympusTumbleStep> Steps, long TotalPoints);

public static class OlympusTumbleRules
{
    public const string LegacyEngineVersion = "olympus-tumble-demo-v1";
    public const string EngineVersionV2 = "olympus-tumble-demo-v2";
    public const string EngineVersion = "olympus-tumble-demo-v3";
    private const int ReelCount = 6;
    private const int RowCount = 5;
    private const int MinimumMatchCount = 8;
    private const int MaximumTumbles = 12;
    private static readonly (string Symbol, decimal Multiplier, int Weight)[] LegacySymbols =
    [
        ("2",.5m,12), ("3",.75m,12), ("4",1m,12), ("5",1.5m,12),
        ("6",2m,12), ("7",3m,12), ("8",3.5m,1), ("A",4m,1), ("J",5m,1)
    ];
    private static readonly (string Symbol, decimal Multiplier, int Weight)[] V2Symbols =
    [
        ("2",.5m,17), ("3",.5m,17), ("4",1m,17), ("5",1.5m,15),
        ("6",3m,11), ("7",4.5m,8), ("8",6m,6), ("A",10m,5), ("J",20m,4)
    ];
    private static readonly (string Symbol, decimal Multiplier, int Weight)[] Symbols =
        V2Symbols.Select(value => value.Symbol == "6"
            ? (value.Symbol, 2.5m, value.Weight) : value).ToArray();
    private static readonly string[][] ShowcaseReels =
    [
        ["2","3","2","8","4"], ["5","3","2","5","3"],
        ["2","A","6","3","2"], ["3","7","J","2","3"],
        ["2","8","3","4","2"], ["A","J","4","6","7"]
    ];
    private static readonly string[] ShowcaseRefills =
        ["3","4","5","6","7","8","A","J","2","3","4","5","6","7","2","3"];

    public static bool SupportsEngineVersion(string? value) =>
        value is LegacyEngineVersion or EngineVersionV2 or EngineVersion;

    public static bool SupportsPaidEngineVersion(string? value) =>
        value is EngineVersionV2 or EngineVersion;

    public static OlympusTumbleOutcome Resolve(string engineVersion, long wagerPoints,
        bool showcase, Func<int, int> nextIndex)
    {
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(wagerPoints);
        ArgumentNullException.ThrowIfNull(nextIndex);
        var (rules, largeMultiplier) = engineVersion switch
        {
            LegacyEngineVersion => (LegacySymbols, 3),
            EngineVersionV2 => (V2Symbols, 4),
            EngineVersion => (Symbols, 4),
            _ => throw new ArgumentException("Unknown Olympus tumble rules.", nameof(engineVersion))
        };
        var pool = rules.SelectMany(value => Enumerable.Repeat(value.Symbol, value.Weight)).ToArray();
        var refillIndex = 0;
        string Draw() => pool[nextIndex(pool.Length)];
        string Refill() => showcase && refillIndex < ShowcaseRefills.Length
            ? ShowcaseRefills[refillIndex++] : Draw();
        var initial = showcase ? ShowcaseReels.Select(reel => reel.ToArray()).ToArray() :
            Enumerable.Range(0, ReelCount).Select(_ => Enumerable.Range(0, RowCount)
                .Select(_ => Draw()).ToArray()).ToArray();
        var reels = initial;
        var steps = new List<OlympusTumbleStep>();
        var total = 0L;
        while (steps.Count < MaximumTumbles)
        {
            var counts = reels.SelectMany(reel => reel).CountBy(symbol => symbol).ToDictionary();
            var winners = rules.Select(rule => (rule.Symbol, rule.Multiplier,
                    Count: counts.GetValueOrDefault(rule.Symbol)))
                .Where(rule => rule.Count >= MinimumMatchCount).ToArray();
            if (winners.Length == 0) break;
            var winnerPositions = winners.ToDictionary(value => value.Symbol,
                _ => new List<GridPosition>());
            var cleared = new List<GridPosition>();
            var nextReels = new string[ReelCount][];
            var dropRows = new int[ReelCount][];
            for (var reel = 0; reel < ReelCount; reel++)
            {
                var survivors = new List<(string Symbol, int Row)>();
                for (var row = 0; row < RowCount; row++)
                {
                    var symbol = reels[reel][row];
                    if (winnerPositions.TryGetValue(symbol, out var positions))
                    {
                        var position = new GridPosition(reel, row);
                        positions.Add(position); cleared.Add(position);
                    }
                    else survivors.Add((symbol, row));
                }
                var gaps = RowCount - survivors.Count;
                var next = new string[RowCount];
                var drops = new int[RowCount];
                for (var row = 0; row < gaps; row++) { next[row] = Refill(); drops[row] = gaps; }
                for (var index = 0; index < survivors.Count; index++)
                {
                    var destination = gaps + index;
                    next[destination] = survivors[index].Symbol;
                    drops[destination] = destination - survivors[index].Row;
                }
                nextReels[reel] = next; dropRows[reel] = drops;
            }
            var awards = winners.Select(rule => new OlympusTumbleAward(rule.Symbol,
                CalculateAward(wagerPoints, rule.Multiplier, rule.Count, largeMultiplier),
                winnerPositions[rule.Symbol])).ToArray();
            var amount = awards.Sum(value => value.AmountPoints);
            total = checked(total + amount); reels = nextReels;
            steps.Add(new(awards, cleared, reels, dropRows, amount));
        }
        return new(initial, steps, total);
    }

    private static long CalculateAward(long wagerPoints, decimal multiplier,
        int count, int largeMultiplier) => checked((long)Math.Round(
        wagerPoints * multiplier * (count >= 12 ? largeMultiplier : count >= 10 ? 2 : 1),
        MidpointRounding.AwayFromZero));
}
