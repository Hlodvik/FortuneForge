using System.Security.Cryptography;
using System.Text.Json;
using FortuneForge.Games.Baccarat;
using FortuneForge.Games.Cards;

var builder = WebApplication.CreateBuilder(args);
builder.WebHost.UseUrls("http://127.0.0.1:5187");
builder.Services.AddCors(options => options.AddDefaultPolicy(policy => policy
    .WithOrigins("http://127.0.0.1:5177", "http://localhost:5177")
    .AllowAnyHeader()
    .AllowAnyMethod()));

var app = builder.Build();
app.UseCors();

const decimal StartingBalance = 1_000m;
var balanceGate = new object();
var playerBalance = StartingBalance;
var jsonOptions = new JsonSerializerOptions { PropertyNameCaseInsensitive = true };

app.MapGet("/", () => Results.Ok(new
{
    game = "Fortune Forge Baccarat",
    api = "/api/games/baccarat",
    mode = "local-free-play-eight-deck",
}));

var api = app.MapGroup("/api/games/baccarat");
api.MapGet("/status", () => Results.Ok(new BaccaratStatusResponse(true, 1m, 100m, 1m, playerBalance, "local-free-play-eight-deck")));
api.MapPost("/rounds", async (HttpRequest request) =>
{
    CreateBaccaratRoundRequest? input;
    try
    {
        input = await JsonSerializer.DeserializeAsync<CreateBaccaratRoundRequest>(request.Body, jsonOptions, request.HttpContext.RequestAborted);
    }
    catch (JsonException)
    {
        return MalformedInput();
    }

    if (input is null) return MalformedInput();
    if (!TryParseBetSide(input.BetSide, out var betSide)) return InvalidBetSide();
    if (input.Stake is not > 0m or > 100m || input.Stake % 1m != 0m) return InvalidStake();

    lock (balanceGate)
    {
        if (playerBalance < input.Stake)
            return Results.BadRequest(new BaccaratErrorResponse("baccarat-insufficient-balance", "There are not enough local credits to place that stake."));

        playerBalance -= input.Stake;
        var round = PuntoBancoRoundDealer.Deal(CreateShuffledEightDeckShoe());
        var settlement = PuntoBancoPaytable.Settle(betSide, input.Stake, round);
        playerBalance += settlement.TotalReturn;
        var roundId = Guid.NewGuid();
        return Results.Created(
            $"/api/games/baccarat/rounds/{roundId}",
            ToResponse(roundId, playerBalance, round, settlement));
    }
});

app.Run();

static IResult MalformedInput() => Results.BadRequest(new BaccaratErrorResponse("baccarat-malformed-input", "The Baccarat request body is malformed."));
static IResult InvalidBetSide() => Results.BadRequest(new BaccaratErrorResponse("baccarat-invalid-bet-side", "Choose player, banker, or tie."));
static IResult InvalidStake() => Results.BadRequest(new BaccaratErrorResponse("baccarat-invalid-stake", "Choose a whole-credit stake from 1 through 100."));

static bool TryParseBetSide(string? value, out BaccaratBetSide betSide)
{
    switch (value?.Trim().ToLowerInvariant())
    {
        case "player": betSide = BaccaratBetSide.Player; return true;
        case "banker": betSide = BaccaratBetSide.Banker; return true;
        case "tie": betSide = BaccaratBetSide.Tie; return true;
        default: betSide = default; return false;
    }
}

static PlayingCard[] CreateShuffledEightDeckShoe()
{
    var shoe = Enumerable.Range(0, 8).SelectMany(_ => StandardDeck.Create()).ToArray();
    for (var index = shoe.Length - 1; index > 0; index--)
    {
        var replacement = RandomNumberGenerator.GetInt32(index + 1);
        (shoe[index], shoe[replacement]) = (shoe[replacement], shoe[index]);
    }

    return shoe;
}

static BaccaratRoundResponse ToResponse(
    Guid roundId,
    decimal balance,
    PuntoBancoRoundResult round,
    BaccaratBetSettlement settlement) => new(
    roundId,
    balance,
    BetSideName(settlement.BetSide),
    settlement.Stake,
    "settled",
    round.PlayerHand.Cards.Select(ToCard).ToArray(),
    round.BankerHand.Cards.Select(ToCard).ToArray(),
    round.PlayerHand.Total,
    round.BankerHand.Total,
    OutcomeName(round.Outcome),
    round.EndedOnNatural,
    DispositionName(settlement.Disposition),
    settlement.Profit,
    settlement.TotalReturn);

static BaccaratCardResponse ToCard(PlayingCard card) => new(CardRankName(card.Rank), CardSuitName(card.Suit));
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
static string BetSideName(BaccaratBetSide betSide) => betSide switch
{
    BaccaratBetSide.Player => "player", BaccaratBetSide.Banker => "banker", BaccaratBetSide.Tie => "tie",
    _ => throw new ArgumentOutOfRangeException(nameof(betSide)),
};
static string OutcomeName(BaccaratRoundOutcome outcome) => outcome switch
{
    BaccaratRoundOutcome.Player => "player", BaccaratRoundOutcome.Banker => "banker", BaccaratRoundOutcome.Tie => "tie",
    _ => throw new ArgumentOutOfRangeException(nameof(outcome)),
};
static string DispositionName(BaccaratBetDisposition disposition) => disposition switch
{
    BaccaratBetDisposition.Win => "win", BaccaratBetDisposition.Loss => "loss", BaccaratBetDisposition.Push => "push",
    _ => throw new ArgumentOutOfRangeException(nameof(disposition)),
};

public sealed record CreateBaccaratRoundRequest(string? BetSide, decimal Stake);
public sealed record BaccaratStatusResponse(bool Available, decimal MinimumStake, decimal MaximumStake, decimal StakeIncrement, decimal Balance, string Mode);
public sealed record BaccaratCardResponse(string Rank, string Suit);
public sealed record BaccaratRoundResponse(
    Guid RoundId,
    decimal Balance,
    string BetSide,
    decimal Stake,
    string Phase,
    IReadOnlyList<BaccaratCardResponse> PlayerCards,
    IReadOnlyList<BaccaratCardResponse> BankerCards,
    int PlayerTotal,
    int BankerTotal,
    string Outcome,
    bool EndedOnNatural,
    string Disposition,
    decimal Profit,
    decimal TotalReturn);
public sealed record BaccaratErrorResponse(string Code, string Message);
