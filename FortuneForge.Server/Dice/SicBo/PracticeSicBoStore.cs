using System.Security.Cryptography;
using System.Text;
using FortuneForge.Games.SicBo;

namespace FortuneForge.Server.Dice.SicBo;

/// <summary>Process-local QA ledger that cannot mutate an account balance.</summary>
internal sealed class PracticeSicBoStore : ISicBoStore
{
    public const long StartingBalanceCents = 1_000_000;
    private readonly Lock sync = new();
    private readonly Dictionary<string, StoredRound> rounds = [];
    private readonly Dictionary<(string UserId, string Key), string> starts = [];
    private readonly Dictionary<string, long> balances = [];

    public Task<SicBoStoreResult> StartAsync(string userId, string idempotencyKey, IReadOnlyList<SicBoStoredBet> bets,
        SicBoRoll roll, DateTimeOffset nowUtc, CancellationToken cancellationToken)
    {
        lock (sync)
        {
            if (starts.TryGetValue((userId, idempotencyKey), out var existingId))
            {
                var existing = rounds[existingId];
                if (!existing.Round.Bets.SequenceEqual(bets)) throw new SicBoRoundConflictException("Practice bet-slip conflict.");
                return Task.FromResult(Result(existing));
            }

            var stake = checked(bets.Sum(bet => bet.StakeCents));
            var balance = Balance(userId);
            if (balance < stake) throw new SicBoInsufficientCreditsException(balance, stake);
            var returned = checked(bets.Sum(bet => ToCents(SicBoPaytable.Settle(SicBoMoney.ToDomain(bet), roll).TotalReturn)));
            var id = Id("sic-bo", userId, idempotencyKey);
            var round = new SicBoStoreRound(id, userId, bets.ToArray(), roll);
            var stored = new StoredRound(round, idempotencyKey);
            balances[userId] = checked(balance - stake + returned);
            rounds[id] = stored;
            starts[(userId, idempotencyKey)] = id;
            return Task.FromResult(Result(stored));
        }
    }

    public Task<SicBoStoreResult?> GetAsync(string userId, string roundId, CancellationToken cancellationToken)
    {
        lock (sync)
            return Task.FromResult(rounds.TryGetValue(roundId, out var stored) && stored.Round.UserId == userId ? Result(stored) : null);
    }

    private SicBoStoreResult Result(StoredRound stored) => new(stored.Round, Balance(stored.Round.UserId));
    private long Balance(string userId) => balances.GetValueOrDefault(userId, StartingBalanceCents);
    private static long ToCents(decimal value) => checked((long)(value * 100m));
    private static string Id(string game, string userId, string key) => Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes($"practice\n{game}\n{userId}\n{key}")));
    private sealed record StoredRound(SicBoStoreRound Round, string StartKey);
}
