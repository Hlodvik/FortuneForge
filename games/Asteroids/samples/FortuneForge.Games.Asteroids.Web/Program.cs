using System.Collections.Concurrent;
using System.Security.Cryptography;
using FortuneForge.Games.Asteroids;

const string apiOrigin = "http://127.0.0.1:5198";
const string clientOrigin = "http://127.0.0.1:5188";
var games = new ConcurrentDictionary<Guid, AsteroidsSession>();
var leaderboard = new ConcurrentDictionary<Guid, AsteroidsLeaderboardEntry>();

var builder = WebApplication.CreateBuilder(args);
builder.WebHost.UseUrls(apiOrigin);
builder.Services.AddCors(options => options.AddDefaultPolicy(policy => policy
    .WithOrigins(clientOrigin, "http://localhost:5188")
    .AllowAnyHeader()
    .AllowAnyMethod()));

var app = builder.Build();
app.UseCors();

app.MapGet("/", () => Results.Ok(new
{
    game = "Fortune Forge Asteroids",
    client = "cd games/Asteroids/client/FortuneForge.Games.Asteroids.Client; npm run dev",
}));

var api = app.MapGroup("/api/games/asteroids");

api.MapGet("/status", () => Results.Ok(new AsteroidsStatusResponse(
    Available: true,
    Width: AsteroidsEngine.DefaultWidth,
    Height: AsteroidsEngine.DefaultHeight,
    TickMilliseconds: AsteroidsEngine.TickMilliseconds,
    StartingLives: AsteroidsEngine.DefaultLives,
    Mode: "local-free-play")));

api.MapGet("/leaderboard", () => Results.Ok(ToLeaderboardResponse(leaderboard.Values)));

api.MapPost("/games", (StartAsteroidsGameRequest? request) =>
{
    var seed = request?.Seed ?? (uint)RandomNumberGenerator.GetInt32(1, int.MaxValue);
    try
    {
        var session = new AsteroidsSession(Guid.NewGuid(), AsteroidsEngine.Start(seed));
        games[session.Id] = session;
        return Results.Created($"/api/games/asteroids/games/{session.Id}", ToResponse(session));
    }
    catch (ArgumentException exception)
    {
        return Results.BadRequest(new AsteroidsErrorResponse("asteroids-invalid-game", exception.Message));
    }
});

api.MapGet("/games/{gameId:guid}", (Guid gameId) =>
    games.TryGetValue(gameId, out var session)
        ? Results.Ok(ToResponse(session))
        : NotFound());

api.MapPost("/games/{gameId:guid}/action", (Guid gameId, AsteroidsActionRequest request) =>
{
    if (!games.TryGetValue(gameId, out var session))
        return NotFound();

    lock (session.SyncRoot)
    {
        try
        {
            var transition = AsteroidsEngine.Apply(session.State, ParseAction(request.Action));
            session.State = transition.State;
            session.LastEvent = EventName(transition.Event);
            session.ScoreGained = transition.ScoreGained;
            session.Message = transition.Message;
            return Results.Ok(ToResponse(session));
        }
        catch (Exception exception) when (exception is ArgumentException or InvalidOperationException)
        {
            return Results.BadRequest(new AsteroidsErrorResponse("asteroids-invalid-action", exception.Message));
        }
    }
});

api.MapPost("/games/{gameId:guid}/reset", (Guid gameId, StartAsteroidsGameRequest? request) =>
{
    if (!games.TryGetValue(gameId, out var session))
        return NotFound();

    lock (session.SyncRoot)
    {
        var seed = request?.Seed ?? (uint)RandomNumberGenerator.GetInt32(1, int.MaxValue);
        try
        {
            session.State = AsteroidsEngine.Start(seed, bestScore: session.State.BestScore);
            session.LastEvent = EventName(AsteroidsEventType.Started);
            session.ScoreGained = 0;
            session.Message = "New mission started.";
            session.SubmissionId = null;
            return Results.Ok(ToResponse(session));
        }
        catch (ArgumentException exception)
        {
            return Results.BadRequest(new AsteroidsErrorResponse("asteroids-invalid-game", exception.Message));
        }
    }
});

api.MapPost("/games/{gameId:guid}/submit", (Guid gameId, AsteroidsSubmitScoreRequest? request) =>
{
    if (!games.TryGetValue(gameId, out var session))
        return NotFound();

    lock (session.SyncRoot)
    {
        if (session.State.Phase is not AsteroidsPhase.GameOver)
            return Results.BadRequest(new AsteroidsErrorResponse("asteroids-game-in-progress", "Finish the mission before submitting a score."));

        if (session.SubmissionId is Guid submittedId && leaderboard.ContainsKey(submittedId))
            return Results.Ok(ToLeaderboardResponse(leaderboard.Values));

        var state = session.State;
        var entry = new AsteroidsLeaderboardEntry(
            Guid.NewGuid(),
            NormalizePlayerName(request?.PlayerName),
            state.Score,
            state.Wave,
            DateTimeOffset.UtcNow);
        leaderboard[entry.Id] = entry;
        session.SubmissionId = entry.Id;
        return Results.Ok(ToLeaderboardResponse(leaderboard.Values));
    }
});

app.Run();

static AsteroidsAction ParseAction(string? action) => action?.Trim().ToLowerInvariant() switch
{
    "tick" => AsteroidsAction.Tick,
    "rotate-left" => AsteroidsAction.RotateLeft,
    "rotate-right" => AsteroidsAction.RotateRight,
    "thrust" => AsteroidsAction.Thrust,
    "fire" => AsteroidsAction.Fire,
    _ => throw new ArgumentException("Action must be tick, rotate-left, rotate-right, thrust, or fire.", nameof(action)),
};

static string EventName(AsteroidsEventType eventType) => eventType switch
{
    AsteroidsEventType.Started => "started",
    AsteroidsEventType.Ticked => "ticked",
    AsteroidsEventType.Rotated => "rotated",
    AsteroidsEventType.Thrusted => "thrusted",
    AsteroidsEventType.Fired => "fired",
    AsteroidsEventType.Hit => "hit",
    AsteroidsEventType.Damaged => "damaged",
    AsteroidsEventType.WaveCleared => "wave-cleared",
    AsteroidsEventType.PowerUpCollected => "power-up-collected",
    AsteroidsEventType.NoOp => "no-op",
    AsteroidsEventType.GameOver => "game-over",
    _ => throw new ArgumentOutOfRangeException(nameof(eventType), eventType, "Unknown Asteroids event."),
};

static AsteroidsGameResponse ToResponse(AsteroidsSession session)
{
    var state = session.State;
    return new AsteroidsGameResponse(
        session.Id,
        state.Width,
        state.Height,
        ToShipResponse(state.Ship),
        state.Asteroids.Select(asteroid => new AsteroidResponse(
            asteroid.Id,
            asteroid.Position.X,
            asteroid.Position.Y,
            asteroid.Velocity.X,
            asteroid.Velocity.Y,
            asteroid.Radius,
            asteroid.HitPoints,
            asteroid.SpriteVariant,
            asteroid.Size switch
            {
                AsteroidSize.Tiny => "tiny",
                AsteroidSize.Small => "small",
                AsteroidSize.Medium => "medium",
                AsteroidSize.Large => "large",
                AsteroidSize.Huge => "huge",
                _ => throw new InvalidOperationException("Unknown asteroid size."),
            })).ToArray(),
        state.Bullets.Select(bullet => new BulletResponse(
            bullet.Id,
            bullet.Position.X,
            bullet.Position.Y,
            bullet.Velocity.X,
            bullet.Velocity.Y,
            bullet.RemainingTicks)).ToArray(),
        state.Score,
        state.BestScore,
        state.Lives,
        state.Wave,
        state.Tick,
        state.Phase == AsteroidsPhase.Playing ? "playing" : "game-over",
        session.LastEvent,
        session.ScoreGained,
        session.Message,
        state.PowerUps.Select(powerUp => new PowerUpResponse(
            powerUp.Id,
            powerUp.Position.X,
            powerUp.Position.Y,
            powerUp.Velocity.X,
            powerUp.Velocity.Y,
            powerUp.RemainingTicks,
            powerUp.Type switch
            {
                AsteroidsPowerUpType.Shield => "shield",
                AsteroidsPowerUpType.RapidFire => "rapid-fire",
                AsteroidsPowerUpType.ExtraLife => "extra-life",
                _ => throw new InvalidOperationException("Unknown Asteroids power-up."),
            })).ToArray(),
        state.RapidFireTicks);
}

static ShipResponse ToShipResponse(AsteroidsShip ship) => new(
    ship.Position.X,
    ship.Position.Y,
    ship.Velocity.X,
    ship.Velocity.Y,
    ship.Angle,
    ship.InvulnerabilityTicks,
    ship.ThrustTicks);

static IResult NotFound() => Results.NotFound(new AsteroidsErrorResponse(
    "asteroids-game-not-found",
    "That local Asteroids game does not exist."));

static AsteroidsLeaderboardResponse ToLeaderboardResponse(IEnumerable<AsteroidsLeaderboardEntry> entries) => new(
    entries
        .OrderByDescending(entry => entry.Score)
        .ThenByDescending(entry => entry.Wave)
        .ThenBy(entry => entry.SubmittedAt)
        .Take(10)
        .Select((entry, index) => new AsteroidsLeaderboardItem(index + 1, entry.PlayerName, entry.Score, entry.Wave))
        .ToArray());

static string NormalizePlayerName(string? playerName)
{
    var normalized = playerName?.Trim();
    if (string.IsNullOrEmpty(normalized))
        return "Player";
    return normalized.Length <= 24 ? normalized : normalized[..24];
}

public sealed class AsteroidsSession(Guid id, AsteroidsState state)
{
    public object SyncRoot { get; } = new();
    public Guid Id { get; } = id;
    public AsteroidsState State { get; set; } = state;
    public string LastEvent { get; set; } = "started";
    public int ScoreGained { get; set; }
    public string Message { get; set; } = "Rotate, thrust, and fire to clear the field.";
    public Guid? SubmissionId { get; set; }
}

public sealed record StartAsteroidsGameRequest(uint? Seed);
public sealed record AsteroidsActionRequest(string Action);
public sealed record AsteroidsStatusResponse(bool Available, int Width, int Height, int TickMilliseconds, int StartingLives, string Mode);
public sealed record ShipResponse(double X, double Y, double VelocityX, double VelocityY, double Angle, int InvulnerabilityTicks, int ThrustTicks);
public sealed record AsteroidResponse(int Id, double X, double Y, double VelocityX, double VelocityY, double Radius, int HitPoints, int SpriteVariant, string Size);
public sealed record BulletResponse(int Id, double X, double Y, double VelocityX, double VelocityY, int RemainingTicks);
public sealed record PowerUpResponse(int Id, double X, double Y, double VelocityX, double VelocityY, int RemainingTicks, string Type);
public sealed record AsteroidsGameResponse(Guid GameId, int Width, int Height, ShipResponse Ship, IReadOnlyList<AsteroidResponse> Asteroids, IReadOnlyList<BulletResponse> Bullets, int Score, int BestScore, int Lives, int Wave, int Tick, string Phase, string LastEvent, int ScoreGained, string Message, IReadOnlyList<PowerUpResponse> PowerUps, int RapidFireTicks);
public sealed record AsteroidsErrorResponse(string Code, string Message);
public sealed record AsteroidsSubmitScoreRequest(string? PlayerName);
public sealed record AsteroidsLeaderboardEntry(Guid Id, string PlayerName, int Score, int Wave, DateTimeOffset SubmittedAt);
public sealed record AsteroidsLeaderboardItem(int Rank, string PlayerName, int Score, int Wave);
public sealed record AsteroidsLeaderboardResponse(IReadOnlyList<AsteroidsLeaderboardItem> Entries);
