using System.Collections.Concurrent;
using System.Security.Cryptography;
using FortuneForge.Games.Cards;
using FortuneForge.Games.Hearts;
using FortuneForge.Games.TrickTaking;

const PlayerSeat humanSeat = PlayerSeat.North;
var botSeats = Enum.GetValues<PlayerSeat>().Where(seat => seat != humanSeat).ToHashSet();
var bot = new HeartsBotAgent();
var botOptions = new CardBotGameOptions { Enabled = true };
var matches = new ConcurrentDictionary<Guid, HeartsSession>();
const int botThinkingDelayMilliseconds = 900;

var builder = WebApplication.CreateBuilder(args);
builder.WebHost.UseUrls("http://127.0.0.1:5188");
builder.Services.AddCors(options => options.AddDefaultPolicy(policy => policy
    .WithOrigins("http://127.0.0.1:5178", "http://localhost:5178")
    .AllowAnyHeader()
    .AllowAnyMethod()));

var app = builder.Build();
app.UseCors();

app.MapGet("/", () => Results.Ok(new
{
    game = "Fortune Forge Hearts",
    client = "cd games/Hearts/client/FortuneForge.Games.Hearts.Client; npm run dev",
}));

var api = app.MapGroup("/api/games/hearts");

api.MapGet("/status", () => Results.Ok(new HeartsStatusResponse(
    Available: true,
    DefaultTargetScore: HeartsMatchEngine.DefaultTargetScore,
    Mode: "local-free-play-bots")));

api.MapPost("/matches", (StartHeartsMatchRequest? request) =>
{
    request ??= new StartHeartsMatchRequest(null, HeartsMatchEngine.DefaultTargetScore);
    var targetScore = request.TargetScore ?? HeartsMatchEngine.DefaultTargetScore;
    if (targetScore <= 0)
        return Results.BadRequest(new HeartsErrorResponse("hearts-invalid-target", "The target score must be positive."));

    var seed = request.Seed ?? (uint)RandomNumberGenerator.GetInt32(1, int.MaxValue);
    var session = new HeartsSession(Guid.NewGuid(), HeartsMatchEngine.Start(seed, targetScore), CardBotSeed.Create());
    matches[session.Id] = session;
    PrepareBotTurn(session);
    return Results.Created($"/api/games/hearts/matches/{session.Id}", ToResponse(session));
});

api.MapGet("/matches/{matchId:guid}", (Guid matchId) =>
    matches.TryGetValue(matchId, out var session)
        ? Results.Ok(ToResponse(session))
        : NotFound());

api.MapPost("/matches/{matchId:guid}/pass", (Guid matchId, PassHeartsRequest request) =>
{
    if (!matches.TryGetValue(matchId, out var session)) return NotFound();
    lock (session.SyncRoot)
    {
        try
        {
            var cards = request.Cards.Select(CardCode.Parse).ToArray();
            ApplyHumanAction(session, new PassHeartsCards(humanSeat, cards));
            return Results.Ok(ToResponse(session));
        }
        catch (Exception exception) when (exception is ArgumentException or ArgumentOutOfRangeException or HeartsRuleException)
        {
            return Results.BadRequest(new HeartsErrorResponse("hearts-invalid-pass", exception.Message));
        }
    }
});

api.MapPost("/matches/{matchId:guid}/card", (Guid matchId, PlayHeartsCardRequest request) =>
{
    if (!matches.TryGetValue(matchId, out var session)) return NotFound();
    lock (session.SyncRoot)
    {
        try
        {
            ApplyHumanAction(session, new PlayHeartsCard(humanSeat, CardCode.Parse(request.Card)));
            return Results.Ok(ToResponse(session));
        }
        catch (Exception exception) when (exception is ArgumentException or ArgumentOutOfRangeException or HeartsRuleException)
        {
            return Results.BadRequest(new HeartsErrorResponse("hearts-invalid-card", exception.Message));
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
        catch (HeartsRuleException exception)
        {
            return Results.BadRequest(new HeartsErrorResponse("hearts-cannot-advance", exception.Message));
        }
    }
});

api.MapPost("/matches/{matchId:guid}/next-round", (Guid matchId, NextHeartsRoundRequest? request) =>
{
    if (!matches.TryGetValue(matchId, out var session)) return NotFound();
    lock (session.SyncRoot)
    {
        try
        {
            var seed = request?.Seed ?? (uint)RandomNumberGenerator.GetInt32(1, int.MaxValue);
            session.State = HeartsMatchEngine.StartNextRound(session.State, seed);
            session.LastCompletedTrick = null;
            session.Message = $"Round {session.State.RoundNumber} is ready. Pass three cards {DirectionName(session.State.PassDirection)}.";
            PrepareBotTurn(session);
            return Results.Ok(ToResponse(session));
        }
        catch (Exception exception) when (exception is ArgumentException or ArgumentOutOfRangeException or HeartsRuleException)
        {
            return Results.BadRequest(new HeartsErrorResponse("hearts-cannot-start-round", exception.Message));
        }
    }
});

app.Run();

void ApplyHumanAction(HeartsSession session, HeartsCommand command)
{
    if (session.State.Round.Phase == HeartsPhase.Playing && session.State.Round.Turn != humanSeat)
        throw new HeartsRuleException($"It is {session.State.Round.Turn}'s turn. The bots are still moving.");

    var completedTricksBefore = session.State.Round.CompletedTricks.Count;
    var transition = HeartsMatchEngine.Apply(session.State, command);
    session.State = transition.State;
    RememberCompletedTrick(session, completedTricksBefore);
    session.Message = transition.Message;
    PrepareBotTurn(session);
}

void AdvanceBots(HeartsSession session)
{
    if (session.State.Winner is not null || session.State.Round.Phase == HeartsPhase.Complete)
    {
        session.BotThinking = false;
        session.NextBotActionAt = null;
        return;
    }

    if (!session.BotThinking || session.NextBotActionAt is not { } actionAt || DateTimeOffset.UtcNow < actionAt)
        return;

    var round = session.State.Round;
    if (round.Phase == HeartsPhase.Passing && !round.SubmittedPasses.Any(pass => pass.Seat == humanSeat))
    {
        session.BotThinking = false;
        session.NextBotActionAt = null;
        return;
    }

    if (round.Phase == HeartsPhase.Playing && !botSeats.Contains(round.Turn))
    {
        session.BotThinking = false;
        session.NextBotActionAt = null;
        return;
    }

    var completedTricksBefore = session.State.Round.CompletedTricks.Count;
    var transition = HeartsMatchEngine.AdvanceBotTurn(
        session.State,
        botSeats,
        bot,
        CardBotSkillLevels.Strong,
        session.BotSeed,
        session.BotActionVersion++,
        botOptions,
        humanSeat);
    session.State = transition.State;
    RememberCompletedTrick(session, completedTricksBefore);
    session.Message = transition.Message;
    session.BotThinking = false;
    session.NextBotActionAt = null;
    PrepareBotTurn(session);
}

void PrepareBotTurn(HeartsSession session)
{
    if (session.State.Winner is not null || session.State.Round.Phase == HeartsPhase.Complete)
    {
        session.BotThinking = false;
        session.NextBotActionAt = null;
        return;
    }

    PlayerSeat? botSeat = null;
    if (session.State.Round.Phase == HeartsPhase.Passing)
    {
        if (!session.State.Round.SubmittedPasses.Any(pass => pass.Seat == humanSeat))
        {
            session.BotThinking = false;
            session.NextBotActionAt = null;
            return;
        }

        botSeat = Enum.GetValues<PlayerSeat>()
            .FirstOrDefault(candidate => botSeats.Contains(candidate) && !session.State.Round.SubmittedPasses.Any(pass => pass.Seat == candidate));
        if (botSeat == PlayerSeat.North)
            botSeat = null;
    }
    else if (session.State.Round.Phase == HeartsPhase.Playing && botSeats.Contains(session.State.Round.Turn))
    {
        botSeat = session.State.Round.Turn;
    }

    if (botSeat is not { } thinkingSeat)
    {
        session.BotThinking = false;
        session.NextBotActionAt = null;
        return;
    }

    if (session.BotThinking)
        return;

    session.BotThinking = true;
    session.NextBotActionAt = DateTimeOffset.UtcNow.AddMilliseconds(botThinkingDelayMilliseconds);
    session.Message = $"{SeatName(thinkingSeat)} is thinking…";
}

static HeartsMatchResponse ToResponse(HeartsSession session)
{
    var state = session.State;
    var round = state.Round;
    var hand = round.HandFor(humanSeat);
    var legal = round.Phase == HeartsPhase.Playing && round.Turn == humanSeat
        ? HeartsEngine.LegalCards(round, humanSeat)
        : [];
    var matchScores = Enum.GetValues<PlayerSeat>().ToDictionary(seat => seat, state.Score.For);
    var roundScores = round.Scores.ToDictionary(score => score.Seat, score => score.Points);

    return new HeartsMatchResponse(
        session.Id,
        state.TargetScore,
        state.RoundNumber,
        PhaseName(round.Phase),
        DirectionName(state.PassDirection),
        SeatName(round.Turn),
        SeatName(humanSeat),
        round.Turn == humanSeat && state.Winner is null,
        session.BotThinking,
        round.HeartsBroken,
        round.SubmittedPasses.Select(pass => SeatName(pass.Seat)).ToArray(),
        Enum.GetValues<PlayerSeat>().Select(seat => new HeartsPlayerResponse(
            SeatName(seat),
            round.HandFor(seat).Count,
            roundScores[seat],
            matchScores[seat],
            round.SubmittedPasses.Any(pass => pass.Seat == seat))).ToArray(),
        hand.Select(ToCard).ToArray(),
        legal.Select(ToCard).ToArray(),
        new HeartsTrickResponse(
            SeatName(round.CurrentTrick.Leader),
            round.CurrentTrick.Plays.Select(play => new HeartsTrickPlayResponse(SeatName(play.Seat), ToCard(play.Card))).ToArray()),
        session.LastCompletedTrick is { } recentTrick
            ? new HeartsRecentTrickResponse(
                recentTrick.Number,
                SeatName(recentTrick.Leader),
                SeatName(recentTrick.Winner),
                recentTrick.Plays.Sum(play => HeartsRules.PointValue(play.Card)),
                recentTrick.Plays.Select(play => new HeartsTrickPlayResponse(SeatName(play.Seat), ToCard(play.Card))).ToArray())
            : null,
        round.CompletedTricks.Select(trick => new HeartsCompletedTrickResponse(trick.Number, SeatName(trick.Leader), SeatName(trick.Winner), trick.Plays.Sum(play => HeartsRules.PointValue(play.Card)))).ToArray(),
        new HeartsScoreResponse(state.Score.North, state.Score.East, state.Score.South, state.Score.West),
        new HeartsScoreResponse(roundScores[PlayerSeat.North], roundScores[PlayerSeat.East], roundScores[PlayerSeat.South], roundScores[PlayerSeat.West]),
        state.Winner is { } winner ? SeatName(winner) : null,
        session.Message);
}

static HeartsCardResponse ToCard(PlayingCard card) => new(card.Code, RankName(card.Rank), SuitName(card.Suit), CardLabel(card));
static string PhaseName(HeartsPhase phase) => phase switch { HeartsPhase.Passing => "passing", HeartsPhase.Playing => "playing", HeartsPhase.Complete => "complete", _ => throw new ArgumentOutOfRangeException(nameof(phase)) };
static string DirectionName(HeartsPassDirection direction) => direction switch { HeartsPassDirection.Left => "left", HeartsPassDirection.Right => "right", HeartsPassDirection.Across => "across", HeartsPassDirection.Hold => "hold", _ => throw new ArgumentOutOfRangeException(nameof(direction)) };
static string SeatName(PlayerSeat seat) => seat switch { PlayerSeat.North => "north", PlayerSeat.East => "east", PlayerSeat.South => "south", PlayerSeat.West => "west", _ => throw new ArgumentOutOfRangeException(nameof(seat)) };
static string RankName(CardRank rank) => rank switch { CardRank.Ace => "ace", CardRank.Jack => "jack", CardRank.Queen => "queen", CardRank.King => "king", _ => ((int)rank).ToString(System.Globalization.CultureInfo.InvariantCulture) };
static string SuitName(CardSuit suit) => suit switch { CardSuit.Clubs => "clubs", CardSuit.Diamonds => "diamonds", CardSuit.Hearts => "hearts", CardSuit.Spades => "spades", _ => throw new ArgumentOutOfRangeException(nameof(suit)) };
static string CardLabel(PlayingCard card) => $"{card.Code.Split('|')[0]}{card.Suit switch { CardSuit.Clubs => "♣", CardSuit.Diamonds => "♦", CardSuit.Hearts => "♥", CardSuit.Spades => "♠", _ => "?" }}";
static IResult NotFound() => Results.NotFound(new HeartsErrorResponse("hearts-match-not-found", "That local Hearts match does not exist."));

static void RememberCompletedTrick(HeartsSession session, int completedTricksBefore)
{
    if (session.State.Round.CompletedTricks.Count > completedTricksBefore)
        session.LastCompletedTrick = session.State.Round.CompletedTricks[^1];
}

public sealed class HeartsSession(Guid id, HeartsMatchState state, ulong botSeed)
{
    public object SyncRoot { get; } = new();
    public Guid Id { get; } = id;
    public HeartsMatchState State { get; set; } = state;
    public ulong BotSeed { get; } = botSeed;
    public int BotActionVersion { get; set; }
    public bool BotThinking { get; set; }
    public DateTimeOffset? NextBotActionAt { get; set; }
    public CompletedTrick? LastCompletedTrick { get; set; }
    public string Message { get; set; } = "Pass three cards to the left.";
}

public sealed record StartHeartsMatchRequest(uint? Seed, int? TargetScore);
public sealed record PassHeartsRequest(IReadOnlyList<string> Cards);
public sealed record PlayHeartsCardRequest(string Card);
public sealed record NextHeartsRoundRequest(uint? Seed);
public sealed record HeartsStatusResponse(bool Available, int DefaultTargetScore, string Mode);
public sealed record HeartsMatchResponse(Guid MatchId, int TargetScore, int RoundNumber, string Phase, string PassDirection, string Turn, string HumanSeat, bool YourTurn, bool BotsThinking, bool HeartsBroken, IReadOnlyList<string> SubmittedPasses, IReadOnlyList<HeartsPlayerResponse> Players, IReadOnlyList<HeartsCardResponse> Hand, IReadOnlyList<HeartsCardResponse> LegalCards, HeartsTrickResponse CurrentTrick, HeartsRecentTrickResponse? RecentTrick, IReadOnlyList<HeartsCompletedTrickResponse> CompletedTricks, HeartsScoreResponse Score, HeartsScoreResponse RoundScore, string? Winner, string Message);
public sealed record HeartsPlayerResponse(string Seat, int HandCount, int RoundScore, int MatchScore, bool HasPassed);
public sealed record HeartsCardResponse(string Code, string Rank, string Suit, string Label);
public sealed record HeartsTrickResponse(string Leader, IReadOnlyList<HeartsTrickPlayResponse> Plays);
public sealed record HeartsTrickPlayResponse(string Seat, HeartsCardResponse Card);
public sealed record HeartsRecentTrickResponse(int Number, string Leader, string Winner, int Points, IReadOnlyList<HeartsTrickPlayResponse> Plays);
public sealed record HeartsCompletedTrickResponse(int Number, string Leader, string Winner, int Points);
public sealed record HeartsScoreResponse(int North, int East, int South, int West);
public sealed record HeartsErrorResponse(string Code, string Message);
