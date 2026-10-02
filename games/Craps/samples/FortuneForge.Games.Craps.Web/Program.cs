using System.Collections.Concurrent;
using System.Security.Cryptography;
using FortuneForge.Games.Craps;
using FortuneForge.Games.Dice;

var builder = WebApplication.CreateBuilder(args);

builder.Services.AddCors(options =>
{
    options.AddDefaultPolicy(policy => policy
        .WithOrigins("http://127.0.0.1:5174", "http://localhost:5174")
        .AllowAnyHeader()
        .AllowAnyMethod());
});

var app = builder.Build();
var rounds = new ConcurrentDictionary<Guid, CrapsPassLineState>();

app.UseCors();

app.MapGet("/", () => Results.Ok(new
{
    game = "Fortune Forge Craps",
    client = "Run npm install and npm run dev from games/Craps/client/FortuneForge.Games.Craps.Client.",
}));

var api = app.MapGroup("/api/games/craps");

api.MapGet("/status", () => Results.Ok(new CrapsStatusResponse(
    Available: true,
    MinimumStake: 1m,
    MaximumStake: 100m,
    StakeIncrement: 1m,
    Mode: "local-free-play")));

api.MapPost("/rounds", (StartCrapsRoundRequest request) =>
{
    if (request.Stake is < 1m or > 100m)
    {
        return Results.BadRequest(new CrapsErrorResponse(
            "craps-invalid-stake",
            "Choose a pass-line stake from R1 to R100."));
    }

    var id = Guid.NewGuid();
    var state = CrapsEngine.StartPassLine(new CrapsPassLineBet("local-player", request.Stake));
    rounds[id] = state;
    return Results.Created($"/api/games/craps/rounds/{id}", ToResponse(id, state));
});

api.MapGet("/rounds/{roundId:guid}", (Guid roundId) =>
    rounds.TryGetValue(roundId, out var state)
        ? Results.Ok(ToResponse(roundId, state))
        : Results.NotFound(new CrapsErrorResponse("craps-round-not-found", "That local round no longer exists.")));

api.MapPost("/rounds/{roundId:guid}/roll", (Guid roundId) =>
{
    while (rounds.TryGetValue(roundId, out var state))
    {
        if (state.Phase == CrapsPassLinePhase.Resolved)
        {
            return Results.Conflict(new CrapsErrorResponse(
                "craps-round-resolved",
                "Start a new round before rolling again."));
        }

        var dice = new DicePair(
            new DieValue(RandomNumberGenerator.GetInt32(1, 7)),
            new DieValue(RandomNumberGenerator.GetInt32(1, 7)));
        var transition = CrapsEngine.Roll(state, dice);
        if (rounds.TryUpdate(roundId, transition.State, state))
            return Results.Ok(ToResponse(roundId, transition.State));
    }

    return Results.NotFound(new CrapsErrorResponse("craps-round-not-found", "That local round no longer exists."));
});

app.Run();

static CrapsRoundResponse ToResponse(Guid id, CrapsPassLineState state) => new(
    RoundId: id,
    Stake: state.Bet.Stake,
    Phase: state.Phase switch
    {
        CrapsPassLinePhase.ComeOut => "come-out",
        CrapsPassLinePhase.Point => "point",
        CrapsPassLinePhase.Resolved => "resolved",
        _ => throw new InvalidOperationException("Unknown Craps phase."),
    },
    Point: state.Point,
    Rolls: state.Rolls.Select((dice, index) => ToRoll(index + 1, dice, state)).ToArray(),
    LastOutcome: state.LastOutcome is null ? null : ToOutcome(state.LastOutcome));

static CrapsRollResponse ToRoll(int rollNumber, DicePair dice, CrapsPassLineState state)
{
    var isLast = rollNumber == state.Rolls.Length;
    return new CrapsRollResponse(
        RollNumber: rollNumber,
        First: dice.First.Value,
        Second: dice.Second.Value,
        Total: dice.Total,
        Result: isLast && state.LastOutcome is not null ? ResultName(state.LastOutcome.Result) : null);
}

static CrapsOutcomeResponse ToOutcome(CrapsRollOutcome outcome) => new(
    First: outcome.Dice.First.Value,
    Second: outcome.Dice.Second.Value,
    Total: outcome.Dice.Total,
    Result: ResultName(outcome.Result),
    IsTerminal: outcome.IsTerminal,
    TotalReturn: outcome.TotalReturn);

static string ResultName(CrapsRollResult result) => result switch
{
    CrapsRollResult.NaturalWin => "natural-win",
    CrapsRollResult.CrapsLoss => "craps-loss",
    CrapsRollResult.PointEstablished => "point-established",
    CrapsRollResult.PointHit => "point-hit",
    CrapsRollResult.SevenOut => "seven-out",
    CrapsRollResult.NoDecision => "no-decision",
    _ => throw new InvalidOperationException("Unknown Craps result."),
};

public sealed record StartCrapsRoundRequest(decimal Stake);

public sealed record CrapsStatusResponse(
    bool Available,
    decimal MinimumStake,
    decimal MaximumStake,
    decimal StakeIncrement,
    string Mode);

public sealed record CrapsRoundResponse(
    Guid RoundId,
    decimal Stake,
    string Phase,
    int? Point,
    IReadOnlyList<CrapsRollResponse> Rolls,
    CrapsOutcomeResponse? LastOutcome);

public sealed record CrapsRollResponse(
    int RollNumber,
    int First,
    int Second,
    int Total,
    string? Result);

public sealed record CrapsOutcomeResponse(
    int First,
    int Second,
    int Total,
    string Result,
    bool IsTerminal,
    decimal? TotalReturn);

public sealed record CrapsErrorResponse(string Code, string Message);
