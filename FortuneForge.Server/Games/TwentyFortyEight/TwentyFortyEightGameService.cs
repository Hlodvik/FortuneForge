using System.Collections.Concurrent;
using System.Security.Cryptography;
using FortuneForge.Games.TwentyFortyEight;

namespace FortuneForge.Server.Games.TwentyFortyEight;

/// <summary>Owns account-isolated, free-play 2048 sessions.</summary>
public sealed class TwentyFortyEightGameService
{
    private readonly ConcurrentDictionary<Guid, TwentyFortyEightSession> games = new();

    public TwentyFortyEightStatusResponse Status() => new(true, TwentyFortyEightEngine.DefaultSize, TwentyFortyEightEngine.TargetTile, "account-free-play");

    public TwentyFortyEightGameResponse Start(string userId, uint? seed) => ToResponse(Create(userId, seed));

    public TwentyFortyEightGameResponse Move(string userId, Guid gameId, string? direction) => Change(userId, gameId, session =>
    {
        var transition = TwentyFortyEightEngine.Move(session.State, ParseDirection(direction));
        if (transition.Event is not TwentyFortyEightEventType.NoMove)
            session.History.Push(session.State);
        session.State = transition.State;
        session.LastEvent = EventName(transition.Event);
        session.ScoreGained = transition.ScoreGained;
        session.Message = transition.Message;
    });

    public TwentyFortyEightGameResponse Undo(string userId, Guid gameId) => Change(userId, gameId, session =>
    {
        if (session.History.Count == 0)
            throw new TwentyFortyEightRuleException("There is no move to undo.");

        session.State = session.History.Pop();
        session.LastEvent = "undone";
        session.ScoreGained = 0;
        session.Message = "Move undone.";
    });

    public TwentyFortyEightGameResponse Continue(string userId, Guid gameId) => Change(userId, gameId, session =>
    {
        if (session.State.Phase is not TwentyFortyEightPhase.Won)
            throw new TwentyFortyEightRuleException("Continue is only available after reaching 2048.");

        session.State = session.State with { Phase = TwentyFortyEightPhase.Playing };
        session.LastEvent = "continued";
        session.ScoreGained = 0;
        session.Message = "Keep going. Build the largest tile you can.";
    });

    public TwentyFortyEightGameResponse Reset(string userId, Guid gameId, uint? seed) => Change(userId, gameId, session =>
    {
        session.State = TwentyFortyEightEngine.Start(Seed(seed));
        session.History.Clear();
        session.LastEvent = "started";
        session.ScoreGained = 0;
        session.Message = "New game started.";
    });

    private TwentyFortyEightSession Create(string userId, uint? seed)
    {
        var session = new TwentyFortyEightSession(userId, TwentyFortyEightEngine.Start(Seed(seed)));
        if (!games.TryAdd(session.Id, session))
            throw new InvalidOperationException("Could not open a 2048 game.");
        return session;
    }

    private TwentyFortyEightGameResponse Change(string userId, Guid gameId, Action<TwentyFortyEightSession> change)
    {
        if (!games.TryGetValue(gameId, out var session) || !string.Equals(session.UserId, userId, StringComparison.Ordinal))
            throw new TwentyFortyEightAccessException();
        lock (session.SyncRoot)
        {
            change(session);
            return ToResponse(session);
        }
    }

    private static uint Seed(uint? seed) => seed is > 0 ? seed.Value : (uint)RandomNumberGenerator.GetInt32(1, int.MaxValue);

    private static TwentyFortyEightDirection ParseDirection(string? direction) => direction?.Trim().ToLowerInvariant() switch
    {
        "up" => TwentyFortyEightDirection.Up,
        "right" => TwentyFortyEightDirection.Right,
        "down" => TwentyFortyEightDirection.Down,
        "left" => TwentyFortyEightDirection.Left,
        _ => throw new TwentyFortyEightRuleException("Direction must be up, right, down, or left."),
    };

    private static string EventName(TwentyFortyEightEventType eventType) => eventType switch
    {
        TwentyFortyEightEventType.Started => "started",
        TwentyFortyEightEventType.Moved => "moved",
        TwentyFortyEightEventType.NoMove => "no-move",
        TwentyFortyEightEventType.Won => "won",
        TwentyFortyEightEventType.Lost => "lost",
        TwentyFortyEightEventType.Undone => "undone",
        TwentyFortyEightEventType.Continued => "continued",
        _ => throw new ArgumentOutOfRangeException(nameof(eventType), eventType, "Unknown 2048 event."),
    };

    private static TwentyFortyEightGameResponse ToResponse(TwentyFortyEightSession session) => new(
        session.Id, session.State.Size, session.State.Tiles, session.State.Score, session.State.Moves,
        session.State.HighestTile, session.State.Phase switch
        {
            TwentyFortyEightPhase.Playing => "playing",
            TwentyFortyEightPhase.Won => "won",
            TwentyFortyEightPhase.Lost => "lost",
            _ => throw new InvalidOperationException("Unknown 2048 phase."),
        }, session.History.Count > 0, session.LastEvent, session.ScoreGained, session.Message);

    private sealed class TwentyFortyEightSession(string userId, TwentyFortyEightState state)
    {
        public Guid Id { get; } = Guid.NewGuid();
        public object SyncRoot { get; } = new();
        public string UserId { get; } = userId;
        public TwentyFortyEightState State { get; set; } = state;
        public Stack<TwentyFortyEightState> History { get; } = new();
        public string LastEvent { get; set; } = "started";
        public int ScoreGained { get; set; }
        public string Message { get; set; } = "Make a move with the arrow keys or controls.";
    }
}

public sealed class TwentyFortyEightAccessException() : InvalidOperationException("That 2048 game is not available.");
public sealed class TwentyFortyEightRuleException(string message) : InvalidOperationException(message);
public sealed record StartTwentyFortyEightRequest(uint? Seed);
public sealed record TwentyFortyEightMoveRequest(string? Direction);
public sealed record TwentyFortyEightStatusResponse(bool Available, int Size, int TargetTile, string Mode);
public sealed record TwentyFortyEightGameResponse(Guid GameId, int Size, IReadOnlyList<int> Tiles, int Score, int Moves, int HighestTile, string Phase, bool CanUndo, string LastEvent, int ScoreGained, string Message);
public sealed record TwentyFortyEightErrorResponse(string Code, string Message);
