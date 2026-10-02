using System.Collections.Concurrent;
using System.Security.Cryptography;
using FortuneForge.Games.Cards;
using FortuneForge.Games.VideoPoker;

var builder = WebApplication.CreateBuilder(args);
builder.WebHost.UseUrls("http://127.0.0.1:5186");
builder.Services.AddCors(options => options.AddDefaultPolicy(policy => policy
    .WithOrigins("http://127.0.0.1:5176", "http://localhost:5176")
    .AllowAnyHeader()
    .AllowAnyMethod()));

var app = builder.Build();
app.UseCors();

const long StartingBalance = 1_000;
var balanceGate = new object();
var playerBalance = StartingBalance;
var rounds = new ConcurrentDictionary<Guid, VideoPokerSession>();

app.MapGet("/", () => Results.Ok(new
{
    game = "Fortune Forge Video Poker",
    api = "/api/games/video-poker",
    mode = "local-free-play-jacks-or-better",
}));

var api = app.MapGroup("/api/games/video-poker");
api.MapGet("/status", () => Results.Ok(new VideoPokerStatusResponse(true, 1, 5, 1, playerBalance, [1, 3, 5])));
api.MapPost("/rounds", (CreateVideoPokerRoundRequest request) =>
{
    if (request.CoinsWagered is < 1 or > 5 || request.HandCount is not (1 or 3 or 5))
        return InvalidWager();

    lock (balanceGate)
    {
        var wager = checked(request.CoinsWagered * request.HandCount);
        if (playerBalance < wager)
            return Results.BadRequest(new VideoPokerErrorResponse("video-poker-insufficient-balance", "There are not enough local credits to place that wager."));

        var id = Guid.NewGuid();
        var round = VideoPokerRoundEngine.Deal(
            Enumerable.Range(0, request.HandCount).Select(_ => (IReadOnlyList<PlayingCard>)CreateShuffledDeck()).ToArray(),
            request.CoinsWagered);
        playerBalance -= wager;
        var session = new VideoPokerSession(round);
        rounds[id] = session;
        return Results.Created($"/api/games/video-poker/rounds/{id}", ToResponse(id, session.Round, playerBalance));
    }
});
api.MapPost("/rounds/{roundId:guid}/draw", (Guid roundId, DrawVideoPokerRoundRequest request) =>
{
    if (!rounds.TryGetValue(roundId, out var session)) return RoundNotFound();
    if (request.HeldPositions is null)
        return Results.BadRequest(new VideoPokerErrorResponse("video-poker-invalid-holds", "Held positions are required."));

    lock (session)
    {
        if (session.Round.Status == VideoPokerRoundStatus.Completed)
            return Results.BadRequest(new VideoPokerErrorResponse("video-poker-round-completed", "This Video Poker round has already been drawn."));

        try
        {
            var holds = new VideoPokerHeldCardPositions(request.HeldPositions.Select(position => (VideoPokerCardPosition)position));
            session.Round = VideoPokerRoundEngine.Draw(session.Round, holds);
            var creditsWon = session.Round.Results.Sum(result => result.PaytableOutcome.CreditsWon);
            long balance;
            lock (balanceGate)
            {
                playerBalance += creditsWon;
                balance = playerBalance;
            }

            return Results.Ok(ToResponse(roundId, session.Round, balance));
        }
        catch (Exception exception) when (exception is ArgumentException or ArgumentOutOfRangeException)
        {
            return Results.BadRequest(new VideoPokerErrorResponse("video-poker-invalid-holds", exception.Message));
        }
        catch (InvalidOperationException exception)
        {
            return Results.BadRequest(new VideoPokerErrorResponse("video-poker-round-completed", exception.Message));
        }
    }
});

app.Run();

static IResult InvalidWager() => Results.BadRequest(new VideoPokerErrorResponse("video-poker-invalid-wager", "Choose a wager from one through five coins."));
static IResult RoundNotFound() => Results.NotFound(new VideoPokerErrorResponse("video-poker-round-not-found", "That local Video Poker round does not exist."));

static PlayingCard[] CreateShuffledDeck()
{
    var deck = StandardDeck.Create().ToArray();
    for (var index = deck.Length - 1; index > 0; index--)
    {
        var replacement = RandomNumberGenerator.GetInt32(index + 1);
        (deck[index], deck[replacement]) = (deck[replacement], deck[index]);
    }

    return deck;
}

static VideoPokerRoundResponse ToResponse(Guid roundId, VideoPokerRound round, long balance) => new(
    roundId,
    balance,
    round.CoinsWagered,
    round.HandCount,
    round.CoinsWagered * round.HandCount,
    round.Status == VideoPokerRoundStatus.AwaitingDraw ? "awaiting-draw" : "completed",
    round.InitialDeal.Cards.Select(ToCard).ToArray(),
    round.HeldCardPositions?.Positions.Select(position => (int)position).ToArray() ?? [],
    round.Result?.FinalHand.Cards.Select(ToCard).ToArray(),
    round.Result is null ? null : HandRankName(round.Result.HandRank),
    round.Results.IsDefaultOrEmpty ? null : round.Results.Sum(result => result.PaytableOutcome.CreditsWon),
    round.Results.IsDefaultOrEmpty ? null : round.Results.Select(result => (IReadOnlyList<VideoPokerCardResponse>)result.FinalHand.Cards.Select(ToCard).ToArray()).ToArray(),
    round.Results.IsDefaultOrEmpty ? null : round.Results.Select(result => HandRankName(result.HandRank)).ToArray(),
    round.Results.IsDefaultOrEmpty ? null : round.Results.Select(result => result.PaytableOutcome.CreditsWon).ToArray());

static VideoPokerCardResponse ToCard(PlayingCard card) => new(CardRankName(card.Rank), CardSuitName(card.Suit));
static string CardRankName(CardRank rank) => rank switch
{
    CardRank.Ace => "ace",
    CardRank.Two => "two",
    CardRank.Three => "three",
    CardRank.Four => "four",
    CardRank.Five => "five",
    CardRank.Six => "six",
    CardRank.Seven => "seven",
    CardRank.Eight => "eight",
    CardRank.Nine => "nine",
    CardRank.Ten => "ten",
    CardRank.Jack => "jack",
    CardRank.Queen => "queen",
    CardRank.King => "king",
    _ => throw new ArgumentOutOfRangeException(nameof(rank)),
};
static string CardSuitName(CardSuit suit) => suit switch
{
    CardSuit.Clubs => "clubs",
    CardSuit.Diamonds => "diamonds",
    CardSuit.Hearts => "hearts",
    CardSuit.Spades => "spades",
    _ => throw new ArgumentOutOfRangeException(nameof(suit)),
};
static string HandRankName(VideoPokerHandRank rank) => rank switch
{
    VideoPokerHandRank.NoWin => "no-win",
    VideoPokerHandRank.Pair => "pair",
    VideoPokerHandRank.TwoPair => "two-pair",
    VideoPokerHandRank.ThreeOfAKind => "three-of-a-kind",
    VideoPokerHandRank.Straight => "straight",
    VideoPokerHandRank.Flush => "flush",
    VideoPokerHandRank.FullHouse => "full-house",
    VideoPokerHandRank.FourOfAKind => "four-of-a-kind",
    VideoPokerHandRank.StraightFlush => "straight-flush",
    VideoPokerHandRank.RoyalFlush => "royal-flush",
    _ => throw new ArgumentOutOfRangeException(nameof(rank)),
};

public sealed class VideoPokerSession(VideoPokerRound round)
{
    public VideoPokerRound Round { get; set; } = round;
}

public sealed record CreateVideoPokerRoundRequest(int CoinsWagered, int HandCount = 1);
public sealed record DrawVideoPokerRoundRequest(int[]? HeldPositions);
public sealed record VideoPokerStatusResponse(bool Available, int MinimumCoinsWagered, int MaximumCoinsWagered, decimal CoinValue, long Balance, IReadOnlyList<int> HandCounts);
public sealed record VideoPokerCardResponse(string Rank, string Suit);
public sealed record VideoPokerRoundResponse(
    Guid RoundId,
    long Balance,
    int CoinsWagered,
    int HandCount,
    long Wager,
    string Phase,
    IReadOnlyList<VideoPokerCardResponse> InitialCards,
    IReadOnlyList<int> HeldPositions,
    IReadOnlyList<VideoPokerCardResponse>? FinalCards,
    string? HandRank,
    int? Payout,
    IReadOnlyList<IReadOnlyList<VideoPokerCardResponse>>? FinalHands,
    IReadOnlyList<string>? HandRanks,
    IReadOnlyList<int>? HandPayouts);
public sealed record VideoPokerErrorResponse(string Code, string Message);
