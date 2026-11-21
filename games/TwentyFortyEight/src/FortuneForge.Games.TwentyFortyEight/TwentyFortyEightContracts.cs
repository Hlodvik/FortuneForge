namespace FortuneForge.Games.TwentyFortyEight;

public sealed class TwentyFortyEightAccessException() : InvalidOperationException("That 2048 game is not available.");
public sealed class TwentyFortyEightRuleException(string message) : InvalidOperationException(message);
public sealed record StartTwentyFortyEightRequest(uint? Seed);
public sealed record TwentyFortyEightMoveRequest(string? Direction);
public sealed record TwentyFortyEightStatusResponse(bool Available, int Size, int TargetTile, string Mode);
public sealed record TwentyFortyEightGameResponse(Guid GameId, int Size, IReadOnlyList<int> Tiles, int Score, int Moves, int HighestTile, string Phase, bool CanUndo, string LastEvent, int ScoreGained, string Message)
{
    public Guid RunId { get; init; }
}
public sealed record TwentyFortyEightErrorResponse(string Code, string Message);

public static class TwentyFortyEightErrors
{
    public static TwentyFortyEightErrorResponse AuthenticationRequired() =>
        new("2048-authentication-required", "Sign in to play 2048.");

    public static TwentyFortyEightErrorResponse GameNotFound(TwentyFortyEightAccessException exception) =>
        new("2048-game-not-found", exception.Message);

    public static TwentyFortyEightErrorResponse InvalidAction(string message) => new("2048-invalid-action", message);

    public static TwentyFortyEightErrorResponse WorkerUnavailable() =>
        new("2048-worker-unavailable", "The 2048 game service is temporarily unavailable.");
}

public sealed record TwentyFortyEightComputeStart(uint Seed);
public sealed record TwentyFortyEightComputeMove(TwentyFortyEightState State, TwentyFortyEightDirection Direction)
{
    public TwentyFortyEightTransition Apply()
    {
        ArgumentNullException.ThrowIfNull(State);
        if (State.Tiles.IsDefault)
            throw new ArgumentException("A 2048 board is required.");

        return TwentyFortyEightEngine.Move(State, Direction);
    }
}
