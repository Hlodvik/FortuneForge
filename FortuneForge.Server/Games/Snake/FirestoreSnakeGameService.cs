using System.Security.Cryptography;
using System.Text.Json;
using FortuneForge.Games.Abstractions.Auditing;
using FortuneForge.Games.Snake;
using FortuneForge.Server.RoundAudit;
using FortuneForge.Server.SecurityAudit;
using Google.Cloud.Firestore;

namespace FortuneForge.Server.Games.Snake;

/// <summary>The Firestore transaction owns every accepted Snake transition and its private journal.</summary>
public sealed class FirestoreSnakeGameService
{
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);
    private const int MaximumActions = 10_000;
    private readonly FirestoreDb database;
    private readonly TimeProvider clock;
    private readonly Func<uint> nextSeed;

    public FirestoreSnakeGameService(FirestoreDb database, TimeProvider clock)
        : this(database, clock, () => (uint)RandomNumberGenerator.GetInt32(1, int.MaxValue)) { }

    internal FirestoreSnakeGameService(FirestoreDb database, TimeProvider clock, Func<uint> nextSeed)
    {
        this.database = database;
        this.clock = clock;
        this.nextSeed = nextSeed;
    }

    internal SnakeStatusResponse Status() => new(true, SnakeEngine.DefaultWidth, SnakeEngine.DefaultHeight,
        SnakeEngine.FoodScore, 180, "account-free-play-hosted");

    internal Task<SnakeGameResponse> StartAsync(string userId, SnakeStartRequest? request, CancellationToken ct)
    {
        RejectClientSeed(request);
        var size = BoardSize(request?.Width, request?.Height, SnakeEngine.DefaultWidth);
        var now = clock.GetUtcNow().UtcDateTime;
        var state = SnakeEngine.Start(Seed(), size, size);
        var game = new SnakeStoredGame(Guid.NewGuid(), Guid.NewGuid(), userId, now, state, 0,
            [], 0, "started", 0, "Use the arrow keys or controls to steer the snake.");
        return database.RunTransactionAsync(async tx =>
        {
            tx.Create(Game(game.GameId), Fields(game, now));
            GameSecurityJournal.Write(tx, database, "snake", game.RunId.ToString("N"), 0, now,
                Observation(game, "start"), [userId], "started", game.GameId.ToString("N"));
            return ToResponse(game);
        }, cancellationToken: ct);
    }

    internal async Task<SnakeGameResponse> GetAsync(string userId, Guid gameId, CancellationToken ct)
    {
        var snapshot = await Game(gameId).GetSnapshotAsync(ct);
        return ToResponse(ReadForUser(snapshot, userId));
    }

    internal Task<SnakeGameResponse> TurnAsync(string userId, Guid gameId, string? direction, CancellationToken ct)
    {
        var parsed = ParseDirection(direction);
        return ChangeAsync(userId, gameId, "turn:" + parsed.ToString().ToLowerInvariant(),
            state => SnakeEngine.Turn(state, parsed), ct);
    }

    internal Task<SnakeGameResponse> TickAsync(string userId, Guid gameId, CancellationToken ct) =>
        ChangeAsync(userId, gameId, "tick", SnakeEngine.Tick, ct);

    internal Task<SnakeGameResponse> ResetAsync(string userId, Guid gameId, SnakeStartRequest? request, CancellationToken ct)
    {
        RejectClientSeed(request);
        return database.RunTransactionAsync(async tx =>
        {
            var reference = Game(gameId);
            var current = ReadForUser(await tx.GetSnapshotAsync(reference, ct), userId);
            var size = BoardSize(request?.Width, request?.Height, current.State.Width);
            var now = clock.GetUtcNow().UtcDateTime;
            if (current.State.Phase == SnakePhase.Playing)
            {
                var abandoned = current with { Version = checked(current.Version + 1),
                    Actions = current.Actions.Append("abandon").ToArray(), LastEvent = "abandoned",
                    ScoreGained = 0, Message = "The run was reset before completion." };
                GameSecurityJournal.Write(tx, database, "snake", current.RunId.ToString("N"), abandoned.Version,
                    now, Observation(abandoned, "reset-abandoned"), [userId], "reset-abandoned", gameId.ToString("N"));
                WriteCompletion(tx, abandoned, now, abandoned: true);
            }
            var best = size == current.State.Width ? current.State.BestScore : 0;
            var state = SnakeEngine.Start(Seed(), size, size, best);
            var restarted = new SnakeStoredGame(gameId, Guid.NewGuid(), userId, now, state, best,
                [], 0, "started", 0, "New game started.");
            tx.Set(reference, Fields(restarted, now));
            GameSecurityJournal.Write(tx, database, "snake", restarted.RunId.ToString("N"), 0, now,
                Observation(restarted, "reset"), [userId], "started", gameId.ToString("N"));
            return ToResponse(restarted);
        }, cancellationToken: ct);
    }

    private Task<SnakeGameResponse> ChangeAsync(string userId, Guid gameId, string action,
        Func<SnakeState, SnakeTransition> change, CancellationToken ct) =>
        database.RunTransactionAsync(async tx =>
        {
            var reference = Game(gameId);
            var current = ReadForUser(await tx.GetSnapshotAsync(reference, ct), userId);
            if (current.Actions.Count >= MaximumActions)
                throw new SnakeRuleException("This Snake run reached its audit action limit. Start a new run.");
            var transition = change(current.State);
            var now = clock.GetUtcNow().UtcDateTime;
            var updated = current with
            {
                State = transition.State,
                Actions = current.Actions.Append(action).ToArray(),
                Version = checked(current.Version + 1),
                LastEvent = EventName(transition.Event),
                ScoreGained = transition.ScoreGained,
                Message = transition.Message
            };
            tx.Set(reference, Fields(updated, now));
            GameSecurityJournal.Write(tx, database, "snake", updated.RunId.ToString("N"), updated.Version,
                now, Observation(updated, action), [userId], updated.LastEvent, gameId.ToString("N"));
            if (updated.State.Phase != SnakePhase.Playing) WriteCompletion(tx, updated, now, abandoned: false);
            return ToResponse(updated);
        }, cancellationToken: ct);

    private void WriteCompletion(Transaction tx, SnakeStoredGame game, DateTime now, bool abandoned)
    {
        var payload = CompletionPayload(game, abandoned);
        var findings = SnakeReplayAudit.Check(game.State, game.InitialBestScore, game.Actions, abandoned);
        var capture = GameSecurityEventSink.FreeCompletion("snake", game.RunId.ToString("N"), game.GameId.ToString("N"),
            1, game.StartedAtUtc, now, payload, game.UserId, "snake-private-run-v1", findings, SnakeReplayAudit.Version);
        RoundAuditFirestore.Write(tx, database, capture);
    }

    internal static object CompletionPayload(SnakeStoredGame game, bool abandoned) => new
    {
        game.GameId, game.RunId, game.StartedAtUtc, game.UserId,
        game.InitialBestScore, game.State, game.Actions, game.Version, game.LastEvent,
        game.ScoreGained, abandoned, scoreClassification = ScoreClassification(game.State.Width),
        engineVersion = SnakeReplayAudit.Version
    };

    private static object Observation(SnakeStoredGame game, string action) => new
    {
        game.GameId, game.RunId, game.UserId, game.StartedAtUtc, game.InitialBestScore,
        action, game.Version, game.State, game.LastEvent, game.ScoreGained,
        scoreClassification = ScoreClassification(game.State.Width)
    };

    private DocumentReference Game(Guid id) => database.Collection("snakeGames").Document(id.ToString("N"));
    private static Dictionary<string, object> Fields(SnakeStoredGame game, DateTime now) => new()
    {
        ["gameId"] = game.GameId.ToString("N"), ["runId"] = game.RunId.ToString("N"),
        ["userId"] = game.UserId, ["stateJson"] = JsonSerializer.Serialize(game, Json),
        ["phase"] = game.State.Phase.ToString().ToLowerInvariant(), ["version"] = game.Version,
        ["scoreClassification"] = ScoreClassification(game.State.Width),
        ["startedAtUtc"] = Timestamp.FromDateTime(game.StartedAtUtc),
        ["updatedAtUtc"] = Timestamp.FromDateTime(now), ["schemaVersion"] = 1L
    };
    private static SnakeStoredGame ReadForUser(DocumentSnapshot snapshot, string userId)
    {
        if (!snapshot.Exists || !snapshot.TryGetValue<string>("userId", out var owner) || owner != userId)
            throw new SnakeAccessException();
        return JsonSerializer.Deserialize<SnakeStoredGame>(snapshot.GetValue<string>("stateJson"), Json)
            ?? throw new InvalidOperationException("Stored Snake state is invalid.");
    }
    private uint Seed()
    {
        var seed = nextSeed();
        if (seed == 0) throw new InvalidOperationException("The Snake seed source returned zero.");
        return seed;
    }
    internal static string ScoreClassification(int size) => $"snake-practice-{size}x{size}-v1";
    private static void RejectClientSeed(SnakeStartRequest? request)
    {
        if (request?.Seed is not null)
            throw new SnakeRuleException("Hosted Snake uses a server-generated seed.");
    }
    private static int BoardSize(int? width, int? height, int fallback)
    {
        var chosenWidth = width ?? fallback;
        var chosenHeight = height ?? fallback;
        if (chosenWidth != chosenHeight || chosenWidth is not (16 or 20 or 24))
            throw new SnakeRuleException("Choose a supported square Snake board: 16, 20, or 24 cells per side.");
        return chosenWidth;
    }
    private static SnakeDirection ParseDirection(string? direction) => direction?.Trim().ToLowerInvariant() switch
    {
        "up" => SnakeDirection.Up, "right" => SnakeDirection.Right,
        "down" => SnakeDirection.Down, "left" => SnakeDirection.Left,
        _ => throw new SnakeRuleException("Direction must be up, right, down, or left.")
    };
    private static string EventName(SnakeEventType value) => value switch
    {
        SnakeEventType.Started => "started", SnakeEventType.Turned => "turned",
        SnakeEventType.Moved => "moved", SnakeEventType.AteFood => "ate-food",
        SnakeEventType.NoOp => "no-op", SnakeEventType.Won => "won", SnakeEventType.Lost => "lost",
        _ => throw new ArgumentOutOfRangeException(nameof(value))
    };
    private static SnakeGameResponse ToResponse(SnakeStoredGame game) => new(game.GameId,
        game.State.Width, game.State.Height, game.State.Body.Select(point => new SnakePointResponse(point.X, point.Y)).ToArray(),
        game.State.Food is { } food ? new SnakePointResponse(food.X, food.Y) : null,
        game.State.Direction.ToString().ToLowerInvariant(), game.State.Score, game.State.BestScore,
        game.State.Moves, game.State.Length, game.State.Phase.ToString().ToLowerInvariant(),
        game.LastEvent, game.ScoreGained, game.Message);
}

internal sealed record SnakeStoredGame(Guid GameId, Guid RunId, string UserId, DateTime StartedAtUtc,
    SnakeState State, int InitialBestScore, IReadOnlyList<string> Actions, long Version,
    string LastEvent, int ScoreGained, string Message);

internal sealed class SnakeAccessException() : Exception("That Snake game is not available.");
internal sealed class SnakeRuleException(string message) : Exception(message);
public sealed record SnakeStartRequest(uint? Seed, int? Width, int? Height);
public sealed record SnakeTurnRequest(string? Direction);
public sealed record SnakeStatusResponse(bool Available, int Width, int Height, int FoodScore, int TickMilliseconds, string Mode);
public sealed record SnakePointResponse(int X, int Y);
public sealed record SnakeGameResponse(Guid GameId, int Width, int Height, IReadOnlyList<SnakePointResponse> Body,
    SnakePointResponse? Food, string Direction, int Score, int BestScore, int Moves, int Length,
    string Phase, string LastEvent, int ScoreGained, string Message);
public sealed record SnakeErrorResponse(string Code, string Message);
