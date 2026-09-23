using System.Collections.Concurrent;
using System.Collections.Immutable;
using System.Security.Cryptography;
using FortuneForge.Games.DropMerge;

const string apiOrigin = "http://127.0.0.1:5202";
const string clientOrigin = "http://127.0.0.1:5192";
var games = new ConcurrentDictionary<Guid, DropMergeSession>();

var builder = WebApplication.CreateBuilder(args);
builder.WebHost.UseUrls(apiOrigin);
builder.Services.AddCors(options => options.AddDefaultPolicy(policy => policy
    .WithOrigins(clientOrigin, "http://localhost:5192")
    .AllowAnyHeader()
    .AllowAnyMethod()));

var app = builder.Build();
app.UseCors();

app.MapGet("/", () => Results.Ok(new
{
    game = "Fortune Forge Drop Merge",
    client = "cd games/DropMerge/client/FortuneForge.Games.DropMerge.Client; npm run dev",
}));

var api = app.MapGroup("/api/games/drop-merge");

api.MapGet("/status", () => Results.Ok(new DropMergeStatusResponse(
    Available: true,
    Columns: DropMergeEngine.DefaultColumns,
    Rows: DropMergeEngine.DefaultRows,
    FirstBigTile: DropMergeEngine.FirstBigTile,
    Mode: "local-free-play")));

api.MapPost("/games", (StartDropMergeRequest? request) =>
{
    var seed = request?.Seed ?? (uint)RandomNumberGenerator.GetInt32(1, int.MaxValue);
    var session = new DropMergeSession(Guid.NewGuid(), DropMergeEngine.Start(seed));
    games[session.Id] = session;
    return Results.Created($"/api/games/drop-merge/games/{session.Id}", ToResponse(session));
});

api.MapGet("/games/{gameId:guid}", (Guid gameId) =>
    games.TryGetValue(gameId, out var session)
        ? Results.Ok(ToResponse(session))
        : NotFound());

api.MapPost("/games/{gameId:guid}/drop", (Guid gameId, DropMergeDropRequest request) =>
{
    if (!games.TryGetValue(gameId, out var session))
        return NotFound();

    lock (session.SyncRoot)
    {
        try
        {
            var transition = DropMergeEngine.Drop(session.State, request.Column);
            if (transition.Event is not DropMergeEventType.NoMove)
                session.History.Push(session.State);
            session.State = transition.State;
            session.LastEvent = EventName(transition.Event);
            session.ScoreGained = transition.ScoreGained;
            session.MergeCount = transition.MergeCount;
            session.DropColumn = transition.DropColumn;
            session.DropRow = transition.DropRow;
            session.RemovedTile = transition.RemovedTile;
            session.IntroducedTile = transition.IntroducedTile;
            session.MergeSteps = transition.MergeSteps;
            session.Message = transition.Message;
            return Results.Ok(ToResponse(session));
        }
        catch (Exception exception) when (exception is ArgumentException or InvalidOperationException)
        {
            return Results.BadRequest(new DropMergeErrorResponse("drop-merge-invalid-drop", exception.Message));
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
            return Results.Conflict(new DropMergeErrorResponse("drop-merge-no-undo", "There is no drop to undo."));

        session.State = session.History.Pop();
        session.LastEvent = EventName(DropMergeEventType.Undone);
        session.ScoreGained = 0;
        session.MergeCount = 0;
        session.DropColumn = -1;
        session.DropRow = -1;
        session.RemovedTile = 0;
        session.IntroducedTile = 0;
        session.MergeSteps = ImmutableArray<DropMergeMergeStep>.Empty;
        session.Message = "Drop undone.";
        return Results.Ok(ToResponse(session));
    }
});

api.MapPost("/games/{gameId:guid}/reset", (Guid gameId, StartDropMergeRequest? request) =>
{
    if (!games.TryGetValue(gameId, out var session))
        return NotFound();

    lock (session.SyncRoot)
    {
        var seed = request?.Seed ?? (uint)RandomNumberGenerator.GetInt32(1, int.MaxValue);
        session.State = DropMergeEngine.Start(seed);
        session.History.Clear();
        session.LastEvent = EventName(DropMergeEventType.Started);
        session.ScoreGained = 0;
        session.MergeCount = 0;
        session.DropColumn = -1;
        session.DropRow = -1;
        session.RemovedTile = 0;
        session.IntroducedTile = 0;
        session.MergeSteps = ImmutableArray<DropMergeMergeStep>.Empty;
        session.Message = "New run started. Drop the tile into any column.";
        return Results.Ok(ToResponse(session));
    }
});

app.Run();

static string EventName(DropMergeEventType eventType) => eventType switch
{
    DropMergeEventType.Started => "started",
    DropMergeEventType.Dropped => "dropped",
    DropMergeEventType.NoMove => "no-move",
    DropMergeEventType.BigTileReached => "big-tile-reached",
    DropMergeEventType.Lost => "lost",
    DropMergeEventType.Undone => "undone",
    _ => throw new ArgumentOutOfRangeException(nameof(eventType), eventType, "Unknown Drop Merge event."),
};

static DropMergeGameResponse ToResponse(DropMergeSession session) => new(
    session.Id,
    session.State.Columns,
    session.State.Rows,
    session.State.Tiles,
    session.State.CurrentTile,
    session.State.NextTile,
    session.State.Score,
    session.State.Moves,
    session.State.Combo,
    session.State.BestCombo,
    session.State.TotalMerges,
    session.State.SmallestTileClearCount,
    session.State.NextBigTile,
    session.State.HighestTile,
    session.State.EmptyTileCount,
    session.State.Phase switch
    {
        DropMergePhase.Playing => "playing",
        DropMergePhase.Lost => "lost",
        _ => throw new InvalidOperationException("Unknown Drop Merge phase."),
    },
    session.History.Count > 0,
    session.LastEvent,
    session.ScoreGained,
    session.MergeCount,
    session.DropColumn,
    session.DropRow,
    session.RemovedTile,
    session.IntroducedTile,
    session.MergeSteps,
    session.State.TempoLevel,
    session.State.DropDurationMilliseconds,
    session.State.DropTimerMilliseconds,
    session.Message);

static IResult NotFound() => Results.NotFound(new DropMergeErrorResponse(
    "drop-merge-game-not-found",
    "That local Drop Merge game does not exist."));

public sealed class DropMergeSession(Guid id, DropMergeState state)
{
    public object SyncRoot { get; } = new();
    public Guid Id { get; } = id;
    public DropMergeState State { get; set; } = state;
    public Stack<DropMergeState> History { get; } = new();
    public string LastEvent { get; set; } = "started";
    public int ScoreGained { get; set; }
    public int MergeCount { get; set; }
    public int DropColumn { get; set; } = -1;
    public int DropRow { get; set; } = -1;
    public int RemovedTile { get; set; }
    public int IntroducedTile { get; set; }
    public ImmutableArray<DropMergeMergeStep> MergeSteps { get; set; } = ImmutableArray<DropMergeMergeStep>.Empty;
    public string Message { get; set; } = "Drop the tile into any column.";
}

public sealed record StartDropMergeRequest(uint? Seed);
public sealed record DropMergeDropRequest(int Column);
public sealed record DropMergeStatusResponse(bool Available, int Columns, int Rows, int FirstBigTile, string Mode);
public sealed record DropMergeGameResponse(Guid GameId, int Columns, int Rows, IReadOnlyList<int> Tiles, int CurrentTile, int NextTile, int Score, int Moves, int Combo, int BestCombo, int TotalMerges, int SmallestTileClearCount, int NextBigTile, int HighestTile, int EmptyTileCount, string Phase, bool CanUndo, string LastEvent, int ScoreGained, int MergeCount, int DropColumn, int DropRow, int RemovedTile, int IntroducedTile, IReadOnlyList<DropMergeMergeStep> MergeSteps, int TempoLevel, int DropDurationMilliseconds, int DropTimerMilliseconds, string Message);
public sealed record DropMergeErrorResponse(string Code, string Message);
