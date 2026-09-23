using System.Collections.Concurrent;
using System.Security.Cryptography;
using FortuneForge.Games.Snake;

const string apiOrigin = "http://127.0.0.1:5194";
const string clientOrigin = "http://127.0.0.1:5184";
var games = new ConcurrentDictionary<Guid, SnakeSession>();

var builder = WebApplication.CreateBuilder(args);
builder.WebHost.UseUrls(apiOrigin);
builder.Services.AddCors(options => options.AddDefaultPolicy(policy => policy
    .WithOrigins(clientOrigin, "http://localhost:5184")
    .AllowAnyHeader()
    .AllowAnyMethod()));

var app = builder.Build();
app.UseCors();

app.MapGet("/", () => Results.Ok(new
{
    game = "Fortune Forge Snake",
    client = "cd games/Snake/client/FortuneForge.Games.Snake.Client; npm run dev",
}));

var api = app.MapGroup("/api/games/snake");

api.MapGet("/status", () => Results.Ok(new SnakeStatusResponse(
    Available: true,
    Width: SnakeEngine.DefaultWidth,
    Height: SnakeEngine.DefaultHeight,
    FoodScore: SnakeEngine.FoodScore,
    TickMilliseconds: 180,
    Mode: "local-free-play")));

api.MapPost("/games", (StartSnakeGameRequest? request) =>
{
    var seed = request?.Seed ?? (uint)RandomNumberGenerator.GetInt32(1, int.MaxValue);
    var width = request?.Width ?? SnakeEngine.DefaultWidth;
    var height = request?.Height ?? SnakeEngine.DefaultHeight;
    try
    {
        var session = new SnakeSession(Guid.NewGuid(), SnakeEngine.Start(seed, width, height));
        games[session.Id] = session;
        return Results.Created($"/api/games/snake/games/{session.Id}", ToResponse(session));
    }
    catch (ArgumentException exception)
    {
        return Results.BadRequest(new SnakeErrorResponse("snake-invalid-game", exception.Message));
    }
});

api.MapGet("/games/{gameId:guid}", (Guid gameId) =>
    games.TryGetValue(gameId, out var session)
        ? Results.Ok(ToResponse(session))
        : NotFound());

api.MapPost("/games/{gameId:guid}/turn", (Guid gameId, SnakeTurnRequest request) =>
{
    if (!games.TryGetValue(gameId, out var session))
        return NotFound();

    lock (session.SyncRoot)
    {
        try
        {
            var transition = SnakeEngine.Turn(session.State, ParseDirection(request.Direction));
            session.State = transition.State;
            session.LastEvent = EventName(transition.Event);
            session.ScoreGained = transition.ScoreGained;
            session.Message = transition.Message;
            return Results.Ok(ToResponse(session));
        }
        catch (Exception exception) when (exception is ArgumentException or InvalidOperationException)
        {
            return Results.BadRequest(new SnakeErrorResponse("snake-invalid-turn", exception.Message));
        }
    }
});

api.MapPost("/games/{gameId:guid}/tick", (Guid gameId) =>
{
    if (!games.TryGetValue(gameId, out var session))
        return NotFound();

    lock (session.SyncRoot)
    {
        try
        {
            var transition = SnakeEngine.Tick(session.State);
            session.State = transition.State;
            session.LastEvent = EventName(transition.Event);
            session.ScoreGained = transition.ScoreGained;
            session.Message = transition.Message;
            return Results.Ok(ToResponse(session));
        }
        catch (InvalidOperationException exception)
        {
            return Results.BadRequest(new SnakeErrorResponse("snake-game-ended", exception.Message));
        }
    }
});

api.MapPost("/games/{gameId:guid}/reset", (Guid gameId, StartSnakeGameRequest? request) =>
{
    if (!games.TryGetValue(gameId, out var session))
        return NotFound();

    lock (session.SyncRoot)
    {
        var seed = request?.Seed ?? (uint)RandomNumberGenerator.GetInt32(1, int.MaxValue);
        var width = request?.Width ?? session.State.Width;
        var height = request?.Height ?? session.State.Height;
        try
        {
            session.State = SnakeEngine.Start(seed, width, height, session.State.BestScore);
            session.LastEvent = EventName(SnakeEventType.Started);
            session.ScoreGained = 0;
            session.Message = "New game started.";
            return Results.Ok(ToResponse(session));
        }
        catch (ArgumentException exception)
        {
            return Results.BadRequest(new SnakeErrorResponse("snake-invalid-game", exception.Message));
        }
    }
});

app.Run();

static SnakeDirection ParseDirection(string? direction) => direction?.Trim().ToLowerInvariant() switch
{
    "up" => SnakeDirection.Up,
    "right" => SnakeDirection.Right,
    "down" => SnakeDirection.Down,
    "left" => SnakeDirection.Left,
    _ => throw new ArgumentException("Direction must be up, right, down, or left.", nameof(direction)),
};

static string EventName(SnakeEventType eventType) => eventType switch
{
    SnakeEventType.Started => "started",
    SnakeEventType.Turned => "turned",
    SnakeEventType.Moved => "moved",
    SnakeEventType.AteFood => "ate-food",
    SnakeEventType.NoOp => "no-op",
    SnakeEventType.Won => "won",
    SnakeEventType.Lost => "lost",
    _ => throw new ArgumentOutOfRangeException(nameof(eventType), eventType, "Unknown Snake event."),
};

static SnakeGameResponse ToResponse(SnakeSession session)
{
    var state = session.State;
    return new SnakeGameResponse(
        session.Id,
        state.Width,
        state.Height,
        state.Body.Select(ToPoint).ToArray(),
        state.Food is { } food ? ToPoint(food) : null,
        state.Direction switch
        {
            SnakeDirection.Up => "up",
            SnakeDirection.Right => "right",
            SnakeDirection.Down => "down",
            SnakeDirection.Left => "left",
            _ => throw new InvalidOperationException("Unknown Snake direction."),
        },
        state.Score,
        state.BestScore,
        state.Moves,
        state.Length,
        state.Phase switch
        {
            SnakePhase.Playing => "playing",
            SnakePhase.Won => "won",
            SnakePhase.Lost => "lost",
            _ => throw new InvalidOperationException("Unknown Snake phase."),
        },
        session.LastEvent,
        session.ScoreGained,
        session.Message);
}

static SnakePointResponse ToPoint(SnakePoint point) => new(point.X, point.Y);

static IResult NotFound() => Results.NotFound(new SnakeErrorResponse(
    "snake-game-not-found",
    "That local Snake game does not exist."));

public sealed class SnakeSession(Guid id, SnakeState state)
{
    public object SyncRoot { get; } = new();
    public Guid Id { get; } = id;
    public SnakeState State { get; set; } = state;
    public string LastEvent { get; set; } = "started";
    public int ScoreGained { get; set; }
    public string Message { get; set; } = "Use the arrow keys or controls to steer the snake.";
}

public sealed record StartSnakeGameRequest(uint? Seed, int? Width, int? Height);
public sealed record SnakeTurnRequest(string Direction);
public sealed record SnakeStatusResponse(bool Available, int Width, int Height, int FoodScore, int TickMilliseconds, string Mode);
public sealed record SnakePointResponse(int X, int Y);
public sealed record SnakeGameResponse(Guid GameId, int Width, int Height, IReadOnlyList<SnakePointResponse> Body, SnakePointResponse? Food, string Direction, int Score, int BestScore, int Moves, int Length, string Phase, string LastEvent, int ScoreGained, string Message);
public sealed record SnakeErrorResponse(string Code, string Message);
