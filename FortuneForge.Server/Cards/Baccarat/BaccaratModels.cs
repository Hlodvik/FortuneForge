using System.Security.Cryptography;
using FortuneForge.Games.Baccarat;
using FortuneForge.Games.Cards;
using FortuneForge.Server.Accounts.Models;

namespace FortuneForge.Server.Cards.Baccarat;

public sealed record CreateBaccaratRoundRequest(string? BetSide, decimal Stake);

public sealed record BaccaratStatusResponse(
    bool Available,
    decimal MinimumStake,
    decimal MaximumStake,
    decimal StakeIncrement,
    decimal Balance,
    string Mode);

public sealed record BaccaratCardResponse(string Rank, string Suit);

public sealed record BaccaratRoundResponse(
    string RoundId,
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

internal sealed record BaccaratStoreRound(
    string RoundId,
    string UserId,
    BaccaratBetSide BetSide,
    PuntoBancoRoundResult Round,
    BaccaratBetSettlement Settlement);

internal sealed record BaccaratStoreResult(BaccaratStoreRound Round, long BalanceCents);

internal sealed class BaccaratRoundNotFoundException() : Exception("Baccarat round not found.");

internal sealed class BaccaratRoundConflictException(string message) : Exception(message);

internal sealed class BaccaratInsufficientCreditsException(long availableCents, long requiredCents)
    : Exception($"This account has R{RandMoney.CentsToRand(availableCents):0.00}, but the stake requires R{RandMoney.CentsToRand(requiredCents):0.00}.")
{
    public decimal Available { get; } = RandMoney.CentsToRand(availableCents);
    public decimal Required { get; } = RandMoney.CentsToRand(requiredCents);
}

internal static class BaccaratMoney
{
    public const long MinimumStakeCents = 100;
    public const long MaximumStakeCents = 10_000;
    public const long StakeIncrementCents = 100;

    public static long ToStakeCents(decimal stake)
    {
        var cents = checked(stake * RandMoney.CentsPerRand);
        if (cents != decimal.Truncate(cents))
            throw new ArgumentOutOfRangeException(nameof(stake), "A Baccarat stake cannot include a fraction of a cent.");
        var value = checked((long)cents);
        if (value < MinimumStakeCents || value > MaximumStakeCents || value % StakeIncrementCents != 0)
        {
            throw new ArgumentOutOfRangeException(
                nameof(stake),
                $"Choose a whole-rand Baccarat stake from R{ToRand(MinimumStakeCents):0.00} through R{ToRand(MaximumStakeCents):0.00}.");
        }
        return value;
    }

    public static decimal ToRand(long cents) => RandMoney.CentsToRand(cents);
}

internal interface IBaccaratStore
{
    Task<BaccaratStoreResult> StartAsync(
        string userId,
        string idempotencyKey,
        BaccaratBetSide betSide,
        long stakeCents,
        IReadOnlyList<PlayingCard> shuffledShoe,
        DateTimeOffset nowUtc,
        CancellationToken cancellationToken);

    Task<BaccaratStoreResult?> GetAsync(string userId, string roundId, CancellationToken cancellationToken);
}

internal sealed class BaccaratService(
    IBaccaratStore store,
    TimeProvider timeProvider,
    Func<IReadOnlyList<PlayingCard>>? createShuffledShoe = null)
{
    private readonly Func<IReadOnlyList<PlayingCard>> createShuffledShoe = createShuffledShoe ?? CreateShuffledShoe;

    public async Task<BaccaratRoundResponse> StartAsync(
        string userId,
        CreateBaccaratRoundRequest request,
        string idempotencyKey,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);
        ValidateIdempotencyKey(idempotencyKey);
        var betSide = ParseBetSide(request.BetSide);
        var stakeCents = BaccaratMoney.ToStakeCents(request.Stake);
        var result = await store.StartAsync(
            userId,
            idempotencyKey,
            betSide,
            stakeCents,
            createShuffledShoe(),
            timeProvider.GetUtcNow(),
            cancellationToken);
        return ToResponse(result);
    }

    public async Task<BaccaratRoundResponse> GetAsync(string userId, string roundId, CancellationToken cancellationToken)
    {
        ValidateRoundId(roundId);
        return ToResponse(await store.GetAsync(userId, roundId, cancellationToken) ?? throw new BaccaratRoundNotFoundException());
    }

    internal static BaccaratRoundResponse ToResponse(BaccaratStoreResult result)
    {
        var stored = result.Round;
        var round = stored.Round;
        var settlement = stored.Settlement;
        return new BaccaratRoundResponse(
            stored.RoundId,
            BaccaratMoney.ToRand(result.BalanceCents),
            BetSideName(stored.BetSide),
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
    }

    internal static BaccaratBetSide ParseBetSide(string? value) => value?.Trim().ToLowerInvariant() switch
    {
        "player" => BaccaratBetSide.Player,
        "banker" => BaccaratBetSide.Banker,
        "tie" => BaccaratBetSide.Tie,
        _ => throw new ArgumentException("Choose player, banker, or tie.", nameof(value)),
    };

    internal static string BetSideName(BaccaratBetSide betSide) => betSide switch
    {
        BaccaratBetSide.Player => "player", BaccaratBetSide.Banker => "banker", BaccaratBetSide.Tie => "tie",
        _ => throw new ArgumentOutOfRangeException(nameof(betSide)),
    };

    internal static string OutcomeName(BaccaratRoundOutcome outcome) => outcome switch
    {
        BaccaratRoundOutcome.Player => "player", BaccaratRoundOutcome.Banker => "banker", BaccaratRoundOutcome.Tie => "tie",
        _ => throw new ArgumentOutOfRangeException(nameof(outcome)),
    };

    internal static string DispositionName(BaccaratBetDisposition disposition) => disposition switch
    {
        BaccaratBetDisposition.Win => "win", BaccaratBetDisposition.Loss => "loss", BaccaratBetDisposition.Push => "push",
        _ => throw new ArgumentOutOfRangeException(nameof(disposition)),
    };

    internal static BaccaratCardResponse ToCard(PlayingCard card) => new(
        card.Rank switch
        {
            CardRank.Ace => "ace", CardRank.Two => "two", CardRank.Three => "three", CardRank.Four => "four",
            CardRank.Five => "five", CardRank.Six => "six", CardRank.Seven => "seven", CardRank.Eight => "eight",
            CardRank.Nine => "nine", CardRank.Ten => "ten", CardRank.Jack => "jack", CardRank.Queen => "queen",
            CardRank.King => "king", _ => throw new ArgumentOutOfRangeException(nameof(card)),
        },
        card.Suit switch
        {
            CardSuit.Clubs => "clubs", CardSuit.Diamonds => "diamonds", CardSuit.Hearts => "hearts",
            CardSuit.Spades => "spades", _ => throw new ArgumentOutOfRangeException(nameof(card)),
        });

    private static IReadOnlyList<PlayingCard> CreateShuffledShoe()
    {
        var shoe = Enumerable.Range(0, 8).SelectMany(_ => StandardDeck.Create()).ToArray();
        for (var index = shoe.Length - 1; index > 0; index--)
        {
            var replacement = RandomNumberGenerator.GetInt32(index + 1);
            (shoe[index], shoe[replacement]) = (shoe[replacement], shoe[index]);
        }
        return shoe;
    }

    private static void ValidateIdempotencyKey(string idempotencyKey)
    {
        if (string.IsNullOrWhiteSpace(idempotencyKey) || idempotencyKey.Length is < 16 or > 128 ||
            idempotencyKey.Any(character => !char.IsAsciiLetterOrDigit(character) && character is not '-' and not '_'))
        {
            throw new ArgumentException("Idempotency-Key must contain 16 to 128 letters, digits, hyphens, or underscores.", nameof(idempotencyKey));
        }
    }

    private static void ValidateRoundId(string roundId)
    {
        if (roundId.Length != 64 || roundId.Any(character => !char.IsAsciiHexDigit(character)))
            throw new ArgumentException("The Baccarat round identifier is invalid.", nameof(roundId));
    }
}
