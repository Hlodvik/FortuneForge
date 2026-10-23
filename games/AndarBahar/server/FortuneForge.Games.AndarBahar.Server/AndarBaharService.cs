using System.Security.Cryptography;
using System.Text;
using System.Text.Json.Serialization;
using FortuneForge.Games.Cards;

namespace FortuneForge.Games.AndarBahar.Server;

[JsonUnmappedMemberHandling(JsonUnmappedMemberHandling.Disallow)]
public sealed record CreateAndarBaharRoundRequest(string Side, decimal Stake);
public sealed record AndarBaharStatusResponse(bool Available, decimal MinimumStake, decimal MaximumStake, decimal StakeIncrement, decimal Balance, string Mode);
public sealed record AndarBaharCardResponse(string Rank, string Suit);
public sealed record AndarBaharDealResponse(int Number, string Side, AndarBaharCardResponse Card);
public sealed record AndarBaharRoundResponse(string RoundId, decimal Balance, string Phase,
    AndarBaharCardResponse Joker, IReadOnlyList<AndarBaharDealResponse> Deals, string Winner,
    string Side, decimal Stake, string Disposition, decimal Profit, decimal TotalReturn);
public sealed record AndarBaharErrorResponse(string Code, string Message);
public sealed record AndarBaharStoredRound(string RoundId, string UserId, string IdempotencyKey,
    AndarBaharSide Side, long StakeCents, AndarBaharRound Round, long BalanceAfterWagerCents,
    long BalanceAfterSettlementCents, DateTimeOffset CreatedAtUtc);
public sealed record AndarBaharStoreResult(AndarBaharStoredRound Round, long BalanceCents);
public sealed class AndarBaharRoundNotFoundException() : Exception("Andar Bahar round not found.");
public sealed class AndarBaharRoundConflictException() : Exception("This Idempotency-Key was already used with a different Andar Bahar wager.");
public sealed class AndarBaharInsufficientCreditsException(long available, long required)
    : Exception($"The balance is R{AndarBaharMoney.ToRand(available):0.00}; this wager requires R{AndarBaharMoney.ToRand(required):0.00}.");

public interface IAndarBaharStore
{
    Task<long> BalanceAsync(string userId, CancellationToken cancellationToken);
    Task<AndarBaharStoreResult> StartAsync(string userId, string key, AndarBaharSide side, long stakeCents,
        IReadOnlyList<PlayingCard> shuffledDeck, DateTimeOffset nowUtc, CancellationToken cancellationToken);
    Task<AndarBaharStoreResult?> GetAsync(string userId, string roundId, CancellationToken cancellationToken);
    Task<AndarBaharStoreResult?> LatestAsync(string userId, CancellationToken cancellationToken);
}

public static class AndarBaharMoney
{
    public const long MinimumStakeCents = 50, MaximumStakeCents = 10_000, StakeIncrementCents = 50;
    public static decimal ToRand(long cents) => cents / 100m;
    public static (AndarBaharSide Side, long StakeCents) Parse(CreateAndarBaharRoundRequest request)
    {
        var side = request.Side switch { "andar" => AndarBaharSide.Andar, "bahar" => AndarBaharSide.Bahar,
            _ => throw new ArgumentException("Choose either andar or bahar.") };
        if (request.Stake is < .50m or > 100m || request.Stake % .50m != 0)
            throw new ArgumentException("Choose a stake in R0.50 increments from R0.50 through R100.00.");
        return (side, checked((long)(request.Stake * 100m)));
    }
    public static void Validate(AndarBaharSide side, long stakeCents)
    {
        if (!Enum.IsDefined(side) || stakeCents is < MinimumStakeCents or > MaximumStakeCents || stakeCents % StakeIncrementCents != 0)
            throw new ArgumentException("The Andar Bahar wager is invalid.");
    }
}

public sealed class AndarBaharService(IAndarBaharStore store, TimeProvider timeProvider,
    Func<IReadOnlyList<PlayingCard>>? createShuffledDeck = null)
{
    private readonly Func<IReadOnlyList<PlayingCard>> createDeck = createShuffledDeck ?? CreateShuffledDeck;
    public async Task<AndarBaharRoundResponse> StartAsync(string userId, CreateAndarBaharRoundRequest request, string key, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        ValidateUser(userId);
        ValidateKey(key);
        var (side, cents) = AndarBaharMoney.Parse(request);
        return ToResponse(await store.StartAsync(userId, key, side, cents, createDeck(), timeProvider.GetUtcNow(), cancellationToken));
    }
    public async Task<AndarBaharRoundResponse> GetAsync(string userId, string roundId, CancellationToken cancellationToken = default)
    {
        ValidateUser(userId);
        if (string.IsNullOrEmpty(roundId) || roundId.Length != 64 || roundId.Any(c => !char.IsAsciiHexDigit(c))) throw new ArgumentException("The Andar Bahar round identifier is invalid.");
        return ToResponse(await store.GetAsync(userId, roundId, cancellationToken) ?? throw new AndarBaharRoundNotFoundException());
    }
    public async Task<AndarBaharRoundResponse?> LatestAsync(string userId, CancellationToken cancellationToken = default)
    {
        ValidateUser(userId);
        var result = await store.LatestAsync(userId, cancellationToken);
        return result is null ? null : ToResponse(result);
    }
    public static AndarBaharRoundResponse ToResponse(AndarBaharStoreResult result)
    {
        var round = result.Round;
        var settled = AndarBaharRules.Settle(round.Side, round.StakeCents, round.Round);
        return new(round.RoundId, AndarBaharMoney.ToRand(result.BalanceCents), "settled", Card(round.Round.Joker),
            round.Round.Deals.Select(deal => new AndarBaharDealResponse(deal.Number, Name(deal.Side), Card(deal.Card))).ToArray(),
            Name(round.Round.Winner), Name(round.Side), AndarBaharMoney.ToRand(round.StakeCents),
            settled.Disposition.ToString().ToLowerInvariant(), AndarBaharMoney.ToRand(settled.ProfitCents), AndarBaharMoney.ToRand(settled.TotalReturnCents));
    }
    public static IReadOnlyList<PlayingCard> CreateShuffledDeck()
    {
        var cards = StandardDeck.Create().ToArray();
        for (var index = cards.Length - 1; index > 0; index--)
        {
            var replacement = RandomNumberGenerator.GetInt32(index + 1);
            (cards[index], cards[replacement]) = (cards[replacement], cards[index]);
        }
        return cards;
    }
    public static string LookupKey(string userId, string key) => Hash($"andar-bahar-v1\n{userId}\n{key}");
    internal static string Hash(string input) => Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(input)));
    internal static string Name(AndarBaharSide side) => side.ToString().ToLowerInvariant();
    internal static void ValidateUser(string user) { if (string.IsNullOrWhiteSpace(user) || user.Length > 256 || user.Contains('/')) throw new ArgumentException("A valid player account is required."); }
    internal static void ValidateKey(string key)
    {
        if (string.IsNullOrWhiteSpace(key) || key.Length is < 16 or > 128 || key.Any(c => !char.IsAsciiLetterOrDigit(c) && c is not '-' and not '_'))
            throw new ArgumentException("Idempotency-Key must contain 16 to 128 letters, digits, hyphens, or underscores.");
    }
    private static AndarBaharCardResponse Card(PlayingCard card) => new(card.Rank.ToString().ToLowerInvariant(), card.Suit.ToString().ToLowerInvariant());
}
