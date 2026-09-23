using System.Collections.Concurrent;
using System.Collections.Immutable;
using System.Security.Cryptography;
using FortuneForge.Games.Roulette;

var builder = WebApplication.CreateBuilder(args);
builder.WebHost.UseUrls("http://127.0.0.1:5185");
builder.Services.AddSingleton<IRoulettePocketSource, SecureRandomPocketSource>();
builder.Services.AddCors(options => options.AddDefaultPolicy(policy => policy.WithOrigins("http://127.0.0.1:5175", "http://localhost:5175").AllowAnyHeader().AllowAnyMethod()));
var app = builder.Build();
app.UseCors();
var rounds = new ConcurrentDictionary<Guid, RouletteSession>();
const decimal startingBalance = 1000m;
var balanceGate = new object();
var playerBalance = startingBalance;

app.MapGet("/", () => Results.Ok(new { game = "Fortune Forge Roulette", client = "cd games/Roulette/client/FortuneForge.Games.Roulette.Client; npm run dev" }));
var api = app.MapGroup("/api/games/roulette");
api.MapGet("/status", () => Results.Ok(new RouletteStatusResponse(true, 1m, 100m, 1m, startingBalance, "local-free-play-single-zero")));
api.MapPost("/rounds", () =>
{
    var id = Guid.NewGuid();
    decimal balance;
    lock (balanceGate) balance = playerBalance;
    var session = new RouletteSession(RouletteEngine.OpenRound(id.ToString("N")), balance);
    rounds[id] = session;
    return Results.Created($"/api/games/roulette/rounds/{id}", ToResponse(id, session));
});
api.MapGet("/rounds/{roundId:guid}", (Guid roundId) => rounds.TryGetValue(roundId, out var session) ? Results.Ok(ToResponse(roundId, session)) : NotFound());
api.MapPost("/rounds/{roundId:guid}/bets", (Guid roundId, PlaceRouletteBetRequest request) =>
{
    if (!rounds.TryGetValue(roundId, out var session)) return NotFound();
    lock (session)
    {
        try
        {
            var bet = new RouletteBet("local-player", ParseKind(request.Kind), request.Stake, request.Number, request.Numbers?.ToImmutableArray() ?? []);
            if (bet.Stake > session.Balance)
                return Results.BadRequest(new RouletteErrorResponse("roulette-insufficient-balance", "The stake exceeds your available balance."));
            session.State = RouletteEngine.PlaceBet(session.State, bet);
            session.Balance -= bet.Stake;
            lock (balanceGate) playerBalance = session.Balance;
            return Results.Ok(ToResponse(roundId, session));
        }
        catch (Exception exception) when (exception is ArgumentException or ArgumentOutOfRangeException or RouletteRuleException)
        { return Results.BadRequest(new RouletteErrorResponse("roulette-invalid-bet", exception.Message)); }
    }
});
api.MapDelete("/rounds/{roundId:guid}/bets/{betIndex:int}", (Guid roundId, int betIndex) =>
{
    if (!rounds.TryGetValue(roundId, out var session)) return NotFound();
    lock (session)
    {
        try
        {
            var bet = session.State.Bets[betIndex];
            session.State = RouletteEngine.RemoveBet(session.State, betIndex);
            session.Balance += bet.Stake;
            lock (balanceGate) playerBalance = session.Balance;
            return Results.Ok(ToResponse(roundId, session));
        }
        catch (Exception exception) when (exception is ArgumentOutOfRangeException or RouletteRuleException)
        { return Results.BadRequest(new RouletteErrorResponse("roulette-invalid-bet", exception.Message)); }
    }
});
api.MapDelete("/rounds/{roundId:guid}/bets", (Guid roundId) =>
{
    if (!rounds.TryGetValue(roundId, out var session)) return NotFound();
    lock (session)
    {
        if (session.State.Phase != RouletteRoundPhase.Open) return Results.BadRequest(new RouletteErrorResponse("roulette-round-settled", "Bets cannot be cleared after the spin."));
        session.Balance += session.State.Bets.Sum(bet => bet.Stake);
        session.State = RouletteEngine.ClearBets(session.State);
        lock (balanceGate) playerBalance = session.Balance;
        return Results.Ok(ToResponse(roundId, session));
    }
});
api.MapPost("/rounds/{roundId:guid}/spin", (Guid roundId, IRoulettePocketSource source) =>
{
    if (!rounds.TryGetValue(roundId, out var session)) return NotFound();
    lock (session)
    {
        try
        {
            var result = RouletteEngine.Spin(session.State, source.Next());
            session.State = result.State;
            session.Balance += result.Settlements.Sum(settlement => settlement.TotalReturn);
            lock (balanceGate) playerBalance = session.Balance;
            return Results.Ok(ToResponse(roundId, session));
        }
        catch (RouletteRuleException exception) { return Results.BadRequest(new RouletteErrorResponse("roulette-cannot-spin", exception.Message)); }
    }
});
app.Run();

static IResult NotFound() => Results.NotFound(new RouletteErrorResponse("roulette-round-not-found", "That local round does not exist."));
static RouletteRoundResponse ToResponse(Guid id, RouletteSession session) => new(id, session.Balance, session.State.Phase == RouletteRoundPhase.Open ? "open" : "settled", session.State.Bets.Select((bet, index) => ToBet(index, bet)).ToArray(), session.State.WinningPocket?.Number, session.State.Settlements.Select(ToSettlement).ToArray());
static RouletteBetResponse ToBet(int index, RouletteBet bet) => new(index, bet.PlayerId, KindName(bet.Kind), bet.Stake, bet.Number, bet.Numbers.IsDefault ? [] : bet.Numbers.ToArray());
static RouletteSettlementResponse ToSettlement(RouletteSettlement settlement) => new(settlement.PlayerId, KindName(settlement.Kind), settlement.Stake, settlement.Won, settlement.TotalReturn);
static RouletteBetKind ParseKind(string kind) => kind switch { "straight" => RouletteBetKind.Straight, "split" => RouletteBetKind.Split, "street" => RouletteBetKind.Street, "corner" => RouletteBetKind.Corner, "six-line" => RouletteBetKind.SixLine, "column" => RouletteBetKind.Column, "dozen" => RouletteBetKind.Dozen, "red" => RouletteBetKind.Red, "black" => RouletteBetKind.Black, "even" => RouletteBetKind.Even, "odd" => RouletteBetKind.Odd, "low" => RouletteBetKind.Low, "high" => RouletteBetKind.High, _ => throw new ArgumentException("Unknown Roulette bet kind.", nameof(kind)) };
static string KindName(RouletteBetKind kind) => kind switch { RouletteBetKind.Straight => "straight", RouletteBetKind.Split => "split", RouletteBetKind.Street => "street", RouletteBetKind.Corner => "corner", RouletteBetKind.SixLine => "six-line", RouletteBetKind.Column => "column", RouletteBetKind.Dozen => "dozen", RouletteBetKind.Red => "red", RouletteBetKind.Black => "black", RouletteBetKind.Even => "even", RouletteBetKind.Odd => "odd", RouletteBetKind.Low => "low", RouletteBetKind.High => "high", _ => throw new InvalidOperationException() };

public interface IRoulettePocketSource { RoulettePocket Next(); }
public sealed class SecureRandomPocketSource : IRoulettePocketSource { public RoulettePocket Next() => new(RandomNumberGenerator.GetInt32(0, 37)); }
public sealed class RouletteSession(RouletteRoundState state, decimal balance) { public RouletteRoundState State { get; set; } = state; public decimal Balance { get; set; } = balance; }
public sealed record PlaceRouletteBetRequest(string Kind, decimal Stake, int? Number, int[]? Numbers);
public sealed record RouletteStatusResponse(bool Available, decimal MinimumStake, decimal MaximumStake, decimal StakeIncrement, decimal StartingBalance, string Mode);
public sealed record RouletteRoundResponse(Guid RoundId, decimal Balance, string Phase, IReadOnlyList<RouletteBetResponse> Bets, int? WinningPocket, IReadOnlyList<RouletteSettlementResponse> Settlements);
public sealed record RouletteBetResponse(int BetIndex, string PlayerId, string Kind, decimal Stake, int? Number, IReadOnlyList<int> Numbers);
public sealed record RouletteSettlementResponse(string PlayerId, string Kind, decimal Stake, bool Won, decimal TotalReturn);
public sealed record RouletteErrorResponse(string Code, string Message);
