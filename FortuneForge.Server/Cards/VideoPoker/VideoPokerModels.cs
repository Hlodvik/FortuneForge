using System.Security.Cryptography;
using FortuneForge.Games.Cards;
using FortuneForge.Games.VideoPoker;
using FortuneForge.Server.Accounts.Models;

namespace FortuneForge.Server.Cards.VideoPoker;

public sealed record CreateVideoPokerRoundRequest(int CoinsWagered, int HandCount = 1);

public sealed record DrawVideoPokerRoundRequest(IReadOnlyList<int>? HeldPositions);

public sealed record VideoPokerStatusResponse(
    bool Available,
    int MinimumCoinsWagered,
    int MaximumCoinsWagered,
    decimal CoinValue,
    decimal Balance,
    IReadOnlyList<int>? HandCounts = null);

public sealed record VideoPokerCardResponse(string Rank, string Suit);

public sealed record VideoPokerRoundResponse(
    string RoundId,
    decimal Balance,
    int CoinsWagered,
    int HandCount,
    decimal Wager,
    string Phase,
    IReadOnlyList<VideoPokerCardResponse> InitialCards,
    IReadOnlyList<int> HeldPositions,
    IReadOnlyList<VideoPokerCardResponse>? FinalCards,
    string? HandRank,
    decimal? Payout,
    IReadOnlyList<IReadOnlyList<VideoPokerCardResponse>>? FinalHands,
    IReadOnlyList<string>? HandRanks,
    IReadOnlyList<decimal>? HandPayouts);

public sealed record VideoPokerErrorResponse(string Code, string Message);

internal sealed record VideoPokerStoreResult(string RoundId, VideoPokerRound Round, long BalanceCents);

internal sealed class VideoPokerRoundNotFoundException() : Exception("Video Poker round not found.");

internal sealed class VideoPokerRoundConflictException(string message) : Exception(message);

internal sealed class VideoPokerInsufficientCreditsException(long availableCents, long requiredCents)
    : Exception($"This account has R{RandMoney.CentsToRand(availableCents):0.00}, but the wager requires R{RandMoney.CentsToRand(requiredCents):0.00}.")
{
    public decimal Available { get; } = RandMoney.CentsToRand(availableCents);
    public decimal Required { get; } = RandMoney.CentsToRand(requiredCents);
}

internal static class VideoPokerMoney
{
    public const int MinimumCoinsWagered = 1;
    public const int MaximumCoinsWagered = 5;
    public const long CoinValueCents = RandMoney.CentsPerRand;

    public static long WagerCents(int coinsWagered, int handCount = 1)
    {
        if (coinsWagered is < MinimumCoinsWagered or > MaximumCoinsWagered)
        {
            throw new ArgumentOutOfRangeException(
                nameof(coinsWagered),
                $"Choose a Video Poker wager from {MinimumCoinsWagered} through {MaximumCoinsWagered} coins.");
        }

        if (handCount is not (1 or 3 or 5))
            throw new ArgumentOutOfRangeException(nameof(handCount), "Choose one, three, or five hands.");

        return checked(coinsWagered * handCount * CoinValueCents);
    }

    public static decimal ToRand(long cents) => RandMoney.CentsToRand(cents);
}

internal interface IVideoPokerStore
{
    Task<VideoPokerStoreResult> StartAsync(
        string userId,
        string idempotencyKey,
        int coinsWagered,
        int handCount,
        IReadOnlyList<IReadOnlyList<PlayingCard>> shuffledDecks,
        DateTimeOffset nowUtc,
        CancellationToken cancellationToken);

    Task<VideoPokerStoreResult?> GetAsync(
        string userId,
        string roundId,
        CancellationToken cancellationToken);

    Task<VideoPokerStoreResult> DrawAsync(
        string userId,
        string roundId,
        string idempotencyKey,
        VideoPokerHeldCardPositions heldPositions,
        DateTimeOffset nowUtc,
        CancellationToken cancellationToken);
}

internal sealed class VideoPokerService(
    IVideoPokerStore store,
    TimeProvider timeProvider,
    Func<IReadOnlyList<PlayingCard>>? createShuffledDeck = null)
{
    private readonly Func<IReadOnlyList<PlayingCard>> createShuffledDeck = createShuffledDeck ?? CreateShuffledDeck;

    public async Task<VideoPokerRoundResponse> StartAsync(
        string userId,
        CreateVideoPokerRoundRequest request,
        string idempotencyKey,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);
        ValidateIdempotencyKey(idempotencyKey);
        VideoPokerMoney.WagerCents(request.CoinsWagered, request.HandCount);
        var shuffledDecks = Enumerable.Range(0, request.HandCount).Select(_ => createShuffledDeck()).ToArray();
        var result = await store.StartAsync(
            userId,
            idempotencyKey,
            request.CoinsWagered,
            request.HandCount,
            shuffledDecks,
            timeProvider.GetUtcNow(),
            cancellationToken);
        return ToResponse(result);
    }

    public async Task<VideoPokerRoundResponse> GetAsync(
        string userId,
        string roundId,
        CancellationToken cancellationToken)
    {
        ValidateRoundId(roundId);
        var result = await store.GetAsync(userId, roundId, cancellationToken)
            ?? throw new VideoPokerRoundNotFoundException();
        return ToResponse(result);
    }

    public async Task<VideoPokerRoundResponse> DrawAsync(
        string userId,
        string roundId,
        DrawVideoPokerRoundRequest request,
        string idempotencyKey,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);
        ValidateRoundId(roundId);
        ValidateIdempotencyKey(idempotencyKey);
        if (request.HeldPositions is null)
            throw new ArgumentException("Held positions are required.", nameof(request));

        var heldPositions = new VideoPokerHeldCardPositions(request.HeldPositions.Select(position =>
            Enum.IsDefined((VideoPokerCardPosition)position)
                ? (VideoPokerCardPosition)position
                : throw new ArgumentOutOfRangeException(nameof(request), "A held card position is invalid.")));
        var result = await store.DrawAsync(
            userId,
            roundId,
            idempotencyKey,
            heldPositions,
            timeProvider.GetUtcNow(),
            cancellationToken);
        return ToResponse(result);
    }

    internal static VideoPokerRoundResponse ToResponse(VideoPokerStoreResult result)
    {
        var round = result.Round;
        var completed = round.Status == VideoPokerRoundStatus.Completed;
        var handPayouts = completed
            ? round.Results.Select(result => VideoPokerMoney.ToRand(checked(result.PaytableOutcome.CreditsWon * VideoPokerMoney.CoinValueCents))).ToArray()
            : null;
        var payout = handPayouts?.Sum();
        return new VideoPokerRoundResponse(
            RoundId: result.RoundId,
            Balance: VideoPokerMoney.ToRand(result.BalanceCents),
            CoinsWagered: round.CoinsWagered,
            HandCount: round.HandCount,
            Wager: VideoPokerMoney.ToRand(VideoPokerMoney.WagerCents(round.CoinsWagered, round.HandCount)),
            Phase: completed ? "completed" : "awaiting-draw",
            InitialCards: round.InitialDeal.Cards.Select(ToCard).ToArray(),
            HeldPositions: completed
                ? round.HeldCardPositions!.Positions.Select(position => (int)position).ToArray()
                : [],
            FinalCards: completed ? round.Result!.FinalHand.Cards.Select(ToCard).ToArray() : null,
            HandRank: completed ? HandRankName(round.Result!.HandRank) : null,
            Payout: payout,
            FinalHands: completed
                ? round.Results.Select(result => (IReadOnlyList<VideoPokerCardResponse>)result.FinalHand.Cards.Select(ToCard).ToArray()).ToArray()
                : null,
            HandRanks: completed ? round.Results.Select(result => HandRankName(result.HandRank)).ToArray() : null,
            HandPayouts: handPayouts);
    }

    internal static void ValidateIdempotencyKey(string idempotencyKey)
    {
        if (string.IsNullOrWhiteSpace(idempotencyKey) || idempotencyKey.Length is < 16 or > 128 ||
            idempotencyKey.Any(character => !char.IsAsciiLetterOrDigit(character) && character is not '-' and not '_'))
        {
            throw new ArgumentException(
                "Idempotency-Key must contain 16 to 128 letters, digits, hyphens, or underscores.",
                nameof(idempotencyKey));
        }
    }

    internal static string HandRankName(VideoPokerHandRank rank) => rank switch
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

    internal static VideoPokerCardResponse ToCard(PlayingCard card) => new(
        card.Rank switch
        {
            CardRank.Ace => "ace", CardRank.Two => "two", CardRank.Three => "three",
            CardRank.Four => "four", CardRank.Five => "five", CardRank.Six => "six",
            CardRank.Seven => "seven", CardRank.Eight => "eight", CardRank.Nine => "nine",
            CardRank.Ten => "ten", CardRank.Jack => "jack", CardRank.Queen => "queen",
            CardRank.King => "king", _ => throw new ArgumentOutOfRangeException(nameof(card)),
        },
        card.Suit switch
        {
            CardSuit.Clubs => "clubs", CardSuit.Diamonds => "diamonds", CardSuit.Hearts => "hearts",
            CardSuit.Spades => "spades", _ => throw new ArgumentOutOfRangeException(nameof(card)),
        });

    private static IReadOnlyList<PlayingCard> CreateShuffledDeck()
    {
        var deck = StandardDeck.Create().ToArray();
        for (var index = deck.Length - 1; index > 0; index--)
        {
            var replacement = RandomNumberGenerator.GetInt32(index + 1);
            (deck[index], deck[replacement]) = (deck[replacement], deck[index]);
        }
        return deck;
    }

    private static void ValidateRoundId(string roundId)
    {
        if (roundId.Length != 64 || roundId.Any(character => !char.IsAsciiHexDigit(character)))
            throw new ArgumentException("The Video Poker round identifier is invalid.", nameof(roundId));
    }
}
