using System.Security.Cryptography;
using System.Text;
using FortuneForge.Games.Cards;

namespace FortuneForge.Games.DragonTiger.Server;

public sealed record CreateDragonTigerRoundRequest(decimal DragonStake = 0, decimal TigerStake = 0, decimal TieStake = 0);
public sealed record DragonTigerStatusResponse(bool Available, decimal MinimumStake, decimal MaximumStake,
    decimal StakeIncrement, decimal MaximumTotalStake, decimal Balance, string Mode);
public sealed record DragonTigerCardResponse(string Rank, string Suit);
public sealed record DragonTigerBetResponse(string Side, decimal Stake, string Disposition, decimal Profit, decimal TotalReturn);
public sealed record DragonTigerRoundResponse(string RoundId, decimal Balance, string Phase,
    DragonTigerCardResponse DragonCard, DragonTigerCardResponse TigerCard,
    int DragonValue, int TigerValue, string Outcome, IReadOnlyList<DragonTigerBetResponse> Bets,
    decimal TotalStake, decimal Profit, decimal TotalReturn, int ShoeCardsUsed, int ShoeCardsRemaining);
public sealed record DragonTigerErrorResponse(string Code, string Message);
public sealed record DragonTigerStoredRound(string RoundId, string UserId, DragonTigerWagers Wagers,
    DragonTigerRound Round, int ShoeCardsUsed, int ShoeCardsRemaining);
public sealed record DragonTigerStoreResult(DragonTigerStoredRound Round, long BalanceCents);
public sealed class DragonTigerRoundNotFoundException() : Exception("Dragon Tiger round not found.");
public sealed class DragonTigerRoundConflictException() : Exception("This Idempotency-Key was already used with different Dragon Tiger stakes.");
public sealed class DragonTigerInsufficientCreditsException(long availableCents, long requiredCents)
    : Exception($"This account has R{DragonTigerMoney.ToRand(availableCents):0.00}, but the wager requires R{DragonTigerMoney.ToRand(requiredCents):0.00}.");

public interface IDragonTigerStore
{
    Task<DragonTigerStoreResult> StartAsync(string userId, string idempotencyKey, DragonTigerWagers wagers,
        IReadOnlyList<PlayingCard> shuffledShoe, DateTimeOffset nowUtc, CancellationToken cancellationToken);
    Task<DragonTigerStoreResult?> GetAsync(string userId, string roundId, CancellationToken cancellationToken);
    Task<DragonTigerStoreResult?> LatestAsync(string userId, CancellationToken cancellationToken);
}

public static class DragonTigerMoney
{
    public const long MinimumStakeCents = 50;
    public const long MaximumStakeCents = 10_000;
    public const long StakeIncrementCents = 50;
    public const long MaximumTotalStakeCents = 10_000;
    public static decimal ToRand(long cents) => cents / 100m;
    public static DragonTigerWagers Parse(CreateDragonTigerRoundRequest request)
    {
        var wagers = new DragonTigerWagers(ToStakeCents(request.DragonStake), ToStakeCents(request.TigerStake), ToStakeCents(request.TieStake));
        Validate(wagers);
        return wagers;
    }
    public static void Validate(DragonTigerWagers wagers)
    {
        foreach (var side in Enum.GetValues<DragonTigerSide>())
        {
            var stake = wagers.For(side);
            if (stake != 0 && (stake < MinimumStakeCents || stake > MaximumStakeCents || stake % StakeIncrementCents != 0))
                throw new ArgumentOutOfRangeException(nameof(wagers), "Choose stakes in R0.50 increments from R0.50 through R100.00 per spot.");
        }
        if (wagers.TotalCents <= 0 || wagers.TotalCents > MaximumTotalStakeCents)
            throw new ArgumentOutOfRangeException(nameof(wagers), "Place at least R0.50 and no more than R100.00 total per deal.");
    }
    private static long ToStakeCents(decimal amount)
    {
        if (amount < 0m || amount > 100m || amount % 0.50m != 0)
            throw new ArgumentOutOfRangeException(nameof(amount), "Choose zero or a R0.50 increment up to R100.00.");
        return checked((long)(amount * 100m));
    }
}

public sealed class DragonTigerService(IDragonTigerStore store, TimeProvider timeProvider,
    Func<IReadOnlyList<PlayingCard>>? createShuffledShoe = null)
{
    private readonly Func<IReadOnlyList<PlayingCard>> createShuffledShoe = createShuffledShoe ?? CreateShuffledShoe;
    public async Task<DragonTigerRoundResponse> StartAsync(string userId, CreateDragonTigerRoundRequest request,
        string idempotencyKey, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        if (string.IsNullOrWhiteSpace(userId)) throw new ArgumentException("A player is required.", nameof(userId));
        if (string.IsNullOrWhiteSpace(idempotencyKey) || idempotencyKey.Length is < 16 or > 128 ||
            idempotencyKey.Any(c => !char.IsAsciiLetterOrDigit(c) && c is not '-' and not '_'))
            throw new ArgumentException("Idempotency-Key must contain 16 to 128 letters, digits, hyphens, or underscores.");
        var wagers = DragonTigerMoney.Parse(request);
        return ToResponse(await store.StartAsync(userId, idempotencyKey, wagers, createShuffledShoe(), timeProvider.GetUtcNow(), cancellationToken));
    }
    public async Task<DragonTigerRoundResponse> GetAsync(string userId, string roundId, CancellationToken cancellationToken = default)
    {
        if (roundId.Length != 64 || roundId.Any(c => !char.IsAsciiHexDigit(c)))
            throw new ArgumentException("The Dragon Tiger round identifier is invalid.");
        return ToResponse(await store.GetAsync(userId, roundId, cancellationToken) ?? throw new DragonTigerRoundNotFoundException());
    }
    public async Task<DragonTigerRoundResponse?> LatestAsync(string userId, CancellationToken cancellationToken = default)
    {
        var result = await store.LatestAsync(userId, cancellationToken);
        return result is null ? null : ToResponse(result);
    }
    public static DragonTigerRoundResponse ToResponse(DragonTigerStoreResult result)
    {
        var stored = result.Round;
        var settlements = DragonTigerRules.Settle(stored.Wagers, stored.Round);
        var totalReturn = settlements.Sum(value => value.TotalReturnCents);
        return new(stored.RoundId, DragonTigerMoney.ToRand(result.BalanceCents), "settled",
            ToCard(stored.Round.DragonCard), ToCard(stored.Round.TigerCard), stored.Round.DragonValue, stored.Round.TigerValue,
            Name(stored.Round.Outcome), settlements.Select(value => new DragonTigerBetResponse(Name(value.Side),
                DragonTigerMoney.ToRand(value.StakeCents), DispositionName(value.Disposition),
                DragonTigerMoney.ToRand(value.ProfitCents), DragonTigerMoney.ToRand(value.TotalReturnCents))).ToArray(),
            DragonTigerMoney.ToRand(stored.Wagers.TotalCents), DragonTigerMoney.ToRand(totalReturn - stored.Wagers.TotalCents),
            DragonTigerMoney.ToRand(totalReturn), stored.ShoeCardsUsed, stored.ShoeCardsRemaining);
    }
    public static string LookupKey(string userId, string key) => Hash($"{userId}\n{key}");
    internal static string Hash(string value) => Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(value)));
    public static IReadOnlyList<PlayingCard> CreateShuffledShoe()
    {
        var shoe = Enumerable.Range(0, DragonTigerRules.DeckCount).SelectMany(_ => StandardDeck.Create()).ToArray();
        for (var index = shoe.Length - 1; index > 0; index--)
        {
            var replacement = RandomNumberGenerator.GetInt32(index + 1);
            (shoe[index], shoe[replacement]) = (shoe[replacement], shoe[index]);
        }
        return shoe;
    }
    internal static string Name(DragonTigerSide side) => side.ToString().ToLowerInvariant();
    private static string DispositionName(DragonTigerDisposition disposition) => disposition switch
    {
        DragonTigerDisposition.Win => "win", DragonTigerDisposition.Loss => "loss",
        DragonTigerDisposition.HalfLoss => "half-loss", DragonTigerDisposition.NotPlaced => "not-placed",
        _ => throw new ArgumentOutOfRangeException(nameof(disposition)),
    };
    private static DragonTigerCardResponse ToCard(PlayingCard card) => new(card.Rank.ToString().ToLowerInvariant(), card.Suit.ToString().ToLowerInvariant());
}
