using System.Collections;
using System.Security.Cryptography;
using System.Text;
using FortuneForge.Games.Keno;
using FortuneForge.Server.Accounts.Models;
using Google.Cloud.Firestore;

namespace FortuneForge.Server.Numbers.Keno;

internal sealed class KenoFirestoreStore(FirestoreDb database) : IKenoStore
{
    private const string CurrencyId = "slotsCredits";
    private const string FractionField = "availableFractionalCents";

    public Task<KenoStoreResult> StartAsync(string userId, string idempotencyKey, KenoTicket ticket, long wagerCents, KenoDraw draw, DateTimeOffset nowUtc, CancellationToken cancellationToken)
    {
        var roundId = Hash($"{userId}\n{idempotencyKey}");
        var roundRef = RoundDocument(roundId);
        var balanceRef = BalanceDocument(userId);
        var eventRef = EventDocument(roundId);
        var wagerRef = BalanceTransactionDocument($"keno-{roundId}-wager");
        var payoutRef = BalanceTransactionDocument($"keno-{roundId}-payout");
        return database.RunTransactionAsync(async transaction =>
        {
            var reads = await Task.WhenAll(
                transaction.GetSnapshotAsync(roundRef, cancellationToken),
                transaction.GetSnapshotAsync(balanceRef, cancellationToken),
                transaction.GetSnapshotAsync(eventRef, cancellationToken),
                transaction.GetSnapshotAsync(wagerRef, cancellationToken),
                transaction.GetSnapshotAsync(payoutRef, cancellationToken));
            var balance = ReadBalance(reads[1]);
            if (reads[0].Exists)
            {
                var existing = ReadRound(reads[0]);
                if (existing.UserId != userId || existing.WagerCents != wagerCents || !existing.Ticket.Numbers.SequenceEqual(ticket.Numbers))
                    throw new KenoRoundConflictException("This Idempotency-Key was already used with a different Keno ticket or wager.");
                if (!reads[2].Exists || !reads[3].Exists || (existing.PayoutCents > 0) != reads[4].Exists)
                    throw new InvalidOperationException("A recorded Keno round is missing its ledger.");
                return new KenoStoreResult(existing, balance);
            }
            if (reads[2].Exists || reads[3].Exists || reads[4].Exists)
                throw new InvalidOperationException("A Keno ledger exists without its round.");
            if (balance < wagerCents) throw new KenoInsufficientCreditsException(balance, wagerCents);

            var played = KenoRoundEngine.Play(ticket, draw, StandardKenoPaytable.Instance);
            var outcome = played.Result.PaytableOutcome ?? throw new InvalidOperationException("The Keno paytable did not resolve the round.");
            var payoutCents = checked(wagerCents * outcome.Value);
            var balanceAfterWager = checked(balance - wagerCents);
            var finalBalance = checked(balanceAfterWager + payoutCents);
            var stored = new KenoStoreRound(roundId, userId, ticket, draw, played.Result.HitCount, wagerCents, payoutCents, outcome.PaytableId);
            transaction.Create(roundRef, RoundData(stored, idempotencyKey, nowUtc));
            transaction.Set(balanceRef, BalanceUpdate(finalBalance, nowUtc), SetOptions.MergeAll);
            transaction.Create(wagerRef, BalanceTransactionData(wagerRef.Id, userId, -wagerCents, balanceAfterWager, "keno-wager", idempotencyKey, nowUtc));
            if (payoutCents > 0)
                transaction.Create(payoutRef, BalanceTransactionData(payoutRef.Id, userId, payoutCents, finalBalance, "keno-payout", $"{idempotencyKey}-payout", nowUtc));
            transaction.Create(eventRef, Event(roundId, userId, idempotencyKey, nowUtc));
            return new KenoStoreResult(stored, finalBalance);
        }, cancellationToken: cancellationToken);
    }

    public async Task<KenoStoreResult?> GetAsync(string userId, string roundId, CancellationToken cancellationToken)
    {
        var snapshots = await Task.WhenAll(RoundDocument(roundId).GetSnapshotAsync(cancellationToken), BalanceDocument(userId).GetSnapshotAsync(cancellationToken));
        if (!snapshots[0].Exists) return null;
        var stored = ReadRound(snapshots[0]);
        return stored.UserId == userId ? new KenoStoreResult(stored, ReadBalance(snapshots[1])) : null;
    }

    internal static string Hash(string value) => Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(value)));
    private DocumentReference RoundDocument(string id) => database.Collection("kenoRounds").Document(id);
    private DocumentReference EventDocument(string id) => database.Collection("kenoRoundEvents").Document(id);
    private DocumentReference BalanceDocument(string userId) => database.Collection("userBalances").Document($"{userId}_{CurrencyId}");
    private DocumentReference BalanceTransactionDocument(string id) => database.Collection("balanceTransactions").Document(id);
    private static Dictionary<string, object> RoundData(KenoStoreRound round, string key, DateTimeOffset now) => new()
    {
        ["roundId"] = round.RoundId, ["userId"] = round.UserId, ["ticket"] = round.Ticket.Numbers.ToArray(), ["draw"] = round.Draw.Numbers.ToArray(), ["hitCount"] = round.HitCount,
        ["wagerCents"] = round.WagerCents, ["payoutCents"] = round.PayoutCents, ["paytableId"] = round.PaytableId,
        ["startIdempotencyKey"] = key, ["createdAt"] = Timestamp.FromDateTime(now.UtcDateTime), ["schemaVersion"] = 2L,
    };
    private static Dictionary<string, object> Event(string roundId, string userId, string key, DateTimeOffset now) => new()
    {
        ["roundId"] = roundId, ["userId"] = userId, ["event"] = "settled", ["idempotencyKey"] = key,
        ["createdAt"] = Timestamp.FromDateTime(now.UtcDateTime), ["schemaVersion"] = 2L,
    };
    private static KenoStoreRound ReadRound(DocumentSnapshot snapshot)
    {
        if (!snapshot.Exists || !snapshot.TryGetValue<string>("roundId", out var id) || id.Length != 64 ||
            !snapshot.TryGetValue<string>("userId", out var userId) || string.IsNullOrWhiteSpace(userId) ||
            !snapshot.TryGetValue<long>("hitCount", out var storedHits) ||
            !snapshot.TryGetValue<long>("wagerCents", out var wagerCents) ||
            !snapshot.TryGetValue<long>("payoutCents", out var payoutCents) ||
            !snapshot.TryGetValue<string>("paytableId", out var paytableId) || !StandardKenoPaytable.Supports(paytableId) ||
            !snapshot.TryGetValue<long>("schemaVersion", out var version) || version != 2)
            throw new InvalidOperationException("The Keno round is corrupt.");
        var ticket = new KenoTicket(ReadNumbers(snapshot, "ticket"));
        var draw = new KenoDraw(ReadNumbers(snapshot, "draw"));
        var played = KenoRoundEngine.Play(ticket, draw);
        var multiplier = StandardKenoPaytable.MultiplierFor(paytableId, ticket.Numbers.Length, played.Result.HitCount);
        var calculatedPayout = checked(KenoMoney.ToWagerCents(KenoMoney.ToRand(wagerCents)) * multiplier);
        if (storedHits != played.Result.HitCount || payoutCents != calculatedPayout)
            throw new InvalidOperationException("The Keno round result is corrupt.");
        return new KenoStoreRound(id, userId, ticket, draw, checked((int)storedHits), wagerCents, payoutCents, paytableId);
    }
    private static Dictionary<string, object> BalanceUpdate(long balanceCents, DateTimeOffset now) => new()
    {
        ["available"] = balanceCents / RandMoney.CentsPerRand,
        [FractionField] = balanceCents % RandMoney.CentsPerRand,
        ["version"] = FieldValue.Increment(1),
        ["updatedAt"] = Timestamp.FromDateTime(now.UtcDateTime),
    };
    private static Dictionary<string, object> BalanceTransactionData(string id, string userId, long amountCents, long balanceAfterCents, string type, string key, DateTimeOffset now) => new()
    {
        ["transactionId"] = id,
        ["userId"] = userId,
        ["currencyId"] = CurrencyId,
        ["amount"] = (double)KenoMoney.ToRand(amountCents),
        ["balanceAfter"] = (double)KenoMoney.ToRand(balanceAfterCents),
        ["type"] = type,
        ["idempotencyKey"] = key,
        ["createdAt"] = Timestamp.FromDateTime(now.UtcDateTime),
    };
    private static long ReadBalance(DocumentSnapshot snapshot) => checked(ReadLong(snapshot, "available") * RandMoney.CentsPerRand + Math.Clamp(ReadLong(snapshot, FractionField), 0, 99));
    private static long ReadLong(DocumentSnapshot snapshot, string field) => snapshot.Exists && snapshot.TryGetValue<long>(field, out var value) ? value : 0;
    private static int[] ReadNumbers(DocumentSnapshot snapshot, string field)
    {
        if (!snapshot.ToDictionary().TryGetValue(field, out var raw) || raw is not IEnumerable values) return [];
        return values.Cast<object?>().Select(value => value switch { long number => checked((int)number), int number => number, _ => int.MinValue }).ToArray();
    }
}
