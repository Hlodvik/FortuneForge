using System.Collections.Immutable;

namespace FortuneForge.Games.Snake;

public static class SnakeEngine
{
    public const int DefaultWidth = 20;
    public const int DefaultHeight = 20;
    public const int FoodScore = 10;

    public static SnakeState Start(
        uint seed,
        int width = DefaultWidth,
        int height = DefaultHeight,
        int bestScore = 0)
    {
        ValidateDimensions(width, height);
        if (bestScore < 0)
            throw new ArgumentOutOfRangeException(nameof(bestScore), "The best score cannot be negative.");

        var random = NormalizeSeed(seed);
        var center = new SnakePoint(width / 2, height / 2);
        var body = ImmutableArray.Create(
            center,
            new SnakePoint(center.X - 1, center.Y),
            new SnakePoint(center.X - 2, center.Y));
        var food = FindFood(body, width, height, ref random);

        return new SnakeState(
            width,
            height,
            seed,
            random,
            body,
            SnakeDirection.Right,
            food,
            0,
            bestScore,
            0,
            SnakePhase.Playing);
    }

    public static SnakeTransition Turn(SnakeState game, SnakeDirection direction)
    {
        ValidateState(game);
        if (game.Phase is not SnakePhase.Playing)
            throw new InvalidOperationException("This Snake game has ended. Start a new game to play again.");
        if (direction == game.Direction)
        {
            return new SnakeTransition(game, SnakeEventType.NoOp, 0, "Keep going.");
        }
        if (game.Body.Length > 1 && IsOpposite(game.Direction, direction))
        {
            return new SnakeTransition(game, SnakeEventType.NoOp, 0, "The snake cannot reverse into itself.");
        }

        return new SnakeTransition(
            game with { Direction = direction },
            SnakeEventType.Turned,
            0,
            $"Turned {DirectionName(direction).ToLowerInvariant()}.");
    }

    public static SnakeTransition Tick(SnakeState game)
    {
        ValidateState(game);
        if (game.Phase is not SnakePhase.Playing)
            throw new InvalidOperationException("This Snake game has ended. Start a new game to play again.");

        var nextHead = Step(game.Head, game.Direction);
        var isEating = game.Food is { } food && food == nextHead;
        var tailMayMove = !isEating;
        if (!IsInside(nextHead, game.Width, game.Height) || HitsBody(game.Body, nextHead, tailMayMove))
        {
            return new SnakeTransition(
                game with { Phase = SnakePhase.Lost, Moves = checked(game.Moves + 1) },
                SnakeEventType.Lost,
                0,
                "The snake crashed. Start a new game to try again.");
        }

        var nextBody = new List<SnakePoint>(game.Body.Length + 1) { nextHead };
        nextBody.AddRange(game.Body);
        if (!isEating)
            nextBody.RemoveAt(nextBody.Count - 1);

        var scoreGained = isEating ? FoodScore : 0;
        var score = checked(game.Score + scoreGained);
        var bestScore = Math.Max(game.BestScore, score);
        var random = game.RandomState;
        var nextFood = isEating ? FindFood(nextBody, game.Width, game.Height, ref random) : game.Food;
        var phase = nextFood is null ? SnakePhase.Won : SnakePhase.Playing;
        var eventType = phase == SnakePhase.Won
            ? SnakeEventType.Won
            : isEating ? SnakeEventType.AteFood : SnakeEventType.Moved;
        var message = phase == SnakePhase.Won
            ? "The snake filled the board. You win!"
            : isEating ? $"Food eaten. +{scoreGained} points." : "Keep moving.";

        return new SnakeTransition(
            game with
            {
                RandomState = random,
                Body = nextBody.ToImmutableArray(),
                Food = nextFood,
                Score = score,
                BestScore = bestScore,
                Moves = checked(game.Moves + 1),
                Phase = phase,
            },
            eventType,
            scoreGained,
            message);
    }

    public static bool IsOpposite(SnakeDirection first, SnakeDirection second) =>
        (first, second) is
            (SnakeDirection.Up, SnakeDirection.Down) or
            (SnakeDirection.Down, SnakeDirection.Up) or
            (SnakeDirection.Left, SnakeDirection.Right) or
            (SnakeDirection.Right, SnakeDirection.Left);

    private static bool HitsBody(ImmutableArray<SnakePoint> body, SnakePoint point, bool tailMayMove)
    {
        var count = tailMayMove ? body.Length - 1 : body.Length;
        for (var index = 0; index < count; index++)
        {
            if (body[index] == point)
                return true;
        }
        return false;
    }

    private static SnakePoint Step(SnakePoint point, SnakeDirection direction) => direction switch
    {
        SnakeDirection.Up => new SnakePoint(point.X, point.Y - 1),
        SnakeDirection.Right => new SnakePoint(point.X + 1, point.Y),
        SnakeDirection.Down => new SnakePoint(point.X, point.Y + 1),
        SnakeDirection.Left => new SnakePoint(point.X - 1, point.Y),
        _ => throw new ArgumentOutOfRangeException(nameof(direction), direction, "Unknown Snake direction."),
    };

    private static SnakePoint? FindFood(
        IReadOnlyCollection<SnakePoint> body,
        int width,
        int height,
        ref uint random)
    {
        var occupied = body.ToHashSet();
        var empty = new List<SnakePoint>(width * height - occupied.Count);
        for (var y = 0; y < height; y++)
        {
            for (var x = 0; x < width; x++)
            {
                var point = new SnakePoint(x, y);
                if (!occupied.Contains(point))
                    empty.Add(point);
            }
        }

        if (empty.Count == 0)
            return null;
        return empty[(int)(NextRandom(ref random) % (uint)empty.Count)];
    }

    private static bool IsInside(SnakePoint point, int width, int height) =>
        point.X >= 0 && point.X < width && point.Y >= 0 && point.Y < height;

    private static string DirectionName(SnakeDirection direction) => direction switch
    {
        SnakeDirection.Up => "Up",
        SnakeDirection.Right => "Right",
        SnakeDirection.Down => "Down",
        SnakeDirection.Left => "Left",
        _ => throw new ArgumentOutOfRangeException(nameof(direction), direction, "Unknown Snake direction."),
    };

    private static uint NormalizeSeed(uint seed) => seed == 0 ? 0x9E3779B9u : seed;

    private static uint NextRandom(ref uint value)
    {
        unchecked
        {
            if (value == 0)
                value = 0x9E3779B9u;
            value ^= value << 13;
            value ^= value >> 17;
            value ^= value << 5;
            return value;
        }
    }

    private static void ValidateState(SnakeState game)
    {
        ArgumentNullException.ThrowIfNull(game);
        ValidateDimensions(game.Width, game.Height);
        if (game.Body.IsDefaultOrEmpty)
            throw new ArgumentException("The snake must contain at least one segment.", nameof(game));
        if (game.Body.Any(point => !IsInside(point, game.Width, game.Height)) || game.Body.Distinct().Count() != game.Body.Length)
            throw new ArgumentException("Snake segments must be distinct points inside the board.", nameof(game));
        if (game.Food is { } food && (!IsInside(food, game.Width, game.Height) || game.Body.Contains(food)))
            throw new ArgumentException("Food must be inside the board and outside the snake.", nameof(game));
        if (game.Score < 0 || game.BestScore < game.Score || game.Moves < 0)
            throw new ArgumentException("Snake scores and moves cannot be negative or inconsistent.", nameof(game));
    }

    private static void ValidateDimensions(int width, int height)
    {
        if (width is < 6 or > 50 || height is < 6 or > 50)
            throw new ArgumentOutOfRangeException(nameof(width), "A Snake board must be between 6×6 and 50×50.");
    }
}
