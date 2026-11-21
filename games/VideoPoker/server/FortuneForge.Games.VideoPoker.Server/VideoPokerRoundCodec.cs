using System.Collections;
using FortuneForge.Games.Cards;
using FortuneForge.Games.VideoPoker;
using Google.Cloud.Firestore;

namespace FortuneForge.Server.Cards.VideoPoker;

internal static class VideoPokerRoundCodec
{
    private static IEnumerable<IEnumerable<PlayingCard>> OrderedDecks(VideoPokerRound round) => round.RemainingDecks.Select(deck => round.InitialDeal.Cards.Concat(deck));
    internal static Dictionary<string, object> RoundData(
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
        ["decks"] = OrderedDecks(round)
            .SelectMany(deck => deck)
            .Select(CardCode.Format)
            .ToArray(),
        ["status"] = "awaiting-draw",
        ["startIdempotencyKey"] = idempotencyKey,
        ["createdAt"] = Timestamp.FromDateTime(nowUtc.UtcDateTime),
        ["updatedAt"] = Timestamp.FromDateTime(nowUtc.UtcDateTime),
        ["schemaVersion"] = 2L,
    };

    internal static Dictionary<string, object> CompletedRoundData(
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

    internal static StoredRound ReadRound(DocumentSnapshot snapshot)
    {
        if (!snapshot.Exists ||
            !snapshot.TryGetValue<string>("roundId", out var roundId) || roundId.Length != 64 || roundId != snapshot.Id ||
            !snapshot.TryGetValue<string>("userId", out var userId) || string.IsNullOrWhiteSpace(userId) ||
            !snapshot.TryGetValue<long>("coinsWagered", out var rawCoins) || rawCoins is < VideoPokerMoney.MinimumCoinsWagered or > VideoPokerMoney.MaximumCoinsWagered ||
            !snapshot.TryGetValue<string>("status", out var status) || status is not ("awaiting-draw" or "completed") ||
            !snapshot.TryGetValue<long>("schemaVersion", out var schemaVersion) || schemaVersion is not (1 or 2))
        {
            throw new InvalidOperationException("The Video Poker round is corrupt.");
        }
        var dealt = ReadDeal(snapshot, schemaVersion, rawCoins);
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

    internal static VideoPokerRound ReadOriginalDeal(DocumentSnapshot snapshot)
    {
        var stored = ReadRound(snapshot);
        return stored.Round.Status == VideoPokerRoundStatus.AwaitingDraw ? stored.Round
            : ReadDeal(snapshot, snapshot.GetValue<long>("schemaVersion"), stored.Round.CoinsWagered);
    }

    private static VideoPokerRound ReadDeal(DocumentSnapshot snapshot, long schemaVersion, long rawCoins)
    {
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

        return VideoPokerRoundEngine.Deal(decks, checked((int)rawCoins));
    }

    private static IReadOnlyList<PlayingCard> ReadDeck(DocumentSnapshot snapshot, string field)
    {
        var deck = ReadStringArray(snapshot, field).Select(CardCode.Parse).ToArray();
        if (deck.Length != StandardDeck.Create().Count || !deck.ToHashSet().SetEquals(StandardDeck.Create()))
            throw new InvalidOperationException("The Video Poker round deck is corrupt.");
        return deck;
    }

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

    internal sealed record StoredRound(string RoundId, string UserId, VideoPokerRound Round);
}
