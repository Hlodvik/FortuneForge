using System.Collections.Concurrent;
using System.Security.Cryptography;
using FortuneForge.Games.Flappy;

const string apiOrigin = "http://127.0.0.1:5195";
const string clientOrigin = "http://127.0.0.1:5185";
var games = new ConcurrentDictionary<Guid, FlappySession>();

var builder = WebApplication.CreateBuilder(args);
builder.WebHost.UseUrls(apiOrigin);
builder.Services.AddCors(options => options.AddDefaultPolicy(policy => policy
    .WithOrigins(clientOrigin, "http://localhost:5185")
    .AllowAnyHeader()
    .AllowAnyMethod()));

var app = builder.Build();
app.UseCors();

app.MapGet("/", () => Results.Ok(new
{
    game = "Fortune Forge Flappy",
    client = "cd games/Flappy/client/FortuneForge.Games.Flappy.Client; npm run dev",
}));

var api = app.MapGroup("/api/games/flappy");
api.MapGet("/status", () => Results.Ok(new
{
    available = true,
    tickMilliseconds = FlappyEngine.TickMilliseconds,
    mode = "local-free-play",
}));

api.MapPost("/games", (StartFlappyGameRequest? request) =>
{
    var seed = request?.Seed ?? (uint)RandomNumberGenerator.GetInt32(1, int.MaxValue);
    var session = new FlappySession(Guid.NewGuid(), FlappyEngine.Start(seed));
    games[session.Id] = session;
    return Results.Created($"/api/games/flappy/games/{session.Id}", ToResponse(session));
});

api.MapPost("/games/{gameId:guid}/step", (Guid gameId, FlappyStepRequest? request) =>
{
    if (!games.TryGetValue(gameId, out var session)) return NotFound();
    lock (session.SyncRoot)
    {
        if (session.State.Phase != FlappyPhase.Playing)
            return Results.BadRequest(new FlappyErrorResponse("flappy-game-ended", "This run has ended. Start a new run to play again."));
        var transition = FlappyEngine.Step(session.State, request?.Flap == true ? FlappyInput.Flap : FlappyInput.None);
        session.State = transition.State;
        return Results.Ok(ToResponse(session));
    }
});

api.MapPost("/games/{gameId:guid}/reset", (Guid gameId, StartFlappyGameRequest? request) =>
{
    if (!games.TryGetValue(gameId, out var session)) return NotFound();
    lock (session.SyncRoot)
    {
        var seed = request?.Seed ?? (uint)RandomNumberGenerator.GetInt32(1, int.MaxValue);
        session.State = FlappyEngine.Start(seed, bestScore: session.State.BestScore);
        return Results.Ok(ToResponse(session));
    }
});

app.Run();

static FlappyGameResponse ToResponse(FlappySession session)
{
    var snapshot = FlappyEngine.ToSnapshot(session.State);
    return new FlappyGameResponse(
        session.Id, snapshot.Width, snapshot.Height, snapshot.BirdX, snapshot.BirdY,
        FlappyEngine.BirdRadius, FlappyEngine.ObstacleWidth, snapshot.Score, snapshot.BestScore,
        snapshot.Level, PhaseName(snapshot.Phase), snapshot.Obstacles.Select(obstacle =>
            new FlappyObstacleResponse(obstacle.Id, obstacle.X, obstacle.GapTop, obstacle.GapBottom)).ToArray());
}

static string PhaseName(FlappyPhase phase) => phase switch
{
    FlappyPhase.Playing => "playing",
    FlappyPhase.ObstacleCollision => "obstacle-collision",
    FlappyPhase.GroundCollision => "ground-collision",
    FlappyPhase.CeilingCollision => "ceiling-collision",
    _ => throw new ArgumentOutOfRangeException(nameof(phase)),
};

static IResult NotFound() => Results.NotFound(new FlappyErrorResponse("flappy-game-not-found", "That local Flappy run does not exist."));

public sealed class FlappySession(Guid id, FlappyState state)
{
    public object SyncRoot { get; } = new();
    public Guid Id { get; } = id;
    public FlappyState State { get; set; } = state;
}

public sealed record StartFlappyGameRequest(uint? Seed);
public sealed record FlappyStepRequest(bool Flap);
public sealed record FlappyObstacleResponse(int Id, double X, double GapTop, double GapBottom);
public sealed record FlappyGameResponse(Guid GameId, int Width, int Height, double BirdX, double BirdY,
    double BirdRadius, double ObstacleWidth, int Score, int BestScore, int Level, string Phase,
    IReadOnlyList<FlappyObstacleResponse> Obstacles);
public sealed record FlappyErrorResponse(string Code, string Message);
