using System.Collections.Immutable;

namespace FortuneForge.Games.TwentyFortyEight;

public static class TwentyFortyEightEngine
{
    public const int DefaultSize = 4;
    public const int TargetTile = 2048;

    public static TwentyFortyEightState Start(uint seed, int size = DefaultSize)
    {
        ValidateSize(size);

        var tiles = new int[size * size];
        var random = NormalizeSeed(seed);
        AddRandomTile(tiles, ref random);
        AddRandomTile(tiles, ref random);

        return new TwentyFortyEightState(
            size,
            seed,
            random,
            tiles.ToImmutableArray(),
            0,
            0,
            TwentyFortyEightPhase.Playing,
            tiles.Max());
    }

    public static TwentyFortyEightTransition Move(
        TwentyFortyEightState game,
        TwentyFortyEightDirection direction)
    {
        ValidateState(game);
        if (game.Phase is not TwentyFortyEightPhase.Playing)
            throw new InvalidOperationException("This 2048 game has ended. Start a new game to keep playing.");

        var tiles = game.Tiles.ToArray();
        var scoreGained = 0;
        var changed = false;

        for (var line = 0; line < game.Size; line++)
        {
            var positions = PositionsForLine(game.Size, direction, line).ToArray();
            var original = positions.Select(position => tiles[position]).ToArray();
            var collapsed = Collapse(original, out var lineScore);
            scoreGained = checked(scoreGained + lineScore);

            for (var index = 0; index < positions.Length; index++)
            {
                if (tiles[positions[index]] != collapsed[index])
                    changed = true;
                tiles[positions[index]] = collapsed[index];
            }
        }

        if (!changed)
        {
            return new TwentyFortyEightTransition(
                game,
                TwentyFortyEightEventType.NoMove,
                0,
                "That move is not possible.");
        }

        var random = game.RandomState;
        AddRandomTile(tiles, ref random);
        var highestTile = tiles.Max();
        var phase = highestTile >= TargetTile
            ? TwentyFortyEightPhase.Won
            : HasAvailableMove(tiles, game.Size)
                ? TwentyFortyEightPhase.Playing
                : TwentyFortyEightPhase.Lost;
        var eventType = phase switch
        {
            TwentyFortyEightPhase.Won => TwentyFortyEightEventType.Won,
            TwentyFortyEightPhase.Lost => TwentyFortyEightEventType.Lost,
            _ => TwentyFortyEightEventType.Moved,
        };
        var message = phase switch
        {
            TwentyFortyEightPhase.Won => "2048 reached. You win!",
            TwentyFortyEightPhase.Lost => "No moves remain. Start a new game to try again.",
            _ => scoreGained > 0 ? $"Merged tiles for +{scoreGained}." : "Board shifted.",
        };

        return new TwentyFortyEightTransition(
            game with
            {
                RandomState = random,
                Tiles = tiles.ToImmutableArray(),
                Score = checked(game.Score + scoreGained),
                Moves = checked(game.Moves + 1),
                Phase = phase,
                HighestTile = highestTile,
            },
            eventType,
            scoreGained,
            message);
    }

    public static bool CanMove(TwentyFortyEightState game)
    {
        ValidateState(game);
        return CanMove(game.Tiles.ToArray(), game.Size);
    }

    public static bool CanMove(IReadOnlyList<int> tiles, int size = DefaultSize)
    {
        ValidateSize(size);
        if (tiles.Count != size * size)
            throw new ArgumentException("The tile list must contain one value per board cell.", nameof(tiles));

        for (var index = 0; index < tiles.Count; index++)
        {
            if (tiles[index] == 0)
                return true;
            var row = index / size;
            var column = index % size;
            if (column + 1 < size && tiles[index] == tiles[index + 1])
                return true;
            if (row + 1 < size && tiles[index] == tiles[index + size])
                return true;
        }

        return false;
    }

    private static int[] Collapse(IReadOnlyList<int> line, out int scoreGained)
    {
        var nonZero = line.Where(tile => tile != 0).ToArray();
        var result = new List<int>(line.Count);
        scoreGained = 0;

        for (var index = 0; index < nonZero.Length; index++)
        {
            if (index + 1 < nonZero.Length && nonZero[index] == nonZero[index + 1])
            {
                var merged = checked(nonZero[index] * 2);
                result.Add(merged);
                scoreGained = checked(scoreGained + merged);
                index++;
            }
            else
            {
                result.Add(nonZero[index]);
            }
        }

        while (result.Count < line.Count)
            result.Add(0);
        return result.ToArray();
    }

    private static IEnumerable<int> PositionsForLine(int size, TwentyFortyEightDirection direction, int line)
    {
        switch (direction)
        {
            case TwentyFortyEightDirection.Left:
                for (var column = 0; column < size; column++)
                    yield return line * size + column;
                break;
            case TwentyFortyEightDirection.Right:
                for (var column = size - 1; column >= 0; column--)
                    yield return line * size + column;
                break;
            case TwentyFortyEightDirection.Up:
                for (var row = 0; row < size; row++)
                    yield return row * size + line;
                break;
            case TwentyFortyEightDirection.Down:
                for (var row = size - 1; row >= 0; row--)
                    yield return row * size + line;
                break;
            default:
                throw new ArgumentOutOfRangeException(nameof(direction), direction, "Unknown 2048 direction.");
        }
    }

    private static bool HasAvailableMove(IReadOnlyList<int> tiles, int size)
    {
        for (var index = 0; index < tiles.Count; index++)
        {
            if (tiles[index] == 0)
                return true;
            var row = index / size;
            var column = index % size;
            if (column + 1 < size && tiles[index] == tiles[index + 1])
                return true;
            if (row + 1 < size && tiles[index] == tiles[index + size])
                return true;
        }
        return false;
    }

    private static void AddRandomTile(int[] tiles, ref uint random)
    {
        var empty = new List<int>(tiles.Length);
        for (var index = 0; index < tiles.Length; index++)
        {
            if (tiles[index] == 0)
                empty.Add(index);
        }

        if (empty.Count == 0)
            return;

        var position = (int)(NextRandom(ref random) % (uint)empty.Count);
        tiles[empty[position]] = NextRandom(ref random) % 10 == 0 ? 4 : 2;
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

    private static void ValidateState(TwentyFortyEightState game)
    {
        ArgumentNullException.ThrowIfNull(game);
        ValidateSize(game.Size);
        if (game.Tiles.Length != game.Size * game.Size)
            throw new ArgumentException("The tile list must contain one value per board cell.", nameof(game));
        if (game.Tiles.Any(tile => tile < 0))
            throw new ArgumentException("Tile values cannot be negative.", nameof(game));
    }

    private static void ValidateSize(int size)
    {
        if (size is < 2 or > 8)
            throw new ArgumentOutOfRangeException(nameof(size), "A 2048 board must be between 2×2 and 8×8.");
    }
}
