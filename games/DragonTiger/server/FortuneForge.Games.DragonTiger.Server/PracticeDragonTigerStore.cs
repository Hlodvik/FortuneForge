using FortuneForge.Games.Cards;

namespace FortuneForge.Games.DragonTiger.Server;

/// <summary>Process-local practice wallet; it cannot write account or payment data.</summary>
public sealed class PracticeDragonTigerStore(long startingBalanceCents = 1_000_000) : IDragonTigerStore
{
    private readonly Lock sync = new();
    private readonly Dictionary<string, DragonTigerStoredRound> rounds = [];
    private readonly Dictionary<string, long> balances = [];
    private readonly Dictionary<string, ShoeState> shoes = [];
    private readonly Dictionary<string, string> latest = [];
    public int RoundCount { get { lock (sync) return rounds.Count; } }
    public long GetBalanceCents(string userId) { lock (sync) return Balance(userId); }
    public Task<DragonTigerStoreResult> StartAsync(string userId, string idempotencyKey, DragonTigerWagers wagers,
        IReadOnlyList<PlayingCard> shuffledShoe, DateTimeOffset nowUtc, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        DragonTigerMoney.Validate(wagers);
        lock (sync)
        {
            var id = DragonTigerService.LookupKey(userId, idempotencyKey);
            if (rounds.TryGetValue(id, out var existing))
            {
                if (existing.UserId != userId || existing.Wagers != wagers) throw new DragonTigerRoundConflictException();
                return Task.FromResult(new DragonTigerStoreResult(existing, Balance(userId)));
            }
            var balance = Balance(userId);
            if (balance < wagers.TotalCents) throw new DragonTigerInsufficientCreditsException(balance, wagers.TotalCents);
            var shoe = shoes.GetValueOrDefault(userId);
            if (shoe is null || shoe.NextIndex >= DragonTigerRules.ReshuffleAt)
            {
                DragonTigerShoe.Validate(shuffledShoe);
                shoe = new(shuffledShoe.ToArray(), 0);
            }
            var round = DragonTigerRules.Deal(shoe.Cards.Skip(shoe.NextIndex).Take(2).ToArray());
            var nextIndex = shoe.NextIndex + 2;
            var totalReturn = DragonTigerRules.Settle(wagers, round).Sum(value => value.TotalReturnCents);
            var stored = new DragonTigerStoredRound(id, userId, wagers, round, nextIndex, shoe.Cards.Count - nextIndex);
            balances[userId] = checked(balance - wagers.TotalCents + totalReturn);
            shoes[userId] = shoe with { NextIndex = nextIndex };
            rounds[id] = stored;
            latest[userId] = id;
            return Task.FromResult(new DragonTigerStoreResult(stored, balances[userId]));
        }
    }
    public Task<DragonTigerStoreResult?> GetAsync(string userId, string roundId, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        lock (sync)
            return Task.FromResult(rounds.TryGetValue(roundId, out var round) && round.UserId == userId
                ? new DragonTigerStoreResult(round, Balance(userId)) : null);
    }
    public Task<DragonTigerStoreResult?> LatestAsync(string userId, CancellationToken cancellationToken)
    {
        lock (sync) return latest.TryGetValue(userId, out var id) ? GetAsync(userId, id, cancellationToken) : Task.FromResult<DragonTigerStoreResult?>(null);
    }
    private long Balance(string userId) => balances.GetValueOrDefault(userId, startingBalanceCents);
    private sealed record ShoeState(IReadOnlyList<PlayingCard> Cards, int NextIndex);
}

internal static class DragonTigerShoe
{
    public static void Validate(IReadOnlyList<PlayingCard> cards)
    {
        ArgumentNullException.ThrowIfNull(cards);
        if (cards.Count != DragonTigerRules.ShoeSize || StandardDeck.Create().Any(card => cards.Count(value => value == card) != DragonTigerRules.DeckCount))
            throw new InvalidOperationException("A Dragon Tiger shoe must contain exactly eight complete standard decks.");
    }
}
