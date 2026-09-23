using System.Security.Cryptography;
using System.Text;
using FortuneForge.Games.Baccarat;
using FortuneForge.Games.Cards;

namespace FortuneForge.Server.Cards.Baccarat;

/// <summary>Process-local QA ledger that never reaches the account or payment stores.</summary>
internal sealed class PracticeBaccaratStore : IBaccaratStore
{
    public const long StartingBalanceCents = 1_000_000;
    private readonly Lock sync = new();
    private readonly Dictionary<string, StoredRound> rounds = [];
    private readonly Dictionary<(string UserId, string Key), string> starts = [];
    private readonly Dictionary<string, long> balances = [];
    private readonly Dictionary<string, ShoeState> shoes = [];

    public Task<BaccaratStoreResult> StartAsync(string userId, string idempotencyKey, BaccaratBetSide betSide,
        long stakeCents, IReadOnlyList<PlayingCard> shuffledShoe, DateTimeOffset nowUtc, CancellationToken cancellationToken)
    {
        lock (sync)
        {
            if (starts.TryGetValue((userId, idempotencyKey), out var existingId))
            {
                var existing = rounds[existingId];
                if (existing.Round.BetSide != betSide || BaccaratMoney.ToStakeCents(existing.Round.Settlement.Stake) != stakeCents)
                    throw new BaccaratRoundConflictException("Practice deal conflict.");
                return Task.FromResult(Result(existing));
            }

            var balance = Balance(userId);
            if (balance < stakeCents) throw new BaccaratInsufficientCreditsException(balance, stakeCents);
            var shoe = shoes.GetValueOrDefault(userId);
            if (shoe is null || shoe.NextIndex >= 312)
                shoe = new ShoeState(shuffledShoe.ToArray(), 0);
            var dealt = PuntoBancoRoundDealer.Deal(shoe.Cards.Skip(shoe.NextIndex).ToArray());
            var nextIndex = checked(shoe.NextIndex + dealt.ConsumedCards.Length);
            shoes[userId] = shoe with { NextIndex = nextIndex };
            var settlement = PuntoBancoPaytable.Settle(betSide, BaccaratMoney.ToRand(stakeCents), dealt);
            var returned = checked((long)(settlement.TotalReturn * 100m));
            var id = Id("baccarat", userId, idempotencyKey);
            var round = new BaccaratStoreRound(id, userId, betSide, dealt, settlement, nextIndex, shoe.Cards.Count - nextIndex);
            var stored = new StoredRound(round, idempotencyKey);
            balances[userId] = checked(balance - stakeCents + returned);
            rounds[id] = stored;
            starts[(userId, idempotencyKey)] = id;
            return Task.FromResult(Result(stored));
        }
    }

    public Task<BaccaratStoreResult?> GetAsync(string userId, string roundId, CancellationToken cancellationToken)
    {
        lock (sync)
            return Task.FromResult(rounds.TryGetValue(roundId, out var stored) && stored.Round.UserId == userId ? Result(stored) : null);
    }

    private BaccaratStoreResult Result(StoredRound stored) => new(stored.Round, Balance(stored.Round.UserId));
    private long Balance(string userId) => balances.GetValueOrDefault(userId, StartingBalanceCents);
    private static string Id(string game, string userId, string key) => Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes($"practice\n{game}\n{userId}\n{key}")));
    private sealed record StoredRound(BaccaratStoreRound Round, string StartKey);
    private sealed record ShoeState(IReadOnlyList<PlayingCard> Cards, int NextIndex);
}
