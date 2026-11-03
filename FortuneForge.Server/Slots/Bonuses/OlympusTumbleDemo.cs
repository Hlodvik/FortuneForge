using FortuneForge.Server.Slots.Models;

namespace FortuneForge.Server.Slots.Bonuses;

public sealed record OlympusTumbleAward(
    string Symbol,
    long AmountPoints,
    IReadOnlyList<GridPosition> Positions);

public sealed record OlympusTumbleStep(
    IReadOnlyList<OlympusTumbleAward> Awards,
    IReadOnlyList<GridPosition> Cleared,
    IReadOnlyList<IReadOnlyList<string>> Reels,
    IReadOnlyList<IReadOnlyList<int>> DropRows,
    long AmountPoints);

public sealed record OlympusTumbleOutcome(
    IReadOnlyList<IReadOnlyList<string>> InitialReels,
    IReadOnlyList<OlympusTumbleStep> Steps,
    long TotalPoints);

public static class OlympusTumbleDemo
{
    public const string LegacyEngineVersion = "olympus-tumble-demo-v1";
    public const string EngineVersionV2 = "olympus-tumble-demo-v2";
    public const string EngineVersion = "olympus-tumble-demo-v3";

    private const int ReelCount = 6;
    private const int RowCount = 5;
    private const int MinimumMatchCount = 8;
    private const int MaximumTumbles = 12;

    // Frozen for replay of demo rounds recorded before the RTP tuning.
    private static readonly (string Symbol, decimal Multiplier, int DrawWeight)[] LegacySymbols =
    [
        ("2", 0.5m, 12),
        ("3", 0.75m, 12),
        ("4", 1m, 12),
        ("5", 1.5m, 12),
        ("6", 2m, 12),
        ("7", 3m, 12),
        ("8", 3.5m, 1),
        ("A", 4m, 1),
        ("J", 5m, 1)
    ];

    // Half-wager steps keep each award exact for every allowed even-point wager.
    private static readonly (string Symbol, decimal Multiplier, int DrawWeight)[] Version2Symbols =
    [
        ("2", 0.5m, 17),
        ("3", 0.5m, 17),
        ("4", 1m, 17),
        ("5", 1.5m, 15),
        ("6", 3m, 11),
        ("7", 4.5m, 8),
        ("8", 6m, 6),
        ("A", 10m, 5),
        ("J", 20m, 4)
    ];

    // Version 3 keeps the draw pool and match tiers, and pays 2.5x on symbol 6.
    private static readonly (string Symbol, decimal Multiplier, int DrawWeight)[] Symbols =
        Version2Symbols.Select(rule => rule.Symbol == "6"
            ? (rule.Symbol, 2.5m, rule.DrawWeight)
            : rule).ToArray();

    private static readonly string[] LegacyDrawPool = BuildDrawPool(LegacySymbols);
    private static readonly string[] Version2DrawPool = BuildDrawPool(Version2Symbols);

    private static readonly string[][] ShowcaseReels =
    [
        ["2", "3", "2", "8", "4"],
        ["5", "3", "2", "5", "3"],
        ["2", "A", "6", "3", "2"],
        ["3", "7", "J", "2", "3"],
        ["2", "8", "3", "4", "2"],
        ["A", "J", "4", "6", "7"]
    ];

    private static readonly string[] ShowcaseRefills =
        ["3", "4", "5", "6", "7", "8", "A", "J", "2", "3", "4", "5", "6", "7", "2", "3"];

    public static bool SupportsEngineVersion(string? engineVersion) =>
        engineVersion == LegacyEngineVersion || SupportsPaidEngineVersion(engineVersion);

    internal static bool SupportsPaidEngineVersion(string? engineVersion) =>
        engineVersion is EngineVersionV2 or EngineVersion;

    // Classic collection awards can lock an average between two cash wagers.
    internal static bool IsLegacyCarryWager(GameWageringDefinition wagering, long wagerPoints) =>
        wagerPoints >= wagering.MinimumWagerPoints &&
        (wagering.MaximumWagerPoints is not { } maximum || wagerPoints <= maximum);

    public static OlympusTumbleOutcome Resolve(long wagerPoints, bool showcase, Func<int, int> nextIndex) =>
        Resolve(EngineVersion, wagerPoints, showcase, nextIndex);

    public static OlympusTumbleOutcome Resolve(
        string engineVersion, long wagerPoints, bool showcase, Func<int, int> nextIndex)
    {
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(wagerPoints);
        ArgumentNullException.ThrowIfNull(nextIndex);

        var (symbols, drawPool, largeMatchMultiplier) = engineVersion switch
        {
            LegacyEngineVersion => (LegacySymbols, LegacyDrawPool, 3),
            EngineVersionV2 => (Version2Symbols, Version2DrawPool, 4),
            EngineVersion => (Symbols, Version2DrawPool, 4),
            _ => throw new ArgumentException("Unknown Olympus tumble rules.", nameof(engineVersion))
        };
        var showcaseRefillIndex = 0;
        string Draw() => drawPool[nextIndex(drawPool.Length)];
        string Refill() => showcase && showcaseRefillIndex < ShowcaseRefills.Length
            ? ShowcaseRefills[showcaseRefillIndex++]
            : Draw();

        var initialReels = showcase
            ? ShowcaseReels.Select(reel => reel.ToArray()).ToArray()
            : Enumerable.Range(0, ReelCount)
                .Select(_ => Enumerable.Range(0, RowCount).Select(_ => Draw()).ToArray())
                .ToArray();
        var reels = initialReels;
        var totalPoints = 0L;
        var steps = new List<OlympusTumbleStep>();

        while (steps.Count < MaximumTumbles)
        {
            var counts = reels.SelectMany(reel => reel).CountBy(symbol => symbol).ToDictionary();
            var winners = symbols
                .Select(rule => (rule.Symbol, rule.Multiplier, Count: counts.GetValueOrDefault(rule.Symbol)))
                .Where(rule => rule.Count >= MinimumMatchCount)
                .ToArray();
            if (winners.Length == 0)
                break;

            var winnerPositions = winners.ToDictionary(rule => rule.Symbol, _ => new List<GridPosition>());
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
                        positions.Add(position);
                        cleared.Add(position);
                    }
                    else
                    {
                        survivors.Add((symbol, row));
                    }
                }

                var gapCount = RowCount - survivors.Count;
                var nextReel = new string[RowCount];
                var reelDrops = new int[RowCount];
                for (var row = 0; row < gapCount; row++)
                {
                    nextReel[row] = Refill();
                    reelDrops[row] = gapCount;
                }
                for (var index = 0; index < survivors.Count; index++)
                {
                    var destination = gapCount + index;
                    nextReel[destination] = survivors[index].Symbol;
                    reelDrops[destination] = destination - survivors[index].Row;
                }
                nextReels[reel] = nextReel;
                dropRows[reel] = reelDrops;
            }

            var awards = winners.Select(rule => new OlympusTumbleAward(
                rule.Symbol,
                CalculateAward(wagerPoints, rule.Multiplier, rule.Count, largeMatchMultiplier),
                winnerPositions[rule.Symbol].ToArray())).ToArray();
            var amountPoints = awards.Sum(award => award.AmountPoints);
            totalPoints = checked(totalPoints + amountPoints);
            reels = nextReels;
            steps.Add(new OlympusTumbleStep(awards, cleared.ToArray(), reels, dropRows, amountPoints));
        }

        return new OlympusTumbleOutcome(initialReels, steps.ToArray(), totalPoints);
    }

    private static string[] BuildDrawPool((string Symbol, decimal Multiplier, int DrawWeight)[] symbols) =>
        symbols.SelectMany(rule => Enumerable.Repeat(rule.Symbol, rule.DrawWeight)).ToArray();

    private static long CalculateAward(long wagerPoints, decimal multiplier, int count, int largeMatchMultiplier)
    {
        var countMultiplier = count >= 12 ? largeMatchMultiplier : count >= 10 ? 2 : 1;
        return checked((long)Math.Round(
            wagerPoints * multiplier * countMultiplier,
            MidpointRounding.AwayFromZero));
    }
}
