using System.Collections;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using FortuneForge.Games.Abstractions.Analytics;
using FortuneForge.Games.Abstractions.Auditing;
using FortuneForge.Games.Abstractions.Wallet;
using Google.Cloud.Firestore;
using FortuneForge.Games.Firestore;

namespace FortuneForge.Games.Keno.Server;

/// <summary>The game fixes its ticket, draw and paytable before submitting its recoverable wallet operation.</summary>
public sealed class KenoWalletStore(FirestoreDb database, IGameWalletClient wallet, IGameWalletAccountReader accounts,
    Func<bool>? admitNewRound = null) : IKenoStore
{
    private DocumentReference Round(string id) => database.Collection("kenoRounds").Document(id);
    public async Task<KenoStoreResult> StartAsync(string userId, string idempotencyKey, KenoTicket ticket,
        long wagerCents, KenoDraw draw, DateTimeOffset nowUtc, CancellationToken cancellationToken)
    {
        ValidateAccount(userId);
        var id = Hash($"{userId}\n{idempotencyKey}");
        var document = Round(id);
        var snapshot = await database.RunTransactionAsync(async transaction =>
        {
            var previous = await transaction.GetSnapshotAsync(document, cancellationToken);
            if (previous.Exists)
            {
                var stored = Read(previous);
                if (stored.UserId != userId || stored.WagerCents != wagerCents || !stored.Ticket.Numbers.SequenceEqual(ticket.Numbers))
                    throw new KenoRoundConflictException("This Idempotency-Key was already used with another ticket or wager.");
                return previous;
            }
            if (admitNewRound?.Invoke() == false) throw new KenoAdmissionClosedException();
            KenoMoney.ToWagerCents(KenoMoney.ToRand(wagerCents));
            var played = KenoRoundEngine.Play(ticket, draw, StandardKenoPaytable.Instance).Result;
            var payout = KenoMoney.PayoutCents(wagerCents, played.PaytableOutcome!.Value);
            var round = new KenoStoreRound(id, userId, ticket, draw, played.HitCount, wagerCents, payout, StandardKenoPaytable.Id);
            if (!KenoOutcomeAudit.Verify(round)) throw new InvalidOperationException("Invalid Keno outcome.");
            transaction.Create(document, new Dictionary<string, object>
            {
                ["roundId"] = id, ["userId"] = userId, ["ticket"] = ticket.Numbers.ToArray(), ["draw"] = draw.Numbers.ToArray(),
                ["hitCount"] = round.HitCount, ["wagerCents"] = wagerCents, ["payoutCents"] = payout, ["paytableId"] = round.PaytableId,
                ["startIdempotencyKey"] = idempotencyKey, ["createdAt"] = Timestamp.FromDateTime(nowUtc.UtcDateTime),
                ["requestObservationJson"] = JsonSerializer.Serialize(GameSecurityContext.Current),
                ["schemaVersion"] = 2L, ["walletProtocol"] = 1L, ["outcomeSha256"] = Digest(round),
            });
            return null;
        }, cancellationToken: cancellationToken);
        return await FinishAsync(snapshot ?? await document.GetSnapshotAsync(cancellationToken), userId, cancellationToken);
    }

    public async Task<KenoStoreResult?> GetAsync(string userId, string roundId, CancellationToken cancellationToken)
    {
        ValidateAccount(userId);
        var snapshot = await Round(roundId).GetSnapshotAsync(cancellationToken);
        if (!snapshot.Exists || snapshot.GetValue<string>("userId") != userId) return null;
        return await FinishAsync(snapshot, userId, cancellationToken);
    }

    private async Task<KenoStoreResult> FinishAsync(DocumentSnapshot snapshot, string userId, CancellationToken token)
    {
        var round = Read(snapshot);
        using var originalRequest = snapshot.TryGetValue<string>("requestObservationJson", out var requestJson) &&
            JsonSerializer.Deserialize<GameRequestObservation>(requestJson) is { } observation ? GameSecurityContext.Push(observation) : null;
        if (round.UserId != userId) throw new KenoRoundNotFoundException();
        // Saved v1/v2/v3 economics remain authoritative. These predate the signed wallet journal.
        if (!snapshot.ContainsField("walletProtocol"))
        {
            var state = await accounts.GetStateAsync(userId, token);
            return new(round, state.SlotsCreditsBalanceCents ?? throw new GameWalletUnavailableException());
        }
        var digest = Digest(round);
        if (snapshot.GetValue<string>("outcomeSha256") != digest) throw new InvalidOperationException("Corrupt Keno outcome digest.");
        var request = new GameWalletSettlementRequest(round.RoundId, digest, round.WagerCents, round.PayoutCents);
        var receipt = ReadWalletReceipt(snapshot, userId, request) ?? await wallet.SettleAsync(userId, request, token);
        if (!GameWalletReceiptVerifier.MatchesSettlement(receipt, "keno", userId, request)) throw new GameWalletUnavailableException();
        if (receipt.InsufficientFunds)
        {
            await snapshot.Reference.UpdateAsync(new Dictionary<string, object>
            {
                ["walletReceiptJson"] = JsonSerializer.Serialize(receipt with { WasAlreadySettled = false }), ["walletDeclined"] = true,
            }, cancellationToken: token);
            throw new KenoInsufficientCreditsException(receipt.BalanceBeforeCents, round.WagerCents);
        }
        if (snapshot.TryGetValue<long>("settledBalanceCents", out var balance))
        {
            if (balance != receipt.BalanceAfterCents) throw new InvalidOperationException("Corrupt Keno settled balance.");
            return new(round, balance);
        }
        var result = new KenoStoreResult(round, receipt.BalanceAfterCents);
        return await database.RunTransactionAsync(async transaction =>
        {
            var current = await transaction.GetSnapshotAsync(snapshot.Reference, token);
            if (current.TryGetValue<long>("settledBalanceCents", out var saved))
            {
                var storedReceipt = ReadWalletReceipt(current, userId, request) ?? throw new InvalidOperationException("Missing Keno wallet receipt.");
                if (storedReceipt.InsufficientFunds || saved != storedReceipt.BalanceAfterCents) throw new InvalidOperationException("Corrupt Keno settled balance.");
                return new KenoStoreResult(round, saved);
            }
            if (Digest(Read(current)) != digest) throw new InvalidOperationException("The prepared Keno outcome changed.");
            transaction.Update(snapshot.Reference, new Dictionary<string, object>
            {
                ["settledBalanceCents"] = result.BalanceCents, ["walletReceiptJson"] = JsonSerializer.Serialize(receipt with { WasAlreadySettled = false }),
                ["settledAt"] = Timestamp.FromDateTime(receipt.SettledAtUtc.UtcDateTime),
            });
            var record = SinglePlayerRoundRecord.Create("keno", round.RoundId, userId, round.WagerCents, round.PayoutCents,
                current.GetValue<Timestamp>("createdAt").ToDateTime(), receipt.SettledAtUtc.UtcDateTime,
                "kenoRounds", round.RoundId, round, "keno-ticket-and-draw-v1", true, KenoOutcomeAudit.Version);
            transaction.Create(database.Collection(GameAnalyticsOutbox.CollectionName).Document(record.Settlement.Id), record.Settlement.Fields);
            FirestoreGameAudit.WriteCompletion(transaction, database, record.Audit);
            FirestoreGameAudit.WriteObservation(transaction, database, "keno", round.RoundId, 1,
                receipt.SettledAtUtc.UtcDateTime, round, [userId], "settled");
            FirestoreGameAudit.WriteAcceptedParticipation(transaction, database, "keno", userId, receipt.SettledAtUtc.UtcDateTime);
            transaction.Create(database.Collection("kenoRoundEvents").Document(round.RoundId), new Dictionary<string, object>
            {
                ["roundId"] = round.RoundId, ["userId"] = userId, ["event"] = "settled", ["schemaVersion"] = 2L,
                ["idempotencyKey"] = current.GetValue<string>("startIdempotencyKey"),
                ["createdAt"] = Timestamp.FromDateTime(receipt.SettledAtUtc.UtcDateTime),
            });
            return result;
        }, cancellationToken: token);
    }

    private static GameWalletSettlementReceipt? ReadWalletReceipt(DocumentSnapshot snapshot, string userId, GameWalletSettlementRequest request)
    {
        if (!snapshot.TryGetValue<string>("walletReceiptJson", out var json))
        {
            if (snapshot.ContainsField("settledBalanceCents")) throw new InvalidOperationException("Missing Keno wallet receipt.");
            return null;
        }
        var receipt = JsonSerializer.Deserialize<GameWalletSettlementReceipt>(json) ?? throw new InvalidOperationException("Empty Keno wallet receipt.");
        if (!GameWalletReceiptVerifier.MatchesSettlement(receipt, "keno", userId, request)) throw new InvalidOperationException("Corrupt Keno wallet receipt.");
        return receipt;
    }

    private static KenoStoreRound Read(DocumentSnapshot snapshot)
    {
        try { return ReadCore(snapshot); }
        catch (Exception error) when (error is ArgumentException or JsonException or OverflowException)
        { throw new InvalidOperationException("The persisted Keno outcome is corrupt.", error); }
    }

    private static KenoStoreRound ReadCore(DocumentSnapshot snapshot)
    {
        if (snapshot.GetValue<long>("schemaVersion") != 2 || snapshot.GetValue<string>("roundId") != snapshot.Id)
            throw new InvalidOperationException("Corrupt Keno identity.");
        var ticket = new KenoTicket(Numbers(snapshot, "ticket"));
        var draw = new KenoDraw(Numbers(snapshot, "draw"));
        var hits = ticket.Numbers.Intersect(draw.Numbers).Count();
        var wager = snapshot.GetValue<long>("wagerCents");
        KenoMoney.ToWagerCents(KenoMoney.ToRand(wager));
        var paytable = snapshot.GetValue<string>("paytableId");
        var payout = KenoMoney.PayoutCents(wager, StandardKenoPaytable.MultiplierFor(paytable, ticket.Numbers.Length, hits));
        if (snapshot.GetValue<long>("hitCount") != hits || snapshot.GetValue<long>("payoutCents") != payout)
            throw new InvalidOperationException("Corrupt Keno outcome.");
        return new(snapshot.Id, snapshot.GetValue<string>("userId"), ticket, draw, hits, wager, payout, paytable);
    }

    private static int[] Numbers(DocumentSnapshot snapshot, string field) =>
        ((IEnumerable)snapshot.ToDictionary()[field]).Cast<object>().Select(Convert.ToInt32).ToArray();
    private static string Digest(KenoStoreRound round) => Hash(JsonSerializer.Serialize(round));
    public static string ReadStoredOutcomeSha256(DocumentSnapshot snapshot) => Digest(Read(snapshot));
    private static string Hash(string value) => Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(value)));
    private static void ValidateAccount(string userId)
    {
        if (string.IsNullOrWhiteSpace(userId) || userId.Length > 256 || userId.Contains('/')) throw new ArgumentException("A valid account is required.");
    }
}

public sealed class KenoAdmissionClosedException() : Exception("Keno is not accepting new wagers.");
