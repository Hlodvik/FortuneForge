using System.Collections;
using System.Security.Cryptography;
using System.Text;
using FortuneForge.Games.Cards;
using FortuneForge.Games.VideoPoker;
using FortuneForge.Server.Accounts.Models;
using Google.Cloud.Firestore;

namespace FortuneForge.Server.Cards.VideoPoker;

internal sealed class VideoPokerFirestoreStore(FirestoreDb database) : IVideoPokerStore
{
    private const string SlotsCreditsCurrencyId = "slotsCredits";
    private const string AvailableFractionalCentsField = "availableFractionalCents";

    public Task<VideoPokerStoreResult> StartAsync(
        string userId,
        string idempotencyKey,
        int coinsWagered,
        int handCount,
        IReadOnlyList<IReadOnlyList<PlayingCard>> shuffledDecks,
        DateTimeOffset nowUtc,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(shuffledDecks);
        var roundId = CreateLookupKey($"{userId}\n{idempotencyKey}");
        var wagerCents = VideoPokerMoney.WagerCents(coinsWagered, handCount);
        var roundReference = RoundDocument(roundId);
        var balanceReference = BalanceDocument(userId);
        var wagerReference = BalanceTransactionDocument($"video-poker-{roundId}-wager");
        var dealtEventReference = RoundEventDocument(roundId, "deal");

        return database.RunTransactionAsync(async transaction =>
        {
            var snapshots = await Task.WhenAll(
                transaction.GetSnapshotAsync(roundReference, cancellationToken),
                transaction.GetSnapshotAsync(balanceReference, cancellationToken),
                transaction.GetSnapshotAsync(wagerReference, cancellationToken),
                transaction.GetSnapshotAsync(dealtEventReference, cancellationToken));
            var roundSnapshot = snapshots[0];
            var balanceCents = ReadBalanceCents(snapshots[1]);
            if (roundSnapshot.Exists)
            {
                var existing = ReadRound(roundSnapshot);
                if (existing.UserId != userId || existing.Round.CoinsWagered != coinsWagered || existing.Round.HandCount != handCount)
                    throw new VideoPokerRoundConflictException("This Idempotency-Key was already used with a different Video Poker request.");
                if (!snapshots[2].Exists || !snapshots[3].Exists)
                    throw new InvalidOperationException("A recorded Video Poker round is missing its deal ledger.");
                return new VideoPokerStoreResult(existing.RoundId, existing.Round, balanceCents);
            }
            if (snapshots[2].Exists || snapshots[3].Exists)
                throw new InvalidOperationException("A Video Poker deal ledger already exists without its round.");
            if (balanceCents < wagerCents)
                throw new VideoPokerInsufficientCreditsException(balanceCents, wagerCents);

            var round = VideoPokerRoundEngine.Deal(shuffledDecks, coinsWagered);
            var remainingCents = checked(balanceCents - wagerCents);
            transaction.Create(roundReference, RoundData(roundId, userId, round, idempotencyKey, nowUtc));
            transaction.Set(balanceReference, BalanceUpdate(remainingCents, nowUtc), SetOptions.MergeAll);
            transaction.Create(wagerReference, BalanceTransactionData(
                wagerReference.Id, userId, -wagerCents, remainingCents, "video-poker-wager", idempotencyKey, nowUtc));
            transaction.Create(dealtEventReference, RoundEventData(roundId, userId, "deal", idempotencyKey, nowUtc));
            return new VideoPokerStoreResult(roundId, round, remainingCents);
        }, cancellationToken: cancellationToken);
    }

    public async Task<VideoPokerStoreResult?> GetAsync(
        string userId,
        string roundId,
        CancellationToken cancellationToken)
    {
        var snapshots = await Task.WhenAll(
            RoundDocument(roundId).GetSnapshotAsync(cancellationToken),
            BalanceDocument(userId).GetSnapshotAsync(cancellationToken));
        if (!snapshots[0].Exists) return null;
        var stored = ReadRound(snapshots[0]);
        return stored.UserId == userId
            ? new VideoPokerStoreResult(stored.RoundId, stored.Round, ReadBalanceCents(snapshots[1]))
            : null;
    }

    public Task<VideoPokerStoreResult> DrawAsync(
        string userId,
        string roundId,
        string idempotencyKey,
        VideoPokerHeldCardPositions heldPositions,
        DateTimeOffset nowUtc,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(heldPositions);
        var roundReference = RoundDocument(roundId);
        var balanceReference = BalanceDocument(userId);
        var payoutReference = BalanceTransactionDocument($"video-poker-{roundId}-payout");
        var drawnEventReference = RoundEventDocument(roundId, "draw");

        return database.RunTransactionAsync(async transaction =>
        {
            var snapshots = await Task.WhenAll(
                transaction.GetSnapshotAsync(roundReference, cancellationToken),
                transaction.GetSnapshotAsync(balanceReference, cancellationToken),
                transaction.GetSnapshotAsync(payoutReference, cancellationToken),
                transaction.GetSnapshotAsync(drawnEventReference, cancellationToken));
            if (!snapshots[0].Exists) throw new VideoPokerRoundNotFoundException();
            var stored = ReadRound(snapshots[0]);
            if (stored.UserId != userId) throw new VideoPokerRoundNotFoundException();
            var balanceCents = ReadBalanceCents(snapshots[1]);
            if (stored.Round.Status == VideoPokerRoundStatus.Completed)
            {
                var completedHolds = stored.Round.HeldCardPositions!.Positions;
                if (!completedHolds.SequenceEqual(heldPositions.Positions))
                    throw new VideoPokerRoundConflictException("This Video Poker round was already drawn with different held cards.");
                if (!snapshots[3].Exists)
                    throw new InvalidOperationException("A completed Video Poker round is missing its draw event.");
                var creditsWon = stored.Round.Results.Sum(result => result.PaytableOutcome.CreditsWon);
                if ((creditsWon > 0) != snapshots[2].Exists)
                    throw new InvalidOperationException("A completed Video Poker round is missing its payout ledger.");
                return new VideoPokerStoreResult(stored.RoundId, stored.Round, balanceCents);
            }
            if (snapshots[2].Exists || snapshots[3].Exists)
                throw new InvalidOperationException("A Video Poker draw ledger already exists without a completed round.");

            var completed = VideoPokerRoundEngine.Draw(stored.Round, heldPositions);
            var payoutCents = checked(completed.Results.Sum(result => result.PaytableOutcome.CreditsWon) * VideoPokerMoney.CoinValueCents);
            var finalBalanceCents = checked(balanceCents + payoutCents);
            transaction.Update(roundReference, CompletedRoundData(completed, idempotencyKey, nowUtc));
            transaction.Create(drawnEventReference, RoundEventData(roundId, userId, "draw", idempotencyKey, nowUtc));
            if (payoutCents > 0)
            {
                transaction.Set(balanceReference, BalanceUpdate(finalBalanceCents, nowUtc), SetOptions.MergeAll);
                transaction.Create(payoutReference, BalanceTransactionData(
                    payoutReference.Id, userId, payoutCents, finalBalanceCents, "video-poker-payout", $"{idempotencyKey}-payout", nowUtc));
            }
            return new VideoPokerStoreResult(stored.RoundId, completed, finalBalanceCents);
        }, cancellationToken: cancellationToken);
    }

    internal static string CreateLookupKey(string value) =>
        Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(value)));

    private DocumentReference RoundDocument(string roundId) => database.Collection("videoPokerRounds").Document(roundId);
    private DocumentReference RoundEventDocument(string roundId, string eventName) =>
        database.Collection("videoPokerRoundEvents").Document($"{roundId}-{eventName}");
    private DocumentReference BalanceDocument(string userId) =>
        database.Collection("userBalances").Document($"{userId}_{SlotsCreditsCurrencyId}");
    private DocumentReference BalanceTransactionDocument(string transactionId) =>
        database.Collection("balanceTransactions").Document(transactionId);

    private static Dictionary<string, object> RoundData(
        string roundId,
        string userId,
        VideoPokerRound round,
        string idempotencyKey,
        DateTimeOffset nowUtc) => new()
    {
        ["roundId"] = roundId,
        ["userId"] = userId,
        ["coinsWagered"] = (long)round.CoinsWagered,
        ["handCount"] = (long)round.HandCount,
        ["decks"] = round.RemainingDecks
            .SelectMany(deck => round.InitialDeal.Cards.Concat(deck))
            .Select(CardCode.Format)
            .ToArray(),
        ["status"] = "awaiting-draw",
        ["startIdempotencyKey"] = idempotencyKey,
        ["createdAt"] = Timestamp.FromDateTime(nowUtc.UtcDateTime),
        ["updatedAt"] = Timestamp.FromDateTime(nowUtc.UtcDateTime),
        ["schemaVersion"] = 2L,
    };

    private static Dictionary<string, object> CompletedRoundData(
        VideoPokerRound round,
        string idempotencyKey,
        DateTimeOffset nowUtc) => new()
    {
        ["status"] = "completed",
        ["heldPositions"] = round.HeldCardPositions!.Positions.Select(position => (long)position).ToArray(),
        ["drawIdempotencyKey"] = idempotencyKey,
        ["payoutCents"] = checked(round.Results.Sum(result => result.PaytableOutcome.CreditsWon) * VideoPokerMoney.CoinValueCents),
        ["handRank"] = VideoPokerService.HandRankName(round.Result!.HandRank),
        ["handRanks"] = round.Results.Select(result => VideoPokerService.HandRankName(result.HandRank)).ToArray(),
        ["handPayoutCents"] = round.Results
            .Select(result => checked((long)result.PaytableOutcome.CreditsWon * VideoPokerMoney.CoinValueCents))
            .ToArray(),
        ["updatedAt"] = Timestamp.FromDateTime(nowUtc.UtcDateTime),
    };

    private static Dictionary<string, object> RoundEventData(
        string roundId,
        string userId,
        string eventName,
        string idempotencyKey,
        DateTimeOffset nowUtc) => new()
    {
        ["roundId"] = roundId,
        ["userId"] = userId,
        ["event"] = eventName,
        ["idempotencyKey"] = idempotencyKey,
        ["createdAt"] = Timestamp.FromDateTime(nowUtc.UtcDateTime),
        ["schemaVersion"] = 1L,
    };

    private static Dictionary<string, object> BalanceUpdate(long balanceCents, DateTimeOffset nowUtc) => new()
    {
        ["available"] = balanceCents / RandMoney.CentsPerRand,
        [AvailableFractionalCentsField] = balanceCents % RandMoney.CentsPerRand,
        ["version"] = FieldValue.Increment(1),
        ["updatedAt"] = Timestamp.FromDateTime(nowUtc.UtcDateTime),
    };

    private static Dictionary<string, object> BalanceTransactionData(
        string transactionId,
        string userId,
        long amountCents,
        long balanceAfterCents,
        string type,
        string idempotencyKey,
        DateTimeOffset nowUtc) => new()
    {
        ["transactionId"] = transactionId,
        ["userId"] = userId,
        ["currencyId"] = SlotsCreditsCurrencyId,
        ["amount"] = (double)VideoPokerMoney.ToRand(amountCents),
        ["balanceAfter"] = (double)VideoPokerMoney.ToRand(balanceAfterCents),
        ["type"] = type,
        ["idempotencyKey"] = idempotencyKey,
        ["createdAt"] = Timestamp.FromDateTime(nowUtc.UtcDateTime),
    };

    private static StoredRound ReadRound(DocumentSnapshot snapshot)
    {
        if (!snapshot.Exists ||
            !snapshot.TryGetValue<string>("roundId", out var roundId) || roundId.Length != 64 ||
            !snapshot.TryGetValue<string>("userId", out var userId) || string.IsNullOrWhiteSpace(userId) ||
            !snapshot.TryGetValue<long>("coinsWagered", out var rawCoins) || rawCoins is < VideoPokerMoney.MinimumCoinsWagered or > VideoPokerMoney.MaximumCoinsWagered ||
            !snapshot.TryGetValue<string>("status", out var status) || status is not ("awaiting-draw" or "completed") ||
            !snapshot.TryGetValue<long>("schemaVersion", out var schemaVersion) || schemaVersion is not (1 or 2))
        {
            throw new InvalidOperationException("The Video Poker round is corrupt.");
        }
        IReadOnlyList<IReadOnlyList<PlayingCard>> decks;
        if (schemaVersion == 1)
        {
            decks = [ReadDeck(snapshot, "deck")];
        }
        else
        {
            if (!snapshot.TryGetValue<long>("handCount", out var rawHandCount) || rawHandCount is not (1 or 3 or 5))
                throw new InvalidOperationException("The Video Poker hand count is corrupt.");
            var flatDeck = ReadStringArray(snapshot, "decks").Select(CardCode.Parse).ToArray();
            if (flatDeck.Length != StandardDeck.Create().Count * rawHandCount)
                throw new InvalidOperationException("The Video Poker replacement decks are corrupt.");
            decks = Enumerable.Range(0, checked((int)rawHandCount))
                .Select(index => (IReadOnlyList<PlayingCard>)flatDeck.Skip(index * StandardDeck.Create().Count).Take(StandardDeck.Create().Count).ToArray())
                .ToArray();
            if (decks.Any(deck => !deck.ToHashSet().SetEquals(StandardDeck.Create())))
                throw new InvalidOperationException("The Video Poker replacement decks are corrupt.");
        }

        var dealt = VideoPokerRoundEngine.Deal(decks, checked((int)rawCoins));
        if (status == "awaiting-draw") return new StoredRound(roundId, userId, dealt);

        var holds = new VideoPokerHeldCardPositions(ReadLongArray(snapshot, "heldPositions").Select(position =>
            Enum.IsDefined((VideoPokerCardPosition)position)
                ? (VideoPokerCardPosition)position
                : throw new InvalidOperationException("The Video Poker held cards are corrupt.")));
        var completed = VideoPokerRoundEngine.Draw(dealt, holds);
        var expectedPayoutCents = checked(completed.Results.Sum(result => result.PaytableOutcome.CreditsWon) * VideoPokerMoney.CoinValueCents);
        if (!snapshot.TryGetValue<long>("payoutCents", out var storedPayoutCents) || storedPayoutCents != expectedPayoutCents ||
            !snapshot.TryGetValue<string>("handRank", out var storedHandRank) || storedHandRank != VideoPokerService.HandRankName(completed.Result!.HandRank) ||
            !snapshot.TryGetValue<string>("drawIdempotencyKey", out var drawIdempotencyKey) || string.IsNullOrWhiteSpace(drawIdempotencyKey))
        {
            throw new InvalidOperationException("The completed Video Poker round is corrupt.");
        }
        if (schemaVersion == 2)
        {
            var storedRanks = ReadStringArray(snapshot, "handRanks");
            var expectedRanks = completed.Results.Select(result => VideoPokerService.HandRankName(result.HandRank)).ToArray();
            var storedPayouts = ReadLongArray(snapshot, "handPayoutCents");
            var expectedPayouts = completed.Results
                .Select(result => checked((long)result.PaytableOutcome.CreditsWon * VideoPokerMoney.CoinValueCents))
                .ToArray();
            if (!storedRanks.SequenceEqual(expectedRanks) || !storedPayouts.SequenceEqual(expectedPayouts))
                throw new InvalidOperationException("The completed Video Poker hand results are corrupt.");
        }
        return new StoredRound(roundId, userId, completed);
    }

    private static IReadOnlyList<PlayingCard> ReadDeck(DocumentSnapshot snapshot, string field)
    {
        var deck = ReadStringArray(snapshot, field).Select(CardCode.Parse).ToArray();
        if (deck.Length != StandardDeck.Create().Count || !deck.ToHashSet().SetEquals(StandardDeck.Create()))
            throw new InvalidOperationException("The Video Poker round deck is corrupt.");
        return deck;
    }

    private static long ReadBalanceCents(DocumentSnapshot snapshot) => checked(
        ReadLong(snapshot, "available") * RandMoney.CentsPerRand +
        Math.Clamp(ReadLong(snapshot, AvailableFractionalCentsField), 0, RandMoney.CentsPerRand - 1));

    private static long ReadLong(DocumentSnapshot snapshot, string field) =>
        snapshot.Exists && snapshot.TryGetValue<long>(field, out var value) ? value : 0;

    private static IReadOnlyList<string> ReadStringArray(DocumentSnapshot snapshot, string field)
    {
        if (!snapshot.ToDictionary().TryGetValue(field, out var raw) || raw is not IEnumerable values) return [];
        return values.Cast<object?>().Select(value => value as string ?? string.Empty).ToArray();
    }

    private static IReadOnlyList<long> ReadLongArray(DocumentSnapshot snapshot, string field)
    {
        if (!snapshot.ToDictionary().TryGetValue(field, out var raw) || raw is not IEnumerable values) return [];
        try { return values.Cast<object?>().Select(value => Convert.ToInt64(value, System.Globalization.CultureInfo.InvariantCulture)).ToArray(); }
        catch (Exception exception) when (exception is FormatException or InvalidCastException or OverflowException)
        {
            throw new InvalidOperationException("The Video Poker held cards are corrupt.", exception);
        }
    }

    private sealed record StoredRound(string RoundId, string UserId, VideoPokerRound Round);
}
