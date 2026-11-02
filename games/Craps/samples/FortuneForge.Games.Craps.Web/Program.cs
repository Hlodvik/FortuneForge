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
var rounds = new ConcurrentDictionary<Guid, LocalCrapsSession>();

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
    if (!ValidStake(request.Stake))
    {
        return Results.BadRequest(new CrapsErrorResponse(
            "craps-invalid-stake",
            "Choose a Pass Line wager from R1 through R100 in whole-Rand steps."));
    }

    var extras = new List<LocalCrapsExtraBet>();
    foreach (var extra in request.ExtraBets ?? [])
    {
        var kind = extra?.Kind?.Trim().ToLowerInvariant();
        if (kind is not ("field" or "any-seven" or "any-craps"))
            return Results.BadRequest(new CrapsErrorResponse("craps-invalid-action",
                "The supported one-roll bets are Field, Any Seven, and Any Craps."));
        if (!ValidStake(extra!.Stake))
            return Results.BadRequest(new CrapsErrorResponse("craps-invalid-stake",
                "Choose each extra bet from R1 through R100 in whole-Rand steps."));
        extras.Add(new LocalCrapsExtraBet(kind, extra.Stake));
    }

    var id = Guid.NewGuid();
    var session = new LocalCrapsSession(CrapsEngine.StartPassLine(new CrapsPassLineBet("local-player", request.Stake)), extras);
    rounds[id] = session;
    return Results.Created($"/api/games/craps/rounds/{id}", ToResponse(id, session));
});

api.MapGet("/rounds/{roundId:guid}", (Guid roundId) =>
{
    if (!rounds.TryGetValue(roundId, out var session)) return RoundNotFound();
    lock (session.SyncRoot) return Results.Ok(ToResponse(roundId, session));
});

api.MapPost("/rounds/{roundId:guid}/roll", (Guid roundId) =>
{
    if (!rounds.TryGetValue(roundId, out var session)) return RoundNotFound();
    lock (session.SyncRoot)
    {
        if (session.State.Phase == CrapsPassLinePhase.Resolved)
        {
            return Results.Conflict(new CrapsErrorResponse(
                "craps-round-resolved",
                "Start a new round before rolling again."));
        }

        var dice = new DicePair(
            new DieValue(RandomNumberGenerator.GetInt32(1, 7)),
            new DieValue(RandomNumberGenerator.GetInt32(1, 7)));
        session.State = CrapsEngine.Roll(session.State, dice).State;
        SettleExtras(session, dice.Total);
        return Results.Ok(ToResponse(roundId, session));
    }
});

api.MapPost("/rounds/{roundId:guid}/odds", (Guid roundId, PlaceCrapsOddsRequest request) =>
{
    if (!rounds.TryGetValue(roundId, out var session)) return RoundNotFound();
    lock (session.SyncRoot)
    {
        if (session.State.Phase is not CrapsPassLinePhase.Point || session.State.Point is null)
            return Results.BadRequest(new CrapsErrorResponse("craps-invalid-action",
                "Pass Line odds are available only after a point is established."));
        if (!ValidStake(request.Stake))
            return Results.BadRequest(new CrapsErrorResponse("craps-invalid-stake",
                "Choose odds from R1 through R100 in whole-Rand steps."));
        if (session.ExtraBets.Any(bet => bet.Kind == "odds" && !bet.Resolved))
            return Results.BadRequest(new CrapsErrorResponse("craps-invalid-action", "Odds are already placed for this point."));
        session.ExtraBets.Add(new LocalCrapsExtraBet("odds", request.Stake));
        return Results.Ok(ToResponse(roundId, session));
    }
});

app.Run();

static bool ValidStake(decimal stake) => stake is >= 1m and <= 100m && stake % 1m == 0m;

static IResult RoundNotFound() => Results.NotFound(new CrapsErrorResponse("craps-round-not-found", "That local round no longer exists."));

// The standalone preview mirrors the production free-play side bets; no wallet is involved.
static void SettleExtras(LocalCrapsSession session, int total)
{
    foreach (var bet in session.ExtraBets.Where(bet => !bet.Resolved))
    {
        if (bet.Kind == "odds" && session.State.Phase is not CrapsPassLinePhase.Resolved) continue;
        bet.Resolved = true;
        bet.TotalReturn = bet.Kind switch
        {
            "field" when total is 2 or 12 => bet.Stake * 3m,
            "field" when total is 3 or 4 or 9 or 10 or 11 => bet.Stake * 2m,
            "any-seven" when total == 7 => bet.Stake * 5m,
            "any-craps" when total is 2 or 3 or 12 => bet.Stake * 8m,
            "odds" when session.State.LastOutcome?.Result is CrapsRollResult.PointHit => session.State.Point switch
            {
                4 or 10 => bet.Stake * 3m,
                5 or 9 => bet.Stake * 2.5m,
                6 or 8 => bet.Stake * 2.2m,
                _ => throw new InvalidOperationException("The point does not support odds."),
            },
            _ => 0m,
        };
        bet.Won = bet.TotalReturn > 0m;
    }
}

static CrapsRoundResponse ToResponse(Guid id, LocalCrapsSession session)
{
    var state = session.State;
    return new(
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
        LastOutcome: state.LastOutcome is null ? null : ToOutcome(state.LastOutcome),
        ExtraBets: session.ExtraBets.Select(bet => new CrapsExtraBetResponse(bet.Kind, bet.Stake, bet.Resolved, bet.Won, bet.TotalReturn)).ToArray());
}

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

public sealed record StartCrapsRoundRequest(decimal Stake, IReadOnlyList<CrapsExtraBetRequest?>? ExtraBets = null);
public sealed record CrapsExtraBetRequest(string? Kind, decimal Stake);
public sealed record PlaceCrapsOddsRequest(decimal Stake);

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
    CrapsOutcomeResponse? LastOutcome,
    IReadOnlyList<CrapsExtraBetResponse> ExtraBets);

public sealed record CrapsExtraBetResponse(string Kind, decimal Stake, bool Resolved, bool Won, decimal? TotalReturn);

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

internal sealed class LocalCrapsSession(CrapsPassLineState state, List<LocalCrapsExtraBet> extraBets)
{
    public object SyncRoot { get; } = new();
    public CrapsPassLineState State { get; set; } = state;
    public List<LocalCrapsExtraBet> ExtraBets { get; } = extraBets;
}

internal sealed class LocalCrapsExtraBet(string kind, decimal stake)
{
    public string Kind { get; } = kind;
    public decimal Stake { get; } = stake;
    public bool Resolved { get; set; }
    public bool Won { get; set; }
    public decimal? TotalReturn { get; set; }
}
