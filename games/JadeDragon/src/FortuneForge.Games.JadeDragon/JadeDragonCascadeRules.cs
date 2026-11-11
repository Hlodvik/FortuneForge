using FortuneForge.Games.Slots;

namespace FortuneForge.Games.JadeDragon;

public sealed record JadeDragonCascadeSymbol(string Id, int Weight, IReadOnlyList<decimal>? Pays);

/// <summary>A matching symbol's exact award before the round cap and point rounding.</summary>
public sealed record JadeDragonCascadeAward(string Symbol, int MatchLength, int Ways,
    IReadOnlyList<GridPosition> Positions, decimal UnroundedPoints);

/// <summary>A paid board and its successor. A terminal step clears no cells and has zero drops.</summary>
public sealed record JadeDragonCascadeStep(IReadOnlyList<JadeDragonCascadeAward> Awards,
    IReadOnlyList<GridPosition> Cleared, IReadOnlyList<IReadOnlyList<string>> Reels,
    IReadOnlyList<IReadOnlyList<int>> DropRows, long AmountPoints, int Multiplier);

public sealed record JadeDragonCascadeOutcome(IReadOnlyList<IReadOnlyList<string>> InitialReels,
    IReadOnlyList<JadeDragonCascadeStep> Steps, long TotalPoints, int FreeSpinsAwarded);

/// <summary>Server resolution of Jade Dragon's 1,024-ways cascade engine.</summary>
public static class JadeDragonCascadeRules
{
    public const string Version = "jade-dragon-cascade-v2";
    public const int ReelCount = 5;
    public const int RowCount = 4;
    public const int Ways = 1024;
    public const int WagerPointValueInCents = 25;
    public const int PayoutPointValueInCents = 1;
    public const int MinimumWagerPoints = 2;
    public const int MaximumWagerPoints = 2_000;
    public const int WagerIncrementPoints = 2;
    public const int MaximumWinningSteps = 30;
    public const int MaximumWinMultiplier = 5000;
    public const decimal PayoutScale = .4675m;
    // The initial board plus at most 29 refills of a completely cleared board.
    public const int MaximumDraws = ReelCount * RowCount * MaximumWinningSteps;
    public const string PearlSymbolId = "pearl";
    public const int RequiredPearls = 3;
    public const int BaseFreeSpins = 10;
    public const int RetriggerFreeSpins = 5;
    public const int SpinsPerExtraPearl = 2;

    public const int MinimumMatchLength = 3;
    private const decimal PayoutWagerDivisor = 20m;

    public static IReadOnlyList<JadeDragonCascadeSymbol> Symbols { get; } =
        Array.AsReadOnly<JadeDragonCascadeSymbol>(
        [
            new("dragon", 10, Array.AsReadOnly<decimal>([2m, 6m, 15m])),
            new("phoenix", 11, Array.AsReadOnly<decimal>([1.5m, 4m, 10m])),
            new("lotus", 14, Array.AsReadOnly<decimal>([1m, 3m, 8m])),
            new("jade", 15, Array.AsReadOnly<decimal>([.8m, 2m, 6m])),
            new("coin", 16, Array.AsReadOnly<decimal>([.6m, 1.5m, 4m])),
            new("bamboo", 16, Array.AsReadOnly<decimal>([.5m, 1.2m, 3m])),
            new("knot", 14, Array.AsReadOnly<decimal>([.4m, 1m, 2.5m])),
            new(PearlSymbolId, 4, null),
        ]);

    public static IReadOnlyList<int> BaseMultipliers { get; } = Array.AsReadOnly<int>([1, 2, 3, 5]);
    public static IReadOnlyList<int> BonusMultipliers { get; } = Array.AsReadOnly<int>([2, 4, 6, 10]);

    private static readonly string[] DrawPool = Symbols
        .SelectMany(symbol => Enumerable.Repeat(symbol.Id, symbol.Weight)).ToArray();

    internal static List<string> CopyDrawPool() => [.. DrawPool];

    public static void ValidateWager(long wagerPoints)
    {
        if (wagerPoints is < MinimumWagerPoints or > MaximumWagerPoints ||
            wagerPoints % WagerIncrementPoints != 0)
            throw new ArgumentOutOfRangeException(nameof(wagerPoints), wagerPoints,
                "The Jade Dragon wager must be an even number of points from 2 to 2,000.");
    }

    /// <summary>
    /// Draws in reel order and top-to-bottom row order. The caller supplies cryptographic
    /// draws for live play or the recorded indexes for replay.
    /// </summary>
    public static JadeDragonCascadeOutcome Resolve(long wagerPoints, bool isFreeSpin, Func<int, int> next)
    {
        ValidateWager(wagerPoints);
        ArgumentNullException.ThrowIfNull(next);

        var initialReels = new string[ReelCount][];
        for (var reel = 0; reel < ReelCount; reel++)
        {
            initialReels[reel] = new string[RowCount];
            for (var row = 0; row < RowCount; row++) initialReels[reel][row] = Draw(next);
        }

        var reels = initialReels;
        var steps = new List<JadeDragonCascadeStep>();
        var multipliers = isFreeSpin ? BonusMultipliers : BaseMultipliers;
        var wagerCents = checked(wagerPoints * WagerPointValueInCents);
        var payoutLimit = checked(wagerCents * MaximumWinMultiplier);
        var totalPoints = 0L;

        for (var step = 0; step < MaximumWinningSteps; step++)
        {
            var multiplier = multipliers[Math.Min(step, multipliers.Count - 1)];
            var awards = Evaluate(reels, wagerCents, multiplier);
            if (awards.Count == 0) break;

            // Even the smallest winning combination credits a cent at the
            // minimum wager. The cap still applies to the entire round.
            var roundedPoints = Math.Max(1L, checked((long)Math.Round(
                awards.Sum(award => award.UnroundedPoints), MidpointRounding.AwayFromZero)));
            var stepPoints = Math.Min(payoutLimit - totalPoints, roundedPoints);
            totalPoints += stepPoints;

            if (totalPoints == payoutLimit || step + 1 == MaximumWinningSteps)
            {
                var noDrops = Enumerable.Range(0, ReelCount).Select(_ => new int[RowCount]).ToArray();
                steps.Add(new(awards, [], reels, noDrops, stepPoints, multiplier));
                break;
            }

            var cleared = awards.SelectMany(award => award.Positions).Distinct()
                .OrderBy(position => position.Reel).ThenBy(position => position.Row).ToArray();
            var (refilled, dropRows) = Refill(reels, cleared, next);
            steps.Add(new(awards, cleared, refilled, dropRows, stepPoints, multiplier));
            reels = refilled;
        }

        var pearls = reels.Sum(reel => reel.Count(symbol => symbol == PearlSymbolId));
        var freeSpins = pearls < RequiredPearls ? 0 :
            (isFreeSpin ? RetriggerFreeSpins : BaseFreeSpins) + (pearls - RequiredPearls) * SpinsPerExtraPearl;
        return new(initialReels, steps, totalPoints, freeSpins);
    }

    private static List<JadeDragonCascadeAward> Evaluate(string[][] reels, long wagerCents, int multiplier)
    {
        var awards = new List<JadeDragonCascadeAward>();
        foreach (var symbol in Symbols)
        {
            if (symbol.Pays is not { } pays) continue;
            var positions = new List<GridPosition>();
            var matchLength = 0;
            var ways = 1;
            for (var reel = 0; reel < ReelCount; reel++)
            {
                var count = 0;
                for (var row = 0; row < RowCount; row++)
                {
                    if (reels[reel][row] != symbol.Id) continue;
                    count++;
                    positions.Add(new(reel, row));
                }
                if (count == 0) break;
                matchLength++;
                ways *= count;
            }

            if (matchLength < MinimumMatchLength) continue;
            var exactPoints = wagerCents / PayoutWagerDivisor * pays[matchLength - MinimumMatchLength] *
                ways * multiplier * PayoutScale;
            awards.Add(new(symbol.Id, matchLength, ways, positions.ToArray(), exactPoints));
        }
        return awards;
    }

    private static (string[][] Reels, int[][] DropRows) Refill(string[][] reels,
        IReadOnlyList<GridPosition> cleared, Func<int, int> next)
    {
        var removed = cleared.ToHashSet();
        var refilled = new string[ReelCount][];
        var dropRows = new int[ReelCount][];
        for (var reel = 0; reel < ReelCount; reel++)
        {
            var survivors = Enumerable.Range(0, RowCount)
                .Where(row => !removed.Contains(new(reel, row))).ToArray();
            var gaps = RowCount - survivors.Length;
            var column = new string[RowCount];
            var drops = new int[RowCount];
            for (var row = 0; row < gaps; row++)
            {
                column[row] = Draw(next);
                drops[row] = gaps;
            }
            for (var index = 0; index < survivors.Length; index++)
            {
                var row = gaps + index;
                column[row] = reels[reel][survivors[index]];
                drops[row] = row - survivors[index];
            }
            refilled[reel] = column;
            dropRows[reel] = drops;
        }
        return (refilled, dropRows);
    }

    private static string Draw(Func<int, int> next)
    {
        var index = next(DrawPool.Length);
        if (index < 0 || index >= DrawPool.Length)
            throw new ArgumentOutOfRangeException(nameof(next), index,
                "The Jade Dragon draw index is outside the weighted symbol pool.");
        return DrawPool[index];
    }
}
