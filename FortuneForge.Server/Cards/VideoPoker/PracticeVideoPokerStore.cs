using System.Security.Cryptography;
using System.Text;
using FortuneForge.Games.VideoPoker;

namespace FortuneForge.Server.Cards.VideoPoker;

/// <summary>Process-local QA ledger. It exercises the production engine without reading or writing account funds.</summary>
internal sealed class PracticeVideoPokerStore : IVideoPokerStore
{
    public const long StartingBalanceCents = 1_000_000;
    private readonly Lock sync = new();
    private readonly Dictionary<string, StoredRound> rounds = [];
    private readonly Dictionary<(string UserId, string Key), string> starts = [];
    private readonly Dictionary<string, long> balances = [];

    public Task<VideoPokerStoreResult> StartAsync(string userId, string idempotencyKey, int coinsWagered,
        IReadOnlyList<PlayingCard> shuffledDeck, DateTimeOffset nowUtc, CancellationToken cancellationToken)
    {
        lock (sync)
        {
            if (starts.TryGetValue((userId, idempotencyKey), out var existingId))
            {
                var existing = rounds[existingId];
                if (existing.Round.CoinsWagered != coinsWagered) throw new VideoPokerRoundConflictException("Practice deal conflict.");
                return Task.FromResult(Result(existing));
            }

            var wager = VideoPokerMoney.WagerCents(coinsWagered);
            var balance = Balance(userId);
            if (balance < wager) throw new VideoPokerInsufficientCreditsException(balance, wager);
            var id = Id("video-poker", userId, idempotencyKey);
            var stored = new StoredRound(id, userId, idempotencyKey, VideoPokerRoundEngine.Deal(shuffledDeck, coinsWagered));
            balances[userId] = balance - wager;
            rounds[id] = stored;
            starts[(userId, idempotencyKey)] = id;
            return Task.FromResult(Result(stored));
        }
    }

    public Task<VideoPokerStoreResult?> GetAsync(string userId, string roundId, CancellationToken cancellationToken)
    {
        lock (sync)
            return Task.FromResult(rounds.TryGetValue(roundId, out var stored) && stored.UserId == userId ? Result(stored) : null);
    }

    public Task<VideoPokerStoreResult> DrawAsync(string userId, string roundId, string idempotencyKey,
        VideoPokerHeldCardPositions heldPositions, DateTimeOffset nowUtc, CancellationToken cancellationToken)
    {
        lock (sync)
        {
            if (!rounds.TryGetValue(roundId, out var stored) || stored.UserId != userId) throw new VideoPokerRoundNotFoundException();
            if (stored.Round.Status == VideoPokerRoundStatus.Completed)
            {
                if (!stored.Round.Draw!.HeldCardPositions.Positions.SequenceEqual(heldPositions.Positions))
                    throw new VideoPokerRoundConflictException("Practice draw conflict.");
                return Task.FromResult(Result(stored));
            }

            var completed = VideoPokerRoundEngine.Draw(stored.Round, heldPositions);
            stored = stored with { Round = completed };
            rounds[roundId] = stored;
            balances[userId] = checked(Balance(userId) + completed.Result!.PaytableOutcome.CreditsWon * VideoPokerMoney.CoinValueCents);
            return Task.FromResult(Result(stored));
        }
    }

    private VideoPokerStoreResult Result(StoredRound stored) => new(stored.RoundId, stored.Round, Balance(stored.UserId));
    private long Balance(string userId) => balances.GetValueOrDefault(userId, StartingBalanceCents);
    private static string Id(string game, string userId, string key) => Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes($"practice\n{game}\n{userId}\n{key}")));
    private sealed record StoredRound(string RoundId, string UserId, string StartKey, VideoPokerRound Round);
}
