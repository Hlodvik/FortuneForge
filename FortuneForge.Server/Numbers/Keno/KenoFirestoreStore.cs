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

    public Task<KenoStoreResult> StartAsync(string userId, string idempotencyKey, KenoTicket ticket, KenoDraw draw, DateTimeOffset nowUtc, CancellationToken cancellationToken)
    {
        var roundId = Hash($"{userId}\n{idempotencyKey}");
        var roundRef = RoundDocument(roundId);
        var balanceRef = BalanceDocument(userId);
        var eventRef = EventDocument(roundId);
        return database.RunTransactionAsync(async transaction =>
        {
            var reads = await Task.WhenAll(transaction.GetSnapshotAsync(roundRef, cancellationToken), transaction.GetSnapshotAsync(balanceRef, cancellationToken), transaction.GetSnapshotAsync(eventRef, cancellationToken));
            var balance = ReadBalance(reads[1]);
            if (reads[0].Exists)
            {
                var existing = ReadRound(reads[0]);
                if (existing.UserId != userId || !existing.Ticket.Numbers.SequenceEqual(ticket.Numbers)) throw new KenoRoundConflictException("This Idempotency-Key was already used with a different Keno ticket.");
                if (!reads[2].Exists) throw new InvalidOperationException("A recorded Keno round is missing its event.");
                return new KenoStoreResult(existing, balance);
            }
            if (reads[2].Exists) throw new InvalidOperationException("A Keno event exists without its round.");

            var played = KenoRoundEngine.Play(ticket, draw);
            var stored = new KenoStoreRound(roundId, userId, ticket, draw, played.Result.HitCount);
            transaction.Create(roundRef, RoundData(stored, idempotencyKey, nowUtc));
            transaction.Create(eventRef, Event(roundId, userId, idempotencyKey, nowUtc));
            return new KenoStoreResult(stored, balance);
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
    private static Dictionary<string, object> RoundData(KenoStoreRound round, string key, DateTimeOffset now) => new()
    {
        ["roundId"] = round.RoundId, ["userId"] = round.UserId, ["ticket"] = round.Ticket.Numbers.ToArray(), ["draw"] = round.Draw.Numbers.ToArray(), ["hitCount"] = round.HitCount,
        ["startIdempotencyKey"] = key, ["createdAt"] = Timestamp.FromDateTime(now.UtcDateTime), ["schemaVersion"] = 1L,
    };
    private static Dictionary<string, object> Event(string roundId, string userId, string key, DateTimeOffset now) => new()
    {
        ["roundId"] = roundId, ["userId"] = userId, ["event"] = "completed", ["idempotencyKey"] = key,
        ["createdAt"] = Timestamp.FromDateTime(now.UtcDateTime), ["schemaVersion"] = 1L,
    };
    private static KenoStoreRound ReadRound(DocumentSnapshot snapshot)
    {
        if (!snapshot.Exists || !snapshot.TryGetValue<string>("roundId", out var id) || id.Length != 64 || !snapshot.TryGetValue<string>("userId", out var userId) || string.IsNullOrWhiteSpace(userId) || !snapshot.TryGetValue<long>("hitCount", out var storedHits) || !snapshot.TryGetValue<long>("schemaVersion", out var version) || version != 1) throw new InvalidOperationException("The Keno round is corrupt.");
        var ticket = new KenoTicket(ReadNumbers(snapshot, "ticket"));
        var draw = new KenoDraw(ReadNumbers(snapshot, "draw"));
        var calculatedHits = KenoRoundEngine.Play(ticket, draw).Result.HitCount;
        if (storedHits != calculatedHits) throw new InvalidOperationException("The Keno round result is corrupt.");
        return new KenoStoreRound(id, userId, ticket, draw, checked((int)storedHits));
    }
    private static long ReadBalance(DocumentSnapshot snapshot) => checked(ReadLong(snapshot, "available") * RandMoney.CentsPerRand + Math.Clamp(ReadLong(snapshot, FractionField), 0, 99));
    private static long ReadLong(DocumentSnapshot snapshot, string field) => snapshot.Exists && snapshot.TryGetValue<long>(field, out var value) ? value : 0;
    private static int[] ReadNumbers(DocumentSnapshot snapshot, string field)
    {
        if (!snapshot.ToDictionary().TryGetValue(field, out var raw) || raw is not IEnumerable values) return [];
        return values.Cast<object?>().Select(value => value switch { long number => checked((int)number), int number => number, _ => int.MinValue }).ToArray();
    }
}
