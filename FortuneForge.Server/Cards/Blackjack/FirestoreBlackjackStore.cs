using System.Security.Cryptography;
using System.Text;
using Google.Cloud.Firestore;

namespace FortuneForge.Server.Cards.Blackjack;

internal sealed class FirestoreBlackjackStore(FirestoreDb database) : IBlackjackStore
{
    private const string SlotsCreditsCurrencyId = "slotsCredits";
    private const string AvailableFractionalCentsField = "availableFractionalCents";

    public Task<BlackjackStoreResult> StartAsync(
        string userId,
        string idempotencyKey,
        long wagerCents,
        IReadOnlyList<string> shuffledDeck,
        DateTime nowUtc,
        CancellationToken cancellationToken)
    {
        var gameId = CreateLookupKey($"{userId}\n{idempotencyKey}");
        var gameReference = GameDocument(gameId);
        var balanceReference = BalanceDocument(userId);
        var wagerReference = BalanceTransactionDocument($"{gameId}-wager");
        var payoutReference = BalanceTransactionDocument($"{gameId}-payout");

        return database.RunTransactionAsync(
            async transaction =>
            {
                var gameSnapshot = await transaction.GetSnapshotAsync(gameReference, cancellationToken);
                var balanceSnapshot = await transaction.GetSnapshotAsync(balanceReference, cancellationToken);
                var availableCents = ReadBalanceCents(balanceSnapshot);
                if (gameSnapshot.Exists)
                {
                    var existing = ReadGame(gameSnapshot);
                    if (!string.Equals(existing.UserId, userId, StringComparison.Ordinal) ||
                        existing.WagerCents != wagerCents)
                    {
                        throw new BlackjackConflictException(
                            "This Idempotency-Key was already used with a different Blackjack request.");
                    }
                    return new BlackjackStoreResult(existing, availableCents);
                }
                if (availableCents < wagerCents)
                {
                    throw new BlackjackInsufficientCreditsException(availableCents, wagerCents);
                }

                var game = BlackjackRules.Deal(
                    gameId,
                    userId,
                    wagerCents,
                    shuffledDeck,
                    nowUtc);
                var afterWagerCents = checked(availableCents - wagerCents);
                var finalBalanceCents = checked(afterWagerCents + game.PayoutCents);

                transaction.Create(gameReference, GameData(game, idempotencyKey));
                transaction.Set(
                    balanceReference,
                    BalanceUpdate(finalBalanceCents, nowUtc),
                    SetOptions.MergeAll);
                transaction.Create(
                    wagerReference,
                    BalanceTransactionData(
                        wagerReference.Id,
                        userId,
                        -wagerCents,
                        afterWagerCents,
                        "blackjack-wager",
                        idempotencyKey,
                        nowUtc));
                if (game.PayoutCents > 0)
                {
                    transaction.Create(
                        payoutReference,
                        BalanceTransactionData(
                            payoutReference.Id,
                            userId,
                            game.PayoutCents,
                            finalBalanceCents,
                            "blackjack-payout",
                            $"{idempotencyKey}-payout",
                            nowUtc));
                }
                return new BlackjackStoreResult(game, finalBalanceCents);
            },
            cancellationToken: cancellationToken);
    }

    public async Task<BlackjackStoreResult?> GetAsync(
        string userId,
        string gameId,
        CancellationToken cancellationToken)
    {
        var snapshots = await Task.WhenAll(
            GameDocument(gameId).GetSnapshotAsync(cancellationToken),
            BalanceDocument(userId).GetSnapshotAsync(cancellationToken));
        if (!snapshots[0].Exists)
        {
            return null;
        }
        var game = ReadGame(snapshots[0]);
        if (!string.Equals(game.UserId, userId, StringComparison.Ordinal))
        {
            return null;
        }
        return new BlackjackStoreResult(game, ReadBalanceCents(snapshots[1]));
    }

    public Task<BlackjackStoreResult> ActAsync(
        string userId,
        string gameId,
        string idempotencyKey,
        int expectedVersion,
        string action,
        DateTime nowUtc,
        CancellationToken cancellationToken)
    {
        var normalizedAction = action.Trim().ToLowerInvariant();
        var gameReference = GameDocument(gameId);
        var actionReference = ActionDocument(userId, idempotencyKey);
        var balanceReference = BalanceDocument(userId);
        var doubleReference = BalanceTransactionDocument($"{gameId}-double");
        var payoutReference = BalanceTransactionDocument($"{gameId}-payout");

        return database.RunTransactionAsync(
            async transaction =>
            {
                var actionSnapshot = await transaction.GetSnapshotAsync(actionReference, cancellationToken);
                var gameSnapshot = await transaction.GetSnapshotAsync(gameReference, cancellationToken);
                var balanceSnapshot = await transaction.GetSnapshotAsync(balanceReference, cancellationToken);
                if (!gameSnapshot.Exists)
                {
                    throw new BlackjackNotFoundException();
                }
                var game = ReadGame(gameSnapshot);
                if (!string.Equals(game.UserId, userId, StringComparison.Ordinal))
                {
                    throw new BlackjackNotFoundException();
                }
                var availableCents = ReadBalanceCents(balanceSnapshot);
                if (actionSnapshot.Exists)
                {
                    var storedGameId = ReadString(actionSnapshot, "gameId");
                    var storedAction = ReadString(actionSnapshot, "action");
                    if (!string.Equals(storedGameId, gameId, StringComparison.Ordinal) ||
                        !string.Equals(storedAction, normalizedAction, StringComparison.Ordinal))
                    {
                        throw new BlackjackConflictException(
                            "This Idempotency-Key was already used with a different Blackjack action.");
                    }
                    return new BlackjackStoreResult(game, availableCents);
                }
                if (game.Version != expectedVersion)
                {
                    throw new BlackjackConflictException(
                        "The Blackjack hand changed. Reload it before choosing another action.");
                }

                var isDouble = normalizedAction == BlackjackActions.Double;
                if (isDouble && game.PlayerCards.Count != 2)
                {
                    throw new BlackjackConflictException("Double is available only before the first hit.");
                }
                if (isDouble && availableCents < game.WagerCents)
                {
                    throw new BlackjackInsufficientCreditsException(availableCents, game.WagerCents);
                }

                var afterActionChargeCents = isDouble
                    ? checked(availableCents - game.WagerCents)
                    : availableCents;
                var updated = BlackjackRules.ApplyAction(game, normalizedAction, nowUtc);
                var payoutCents = updated.Status == BlackjackStatuses.Completed
                    ? updated.PayoutCents
                    : 0;
                var finalBalanceCents = checked(afterActionChargeCents + payoutCents);

                transaction.Update(gameReference, GameUpdate(updated));
                transaction.Create(actionReference, new Dictionary<string, object>
                {
                    ["userId"] = userId,
                    ["gameId"] = gameId,
                    ["action"] = normalizedAction,
                    ["expectedVersion"] = expectedVersion,
                    ["resultVersion"] = updated.Version,
                    ["createdAt"] = Timestamp.FromDateTime(nowUtc)
                });
                if (isDouble)
                {
                    transaction.Create(
                        doubleReference,
                        BalanceTransactionData(
                            doubleReference.Id,
                            userId,
                            -game.WagerCents,
                            afterActionChargeCents,
                            "blackjack-double",
                            idempotencyKey,
                            nowUtc));
                }
                if (payoutCents > 0)
                {
                    transaction.Create(
                        payoutReference,
                        BalanceTransactionData(
                            payoutReference.Id,
                            userId,
                            payoutCents,
                            finalBalanceCents,
                            "blackjack-payout",
                            $"{idempotencyKey}-payout",
                            nowUtc));
                }
                if (isDouble || payoutCents > 0)
                {
                    transaction.Set(
                        balanceReference,
                        BalanceUpdate(finalBalanceCents, nowUtc),
                        SetOptions.MergeAll);
                }
                return new BlackjackStoreResult(updated, finalBalanceCents);
            },
            cancellationToken: cancellationToken);
    }

    internal static string CreateLookupKey(string value)
    {
        var digest = SHA256.HashData(Encoding.UTF8.GetBytes(value));
        return Convert.ToHexStringLower(digest);
    }

    private DocumentReference GameDocument(string gameId) =>
        database.Collection("blackjackGames").Document(gameId);

    private DocumentReference ActionDocument(string userId, string idempotencyKey) =>
        database.Collection("blackjackActions").Document(CreateLookupKey($"{userId}\n{idempotencyKey}"));

    private DocumentReference BalanceDocument(string userId) =>
        database.Collection("userBalances").Document($"{userId}_{SlotsCreditsCurrencyId}");

    private DocumentReference BalanceTransactionDocument(string transactionId) =>
        database.Collection("balanceTransactions").Document(transactionId);

    private static Dictionary<string, object> GameData(
        BlackjackGame game,
        string idempotencyKey)
    {
        var data = GameUpdate(game);
        data["gameId"] = game.GameId;
        data["userId"] = game.UserId;
        data["wagerCents"] = game.WagerCents;
        data["deck"] = game.Deck.ToArray();
        data["startIdempotencyKey"] = idempotencyKey;
        data["createdAt"] = Timestamp.FromDateTime(game.CreatedAtUtc);
        data["schemaVersion"] = 1L;
        return data;
    }

    private static Dictionary<string, object> GameUpdate(BlackjackGame game) => new()
    {
        ["totalWagerCents"] = game.TotalWagerCents,
        ["payoutCents"] = game.PayoutCents,
        ["status"] = game.Status,
        ["outcome"] = game.Outcome ?? string.Empty,
        ["nextCardIndex"] = game.NextCardIndex,
        ["playerCards"] = game.PlayerCards.ToArray(),
        ["dealerCards"] = game.DealerCards.ToArray(),
        ["version"] = game.Version,
        ["updatedAt"] = Timestamp.FromDateTime(game.UpdatedAtUtc)
    };

    private static BlackjackGame ReadGame(DocumentSnapshot snapshot) => new(
        ReadString(snapshot, "gameId"),
        ReadString(snapshot, "userId"),
        ReadLong(snapshot, "wagerCents"),
        ReadLong(snapshot, "totalWagerCents"),
        ReadLong(snapshot, "payoutCents"),
        ReadString(snapshot, "status"),
        EmptyToNull(ReadString(snapshot, "outcome")),
        ReadStringArray(snapshot, "deck"),
        checked((int)ReadLong(snapshot, "nextCardIndex")),
        ReadStringArray(snapshot, "playerCards"),
        ReadStringArray(snapshot, "dealerCards"),
        checked((int)ReadLong(snapshot, "version")),
        ReadTimestamp(snapshot, "createdAt"),
        ReadTimestamp(snapshot, "updatedAt"));

    private static Dictionary<string, object> BalanceUpdate(long balanceCents, DateTime nowUtc) => new()
    {
        ["available"] = balanceCents / BlackjackMoney.CentsPerRand,
        [AvailableFractionalCentsField] = balanceCents % BlackjackMoney.CentsPerRand,
        ["version"] = FieldValue.Increment(1),
        ["updatedAt"] = Timestamp.FromDateTime(nowUtc)
    };

    private static Dictionary<string, object> BalanceTransactionData(
        string transactionId,
        string userId,
        long amountCents,
        long balanceAfterCents,
        string type,
        string idempotencyKey,
        DateTime nowUtc) => new()
    {
        ["transactionId"] = transactionId,
        ["userId"] = userId,
        ["currencyId"] = SlotsCreditsCurrencyId,
        ["amount"] = (double)BlackjackMoney.ToRand(amountCents),
        ["balanceAfter"] = (double)BlackjackMoney.ToRand(balanceAfterCents),
        ["type"] = type,
        ["idempotencyKey"] = idempotencyKey,
        ["createdAt"] = Timestamp.FromDateTime(nowUtc)
    };

    private static long ReadBalanceCents(DocumentSnapshot snapshot) => checked(
        ReadLong(snapshot, "available") * BlackjackMoney.CentsPerRand +
        Math.Clamp(ReadLong(snapshot, AvailableFractionalCentsField), 0, 99));

    private static long ReadLong(DocumentSnapshot snapshot, string field) =>
        snapshot.Exists && snapshot.TryGetValue<long>(field, out var value) ? value : 0;

    private static string ReadString(DocumentSnapshot snapshot, string field) =>
        snapshot.Exists && snapshot.TryGetValue<string>(field, out var value)
            ? value
            : string.Empty;

    private static IReadOnlyList<string> ReadStringArray(DocumentSnapshot snapshot, string field)
    {
        var values = snapshot.ToDictionary();
        if (!values.TryGetValue(field, out var raw) || raw is not IEnumerable<object> items)
        {
            return [];
        }
        return items.Select(item => item as string ?? string.Empty).ToArray();
    }

    private static DateTime ReadTimestamp(DocumentSnapshot snapshot, string field) =>
        snapshot.Exists && snapshot.TryGetValue<Timestamp>(field, out var value)
            ? value.ToDateTime()
            : DateTime.UnixEpoch;

    private static string? EmptyToNull(string value) =>
        string.IsNullOrEmpty(value) ? null : value;
}
