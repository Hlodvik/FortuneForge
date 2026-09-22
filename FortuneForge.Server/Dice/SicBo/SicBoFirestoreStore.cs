using System.Collections;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using FortuneForge.Games.SicBo;
using FortuneForge.Server.Accounts.Models;
using Google.Cloud.Firestore;

namespace FortuneForge.Server.Dice.SicBo;

internal sealed class SicBoFirestoreStore(FirestoreDb database) : ISicBoStore
{
    private const string CurrencyId = "slotsCredits";
    private const string FractionField = "availableFractionalCents";

    public Task<SicBoStoreResult> StartAsync(string userId, string idempotencyKey, IReadOnlyList<SicBoStoredBet> bets, SicBoRoll roll, DateTimeOffset nowUtc, CancellationToken cancellationToken)
    {
        var roundId = Hash($"{userId}\n{idempotencyKey}");
        var roundRef = RoundDocument(roundId);
        var balanceRef = BalanceDocument(userId);
        var wagerRef = LedgerDocument($"sic-bo-{roundId}-wager");
        var payoutRef = LedgerDocument($"sic-bo-{roundId}-payout");
        var eventRef = EventDocument(roundId);
        return database.RunTransactionAsync(async transaction =>
        {
            var reads = await Task.WhenAll(
                transaction.GetSnapshotAsync(roundRef, cancellationToken),
                transaction.GetSnapshotAsync(balanceRef, cancellationToken),
                transaction.GetSnapshotAsync(wagerRef, cancellationToken),
                transaction.GetSnapshotAsync(payoutRef, cancellationToken),
                transaction.GetSnapshotAsync(eventRef, cancellationToken));
            var balance = ReadBalance(reads[1]);
            if (reads[0].Exists)
            {
                var existing = ReadRound(reads[0]);
                if (existing.UserId != userId || !existing.Bets.SequenceEqual(bets))
                    throw new SicBoRoundConflictException("This Idempotency-Key was already used with a different Sic Bo bet slip.");
                var existingReturn = ReturnCents(existing);
                if (!reads[2].Exists || !reads[4].Exists || (existingReturn > 0) != reads[3].Exists)
                    throw new InvalidOperationException("A recorded Sic Bo round is missing its ledger.");
                return new SicBoStoreResult(existing, balance);
            }
            if (reads[2].Exists || reads[3].Exists || reads[4].Exists)
                throw new InvalidOperationException("A Sic Bo ledger exists without its round.");

            var totalStake = checked(bets.Sum(bet => bet.StakeCents));
            if (balance < totalStake) throw new SicBoInsufficientCreditsException(balance, totalStake);
            var stored = new SicBoStoreRound(roundId, userId, bets.ToArray(), roll);
            var returned = ReturnCents(stored);
            var afterWager = checked(balance - totalStake);
            var finalBalance = checked(afterWager + returned);

            transaction.Create(roundRef, RoundData(stored, idempotencyKey, totalStake, returned, nowUtc));
            transaction.Set(balanceRef, BalanceUpdate(finalBalance, nowUtc), SetOptions.MergeAll);
            transaction.Create(wagerRef, Ledger(wagerRef.Id, userId, -totalStake, afterWager, "sic-bo-wager", idempotencyKey, nowUtc));
            if (returned > 0) transaction.Create(payoutRef, Ledger(payoutRef.Id, userId, returned, finalBalance, "sic-bo-payout", $"{idempotencyKey}-payout", nowUtc));
            transaction.Create(eventRef, Event(roundId, userId, idempotencyKey, nowUtc));
            return new SicBoStoreResult(stored, finalBalance);
        }, cancellationToken: cancellationToken);
    }

    public async Task<SicBoStoreResult?> GetAsync(string userId, string roundId, CancellationToken cancellationToken)
    {
        var snapshots = await Task.WhenAll(RoundDocument(roundId).GetSnapshotAsync(cancellationToken), BalanceDocument(userId).GetSnapshotAsync(cancellationToken));
        if (!snapshots[0].Exists) return null;
        var stored = ReadRound(snapshots[0]);
        return stored.UserId == userId ? new SicBoStoreResult(stored, ReadBalance(snapshots[1])) : null;
    }

    internal static string Hash(string value) => Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(value)));
    private DocumentReference RoundDocument(string id) => database.Collection("sicBoRounds").Document(id);
    private DocumentReference EventDocument(string id) => database.Collection("sicBoRoundEvents").Document(id);
    private DocumentReference BalanceDocument(string userId) => database.Collection("userBalances").Document($"{userId}_{CurrencyId}");
    private DocumentReference LedgerDocument(string id) => database.Collection("balanceTransactions").Document(id);

    private static Dictionary<string, object> RoundData(SicBoStoreRound stored, string idempotencyKey, long totalStake, long totalReturn, DateTimeOffset now) => new()
    {
        ["roundId"] = stored.RoundId,
        ["userId"] = stored.UserId,
        ["betsJson"] = JsonSerializer.Serialize(stored.Bets),
        ["dice"] = stored.Roll.Values.ToArray(),
        ["totalStakeCents"] = totalStake,
        ["totalReturnCents"] = totalReturn,
        ["startIdempotencyKey"] = idempotencyKey,
        ["createdAt"] = Timestamp.FromDateTime(now.UtcDateTime),
        ["schemaVersion"] = 1L,
    };
    private static Dictionary<string, object> Event(string id, string userId, string key, DateTimeOffset now) => new()
    {
        ["roundId"] = id, ["userId"] = userId, ["event"] = "settled", ["idempotencyKey"] = key,
        ["createdAt"] = Timestamp.FromDateTime(now.UtcDateTime), ["schemaVersion"] = 1L,
    };
    private static Dictionary<string, object> BalanceUpdate(long cents, DateTimeOffset now) => new()
    {
        ["available"] = cents / RandMoney.CentsPerRand, [FractionField] = cents % RandMoney.CentsPerRand,
        ["version"] = FieldValue.Increment(1), ["updatedAt"] = Timestamp.FromDateTime(now.UtcDateTime),
    };
    private static Dictionary<string, object> Ledger(string id, string user, long amount, long after, string type, string key, DateTimeOffset now) => new()
    {
        ["transactionId"] = id, ["userId"] = user, ["currencyId"] = CurrencyId,
        ["amount"] = (double)SicBoMoney.ToRand(amount), ["balanceAfter"] = (double)SicBoMoney.ToRand(after),
        ["type"] = type, ["idempotencyKey"] = key, ["createdAt"] = Timestamp.FromDateTime(now.UtcDateTime),
    };

    private static SicBoStoreRound ReadRound(DocumentSnapshot snapshot)
    {
        if (!snapshot.Exists || !snapshot.TryGetValue<string>("roundId", out var id) || id.Length != 64 ||
            !snapshot.TryGetValue<string>("userId", out var userId) || string.IsNullOrWhiteSpace(userId) ||
            !snapshot.TryGetValue<string>("betsJson", out var betsJson) || !snapshot.TryGetValue<long>("schemaVersion", out var version) || version != 1)
        {
            throw new InvalidOperationException("The Sic Bo round is corrupt.");
        }
        var bets = JsonSerializer.Deserialize<SicBoStoredBet[]>(betsJson) ?? throw new InvalidOperationException("The Sic Bo bet slip is corrupt.");
        if (bets.Length is < 1 or > SicBoMoney.MaximumBetsPerRound) throw new InvalidOperationException("The Sic Bo bet slip is corrupt.");
        foreach (var bet in bets) _ = SicBoMoney.ToDomain(bet);
        var values = ReadLongArray(snapshot, "dice");
        if (values.Length != 3) throw new InvalidOperationException("The Sic Bo roll is corrupt.");
        var roll = new SicBoRoll(values.Select(checkedValue => checked((int)checkedValue)));
        var stored = new SicBoStoreRound(id, userId, bets, roll);
        var totalStake = checked(bets.Sum(bet => bet.StakeCents));
        var totalReturn = ReturnCents(stored);
        if (!snapshot.TryGetValue<long>("totalStakeCents", out var persistedStake) || persistedStake != totalStake ||
            !snapshot.TryGetValue<long>("totalReturnCents", out var persistedReturn) || persistedReturn != totalReturn)
        {
            throw new InvalidOperationException("The Sic Bo settlement is corrupt.");
        }
        return stored;
    }

    private static long ReturnCents(SicBoStoreRound stored) => checked(stored.Bets.Sum(bet => ToCents(SicBoPaytable.Settle(SicBoMoney.ToDomain(bet), stored.Roll).TotalReturn)));
    private static long ToCents(decimal value) { var cents = checked(value * RandMoney.CentsPerRand); if (cents != decimal.Truncate(cents)) throw new InvalidOperationException("A Sic Bo settlement resolved to a fraction of a cent."); return checked((long)cents); }
    private static long ReadBalance(DocumentSnapshot snapshot) => checked(ReadLong(snapshot, "available") * RandMoney.CentsPerRand + Math.Clamp(ReadLong(snapshot, FractionField), 0, 99));
    private static long ReadLong(DocumentSnapshot snapshot, string field) => snapshot.Exists && snapshot.TryGetValue<long>(field, out var value) ? value : 0;
    private static long[] ReadLongArray(DocumentSnapshot snapshot, string field)
    {
        if (!snapshot.ToDictionary().TryGetValue(field, out var raw) || raw is not IEnumerable values) return [];
        return values.Cast<object?>().Select(value => value switch { long number => number, int number => number, _ => long.MinValue }).ToArray();
    }
}
