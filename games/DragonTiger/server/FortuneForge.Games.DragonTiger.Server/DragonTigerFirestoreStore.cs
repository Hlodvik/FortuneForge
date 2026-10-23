using System.Collections;
using FortuneForge.Games.Cards;
using Google.Cloud.Firestore;

namespace FortuneForge.Games.DragonTiger.Server;

/// <summary>One Firestore transaction commits the wager, outcome, payout, wallet, event, and shoe position.</summary>
public sealed class DragonTigerFirestoreStore(FirestoreDb database) : IDragonTigerStore
{
    private const string CurrencyId = "slotsCredits";
    public Task<DragonTigerStoreResult> StartAsync(string userId, string idempotencyKey, DragonTigerWagers wagers,
        IReadOnlyList<PlayingCard> shuffledShoe, DateTimeOffset nowUtc, CancellationToken cancellationToken)
    {
        DragonTigerMoney.Validate(wagers);
        DragonTigerShoe.Validate(shuffledShoe);
        var roundId = DragonTigerService.LookupKey(userId, idempotencyKey);
        var roundReference = RoundDocument(roundId);
        var balanceReference = BalanceDocument(userId);
        var wagerReference = LedgerDocument($"dragon-tiger-{roundId}-wager");
        var payoutReference = LedgerDocument($"dragon-tiger-{roundId}-payout");
        var eventReference = EventDocument(roundId);
        var shoeReference = ShoeDocument(userId);
        return database.RunTransactionAsync(async transaction =>
        {
            var snapshots = await transaction.GetAllSnapshotsAsync(
                [roundReference, balanceReference, wagerReference, payoutReference, eventReference, shoeReference], cancellationToken);
            var balanceCents = ReadBalanceCents(snapshots[1]);
            if (snapshots[0].Exists)
            {
                var existing = ReadRound(snapshots[0]);
                if (existing.UserId != userId || existing.Wagers != wagers) throw new DragonTigerRoundConflictException();
                var returned = TotalReturn(existing);
                if (!snapshots[2].Exists || !snapshots[4].Exists || snapshots[3].Exists != (returned > 0))
                    throw new InvalidOperationException("A recorded Dragon Tiger round is missing its ledger.");
                VerifyLedger(snapshots[2], userId, -wagers.TotalCents, "dragon-tiger-wager", idempotencyKey, RequiredLong(snapshots[0], "balanceAfterWagerCents"));
                if (returned > 0) VerifyLedger(snapshots[3], userId, returned, "dragon-tiger-payout", $"{idempotencyKey}-payout", RequiredLong(snapshots[0], "balanceAfterSettlementCents"));
                if (RequiredString(snapshots[4], "roundId") != roundId || RequiredString(snapshots[4], "userId") != userId ||
                    RequiredString(snapshots[4], "event") != "settled" || RequiredString(snapshots[4], "idempotencyKey") != idempotencyKey || RequiredLong(snapshots[4], "schemaVersion") != 1)
                    throw new InvalidOperationException("The Dragon Tiger event is corrupt.");
                return new DragonTigerStoreResult(existing, balanceCents);
            }
            if (snapshots[2].Exists || snapshots[3].Exists || snapshots[4].Exists)
                throw new InvalidOperationException("A Dragon Tiger ledger exists without its round.");
            if (balanceCents < wagers.TotalCents) throw new DragonTigerInsufficientCreditsException(balanceCents, wagers.TotalCents);
            var shoe = snapshots[5].Exists ? ReadShoe(snapshots[5], userId) : new ShoeState(shuffledShoe, 0);
            if (shoe.NextIndex >= DragonTigerRules.ReshuffleAt) shoe = new(shuffledShoe, 0);
            var round = DragonTigerRules.Deal(shoe.Cards.Skip(shoe.NextIndex).Take(2).ToArray());
            var nextIndex = shoe.NextIndex + 2;
            var stored = new DragonTigerStoredRound(roundId, userId, wagers, round, nextIndex, DragonTigerRules.ShoeSize - nextIndex);
            var totalReturn = TotalReturn(stored);
            var afterWager = checked(balanceCents - wagers.TotalCents);
            var finalBalance = checked(afterWager + totalReturn);
            transaction.Create(roundReference, RoundData(stored, idempotencyKey, nowUtc, afterWager, finalBalance));
            transaction.Set(shoeReference, new Dictionary<string, object>
            {
                ["userId"] = userId, ["cards"] = shoe.Cards.Select(CardCode.Format).ToArray(),
                ["nextIndex"] = (long)nextIndex, ["latestRoundId"] = roundId,
                ["updatedAt"] = Timestamp.FromDateTime(nowUtc.UtcDateTime), ["schemaVersion"] = 1L,
            });
            transaction.Set(balanceReference, new Dictionary<string, object>
            {
                ["available"] = finalBalance / 100, ["availableFractionalCents"] = finalBalance % 100,
                ["version"] = FieldValue.Increment(1), ["updatedAt"] = Timestamp.FromDateTime(nowUtc.UtcDateTime),
            }, SetOptions.MergeAll);
            transaction.Create(wagerReference, LedgerData(wagerReference.Id, userId, -wagers.TotalCents,
                afterWager, "dragon-tiger-wager", idempotencyKey, nowUtc));
            if (totalReturn > 0) transaction.Create(payoutReference, LedgerData(payoutReference.Id, userId, totalReturn,
                finalBalance, "dragon-tiger-payout", $"{idempotencyKey}-payout", nowUtc));
            transaction.Create(eventReference, new Dictionary<string, object>
            {
                ["roundId"] = roundId, ["userId"] = userId, ["event"] = "settled",
                ["idempotencyKey"] = idempotencyKey, ["createdAt"] = Timestamp.FromDateTime(nowUtc.UtcDateTime), ["schemaVersion"] = 1L,
            });
            return new DragonTigerStoreResult(stored, finalBalance);
        }, options: TransactionOptions.ForMaxAttempts(10), cancellationToken: cancellationToken);
    }

    public Task<DragonTigerStoreResult?> GetAsync(string userId, string roundId, CancellationToken cancellationToken) =>
        database.RunTransactionAsync(async transaction =>
        {
            var snapshots = await transaction.GetAllSnapshotsAsync([RoundDocument(roundId), BalanceDocument(userId)], cancellationToken);
            if (!snapshots[0].Exists) return null;
            var stored = ReadRound(snapshots[0]);
            return stored.UserId == userId ? new DragonTigerStoreResult(stored, ReadBalanceCents(snapshots[1])) : null;
        }, cancellationToken: cancellationToken);

    public Task<DragonTigerStoreResult?> LatestAsync(string userId, CancellationToken cancellationToken) =>
        database.RunTransactionAsync(async transaction =>
        {
            var shoe = await transaction.GetSnapshotAsync(ShoeDocument(userId), cancellationToken);
            if (!shoe.Exists) return null;
            _ = ReadShoe(shoe, userId);
            var id = RequiredString(shoe, "latestRoundId");
            var snapshots = await transaction.GetAllSnapshotsAsync([RoundDocument(id), BalanceDocument(userId)], cancellationToken);
            var stored = ReadRound(snapshots[0]);
            if (stored.UserId != userId) throw new InvalidOperationException("The Dragon Tiger latest-round record is corrupt.");
            return new DragonTigerStoreResult(stored, ReadBalanceCents(snapshots[1]));
        }, cancellationToken: cancellationToken);

    private DocumentReference RoundDocument(string id) => database.Collection("dragonTigerRounds").Document(id);
    private DocumentReference ShoeDocument(string userId) => database.Collection("dragonTigerShoes").Document(DragonTigerService.Hash(userId));
    private DocumentReference EventDocument(string id) => database.Collection("dragonTigerRoundEvents").Document($"{id}-settled");
    private DocumentReference BalanceDocument(string userId) => database.Collection("userBalances").Document($"{userId}_{CurrencyId}");
    private DocumentReference LedgerDocument(string id) => database.Collection("balanceTransactions").Document(id);
    private static long TotalReturn(DragonTigerStoredRound stored) => DragonTigerRules.Settle(stored.Wagers, stored.Round).Sum(value => value.TotalReturnCents);
    private static Dictionary<string, object> RoundData(DragonTigerStoredRound stored, string key, DateTimeOffset now, long afterWagerCents, long finalBalanceCents) => new()
    {
        ["roundId"] = stored.RoundId, ["userId"] = stored.UserId,
        ["dragonStakeCents"] = stored.Wagers.DragonCents, ["tigerStakeCents"] = stored.Wagers.TigerCents,
        ["tieStakeCents"] = stored.Wagers.TieCents, ["totalStakeCents"] = stored.Wagers.TotalCents,
        ["dragonCard"] = CardCode.Format(stored.Round.DragonCard), ["tigerCard"] = CardCode.Format(stored.Round.TigerCard),
        ["outcome"] = DragonTigerService.Name(stored.Round.Outcome), ["totalReturnCents"] = TotalReturn(stored),
        ["profitCents"] = TotalReturn(stored) - stored.Wagers.TotalCents,
        ["balanceAfterWagerCents"] = afterWagerCents, ["balanceAfterSettlementCents"] = finalBalanceCents,
        ["shoeCardsUsed"] = (long)stored.ShoeCardsUsed, ["shoeCardsRemaining"] = (long)stored.ShoeCardsRemaining,
        ["startIdempotencyKey"] = key, ["createdAt"] = Timestamp.FromDateTime(now.UtcDateTime), ["schemaVersion"] = 1L,
    };
    private static DragonTigerStoredRound ReadRound(DocumentSnapshot snapshot)
    {
        try { return ReadRoundCore(snapshot); }
        catch (Exception exception) when (exception is ArgumentException or OverflowException)
        { throw new InvalidOperationException("The persisted Dragon Tiger round is corrupt.", exception); }
    }
    private static DragonTigerStoredRound ReadRoundCore(DocumentSnapshot snapshot)
    {
        if (!snapshot.Exists || RequiredLong(snapshot, "schemaVersion") != 1) throw new InvalidOperationException("The Dragon Tiger round is corrupt.");
        var id = RequiredString(snapshot, "roundId");
        var userId = RequiredString(snapshot, "userId");
        var key = RequiredString(snapshot, "startIdempotencyKey");
        if (DragonTigerService.LookupKey(userId, key) != id || snapshot.Id != id)
            throw new InvalidOperationException("The Dragon Tiger round identity is corrupt.");
        var wagers = new DragonTigerWagers(RequiredLong(snapshot, "dragonStakeCents"), RequiredLong(snapshot, "tigerStakeCents"), RequiredLong(snapshot, "tieStakeCents"));
        DragonTigerMoney.Validate(wagers);
        var round = DragonTigerRules.Deal([CardCode.Parse(RequiredString(snapshot, "dragonCard")), CardCode.Parse(RequiredString(snapshot, "tigerCard"))]);
        var used = RequiredLong(snapshot, "shoeCardsUsed");
        var remaining = RequiredLong(snapshot, "shoeCardsRemaining");
        if (used is < 2 or > DragonTigerRules.ReshuffleAt || used % 2 != 0 || used + remaining != DragonTigerRules.ShoeSize)
            throw new InvalidOperationException("The Dragon Tiger shoe position is corrupt.");
        var stored = new DragonTigerStoredRound(id, userId, wagers, round, checked((int)used), checked((int)remaining));
        var totalReturn = TotalReturn(stored);
        if (RequiredString(snapshot, "outcome") != DragonTigerService.Name(round.Outcome) ||
            RequiredLong(snapshot, "totalStakeCents") != wagers.TotalCents ||
            RequiredLong(snapshot, "totalReturnCents") != totalReturn || RequiredLong(snapshot, "profitCents") != totalReturn - wagers.TotalCents ||
            RequiredLong(snapshot, "balanceAfterWagerCents") < 0 ||
            RequiredLong(snapshot, "balanceAfterSettlementCents") != checked(RequiredLong(snapshot, "balanceAfterWagerCents") + totalReturn))
            throw new InvalidOperationException("The Dragon Tiger settlement is corrupt.");
        return stored;
    }
    private static ShoeState ReadShoe(DocumentSnapshot snapshot, string userId)
    {
        try { return ReadShoeCore(snapshot, userId); }
        catch (Exception exception) when (exception is ArgumentException or OverflowException)
        { throw new InvalidOperationException("The persisted Dragon Tiger shoe is corrupt.", exception); }
    }
    private static ShoeState ReadShoeCore(DocumentSnapshot snapshot, string userId)
    {
        if (RequiredString(snapshot, "userId") != userId || RequiredLong(snapshot, "schemaVersion") != 1)
            throw new InvalidOperationException("The Dragon Tiger shoe identity is corrupt.");
        var nextIndex = RequiredLong(snapshot, "nextIndex");
        if (nextIndex is < 0 or > DragonTigerRules.ReshuffleAt || nextIndex % 2 != 0)
            throw new InvalidOperationException("The Dragon Tiger shoe position is corrupt.");
        if (!snapshot.TryGetValue<object>("cards", out var value) || value is not IEnumerable values)
            throw new InvalidOperationException("The Dragon Tiger shoe cards are missing.");
        var cards = values.Cast<object>().Select(card => CardCode.Parse(card as string ?? throw new InvalidOperationException("Invalid shoe card."))).ToArray();
        DragonTigerShoe.Validate(cards);
        return new(cards, checked((int)nextIndex));
    }
    private static Dictionary<string, object> LedgerData(string id, string userId, long amountCents, long afterCents,
        string type, string key, DateTimeOffset now) => new()
    {
        ["transactionId"] = id, ["userId"] = userId, ["currencyId"] = CurrencyId,
        ["amount"] = (double)DragonTigerMoney.ToRand(amountCents), ["balanceAfter"] = (double)DragonTigerMoney.ToRand(afterCents),
        ["type"] = type, ["idempotencyKey"] = key, ["createdAt"] = Timestamp.FromDateTime(now.UtcDateTime),
    };
    private static void VerifyLedger(DocumentSnapshot snapshot, string userId, long amountCents, string type, string key, long expectedBalanceAfterCents)
    {
        if (RequiredString(snapshot, "transactionId") != snapshot.Id || RequiredString(snapshot, "idempotencyKey") != key ||
            RequiredString(snapshot, "userId") != userId || RequiredString(snapshot, "currencyId") != CurrencyId ||
            RequiredString(snapshot, "type") != type || !snapshot.TryGetValue<double>("amount", out var amount) ||
            amount != (double)DragonTigerMoney.ToRand(amountCents) || !snapshot.TryGetValue<double>("balanceAfter", out var after) ||
            after != (double)DragonTigerMoney.ToRand(expectedBalanceAfterCents))
            throw new InvalidOperationException("The Dragon Tiger wallet ledger is corrupt.");
    }
    private static long ReadBalanceCents(DocumentSnapshot snapshot)
    {
        if (!snapshot.Exists) return 0;
        var whole = snapshot.TryGetValue<long>("available", out var value) ? value
            : throw new InvalidOperationException("The account wallet balance is missing available.");
        var fractional = snapshot.TryGetValue<long>("availableFractionalCents", out var fraction) ? fraction : 0;
        if (whole < 0 || fractional is < 0 or > 99) throw new InvalidOperationException("The account wallet balance is corrupt.");
        return checked(whole * 100 + fractional);
    }
    private static long RequiredLong(DocumentSnapshot snapshot, string field) => snapshot.TryGetValue<long>(field, out var value)
        ? value : throw new InvalidOperationException($"The Dragon Tiger record is missing {field}.");
    private static string RequiredString(DocumentSnapshot snapshot, string field) => snapshot.TryGetValue<string>(field, out var value) && !string.IsNullOrWhiteSpace(value)
        ? value : throw new InvalidOperationException($"The Dragon Tiger record is missing {field}.");
    private sealed record ShoeState(IReadOnlyList<PlayingCard> Cards, int NextIndex);
}
