using System.Collections;
using FortuneForge.Games.Cards;
using Google.Cloud.Firestore;

namespace FortuneForge.Games.DragonTiger.Server;

/// <summary>Read-only compatibility for historical rounds settled before signed wallet ownership.</summary>
public sealed class DragonTigerFirestoreStore(FirestoreDb database) : IDragonTigerStore
{
    private const string CurrencyId = "slotsCredits";
    public Task<DragonTigerStoreResult> StartAsync(string userId, string idempotencyKey, DragonTigerWagers wagers,
        IReadOnlyList<PlayingCard> shuffledShoe, DateTimeOffset nowUtc, CancellationToken cancellationToken) =>
        database.RunTransactionAsync(async transaction =>
        {
            var prior = await ReadOwned(transaction, userId, DragonTigerService.LookupKey(userId, idempotencyKey), cancellationToken)
                ?? throw new InvalidOperationException("New Dragon Tiger rounds require the signed wallet store.");
            if (prior.Round.Wagers != wagers) throw new DragonTigerRoundConflictException();
            return prior;
        }, cancellationToken: cancellationToken);

    public Task<DragonTigerStoreResult?> GetAsync(string userId, string roundId, CancellationToken cancellationToken) =>
        database.RunTransactionAsync(transaction => ReadOwned(transaction, userId, roundId, cancellationToken), cancellationToken: cancellationToken);

    public Task<DragonTigerStoreResult?> LatestAsync(string userId, CancellationToken cancellationToken) =>
        database.RunTransactionAsync(async transaction =>
        {
            var shoe = await transaction.GetSnapshotAsync(ShoeDocument(userId), cancellationToken);
            if (!shoe.Exists) return null;
            _ = ReadShoe(shoe, userId);
            return await ReadOwned(transaction, userId, RequiredString(shoe, "latestRoundId"), cancellationToken)
                ?? throw new InvalidOperationException("The Dragon Tiger latest-round record is missing.");
        }, cancellationToken: cancellationToken);

    private async Task<DragonTigerStoreResult?> ReadOwned(Transaction transaction, string user, string id, CancellationToken token)
    {
        var snapshots = await transaction.GetAllSnapshotsAsync([RoundDocument(id), LedgerDocument($"dragon-tiger-{id}-wager"),
            LedgerDocument($"dragon-tiger-{id}-payout"), EventDocument(id)], token);
        if (!snapshots[0].Exists || RequiredString(snapshots[0], "userId") != user) return null;
        var stored = ReadRound(snapshots[0]);
        var returned = TotalReturn(stored);
        var key = RequiredString(snapshots[0], "startIdempotencyKey");
        if (!snapshots[1].Exists || !snapshots[3].Exists || snapshots[2].Exists != (returned > 0))
            throw new InvalidOperationException("A recorded Dragon Tiger round is missing its ledger.");
        VerifyLedger(snapshots[1], user, -stored.Wagers.TotalCents, "dragon-tiger-wager", key, RequiredLong(snapshots[0], "balanceAfterWagerCents"));
        if (returned > 0) VerifyLedger(snapshots[2], user, returned, "dragon-tiger-payout", key + "-payout", RequiredLong(snapshots[0], "balanceAfterSettlementCents"));
        if (RequiredString(snapshots[3], "roundId") != id || RequiredString(snapshots[3], "userId") != user ||
            RequiredString(snapshots[3], "event") != "settled" || RequiredString(snapshots[3], "idempotencyKey") != key || RequiredLong(snapshots[3], "schemaVersion") != 1)
            throw new InvalidOperationException("The Dragon Tiger event is corrupt.");
        return new(stored, 0);
    }
    internal async Task EnsureNoOrphansAsync(Transaction transaction, string id, CancellationToken token)
    {
        var proof = await transaction.GetAllSnapshotsAsync([LedgerDocument($"dragon-tiger-{id}-wager"),
            LedgerDocument($"dragon-tiger-{id}-payout"), EventDocument(id)], token);
        if (proof.Any(snapshot => snapshot.Exists))
            throw new FortuneForge.Games.Abstractions.Wallet.GameWalletUnavailableException();
    }
    private DocumentReference RoundDocument(string id) => database.Collection("dragonTigerRounds").Document(id);
    private DocumentReference ShoeDocument(string userId) => database.Collection("dragonTigerShoes").Document(DragonTigerService.Hash(userId));
    private DocumentReference EventDocument(string id) => database.Collection("dragonTigerRoundEvents").Document(id + "-settled");
    private DocumentReference LedgerDocument(string id) => database.Collection("balanceTransactions").Document(id);
    private static long TotalReturn(DragonTigerStoredRound stored) => DragonTigerRules.Settle(stored.Wagers, stored.Round).Sum(value => value.TotalReturnCents);
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
    private static void VerifyLedger(DocumentSnapshot snapshot, string userId, long amountCents, string type, string key, long expectedBalanceAfterCents)
    {
        if (RequiredString(snapshot, "transactionId") != snapshot.Id || RequiredString(snapshot, "idempotencyKey") != key ||
            RequiredString(snapshot, "userId") != userId || RequiredString(snapshot, "currencyId") != CurrencyId ||
            RequiredString(snapshot, "type") != type || !snapshot.TryGetValue<double>("amount", out var amount) ||
            amount != (double)DragonTigerMoney.ToRand(amountCents) || !snapshot.TryGetValue<double>("balanceAfter", out var after) ||
            after != (double)DragonTigerMoney.ToRand(expectedBalanceAfterCents))
            throw new InvalidOperationException("The Dragon Tiger wallet ledger is corrupt.");
    }
    private static long RequiredLong(DocumentSnapshot snapshot, string field) => snapshot.TryGetValue<long>(field, out var value)
        ? value : throw new InvalidOperationException($"The Dragon Tiger record is missing {field}.");
    private static string RequiredString(DocumentSnapshot snapshot, string field) => snapshot.TryGetValue<string>(field, out var value) && !string.IsNullOrWhiteSpace(value)
        ? value : throw new InvalidOperationException($"The Dragon Tiger record is missing {field}.");
    private sealed record ShoeState(IReadOnlyList<PlayingCard> Cards, int NextIndex);
}
