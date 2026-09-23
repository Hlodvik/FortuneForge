using System.Collections.Concurrent;
using System.Security.Cryptography;
using FortuneForge.Games.Cards;
using FortuneForge.Games.Dice;
using FortuneForge.Games.LiarsDice;

const string humanId = "you";
var botIds = new HashSet<string>(["bot-1", "bot-2", "bot-3"], StringComparer.Ordinal);
var bot = new LiarsDiceBotAgent();
var botOptions = new CardBotGameOptions { Enabled = true };
var matches = new ConcurrentDictionary<Guid, LiarsDiceSession>();

var builder = WebApplication.CreateBuilder(args);
builder.WebHost.UseUrls("http://127.0.0.1:5190");
builder.Services.AddCors(options => options.AddDefaultPolicy(policy => policy
    .WithOrigins("http://127.0.0.1:5180", "http://localhost:5180")
    .AllowAnyHeader()
    .AllowAnyMethod()));

var app = builder.Build();
app.UseCors();

app.MapGet("/", () => Results.Ok(new
{
    game = "Fortune Forge Liar's Dice",
    client = "cd games/LiarsDice/client/FortuneForge.Games.LiarsDice.Client; npm run dev",
}));

var api = app.MapGroup("/api/games/liars-dice");

api.MapGet("/status", () => Results.Ok(new LiarsDiceStatusResponse(
    Available: true,
    StartingDicePerPlayer: LiarsDiceMatchEngine.DefaultDicePerPlayer,
    Mode: "local-free-play-bots")));

api.MapPost("/matches", (StartLiarsDiceMatchRequest? request) =>
{
    request ??= new StartLiarsDiceMatchRequest(null, LiarsDiceMatchEngine.DefaultDicePerPlayer);
    var dicePerPlayer = request.DicePerPlayer ?? LiarsDiceMatchEngine.DefaultDicePerPlayer;
    var seed = request.Seed ?? (uint)RandomNumberGenerator.GetInt32(1, int.MaxValue);
    try
    {
        var session = new LiarsDiceSession(Guid.NewGuid(), LiarsDiceMatchEngine.Start(seed, null, dicePerPlayer), CardBotSeed.Create());
        matches[session.Id] = session;
        return Results.Created($"/api/games/liars-dice/matches/{session.Id}", ToResponse(session));
    }
    catch (ArgumentException exception)
    {
        return Results.BadRequest(new LiarsDiceErrorResponse("liars-dice-invalid-match", exception.Message));
    }
});

api.MapGet("/matches/{matchId:guid}", (Guid matchId) =>
    matches.TryGetValue(matchId, out var session)
        ? Results.Ok(ToResponse(session))
        : NotFound());

api.MapPost("/matches/{matchId:guid}/bid", (Guid matchId, PlaceLiarsDiceBidRequest request) =>
{
    if (!matches.TryGetValue(matchId, out var session)) return NotFound();
    lock (session.SyncRoot)
    {
        try
        {
            var bid = new LiarsDiceBid(request.Quantity, new DieValue(request.Face));
            ApplyHumanAction(session, new PlaceLiarsDiceBid(humanId, bid));
            return Results.Ok(ToResponse(session));
        }
        catch (Exception exception) when (exception is ArgumentException or ArgumentOutOfRangeException or LiarsDiceRuleException)
        {
            return Results.BadRequest(new LiarsDiceErrorResponse("liars-dice-invalid-bid", exception.Message));
        }
    }
});

api.MapPost("/matches/{matchId:guid}/challenge", (Guid matchId) =>
{
    if (!matches.TryGetValue(matchId, out var session)) return NotFound();
    lock (session.SyncRoot)
    {
        try
        {
            ApplyHumanAction(session, new ChallengeLiarsDiceBid(humanId));
            return Results.Ok(ToResponse(session));
        }
        catch (LiarsDiceRuleException exception)
        {
            return Results.BadRequest(new LiarsDiceErrorResponse("liars-dice-invalid-challenge", exception.Message));
        }
    }
});

api.MapPost("/matches/{matchId:guid}/advance", (Guid matchId) =>
{
    if (!matches.TryGetValue(matchId, out var session)) return NotFound();
    lock (session.SyncRoot)
    {
        try
        {
            AdvanceBots(session);
            return Results.Ok(ToResponse(session));
        }
        catch (LiarsDiceRuleException exception)
        {
            return Results.BadRequest(new LiarsDiceErrorResponse("liars-dice-cannot-advance", exception.Message));
        }
    }
});

api.MapPost("/matches/{matchId:guid}/next-round", (Guid matchId, NextLiarsDiceRoundRequest? request) =>
{
    if (!matches.TryGetValue(matchId, out var session)) return NotFound();
    lock (session.SyncRoot)
    {
        try
        {
            var seed = request?.Seed ?? (uint)RandomNumberGenerator.GetInt32(1, int.MaxValue);
            session.State = LiarsDiceMatchEngine.StartNextRound(session.State, seed);
            session.Message = $"Round {session.State.RoundNumber} begins. {session.State.CurrentPlayerId} acts first.";
            AdvanceBots(session);
            return Results.Ok(ToResponse(session));
        }
        catch (Exception exception) when (exception is ArgumentException or ArgumentOutOfRangeException or LiarsDiceRuleException)
        {
            return Results.BadRequest(new LiarsDiceErrorResponse("liars-dice-cannot-start-round", exception.Message));
        }
    }
});

app.Run();

void ApplyHumanAction(LiarsDiceSession session, LiarsDiceCommand command)
{
    if (!string.Equals(session.State.CurrentPlayerId, humanId, StringComparison.Ordinal))
        throw new LiarsDiceRuleException($"It is {session.State.CurrentPlayerId}'s turn. The bots are still acting.");
    var transition = LiarsDiceMatchEngine.Apply(session.State, command);
    session.State = transition.State;
    session.Message = transition.Message;
    AdvanceBots(session);
}

void AdvanceBots(LiarsDiceSession session)
{
    if (session.State.Winner is not null || session.State.Round.Phase == LiarsDiceRoundPhase.Resolved)
        return;

    string? lastMessage = null;
    for (var action = 0; action < 256; action++)
    {
        if (session.State.Winner is not null ||
            session.State.Round.Phase == LiarsDiceRoundPhase.Resolved ||
            !botIds.Contains(session.State.CurrentPlayerId))
            break;

        var transition = LiarsDiceMatchEngine.AdvanceBotTurn(
            session.State,
            botIds,
            bot,
            CardBotSkillLevels.Strong,
            session.BotSeed,
            session.BotActionVersion++,
            botOptions);
        session.State = transition.State;
        lastMessage = transition.Message;
    }

    if (lastMessage is not null)
        session.Message = lastMessage;
}

static LiarsDiceMatchResponse ToResponse(LiarsDiceSession session)
{
    var state = session.State;
    var round = state.Round;
    var hand = round.Hands.TryGetValue(humanId, out var humanHand) ? humanHand : [];
    var players = state.AllPlayerIds.Select(player => new LiarsDicePlayerResponse(
        player,
        DisplayName(player),
        state.DiceCounts[player],
        state.DiceCounts[player] > 0,
        string.Equals(player, humanId, StringComparison.Ordinal))).ToArray();
    var currentBid = round.CurrentBid is { } bid
        ? new LiarsDiceBidResponse(bid.Quantity, bid.Face.Value)
        : null;
    var outcome = state.LastOutcome is { } result
        ? new LiarsDiceOutcomeResponse(result.ChallengerId, result.BidderId, result.LoserId, result.Bid.Quantity, result.Bid.Face.Value, result.MatchingDice)
        : null;

    return new LiarsDiceMatchResponse(
        session.Id,
        PhaseName(round.Phase),
        state.RoundNumber,
        round.CurrentPlayerId,
        currentBid,
        round.CurrentBidderId,
        round.Hands.Values.Sum(handValues => handValues.Length),
        hand.Select(die => die.Value).ToArray(),
        players,
        outcome,
        state.Winner,
        session.Message);
}

static string PhaseName(LiarsDiceRoundPhase phase) => phase switch
{
    LiarsDiceRoundPhase.Bidding => "bidding",
    LiarsDiceRoundPhase.Resolved => "resolved",
    _ => throw new ArgumentOutOfRangeException(nameof(phase)),
};

static string DisplayName(string player) => player switch
{
    "you" => "You",
    "bot-1" => "Amber Badger",
    "bot-2" => "Copper Finch",
    "bot-3" => "Silver Otter",
    _ => player,
};

static IResult NotFound() => Results.NotFound(new LiarsDiceErrorResponse(
    "liars-dice-match-not-found",
    "That local Liar's Dice match does not exist."));

public sealed class LiarsDiceSession(Guid id, LiarsDiceMatchState state, ulong botSeed)
{
    public object SyncRoot { get; } = new();
    public Guid Id { get; } = id;
    public LiarsDiceMatchState State { get; set; } = state;
    public ulong BotSeed { get; } = botSeed;
    public int BotActionVersion { get; set; }
    public string Message { get; set; } = "Place a bid or wait for the opening bot turn.";
}

public sealed record StartLiarsDiceMatchRequest(uint? Seed, int? DicePerPlayer);
public sealed record PlaceLiarsDiceBidRequest(int Quantity, int Face);
public sealed record NextLiarsDiceRoundRequest(uint? Seed);
public sealed record LiarsDiceStatusResponse(bool Available, int StartingDicePerPlayer, string Mode);
public sealed record LiarsDiceMatchResponse(Guid MatchId, string Phase, int RoundNumber, string CurrentPlayerId, LiarsDiceBidResponse? CurrentBid, string? CurrentBidderId, int TotalDice, IReadOnlyList<int> Hand, IReadOnlyList<LiarsDicePlayerResponse> Players, LiarsDiceOutcomeResponse? Outcome, string? Winner, string Message);
public sealed record LiarsDicePlayerResponse(string Id, string DisplayName, int DiceCount, bool Active, bool IsHuman);
public sealed record LiarsDiceBidResponse(int Quantity, int Face);
public sealed record LiarsDiceOutcomeResponse(string ChallengerId, string BidderId, string LoserId, int Quantity, int Face, int MatchingDice);
public sealed record LiarsDiceErrorResponse(string Code, string Message);
