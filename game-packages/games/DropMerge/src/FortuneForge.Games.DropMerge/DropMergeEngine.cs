using System.Collections.Immutable;

namespace FortuneForge.Games.DropMerge;

public static class DropMergeEngine
{
    public const int DefaultColumns = 7;
    public const int DefaultRows = 7;
    public const int SmallestSpawnTile = 2;
    public const int FirstBigTile = 1024;
    public const int MovesPerTempoLevel = 18;
    public const int StartingDropDurationMilliseconds = 680;
    public const int DropDurationStepMilliseconds = 24;
    public const int MinimumDropDurationMilliseconds = 520;
    public const int StartingDropTimerMilliseconds = 10000;
    public const double DropTimerReductionPerClear = 0.025;
    public static ImmutableArray<int> SpawnRankWeights { get; } = [18, 24, 24, 18, 16];

    public static DropMergeState Start(uint seed)
    {
        var random = NormalizeSeed(seed);
        var currentTile = DrawTile(ref random, SmallestSpawnTile);
        var nextTile = DrawTile(ref random, SmallestSpawnTile);

        return new DropMergeState(
            DefaultColumns,
            DefaultRows,
            seed,
            random,
            new int[DefaultColumns * DefaultRows].ToImmutableArray(),
            currentTile,
            nextTile,
            0,
            0,
            0,
            0,
            0,
            0,
            SmallestSpawnTile,
            FirstBigTile,
            DropMergePhase.Playing,
            0);
    }

    public static DropMergeTransition Drop(DropMergeState game, int column)
    {
        ValidateState(game);
        if (game.Phase is not DropMergePhase.Playing)
            throw new InvalidOperationException("This Drop Merge game has ended. Start a new game to keep playing.");
        if (column is < 0 or >= DefaultColumns)
            throw new ArgumentOutOfRangeException(nameof(column), "Choose a column from 1 through 7.");

        var tiles = game.Tiles.ToArray();
        var row = LowestEmptyRow(tiles, column);
        if (row < 0)
        {
            return new DropMergeTransition(
                game with { Phase = DropMergePhase.Lost },
                DropMergeEventType.Lost,
                0,
                0,
                column,
                -1,
                0,
                0,
                ImmutableArray<DropMergeMergeStep>.Empty,
                $"Column {column + 1} is full. This run is complete.");
        }

        var dropIndex = row * DefaultColumns + column;
        tiles[dropIndex] = game.CurrentTile;
        var scoreGained = 0;
        var (mergeCount, highestMergedTile, mergeSteps) = ResolveMerges(tiles, dropIndex, ref scoreGained);
        var totalMerges = checked(game.TotalMerges + mergeCount);
        var combo = mergeCount;
        var bestCombo = Math.Max(game.BestCombo, combo);
        var random = game.RandomState;
        var removedTile = 0;
        var introducedTile = 0;
        var smallestTileClearCount = game.SmallestTileClearCount;
        var smallestQueuedTile = game.SmallestQueuedTile;
        var nextBigTile = game.NextBigTile;

        if (highestMergedTile >= nextBigTile)
        {
            removedTile = smallestQueuedTile;
            ApplySmallestTileClear(tiles, removedTile);
            smallestTileClearCount = checked(smallestTileClearCount + 1);
            smallestQueuedTile = checked(smallestQueuedTile * 2);
            nextBigTile = checked(nextBigTile * 2);
        }

        var currentTile = game.NextTile >= smallestQueuedTile
            ? game.NextTile
            : DrawTile(ref random, smallestQueuedTile);
        var nextTile = DrawTile(ref random, smallestQueuedTile);
        var highestTile = tiles.Max();
        var phase = CanDrop(tiles) ? DropMergePhase.Playing : DropMergePhase.Lost;
        var eventType = phase is DropMergePhase.Lost
            ? DropMergeEventType.Lost
            : removedTile > 0
                ? DropMergeEventType.BigTileReached
                : DropMergeEventType.Dropped;
        var message = phase is DropMergePhase.Lost
            ? "The board is full. Start a new run and build a bigger chain."
            : removedTile > 0
                ? $"{highestMergedTile} reached: all {removedTile}s cleared; future tiles now start at {smallestQueuedTile}."
                : mergeCount > 0
                    ? $"{mergeCount} merge{(mergeCount == 1 ? "" : "s")} in the drop."
                    : "Tile locked. Click a column to drop the next tile.";

        return new DropMergeTransition(
            game with
            {
                RandomState = random,
                Tiles = tiles.ToImmutableArray(),
                CurrentTile = currentTile,
                NextTile = nextTile,
                Score = checked(game.Score + scoreGained),
                Moves = checked(game.Moves + 1),
                Combo = combo,
                BestCombo = bestCombo,
                TotalMerges = totalMerges,
                SmallestTileClearCount = smallestTileClearCount,
                SmallestQueuedTile = smallestQueuedTile,
                NextBigTile = nextBigTile,
                Phase = phase,
                HighestTile = highestTile,
            },
            eventType,
            scoreGained,
            mergeCount,
            column,
            row,
            removedTile,
            introducedTile,
            mergeSteps,
            message);
    }

    public static bool CanDrop(DropMergeState game)
    {
        ValidateState(game);
        return CanDrop(game.Tiles);
    }

    public static bool CanDrop(IReadOnlyList<int> tiles)
    {
        if (tiles.Count != DefaultColumns * DefaultRows)
            throw new ArgumentException("The tile list must contain one value per 7×7 board cell.", nameof(tiles));
        return tiles.Any(tile => tile == 0);
    }

    private static (int MergeCount, int HighestMergedTile, ImmutableArray<DropMergeMergeStep> MergeSteps) ResolveMerges(int[] tiles, int droppedIndex, ref int scoreGained)
    {
        var mergeCount = 0;
        var highestMergedTile = 0;
        var preferredIndex = droppedIndex;
        var mergeSteps = ImmutableArray.CreateBuilder<DropMergeMergeStep>();

        while (true)
        {
            var group = FindMergeGroup(tiles, preferredIndex);
            if (group is null)
                return (mergeCount, highestMergedTile, mergeSteps.ToImmutable());

            var sourceValue = tiles[group[0]];
            var sources = group
                .Select(index => new DropMergeMergeTile(index / DefaultColumns, index % DefaultColumns, tiles[index]))
                .ToImmutableArray();
            var mergedValue = sourceValue;
            for (var count = 1; count < group.Count; count++)
                mergedValue = checked(mergedValue * 2);

            var anchorIndex = group.Contains(preferredIndex)
                ? preferredIndex
                : LowestLeftmostIndex(group);
            var anchorColumn = anchorIndex % DefaultColumns;
            var affectedColumns = group.Select(index => index % DefaultColumns).Distinct().ToArray();

            foreach (var index in group)
                tiles[index] = 0;
            foreach (var affectedColumn in affectedColumns)
                ApplyGravity(tiles, affectedColumn);

            var mergedRow = LowestEmptyRow(tiles, anchorColumn);
            preferredIndex = mergedRow * DefaultColumns + anchorColumn;
            tiles[preferredIndex] = mergedValue;
            mergeSteps.Add(new DropMergeMergeStep(
                sources,
                new DropMergeMergeTile(mergedRow, anchorColumn, mergedValue),
                tiles.ToImmutableArray()));
            scoreGained = checked(scoreGained + mergedValue);
            mergeCount = checked(mergeCount + group.Count - 1);
            highestMergedTile = Math.Max(highestMergedTile, mergedValue);
        }
    }

    private static List<int>? FindMergeGroup(IReadOnlyList<int> tiles, int preferredIndex)
    {
        if (preferredIndex >= 0 && preferredIndex < tiles.Count && tiles[preferredIndex] > 0)
        {
            var preferredGroup = ConnectedGroup(tiles, preferredIndex);
            if (preferredGroup.Count > 1)
                return preferredGroup;
        }

        for (var index = 0; index < tiles.Count; index++)
        {
            if (tiles[index] == 0)
                continue;

            var group = ConnectedGroup(tiles, index);
            if (group.Count > 1)
                return group;
        }

        return null;
    }

    private static List<int> ConnectedGroup(IReadOnlyList<int> tiles, int startIndex)
    {
        var targetValue = tiles[startIndex];
        var visited = new bool[tiles.Count];
        var pending = new Queue<int>();
        var group = new List<int>();
        visited[startIndex] = true;
        pending.Enqueue(startIndex);

        while (pending.Count > 0)
        {
            var index = pending.Dequeue();
            group.Add(index);
            var row = index / DefaultColumns;
            var column = index % DefaultColumns;

            Visit(row - 1, column);
            Visit(row + 1, column);
            Visit(row, column - 1);
            Visit(row, column + 1);
        }

        return group;

        void Visit(int row, int column)
        {
            if (row < 0 || row >= DefaultRows || column < 0 || column >= DefaultColumns)
                return;

            var index = row * DefaultColumns + column;
            if (visited[index] || tiles[index] != targetValue)
                return;

            visited[index] = true;
            pending.Enqueue(index);
        }
    }

    private static int LowestLeftmostIndex(IEnumerable<int> indices)
    {
        var selected = -1;
        foreach (var index in indices)
        {
            if (selected < 0 || index / DefaultColumns > selected / DefaultColumns ||
                (index / DefaultColumns == selected / DefaultColumns && index % DefaultColumns < selected % DefaultColumns))
                selected = index;
        }
        return selected;
    }

    private static void ApplySmallestTileClear(int[] tiles, int smallestQueuedTile)
    {
        var removedIndices = Enumerable.Range(0, tiles.Length)
            .Where(index => tiles[index] == smallestQueuedTile)
            .ToArray();
        var affectedColumns = removedIndices.Select(index => index % DefaultColumns).Distinct();
        foreach (var index in removedIndices)
            tiles[index] = 0;
        foreach (var column in affectedColumns)
            ApplyGravity(tiles, column);
    }

    private static void ApplyGravity(int[] tiles, int column)
    {
        var values = new int[DefaultRows];
        var count = 0;
        for (var row = 0; row < DefaultRows; row++)
        {
            var value = tiles[row * DefaultColumns + column];
            if (value > 0)
                values[count++] = value;
        }

        for (var row = 0; row < DefaultRows; row++)
            tiles[row * DefaultColumns + column] = row >= DefaultRows - count ? values[row - (DefaultRows - count)] : 0;
    }

    private static int LowestEmptyRow(IReadOnlyList<int> tiles, int column)
    {
        for (var row = DefaultRows - 1; row >= 0; row--)
        {
            if (tiles[row * DefaultColumns + column] == 0)
                return row;
        }
        return -1;
    }

    private static int DrawTile(ref uint random, int smallestQueuedTile)
    {
        var roll = (int)(NextRandom(ref random) % (uint)SpawnRankWeights.Sum());
        for (var rank = 0; rank < SpawnRankWeights.Length; rank++)
        {
            if (roll < SpawnRankWeights[rank])
                return checked(smallestQueuedTile * (1 << rank));

            roll -= SpawnRankWeights[rank];
        }

        throw new InvalidOperationException("Drop Merge spawn weights must add up to a positive value.");
    }

    private static uint NormalizeSeed(uint seed) => seed == 0 ? 0xA341316Cu : seed;

    private static uint NextRandom(ref uint value)
    {
        unchecked
        {
            value ^= value << 13;
            value ^= value >> 17;
            value ^= value << 5;
            return value;
        }
    }

    private static void ValidateState(DropMergeState game)
    {
        ArgumentNullException.ThrowIfNull(game);
        if (game.Columns != DefaultColumns || game.Rows != DefaultRows)
            throw new ArgumentException("Drop Merge uses a 7×7 board.", nameof(game));
        if (game.Tiles.Length != DefaultColumns * DefaultRows)
            throw new ArgumentException("The tile list must contain one value per 7×7 board cell.", nameof(game));
        if (game.Tiles.Any(tile => tile < 0))
            throw new ArgumentException("Tile values cannot be negative.", nameof(game));
        if (game.CurrentTile <= 0 || game.NextTile <= 0)
            throw new ArgumentException("The current and next tiles must be positive.", nameof(game));
        if (game.NextBigTile < FirstBigTile)
            throw new ArgumentException("The next big tile must be at least 1024.", nameof(game));
        if (game.SmallestQueuedTile < SmallestSpawnTile || game.SmallestQueuedTile % SmallestSpawnTile != 0)
            throw new ArgumentException("The smallest queued tile must remain a valid tile value.", nameof(game));
    }
}
