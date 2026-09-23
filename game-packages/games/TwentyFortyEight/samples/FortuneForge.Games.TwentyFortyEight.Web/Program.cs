using System.Collections.Concurrent;
using System.Security.Cryptography;
using FortuneForge.Games.TwentyFortyEight;

const string apiOrigin = "http://127.0.0.1:5192";
const string clientOrigin = "http://127.0.0.1:5182";
var games = new ConcurrentDictionary<Guid, TwentyFortyEightSession>();

var builder = WebApplication.CreateBuilder(args);
builder.WebHost.UseUrls(apiOrigin);
builder.Services.AddCors(options => options.AddDefaultPolicy(policy => policy
    .WithOrigins(clientOrigin, "http://localhost:5182")
    .AllowAnyHeader()
    .AllowAnyMethod()));

var app = builder.Build();
app.UseCors();

app.MapGet("/", () => Results.Ok(new
{
    game = "Fortune Forge 2048",
    client = "cd games/TwentyFortyEight/client/FortuneForge.Games.TwentyFortyEight.Client; npm run dev",
}));

var api = app.MapGroup("/api/games/2048");

api.MapGet("/status", () => Results.Ok(new TwentyFortyEightStatusResponse(
    Available: true,
    Size: TwentyFortyEightEngine.DefaultSize,
    TargetTile: TwentyFortyEightEngine.TargetTile,
    Mode: "local-free-play")));

api.MapPost("/games", (StartTwentyFortyEightRequest? request) =>
{
    var seed = request?.Seed ?? (uint)RandomNumberGenerator.GetInt32(1, int.MaxValue);
    var session = new TwentyFortyEightSession(Guid.NewGuid(), TwentyFortyEightEngine.Start(seed));
    games[session.Id] = session;
    return Results.Created($"/api/games/2048/games/{session.Id}", ToResponse(session));
});

api.MapGet("/games/{gameId:guid}", (Guid gameId) =>
    games.TryGetValue(gameId, out var session)
        ? Results.Ok(ToResponse(session))
        : NotFound());

api.MapPost("/games/{gameId:guid}/move", (Guid gameId, TwentyFortyEightMoveRequest request) =>
{
    if (!games.TryGetValue(gameId, out var session))
        return NotFound();

    lock (session.SyncRoot)
    {
        try
        {
            var direction = ParseDirection(request.Direction);
            var transition = TwentyFortyEightEngine.Move(session.State, direction);
            if (transition.Event is not TwentyFortyEightEventType.NoMove)
                session.History.Push(session.State);
            session.State = transition.State;
            session.LastEvent = EventName(transition.Event);
            session.ScoreGained = transition.ScoreGained;
            session.Message = transition.Message;
            return Results.Ok(ToResponse(session));
        }
        catch (Exception exception) when (exception is ArgumentException or InvalidOperationException)
        {
            return Results.BadRequest(new TwentyFortyEightErrorResponse("2048-invalid-move", exception.Message));
        }
    }
});

api.MapPost("/games/{gameId:guid}/undo", (Guid gameId) =>
{
    if (!games.TryGetValue(gameId, out var session))
        return NotFound();

    lock (session.SyncRoot)
    {
        if (session.History.Count == 0)
        {
            return Results.Conflict(new TwentyFortyEightErrorResponse(
                "2048-no-undo",
                "There is no move to undo."));
        }

        session.State = session.History.Pop();
        session.LastEvent = EventName(TwentyFortyEightEventType.Undone);
        session.ScoreGained = 0;
        session.Message = "Move undone.";
        return Results.Ok(ToResponse(session));
    }
});

api.MapPost("/games/{gameId:guid}/continue", (Guid gameId) =>
{
    if (!games.TryGetValue(gameId, out var session)) return NotFound();
    lock (session.SyncRoot)
    {
        if (session.State.Phase is not TwentyFortyEightPhase.Won)
            return Results.BadRequest(new TwentyFortyEightErrorResponse("2048-invalid-action", "Continue is only available after reaching 2048."));
        session.State = session.State with { Phase = TwentyFortyEightPhase.Playing };
        session.LastEvent = EventName(TwentyFortyEightEventType.Continued);
        session.ScoreGained = 0;
        session.Message = "Keep going. Build the largest tile you can.";
        return Results.Ok(ToResponse(session));
    }
});

api.MapPost("/games/{gameId:guid}/reset", (Guid gameId, StartTwentyFortyEightRequest? request) =>
{
    if (!games.TryGetValue(gameId, out var session))
        return NotFound();

    lock (session.SyncRoot)
    {
        var seed = request?.Seed ?? (uint)RandomNumberGenerator.GetInt32(1, int.MaxValue);
        session.State = TwentyFortyEightEngine.Start(seed);
        session.History.Clear();
        session.LastEvent = EventName(TwentyFortyEightEventType.Started);
        session.ScoreGained = 0;
        session.Message = "New game started.";
        return Results.Ok(ToResponse(session));
    }
});

app.Run();

static TwentyFortyEightDirection ParseDirection(string? direction) => direction?.Trim().ToLowerInvariant() switch
{
    "up" => TwentyFortyEightDirection.Up,
    "right" => TwentyFortyEightDirection.Right,
    "down" => TwentyFortyEightDirection.Down,
    "left" => TwentyFortyEightDirection.Left,
    _ => throw new ArgumentException("Direction must be up, right, down, or left.", nameof(direction)),
};

static string EventName(TwentyFortyEightEventType eventType) => eventType switch
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

static TwentyFortyEightGameResponse ToResponse(TwentyFortyEightSession session) => new(
    session.Id,
    session.State.Size,
    session.State.Tiles,
    session.State.Score,
    session.State.Moves,
    session.State.HighestTile,
    session.State.Phase switch
    {
        TwentyFortyEightPhase.Playing => "playing",
        TwentyFortyEightPhase.Won => "won",
        TwentyFortyEightPhase.Lost => "lost",
        _ => throw new InvalidOperationException("Unknown 2048 phase."),
    },
    session.History.Count > 0,
    session.LastEvent,
    session.ScoreGained,
    session.Message);

static IResult NotFound() => Results.NotFound(new TwentyFortyEightErrorResponse(
    "2048-game-not-found",
    "That local 2048 game does not exist."));

public sealed class TwentyFortyEightSession(Guid id, TwentyFortyEightState state)
{
    public object SyncRoot { get; } = new();
    public Guid Id { get; } = id;
    public TwentyFortyEightState State { get; set; } = state;
    public Stack<TwentyFortyEightState> History { get; } = new();
    public string LastEvent { get; set; } = "started";
    public int ScoreGained { get; set; }
    public string Message { get; set; } = "Make a move with the arrow keys or controls.";
}

public sealed record StartTwentyFortyEightRequest(uint? Seed);
public sealed record TwentyFortyEightMoveRequest(string Direction);
public sealed record TwentyFortyEightStatusResponse(bool Available, int Size, int TargetTile, string Mode);
public sealed record TwentyFortyEightGameResponse(Guid GameId, int Size, IReadOnlyList<int> Tiles, int Score, int Moves, int HighestTile, string Phase, bool CanUndo, string LastEvent, int ScoreGained, string Message);
public sealed record TwentyFortyEightErrorResponse(string Code, string Message);
