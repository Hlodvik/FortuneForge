using System.Security.Cryptography;
using System.Text.Json;
using FortuneForge.Games.Cards;
using FortuneForge.Games.CasinoWar;

var builder = WebApplication.CreateBuilder(args);
builder.WebHost.UseUrls("http://127.0.0.1:5188");
builder.Services.AddCors(options => options.AddDefaultPolicy(policy => policy
    .WithOrigins("http://127.0.0.1:5178", "http://localhost:5178")
    .AllowAnyHeader()
    .AllowAnyMethod()));

var app = builder.Build();
app.UseCors();

const decimal StartingBalance = 1_000m;
const decimal MinimumPrimaryStake = 1m;
const decimal MaximumPrimaryStake = 100m;
const decimal StakeIncrement = 1m;
const decimal MaximumTieStake = 25m;
var transactionGate = new object();
var playerBalance = StartingBalance;
var sessions = new Dictionary<Guid, CasinoWarSession>();
var jsonOptions = new JsonSerializerOptions { PropertyNameCaseInsensitive = true };

app.MapGet("/", () => Results.Ok(new
{
    game = "Fortune Forge Casino War",
    api = "/api/games/casino-war",
    mode = "local-free-play-six-deck",
}));

var api = app.MapGroup("/api/games/casino-war");
api.MapGet("/status", () => Results.Ok(new CasinoWarStatusResponse(
    true,
    MinimumPrimaryStake,
    MaximumPrimaryStake,
    StakeIncrement,
    MaximumTieStake,
    playerBalance,
    "local-free-play-six-deck")));

api.MapPost("/rounds", async (HttpRequest request) =>
{
    CreateCasinoWarRoundRequest? input;
    try
    {
        input = await JsonSerializer.DeserializeAsync<CreateCasinoWarRoundRequest>(request.Body, jsonOptions, request.HttpContext.RequestAborted);
    }
    catch (JsonException)
    {
        return MalformedInput();
    }

    if (input is null) return MalformedInput();
    if (!IsValidPrimaryStake(input.PrimaryStake) || !IsValidTieStake(input.TieStake)) return InvalidStake();

    lock (transactionGate)
    {
        var primaryStake = input.PrimaryStake!.Value;
        var tieStake = input.TieStake!.Value;
        var openingWager = primaryStake + tieStake;
        if (playerBalance < openingWager) return InsufficientBalance();

        playerBalance -= openingWager;
        var round = CasinoWarRoundEngine.Start(CreateShuffledSixDeckShoe(), primaryStake);
        var tieSettlement = tieStake == 0m ? null : CasinoWarTieBetPaytable.Settle(round.Opening, tieStake);
        if (tieSettlement is not null) playerBalance += tieSettlement.TotalReturn;
        if (round.Settlement is not null) playerBalance += round.Settlement.TotalReturn;

        var roundId = Guid.NewGuid();
        sessions.Add(roundId, new CasinoWarSession(round, tieSettlement));
        return Results.Created($"/api/games/casino-war/rounds/{roundId}", ToResponse(roundId, playerBalance, round, tieSettlement));
    }
});

api.MapPost("/rounds/{roundId:guid}/decision", async (Guid roundId, HttpRequest request) =>
{
    DecideCasinoWarRoundRequest? input;
    try
    {
        input = await JsonSerializer.DeserializeAsync<DecideCasinoWarRoundRequest>(request.Body, jsonOptions, request.HttpContext.RequestAborted);
    }
    catch (JsonException)
    {
        return MalformedInput();
    }

    if (input is null) return MalformedInput();
    if (!TryParseDecision(input.Decision, out var decision)) return InvalidDecision();

    lock (transactionGate)
    {
        if (!sessions.TryGetValue(roundId, out var session)) return UnknownRound();
        if (session.Round.Phase == CasinoWarRoundPhase.Completed) return CompletedRound();
        CasinoWarRound completedRound;
        try
        {
            completedRound = CasinoWarRoundEngine.Decide(session.Round, decision);
        }
        catch (ArgumentException)
        {
            return InvalidDecision();
        }
        catch (InvalidOperationException)
        {
            return CompletedRound();
        }

        if (decision == CasinoWarTieDecision.GoToWar && playerBalance < session.Round.PrimaryStake) return InsufficientBalance();
        if (decision == CasinoWarTieDecision.GoToWar) playerBalance -= session.Round.PrimaryStake;
        playerBalance += completedRound.Settlement!.TotalReturn;
        session.Round = completedRound;
        return Results.Ok(ToResponse(roundId, playerBalance, completedRound, session.TieSettlement));
    }
});

app.Run();

static bool IsValidPrimaryStake(decimal? stake) => stake is >= MinimumPrimaryStake and <= MaximumPrimaryStake && stake % StakeIncrement == 0m;
static bool IsValidTieStake(decimal? stake) => stake is >= 0m and <= MaximumTieStake && stake % StakeIncrement == 0m;

static IResult MalformedInput() => Results.BadRequest(new CasinoWarErrorResponse("casino-war-malformed-input", "The Casino War request body is malformed."));
static IResult InvalidStake() => Results.BadRequest(new CasinoWarErrorResponse("casino-war-invalid-stakes", "Use whole-credit primary stakes from 1 through 100 and Tie stakes from 0 through 25."));
static IResult InvalidDecision() => Results.BadRequest(new CasinoWarErrorResponse("casino-war-invalid-decision", "Choose surrender or go-to-war."));
static IResult InsufficientBalance() => Results.BadRequest(new CasinoWarErrorResponse("casino-war-insufficient-balance", "There are not enough local credits for that wager."));
static IResult UnknownRound() => Results.NotFound(new CasinoWarErrorResponse("casino-war-unknown-round", "The Casino War round was not found."));
static IResult CompletedRound() => Results.Conflict(new CasinoWarErrorResponse("casino-war-completed-round", "The Casino War round has already been completed."));

static bool TryParseDecision(string? value, out CasinoWarTieDecision decision)
{
    switch (value?.Trim().ToLowerInvariant())
    {
        case "surrender": decision = CasinoWarTieDecision.Surrender; return true;
        case "go-to-war": decision = CasinoWarTieDecision.GoToWar; return true;
        default: decision = default; return false;
    }
}

static PlayingCard[] CreateShuffledSixDeckShoe()
{
    var shoe = Enumerable.Range(0, 6).SelectMany(_ => StandardDeck.Create()).ToArray();
    for (var index = shoe.Length - 1; index > 0; index--)
    {
        var replacement = RandomNumberGenerator.GetInt32(index + 1);
        (shoe[index], shoe[replacement]) = (shoe[replacement], shoe[index]);
    }

    return shoe;
}

static CasinoWarRoundResponse ToResponse(
    Guid roundId,
    decimal balance,
    CasinoWarRound round,
    CasinoWarTieBetSettlement? tieSettlement) => new(
    roundId,
    balance,
    round.PrimaryStake,
    tieSettlement?.Stake ?? 0m,
    PhaseName(round.Phase),
    ToCard(round.PlayerOpeningCard),
    ToCard(round.DealerOpeningCard),
    round.TieDecision is null ? null : DecisionName(round.TieDecision.Value),
    round.PlayerWarCard is null ? null : ToCard(round.PlayerWarCard.Value),
    round.DealerWarCard is null ? null : ToCard(round.DealerWarCard.Value),
    round.Settlement is null ? null : ToPrimarySettlement(round.Settlement),
    tieSettlement is null ? null : ToTieSettlement(tieSettlement));

static CasinoWarCardResponse ToCard(PlayingCard card) => new(CardRankName(card.Rank), CardSuitName(card.Suit));
static CasinoWarPrimarySettlementResponse ToPrimarySettlement(CasinoWarSettlement settlement) => new(
    DispositionName(settlement.Disposition),
    RoundOutcomeName(settlement.Outcome),
    settlement.TotalWagered,
    settlement.TotalReturn,
    settlement.Profit);
static CasinoWarTieSettlementResponse ToTieSettlement(CasinoWarTieBetSettlement settlement) => new(
    settlement.Won,
    DispositionName(settlement.Disposition),
    OpeningOutcomeName(settlement.OpeningOutcome),
    settlement.Stake,
    settlement.TotalReturn,
    settlement.Profit);

static string PhaseName(CasinoWarRoundPhase phase) => phase switch
{
    CasinoWarRoundPhase.AwaitingTieDecision => "awaiting-tie-decision",
    CasinoWarRoundPhase.Completed => "completed",
    _ => throw new ArgumentOutOfRangeException(nameof(phase)),
};
static string DecisionName(CasinoWarTieDecision decision) => decision switch
{
    CasinoWarTieDecision.Surrender => "surrender",
    CasinoWarTieDecision.GoToWar => "go-to-war",
    _ => throw new ArgumentOutOfRangeException(nameof(decision)),
};
static string DispositionName(CasinoWarSettlementDisposition disposition) => disposition switch
{
    CasinoWarSettlementDisposition.Win => "win",
    CasinoWarSettlementDisposition.Loss => "loss",
    CasinoWarSettlementDisposition.Surrender => "surrender",
    _ => throw new ArgumentOutOfRangeException(nameof(disposition)),
};
static string OpeningOutcomeName(CasinoWarOpeningOutcome outcome) => outcome switch
{
    CasinoWarOpeningOutcome.PlayerWin => "player-win",
    CasinoWarOpeningOutcome.DealerWin => "dealer-win",
    CasinoWarOpeningOutcome.TieDecisionRequired => "tie-decision-required",
    _ => throw new ArgumentOutOfRangeException(nameof(outcome)),
};
static string RoundOutcomeName(CasinoWarRoundOutcome outcome) => outcome switch
{
    CasinoWarRoundOutcome.PlayerOpeningWin => "player-opening-win",
    CasinoWarRoundOutcome.DealerOpeningWin => "dealer-opening-win",
    CasinoWarRoundOutcome.PlayerSurrendered => "player-surrendered",
    CasinoWarRoundOutcome.PlayerWarWin => "player-war-win",
    CasinoWarRoundOutcome.DealerWarWin => "dealer-war-win",
    CasinoWarRoundOutcome.PlayerWarTieWin => "player-war-tie-win",
    _ => throw new ArgumentOutOfRangeException(nameof(outcome)),
};
static string CardRankName(CardRank rank) => rank switch
{
    CardRank.Ace => "ace", CardRank.Two => "two", CardRank.Three => "three", CardRank.Four => "four",
    CardRank.Five => "five", CardRank.Six => "six", CardRank.Seven => "seven", CardRank.Eight => "eight",
    CardRank.Nine => "nine", CardRank.Ten => "ten", CardRank.Jack => "jack", CardRank.Queen => "queen",
    CardRank.King => "king", _ => throw new ArgumentOutOfRangeException(nameof(rank)),
};
static string CardSuitName(CardSuit suit) => suit switch
{
    CardSuit.Clubs => "clubs", CardSuit.Diamonds => "diamonds", CardSuit.Hearts => "hearts", CardSuit.Spades => "spades",
    _ => throw new ArgumentOutOfRangeException(nameof(suit)),
};

sealed class CasinoWarSession(CasinoWarRound round, CasinoWarTieBetSettlement? tieSettlement)
{
    public CasinoWarRound Round { get; set; } = round;
    public CasinoWarTieBetSettlement? TieSettlement { get; } = tieSettlement;
}

public sealed record CreateCasinoWarRoundRequest(decimal? PrimaryStake, decimal? TieStake);
public sealed record DecideCasinoWarRoundRequest(string? Decision);
public sealed record CasinoWarStatusResponse(bool Available, decimal MinimumPrimaryStake, decimal MaximumPrimaryStake, decimal StakeIncrement, decimal MaximumTieStake, decimal Balance, string Mode);
public sealed record CasinoWarCardResponse(string Rank, string Suit);
public sealed record CasinoWarPrimarySettlementResponse(string Disposition, string Outcome, decimal TotalWagered, decimal TotalReturn, decimal Profit);
public sealed record CasinoWarTieSettlementResponse(bool Won, string Disposition, string Outcome, decimal Stake, decimal TotalReturn, decimal Profit);
public sealed record CasinoWarRoundResponse(
    Guid RoundId,
    decimal Balance,
    decimal PrimaryStake,
    decimal TieStake,
    string Phase,
    CasinoWarCardResponse PlayerOpeningCard,
    CasinoWarCardResponse DealerOpeningCard,
    string? Decision,
    CasinoWarCardResponse? PlayerWarCard,
    CasinoWarCardResponse? DealerWarCard,
    CasinoWarPrimarySettlementResponse? PrimarySettlement,
    CasinoWarTieSettlementResponse? TieSettlement);
public sealed record CasinoWarErrorResponse(string Code, string Message);
