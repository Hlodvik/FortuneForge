using Google.Cloud.Firestore;
using FortuneForge.Games.Cards;

namespace FortuneForge.Games.AndarBahar.Server;

/// <summary>One transaction durably settles the round, wallet, ledger, shared history, and recovery pointer.</summary>
public sealed class AndarBaharFirestoreStore(FirestoreDb database) : IAndarBaharStore
{
    private const string Currency = "slotsCredits";
    public async Task<long> BalanceAsync(string userId, CancellationToken cancellationToken) =>
        ReadBalance(await BalanceDocument(userId).GetSnapshotAsync(cancellationToken));

    public Task<AndarBaharStoreResult> StartAsync(string userId, string key, AndarBaharSide side, long stakeCents,
        IReadOnlyList<PlayingCard> shuffledDeck, DateTimeOffset nowUtc, CancellationToken cancellationToken, bool allowNewRounds = true)
    {
        AndarBaharMoney.Validate(side, stakeCents);
        var id = AndarBaharService.LookupKey(userId, key);
        var roundRef = RoundDocument(id);
        var balanceRef = BalanceDocument(userId);
        var wagerRef = LedgerDocument(id, "wager");
        var payoutRef = LedgerDocument(id, "payout");
        var eventRef = EventDocument(id);
        var historyRef = HistoryDocument(id);
        return database.RunTransactionAsync(async transaction =>
        {
            var snapshots = await transaction.GetAllSnapshotsAsync([roundRef, balanceRef, wagerRef, payoutRef, eventRef, historyRef], cancellationToken);
            var balance = ReadBalance(snapshots[1]);
            if (snapshots[0].Exists)
            {
                var prior = ReadRound(snapshots[0]);
                if (prior.UserId != userId || prior.Side != side || prior.StakeCents != stakeCents) throw new AndarBaharRoundConflictException();
                VerifyRecords(prior, snapshots[0], snapshots[2], snapshots[3], snapshots[4], snapshots[5]);
                return new AndarBaharStoreResult(prior, balance);
            }
            if (snapshots[2].Exists || snapshots[3].Exists || snapshots[4].Exists || snapshots[5].Exists) throw new InvalidOperationException("An audit record exists without its Andar Bahar round.");
            if (!allowNewRounds) throw new AndarBaharDisabledException();
            if (balance < stakeCents) throw new AndarBaharInsufficientCreditsException(balance, stakeCents);
            var round = AndarBaharRules.Deal(shuffledDeck);
            var returned = AndarBaharRules.Settle(side, stakeCents, round).TotalReturnCents;
            var afterWager = checked(balance - stakeCents);
            var finalBalance = checked(afterWager + returned);
            var stored = new AndarBaharStoredRound(id, userId, key, side, stakeCents, round, afterWager, finalBalance, nowUtc);
            transaction.Create(roundRef, new Dictionary<string, object>
            {
                ["roundId"] = id, ["userId"] = userId, ["schemaVersion"] = 2L,
                ["recordJson"] = AndarBaharPersistence.Serialize(stored), ["createdAt"] = Timestamp.FromDateTime(nowUtc.UtcDateTime),
            });
            transaction.Set(balanceRef, new Dictionary<string, object>
            {
                ["available"] = finalBalance / 100, ["availableFractionalCents"] = finalBalance % 100,
                ["version"] = FieldValue.Increment(1), ["updatedAt"] = Timestamp.FromDateTime(nowUtc.UtcDateTime),
            }, SetOptions.MergeAll);
            transaction.Create(wagerRef, Ledger(wagerRef.Id, userId, id, -stakeCents, afterWager, "wager", key, nowUtc));
            if (returned > 0) transaction.Create(payoutRef, Ledger(payoutRef.Id, userId, id, returned, finalBalance, "payout", $"{key}-payout", nowUtc));
            transaction.Create(historyRef, History(historyRef.Id, stored));
            transaction.Create(eventRef, new Dictionary<string, object>
            {
                ["roundId"] = id, ["userId"] = userId, ["event"] = "settled", ["idempotencyKey"] = key,
                ["schemaVersion"] = 1L, ["createdAt"] = Timestamp.FromDateTime(nowUtc.UtcDateTime),
            });
            transaction.Set(LatestDocument(userId), new Dictionary<string, object>
            {
                ["userId"] = userId, ["roundId"] = id, ["schemaVersion"] = 1L, ["updatedAt"] = Timestamp.FromDateTime(nowUtc.UtcDateTime),
            });
            return new AndarBaharStoreResult(stored, finalBalance);
        }, options: TransactionOptions.ForMaxAttempts(10), cancellationToken: cancellationToken);
    }

    public Task<AndarBaharStoreResult?> GetAsync(string userId, string roundId, CancellationToken cancellationToken) =>
        database.RunTransactionAsync(async transaction => await ReadOwned(transaction, userId, roundId, cancellationToken), cancellationToken: cancellationToken);

    public Task<AndarBaharStoreResult?> LatestAsync(string userId, CancellationToken cancellationToken) =>
        database.RunTransactionAsync(async transaction =>
        {
            var latest = await transaction.GetSnapshotAsync(LatestDocument(userId), cancellationToken);
            if (!latest.Exists) return null;
            if (RequiredString(latest, "userId") != userId || RequiredLong(latest, "schemaVersion") != 1) throw new InvalidOperationException("The Andar Bahar recovery pointer is corrupt.");
            return await ReadOwned(transaction, userId, RequiredString(latest, "roundId"), cancellationToken)
                ?? throw new InvalidOperationException("The Andar Bahar recovery round is missing.");
        }, cancellationToken: cancellationToken);

    private async Task<AndarBaharStoreResult?> ReadOwned(Transaction transaction, string user, string id, CancellationToken token)
    {
        var snapshots = await transaction.GetAllSnapshotsAsync([RoundDocument(id), BalanceDocument(user), LedgerDocument(id, "wager"), LedgerDocument(id, "payout"), EventDocument(id), HistoryDocument(id)], token);
        if (!snapshots[0].Exists || RequiredString(snapshots[0], "userId") != user) return null;
        var stored = ReadRound(snapshots[0]);
        VerifyRecords(stored, snapshots[0], snapshots[2], snapshots[3], snapshots[4], snapshots[5]);
        return new(stored, ReadBalance(snapshots[1]));
    }
    private static AndarBaharStoredRound ReadRound(DocumentSnapshot snapshot)
    {
        var stored = AndarBaharPersistence.Deserialize(RequiredString(snapshot, "recordJson"));
        if (snapshot.Id != stored.RoundId || RequiredString(snapshot, "roundId") != stored.RoundId ||
            RequiredString(snapshot, "userId") != stored.UserId || RequiredLong(snapshot, "schemaVersion") is not (1 or 2))
            throw new InvalidOperationException("The Andar Bahar record identity is corrupt.");
        return stored;
    }
    private static void VerifyRecords(AndarBaharStoredRound stored, DocumentSnapshot round, DocumentSnapshot wager, DocumentSnapshot payout, DocumentSnapshot settled, DocumentSnapshot history)
    {
        // Schema 1 records predate shared history. Preserve their read/replay path;
        // every new schema 2 settlement must retain its linked ledger and history.
        var linkedHistory = RequiredLong(round, "schemaVersion") == 2;
        var returned = AndarBaharRules.Settle(stored.Side, stored.StakeCents, stored.Round).TotalReturnCents;
        if (!wager.Exists || !settled.Exists || payout.Exists != (returned > 0)) throw new InvalidOperationException("The Andar Bahar wallet ledger is incomplete.");
        VerifyLedger(wager, stored.UserId, stored.RoundId, -stored.StakeCents, stored.BalanceAfterWagerCents, "wager", stored.IdempotencyKey, linkedHistory);
        if (returned > 0) VerifyLedger(payout, stored.UserId, stored.RoundId, returned, stored.BalanceAfterSettlementCents, "payout", $"{stored.IdempotencyKey}-payout", linkedHistory);
        if (RequiredString(settled, "roundId") != stored.RoundId || RequiredString(settled, "userId") != stored.UserId ||
            RequiredString(settled, "idempotencyKey") != stored.IdempotencyKey || RequiredString(settled, "event") != "settled" || RequiredLong(settled, "schemaVersion") != 1)
            throw new InvalidOperationException("The Andar Bahar settlement event is corrupt.");
        if (linkedHistory || history.Exists) VerifyHistory(history, stored);
    }
    private static Dictionary<string, object> Ledger(string id, string user, string roundId, long cents, long after, string kind, string key, DateTimeOffset now) => new()
    {
        ["transactionId"] = id, ["userId"] = user, ["currencyId"] = Currency,
        ["roundId"] = roundId, ["gameId"] = "andar-bahar",
        ["amount"] = (double)AndarBaharMoney.ToRand(cents), ["balanceAfter"] = (double)AndarBaharMoney.ToRand(after),
        ["amountCents"] = cents, ["balanceAfterCents"] = after, ["type"] = $"andar-bahar-{kind}",
        ["idempotencyKey"] = key, ["createdAt"] = Timestamp.FromDateTime(now.UtcDateTime),
    };
    private static void VerifyLedger(DocumentSnapshot entry, string user, string roundId, long cents, long after, string kind, string key, bool linkedHistory)
    {
        if (RequiredString(entry, "transactionId") != entry.Id || RequiredString(entry, "userId") != user ||
            RequiredString(entry, "currencyId") != Currency || RequiredString(entry, "type") != $"andar-bahar-{kind}" ||
            RequiredString(entry, "idempotencyKey") != key || RequiredLong(entry, "amountCents") != cents || RequiredLong(entry, "balanceAfterCents") != after ||
            !entry.TryGetValue<double>("amount", out var amount) || amount != (double)AndarBaharMoney.ToRand(cents) ||
            !entry.TryGetValue<double>("balanceAfter", out var balance) || balance != (double)AndarBaharMoney.ToRand(after) ||
            ((linkedHistory || entry.ContainsField("roundId")) && RequiredString(entry, "roundId") != roundId) ||
            ((linkedHistory || entry.ContainsField("gameId")) && RequiredString(entry, "gameId") != "andar-bahar"))
            throw new InvalidOperationException("The Andar Bahar wallet ledger is corrupt.");
    }
    private static Dictionary<string, object> History(string id, AndarBaharStoredRound stored)
    {
        var settlement = AndarBaharRules.Settle(stored.Side, stored.StakeCents, stored.Round);
        // Firestore stores microseconds, whereas a .NET clock can include 100ns ticks.
        var created = stored.CreatedAtUtc.UtcDateTime;
        var timestamp = Timestamp.FromDateTime(new DateTime(created.Ticks - created.Ticks % 10, DateTimeKind.Utc));
        return new()
        {
            ["resultId"] = id, ["game"] = "andar-bahar", ["mode"] = "credit", ["userId"] = stored.UserId,
            ["matchId"] = stored.RoundId, ["wagerCents"] = stored.StakeCents,
            ["payoutCents"] = settlement.TotalReturnCents, ["netCents"] = settlement.ProfitCents,
            ["outcome"] = settlement.Disposition.ToString().ToLowerInvariant(),
            ["side"] = AndarBaharService.Name(stored.Side), ["winner"] = AndarBaharService.Name(stored.Round.Winner),
            ["settlementStatus"] = "paid", ["claimStatus"] = "completed",
            ["startedAt"] = timestamp, ["completedAt"] = timestamp, ["schemaVersion"] = 1L,
        };
    }
    private static void VerifyHistory(DocumentSnapshot history, AndarBaharStoredRound stored)
    {
        if (!history.Exists) throw new InvalidOperationException("The Andar Bahar shared history is missing.");
        foreach (var (field, value) in History(history.Id, stored))
        {
            if (!history.TryGetValue<object>(field, out var actual) || !Equals(actual, value))
                throw new InvalidOperationException("The Andar Bahar shared history is corrupt.");
        }
    }
    private static long ReadBalance(DocumentSnapshot snapshot)
    {
        if (!snapshot.Exists) return 0;
        var whole = RequiredLong(snapshot, "available");
        var fraction = snapshot.TryGetValue<long>("availableFractionalCents", out var value) ? value : 0;
        if (whole < 0 || fraction is < 0 or > 99) throw new InvalidOperationException("The account wallet balance is corrupt.");
        return checked(whole * 100 + fraction);
    }
    private DocumentReference RoundDocument(string id) => database.Collection("andarBaharRounds").Document(id);
    private DocumentReference LatestDocument(string user) => database.Collection("andarBaharAccounts").Document(AndarBaharService.Hash(user));
    private DocumentReference EventDocument(string id) => database.Collection("andarBaharRoundEvents").Document($"{id}-settled");
    private DocumentReference HistoryDocument(string id) => database.Collection("cardGameResults").Document($"andar-bahar-{id}");
    private DocumentReference BalanceDocument(string user) => database.Collection("userBalances").Document($"{user}_{Currency}");
    private DocumentReference LedgerDocument(string id, string kind) => database.Collection("balanceTransactions").Document($"andar-bahar-{id}-{kind}");
    private static string RequiredString(DocumentSnapshot snapshot, string field) => snapshot.TryGetValue<string>(field, out var value) && !string.IsNullOrWhiteSpace(value)
        ? value : throw new InvalidOperationException($"The Andar Bahar record is missing {field}.");
    private static long RequiredLong(DocumentSnapshot snapshot, string field) => snapshot.TryGetValue<long>(field, out var value)
        ? value : throw new InvalidOperationException($"The Andar Bahar record is missing {field}.");
}
