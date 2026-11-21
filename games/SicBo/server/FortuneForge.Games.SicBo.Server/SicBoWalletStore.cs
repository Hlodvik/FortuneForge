using System.Collections;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using FortuneForge.Games.Abstractions.Analytics;
using FortuneForge.Games.Abstractions.Auditing;
using FortuneForge.Games.Abstractions.Wallet;
using Google.Cloud.Firestore;
using FortuneForge.Games.Firestore;

namespace FortuneForge.Games.SicBo.Server;

/// <summary>Persists the immutable dice before the wallet RPC; every recovery repeats that same settlement.</summary>
public sealed class SicBoWalletStore(FirestoreDb database, IGameWalletClient wallet, IGameWalletAccountReader accounts,
    Func<bool>? admitNewRound = null) : ISicBoStore
{
    private DocumentReference Round(string id) => database.Collection("sicBoRounds").Document(id);
    public async Task<SicBoStoreResult> StartAsync(string userId, string idempotencyKey,
        IReadOnlyList<SicBoStoredBet> bets, SicBoRoll roll, DateTimeOffset nowUtc, CancellationToken cancellationToken)
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
                if (stored.UserId != userId || !stored.Bets.SequenceEqual(bets))
                    throw new SicBoRoundConflictException("This Idempotency-Key was already used with another bet slip.");
                return previous;
            }
            if (admitNewRound?.Invoke() == false) throw new SicBoAdmissionClosedException();
            SicBoMoney.ValidateNewBets(bets);
            var round = new SicBoStoreRound(id, userId, bets.ToArray(), roll);
            var wager = round.Bets.Sum(bet => bet.StakeCents);
            var payout = ReturnCents(round);
            if (!SicBoOutcomeAudit.Verify(round, wager, payout)) throw new InvalidOperationException("Invalid Sic Bo outcome.");
            transaction.Create(document, new Dictionary<string, object>
            {
                ["roundId"] = id, ["userId"] = userId, ["betsJson"] = JsonSerializer.Serialize(round.Bets),
                ["dice"] = roll.Values.ToArray(), ["totalStakeCents"] = wager, ["totalReturnCents"] = payout,
                ["startIdempotencyKey"] = idempotencyKey, ["createdAt"] = Timestamp.FromDateTime(nowUtc.UtcDateTime),
                ["requestObservationJson"] = JsonSerializer.Serialize(GameSecurityContext.Current),
                ["schemaVersion"] = 1L, ["walletProtocol"] = 1L,
                ["outcomeSha256"] = Digest(round),
            });
            return null;
        }, cancellationToken: cancellationToken);
        return await FinishAsync(snapshot ?? await document.GetSnapshotAsync(cancellationToken), userId, cancellationToken);
    }

    public async Task<SicBoStoreResult?> GetAsync(string userId, string roundId, CancellationToken cancellationToken)
    {
        ValidateAccount(userId);
        var snapshot = await Round(roundId).GetSnapshotAsync(cancellationToken);
        if (!snapshot.Exists || snapshot.GetValue<string>("userId") != userId) return null;
        return await FinishAsync(snapshot, userId, cancellationToken);
    }

    private async Task<SicBoStoreResult> FinishAsync(DocumentSnapshot snapshot, string userId, CancellationToken token)
    {
        var round = Read(snapshot);
        using var originalRequest = snapshot.TryGetValue<string>("requestObservationJson", out var requestJson) &&
            JsonSerializer.Deserialize<GameRequestObservation>(requestJson) is { } observation ? GameSecurityContext.Push(observation) : null;
        if (round.UserId != userId) throw new SicBoRoundNotFoundException();
        // Historical rounds were settled atomically by the original backend. Never send them to the wallet again.
        if (!snapshot.ContainsField("walletProtocol"))
        {
            var state = await accounts.GetStateAsync(userId, token);
            return new(round, state.SlotsCreditsBalanceCents ?? throw new GameWalletUnavailableException());
        }
        var digest = Digest(round);
        if (snapshot.GetValue<string>("outcomeSha256") != digest) throw new InvalidOperationException("Corrupt Sic Bo outcome digest.");
        var wager = round.Bets.Sum(bet => bet.StakeCents);
        var payout = ReturnCents(round);
        var request = new GameWalletSettlementRequest(round.RoundId, digest, wager, payout);
        var receipt = ReadWalletReceipt(snapshot, userId, request) ?? await wallet.SettleAsync(userId, request, token);
        if (!GameWalletReceiptVerifier.MatchesSettlement(receipt, "sic-bo", userId, request)) throw new GameWalletUnavailableException();
        if (receipt.InsufficientFunds)
        {
            await snapshot.Reference.UpdateAsync(new Dictionary<string, object>
            {
                ["walletReceiptJson"] = JsonSerializer.Serialize(receipt with { WasAlreadySettled = false }), ["walletDeclined"] = true,
            }, cancellationToken: token);
            throw new SicBoInsufficientCreditsException(receipt.BalanceBeforeCents, wager);
        }
        if (snapshot.TryGetValue<long>("settledBalanceCents", out var balance))
        {
            if (balance != receipt.BalanceAfterCents) throw new InvalidOperationException("Corrupt SicBo settled balance.");
            return new(round, balance);
        }
        var result = new SicBoStoreResult(round, receipt.BalanceAfterCents);
        return await database.RunTransactionAsync(async transaction =>
        {
            var current = await transaction.GetSnapshotAsync(snapshot.Reference, token);
            if (current.TryGetValue<long>("settledBalanceCents", out var saved))
            {
                var storedReceipt = ReadWalletReceipt(current, userId, request) ?? throw new InvalidOperationException("Missing SicBo wallet receipt.");
                if (storedReceipt.InsufficientFunds || saved != storedReceipt.BalanceAfterCents) throw new InvalidOperationException("Corrupt SicBo settled balance.");
                return new SicBoStoreResult(round, saved);
            }
            if (Digest(Read(current)) != digest) throw new InvalidOperationException("The prepared Sic Bo outcome changed.");
            transaction.Update(snapshot.Reference, new Dictionary<string, object>
            {
                ["settledBalanceCents"] = result.BalanceCents, ["walletReceiptJson"] = JsonSerializer.Serialize(receipt with { WasAlreadySettled = false }),
                ["settledAt"] = Timestamp.FromDateTime(receipt.SettledAtUtc.UtcDateTime),
            });
            var record = SinglePlayerRoundRecord.Create("sic-bo", round.RoundId, userId, wager, payout,
                current.GetValue<Timestamp>("createdAt").ToDateTime(), receipt.SettledAtUtc.UtcDateTime,
                "sicBoRounds", round.RoundId, round, "sic-bo-bets-and-dice-v1", true, SicBoOutcomeAudit.Version);
            transaction.Create(database.Collection(GameAnalyticsOutbox.CollectionName).Document(record.Settlement.Id), record.Settlement.Fields);
            FirestoreGameAudit.WriteCompletion(transaction, database, record.Audit);
            FirestoreGameAudit.WriteObservation(transaction, database, "sic-bo", round.RoundId, 1,
                receipt.SettledAtUtc.UtcDateTime, round, [userId], "settled");
            FirestoreGameAudit.WriteAcceptedParticipation(transaction, database, "sic-bo", userId, receipt.SettledAtUtc.UtcDateTime);
            transaction.Create(database.Collection("sicBoRoundEvents").Document(round.RoundId), new Dictionary<string, object>
            {
                ["roundId"] = round.RoundId, ["userId"] = userId, ["event"] = "settled",
                ["idempotencyKey"] = current.GetValue<string>("startIdempotencyKey"), ["schemaVersion"] = 1L,
                ["createdAt"] = Timestamp.FromDateTime(receipt.SettledAtUtc.UtcDateTime),
            });
            return result;
        }, cancellationToken: token);
    }

    private static GameWalletSettlementReceipt? ReadWalletReceipt(DocumentSnapshot snapshot, string userId, GameWalletSettlementRequest request)
    {
        if (!snapshot.TryGetValue<string>("walletReceiptJson", out var json))
        {
            if (snapshot.ContainsField("settledBalanceCents")) throw new InvalidOperationException("Missing SicBo wallet receipt.");
            return null;
        }
        var receipt = JsonSerializer.Deserialize<GameWalletSettlementReceipt>(json) ?? throw new InvalidOperationException("Empty SicBo wallet receipt.");
        if (!GameWalletReceiptVerifier.MatchesSettlement(receipt, "sic-bo", userId, request)) throw new InvalidOperationException("Corrupt SicBo wallet receipt.");
        return receipt;
    }

    private static SicBoStoreRound Read(DocumentSnapshot snapshot)
    {
        try { return ReadCore(snapshot); }
        catch (Exception error) when (error is ArgumentException or JsonException or OverflowException)
        { throw new InvalidOperationException("The persisted SicBo outcome is corrupt.", error); }
    }

    private static SicBoStoreRound ReadCore(DocumentSnapshot snapshot)
    {
        if (snapshot.GetValue<long>("schemaVersion") != 1 || snapshot.GetValue<string>("roundId") != snapshot.Id)
            throw new InvalidOperationException("Corrupt Sic Bo identity.");
        var bets = JsonSerializer.Deserialize<SicBoStoredBet[]>(snapshot.GetValue<string>("betsJson"))
            ?? throw new InvalidOperationException("Missing Sic Bo bets.");
        foreach (var bet in bets) _ = SicBoMoney.ToDomain(bet);
        var dice = ((IEnumerable)snapshot.ToDictionary()["dice"]).Cast<object>().Select(Convert.ToInt32);
        var round = new SicBoStoreRound(snapshot.Id, snapshot.GetValue<string>("userId"), bets, new SicBoRoll(dice));
        if (!SicBoOutcomeAudit.Verify(round, snapshot.GetValue<long>("totalStakeCents"), snapshot.GetValue<long>("totalReturnCents")))
            throw new InvalidOperationException("Corrupt Sic Bo settlement.");
        return round;
    }
    private static long ReturnCents(SicBoStoreRound round) => checked((long)(round.Bets.Sum(bet => SicBoPaytable.Settle(SicBoMoney.ToDomain(bet), round.Roll).TotalReturn) * 100m));
    private static string Digest(SicBoStoreRound round) => Hash(JsonSerializer.Serialize(round));
    public static string ReadStoredOutcomeSha256(DocumentSnapshot snapshot) => Digest(Read(snapshot));
    private static string Hash(string value) => Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(value)));
    private static void ValidateAccount(string userId)
    {
        if (string.IsNullOrWhiteSpace(userId) || userId.Length > 256 || userId.Contains('/')) throw new ArgumentException("A valid account is required.");
    }
}

public sealed class SicBoAdmissionClosedException() : Exception("Sic Bo is not accepting new wagers.");
