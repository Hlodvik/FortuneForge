using System.Security.Cryptography;
using System.Text;
using FortuneForge.Games.Cards;
using FortuneForge.Games.CasinoWar;

namespace FortuneForge.Server.Cards.CasinoWar;

/// <summary>Process-local QA ledger that settles real rules against practice credits only.</summary>
internal sealed class PracticeCasinoWarStore : ICasinoWarStore
{
    public const long StartingBalanceCents = 1_000_000;
    private readonly Lock sync = new();
    private readonly Dictionary<string, StoredRound> rounds = [];
    private readonly Dictionary<(string UserId, string Key), string> starts = [];
    private readonly Dictionary<string, long> balances = [];

    public Task<CasinoWarStoreResult> StartAsync(string userId, string idempotencyKey, long primaryStakeCents,
        long tieStakeCents, IReadOnlyList<PlayingCard> shuffledShoe, DateTimeOffset nowUtc, CancellationToken cancellationToken)
    {
        lock (sync)
        {
            if (starts.TryGetValue((userId, idempotencyKey), out var existingId))
            {
                var existing = rounds[existingId];
                if (CasinoWarMoney.PrimaryCents(existing.Round.Round.PrimaryStake) != primaryStakeCents ||
                    CasinoWarMoney.TieCents(existing.Round.TieSettlement?.Stake ?? 0m) != tieStakeCents)
                    throw new CasinoWarRoundConflictException("Practice opening conflict.");
                return Task.FromResult(Result(existing));
            }

            var charge = checked(primaryStakeCents + tieStakeCents);
            var balance = Balance(userId);
            if (balance < charge) throw new CasinoWarInsufficientCreditsException(balance, charge);
            var deck = shuffledShoe.Take(CasinoWarRoundEngine.WarCardsRequired).ToArray();
            var started = CasinoWarRoundEngine.Start(deck, CasinoWarMoney.ToRand(primaryStakeCents));
            var tie = tieStakeCents == 0 ? null : CasinoWarTieBetPaytable.Settle(started.Opening, CasinoWarMoney.ToRand(tieStakeCents));
            var returned = checked(CasinoWarMoney.ToCents(tie?.TotalReturn ?? 0m) + CasinoWarMoney.ToCents(started.Settlement?.TotalReturn ?? 0m));
            var id = Id(CasinoWarModule.GameId, userId, idempotencyKey);
            var round = new CasinoWarStoreRound(id, userId, started, tie, deck);
            var stored = new StoredRound(round, idempotencyKey, null, nowUtc);
            balances[userId] = checked(balance - charge + returned);
            rounds[id] = stored;
            starts[(userId, idempotencyKey)] = id;
            return Task.FromResult(Result(stored));
        }
    }

    public Task<CasinoWarStoreResult?> GetAsync(string userId, string roundId, CancellationToken cancellationToken)
    {
        lock (sync)
            return Task.FromResult(rounds.TryGetValue(roundId, out var stored) && stored.Round.UserId == userId ? Result(stored) : null);
    }

    public Task<CasinoWarStoreResult> DecideAsync(string userId, string roundId, string idempotencyKey,
        CasinoWarTieDecision decision, DateTimeOffset nowUtc, CancellationToken cancellationToken)
    {
        lock (sync)
        {
            if (!rounds.TryGetValue(roundId, out var stored) || stored.Round.UserId != userId) throw new CasinoWarRoundNotFoundException();
            if (stored.Round.Round.Phase == CasinoWarRoundPhase.Completed)
            {
                if (stored.Round.Round.TieDecision != decision) throw new CasinoWarRoundConflictException("Practice decision conflict.");
                return Task.FromResult(Result(stored));
            }

            var extraCharge = decision == CasinoWarTieDecision.GoToWar
                ? CasinoWarMoney.PrimaryCents(stored.Round.Round.PrimaryStake)
                : 0;
            var balance = Balance(userId);
            if (balance < extraCharge) throw new CasinoWarInsufficientCreditsException(balance, extraCharge);
            var completed = CasinoWarRoundEngine.Decide(stored.Round.Round, decision);
            balances[userId] = checked(balance - extraCharge + CasinoWarMoney.ToCents(completed.Settlement!.TotalReturn));
            stored = stored with { Round = stored.Round with { Round = completed }, DecisionKey = idempotencyKey };
            rounds[roundId] = stored;
            return Task.FromResult(Result(stored));
        }
    }

    internal CasinoWarRecoveryResponse Recover(string userId)
    {
        lock (sync)
            return new(null, rounds.Values.Where(saved => saved.Round.UserId == userId &&
                    saved.Round.Round.Phase == CasinoWarRoundPhase.AwaitingTieDecision)
                .OrderByDescending(saved => saved.StartedAt).Take(100)
                .Select(saved => CasinoWarService.ToResponse(Result(saved))).ToArray());
    }

    internal IReadOnlyList<CasinoWarRoundResponse> History(string userId, int limit)
    {
        if (limit is < 1 or > 100) throw new ArgumentOutOfRangeException(nameof(limit));
        lock (sync)
            return rounds.Values.Where(saved => saved.Round.UserId == userId)
                .OrderByDescending(saved => saved.StartedAt).Take(limit)
                .Select(saved => CasinoWarService.ToResponse(Result(saved))).ToArray();
    }

    private CasinoWarStoreResult Result(StoredRound stored) => new(stored.Round, Balance(stored.Round.UserId));
    private long Balance(string userId) => balances.GetValueOrDefault(userId, StartingBalanceCents);
    private static string Id(string game, string userId, string key) => Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes($"practice\n{game}\n{userId}\n{key}")));
    private sealed record StoredRound(CasinoWarStoreRound Round, string StartKey, string? DecisionKey, DateTimeOffset StartedAt);
}
