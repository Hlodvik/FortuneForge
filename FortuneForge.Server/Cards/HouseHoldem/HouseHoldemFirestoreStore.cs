using System.Collections;
using System.Security.Cryptography;
using System.Text;
using FortuneForge.Games.HouseHoldem;
using FortuneForge.Server.Accounts.Models;
using Google.Cloud.Firestore;
using Grpc.Core;

namespace FortuneForge.Server.Cards.HouseHoldem;

/// <summary>All wagers, phase changes and payouts are committed with the round in one Firestore transaction.</summary>
internal sealed class HouseHoldemFirestoreStore(FirestoreDb database) : IHouseHoldemStore
{
    private const string CurrencyId = "slotsCredits";
    private const string FractionalCentsField = "availableFractionalCents";

    public Task<HouseHoldemStoreResult> StartAsync(
        string userId, string idempotencyKey, long anteCents, long tripsCents,
        IReadOnlyList<string> shuffledDeck, DateTimeOffset nowUtc, CancellationToken cancellationToken)
    {
        var roundId = LookupKey($"{userId}\n{idempotencyKey}");
        var roundReference = RoundDocument(roundId);
        var activeReference = ActiveRoundDocument(userId);
        var balanceReference = BalanceDocument(userId);
        var wagerReference = BalanceTransactionDocument($"house-holdem-{roundId}-initial-wager");
        var eventReference = EventDocument(roundId, idempotencyKey);
        return RunWithContentionRetryAsync(() => database.RunTransactionAsync(async transaction =>
        {
            var roundSnapshot = await transaction.GetSnapshotAsync(roundReference, cancellationToken);
            var activeSnapshot = await transaction.GetSnapshotAsync(activeReference, cancellationToken);
            var balanceSnapshot = await transaction.GetSnapshotAsync(balanceReference, cancellationToken);
            var wagerSnapshot = await transaction.GetSnapshotAsync(wagerReference, cancellationToken);
            var eventSnapshot = await transaction.GetSnapshotAsync(eventReference, cancellationToken);
            var balanceCents = ReadBalanceCents(balanceSnapshot);
            if (roundSnapshot.Exists)
            {
                var existing = ReadRound(roundSnapshot);
                if (existing.UserId != userId || existing.Round.AnteCents != anteCents || existing.Round.TripsCents != tripsCents)
                    throw new HouseHoldemRoundConflictException("This Idempotency-Key was already used with a different House Hold'em request.");
                if (!wagerSnapshot.Exists || !eventSnapshot.Exists)
                    throw new InvalidOperationException("A recorded House Hold'em round is missing its initial wager ledger.");
                return new HouseHoldemStoreResult(roundId, existing.Round, balanceCents);
            }
            if (wagerSnapshot.Exists || eventSnapshot.Exists)
                throw new InvalidOperationException("A House Hold'em initial wager ledger exists without its round.");
            var activeRoundId = ReadString(activeSnapshot, "roundId");
            if (!string.IsNullOrEmpty(activeRoundId))
                throw new HouseHoldemRoundConflictException("Finish the current House Hold'em hand before starting another.");

            var round = HouseHoldemRules.StartRound(shuffledDeck, anteCents, tripsCents);
            var wagerCents = round.TotalWagerCents;
            // The final Play wager is mandatory unless the player folds. Keep one Ante
            // available so a hand cannot start with folding as its only affordable option.
            var minimumHandExposureCents = checked(wagerCents + anteCents);
            if (balanceCents < minimumHandExposureCents)
                throw new HouseHoldemInsufficientCreditsException(balanceCents, minimumHandExposureCents);
            var remainingCents = checked(balanceCents - wagerCents);
            transaction.Create(roundReference, RoundData(roundId, userId, round, shuffledDeck, idempotencyKey, nowUtc));
            FortuneForge.Server.SecurityAudit.GameSecurityJournal.Write(transaction, database, "house-holdem", roundId,
                0, nowUtc.UtcDateTime, new { round, deck = shuffledDeck, steps = Array.Empty<string>() }, [userId], "deal");
            transaction.Set(activeReference, ActiveRoundData(roundId, nowUtc));
            transaction.Set(balanceReference, BalanceData(remainingCents, nowUtc), SetOptions.MergeAll);
            transaction.Create(wagerReference, BalanceTransactionData(wagerReference.Id, userId,
                -wagerCents, remainingCents, "house-holdem-wager", idempotencyKey, nowUtc));
            transaction.Create(eventReference, EventData(roundId, userId, "start", idempotencyKey, nowUtc));
            return new HouseHoldemStoreResult(roundId, round, remainingCents);
        }, cancellationToken: cancellationToken), cancellationToken);
    }

    public async Task<HouseHoldemStoreResult?> GetAsync(
        string userId, string roundId, CancellationToken cancellationToken)
    {
        var roundSnapshot = await RoundDocument(roundId).GetSnapshotAsync(cancellationToken);
        if (!roundSnapshot.Exists) return null;
        var stored = ReadRound(roundSnapshot);
        if (stored.UserId != userId) return null;
        var balanceSnapshot = await BalanceDocument(userId).GetSnapshotAsync(cancellationToken);
        return new HouseHoldemStoreResult(roundId, stored.Round, ReadBalanceCents(balanceSnapshot));
    }

    public async Task<string?> GetActiveRoundIdAsync(string userId, CancellationToken cancellationToken)
    {
        var snapshot = await ActiveRoundDocument(userId).GetSnapshotAsync(cancellationToken);
        var roundId = ReadString(snapshot, "roundId");
        return string.IsNullOrEmpty(roundId) ? null : roundId;
    }

    public Task<HouseHoldemStoreResult> TransitionAsync(
        string userId, string roundId, string idempotencyKey, string command,
        DateTimeOffset nowUtc, CancellationToken cancellationToken)
    {
        var roundReference = RoundDocument(roundId);
        var activeReference = ActiveRoundDocument(userId);
        var balanceReference = BalanceDocument(userId);
        var eventReference = EventDocument(roundId, idempotencyKey);
        var debitReference = BalanceTransactionDocument($"house-holdem-{roundId}-{LookupKey(idempotencyKey)}-wager");
        var payoutReference = BalanceTransactionDocument($"house-holdem-{roundId}-payout");
        var resultReference = ResultDocument(roundId);
        return RunWithContentionRetryAsync(() => database.RunTransactionAsync(async transaction =>
        {
            // Read everything before the first write. Concurrent commands retry against the new round version.
            var roundSnapshot = await transaction.GetSnapshotAsync(roundReference, cancellationToken);
            var activeSnapshot = await transaction.GetSnapshotAsync(activeReference, cancellationToken);
            var balanceSnapshot = await transaction.GetSnapshotAsync(balanceReference, cancellationToken);
            var eventSnapshot = await transaction.GetSnapshotAsync(eventReference, cancellationToken);
            var debitSnapshot = await transaction.GetSnapshotAsync(debitReference, cancellationToken);
            var payoutSnapshot = await transaction.GetSnapshotAsync(payoutReference, cancellationToken);
            var resultSnapshot = await transaction.GetSnapshotAsync(resultReference, cancellationToken);
            if (!roundSnapshot.Exists) throw new HouseHoldemRoundNotFoundException();
            var stored = ReadRound(roundSnapshot);
            if (stored.UserId != userId) throw new HouseHoldemRoundNotFoundException();
            if (ReadString(activeSnapshot, "roundId") != roundId && stored.Round.Settlement is null)
                throw new InvalidOperationException("The House Hold'em active round does not match its saved hand.");
            var balanceCents = ReadBalanceCents(balanceSnapshot);
            if (eventSnapshot.Exists)
            {
                if (ReadString(eventSnapshot, "userId") != userId ||
                    ReadString(eventSnapshot, "command") != command)
                    throw new HouseHoldemRoundConflictException("This Idempotency-Key was already used for a different House Hold'em command.");
                return new HouseHoldemStoreResult(roundId, stored.Round, balanceCents);
            }
            if (debitSnapshot.Exists)
                throw new InvalidOperationException("A House Hold'em command wager exists without its event.");
            if (stored.Round.Settlement is not null)
                throw new HouseHoldemRoundConflictException("This House Hold'em hand has already ended.");
            if (payoutSnapshot.Exists || resultSnapshot.Exists)
                throw new InvalidOperationException("A House Hold'em result exists before the round settled.");

            long debitCents;
            if (command == HouseHoldemCommands.Advance)
            {
                if (!HouseHoldemRules.CanAdvance(stored.Round))
                    throw new HouseHoldemRoundConflictException("This House Hold'em hand is waiting for an action.");
                debitCents = 0;
                HouseHoldemRules.Advance(stored.Round);
            }
            else
            {
                if (!HouseHoldemRules.LegalActions(stored.Round).Contains(command, StringComparer.Ordinal))
                    throw new HouseHoldemRoundConflictException("That House Hold'em action is not legal at this stage of the hand.");
                debitCents = HouseHoldemRules.RequiredAdditionalWagerCents(stored.Round, command);
                if (balanceCents < debitCents)
                    throw new HouseHoldemInsufficientCreditsException(balanceCents, debitCents);
                HouseHoldemRules.ApplyAction(stored.Round, command);
            }

            var payoutCents = stored.Round.PayoutCents;
            var afterDebitCents = checked(balanceCents - debitCents);
            var finalBalanceCents = checked(afterDebitCents + payoutCents);
            var updatedSteps = stored.Steps.Append(command).ToArray();
            transaction.Update(roundReference, UpdatedRoundData(stored.Round, updatedSteps, nowUtc));
            if (stored.Round.Settlement is null)
                FortuneForge.Server.SecurityAudit.GameSecurityJournal.Write(transaction, database, "house-holdem", roundId,
                    updatedSteps.Length, nowUtc.UtcDateTime, new { stored.Round, steps = updatedSteps }, [userId], command);
            transaction.Create(eventReference, EventData(roundId, userId, command, idempotencyKey, nowUtc));
            if (debitCents > 0)
                transaction.Create(debitReference, BalanceTransactionData(debitReference.Id, userId,
                    -debitCents, afterDebitCents, "house-holdem-play-wager", idempotencyKey, nowUtc));
            if (payoutCents > 0)
                transaction.Create(payoutReference, BalanceTransactionData(payoutReference.Id, userId,
                    payoutCents, finalBalanceCents, "house-holdem-payout", $"{idempotencyKey}-payout", nowUtc));
            if (debitCents > 0 || payoutCents > 0)
                transaction.Set(balanceReference, BalanceData(finalBalanceCents, nowUtc), SetOptions.MergeAll);
            if (stored.Round.Settlement is not null)
            {
                FortuneForge.Server.RoundAudit.SinglePlayerRoundRecording.Write(transaction, database,
                    "house-holdem", roundId, userId, stored.Round.TotalWagerCents, payoutCents,
                    stored.CreatedAt.UtcDateTime, nowUtc.UtcDateTime, "houseHoldemRounds", roundId,
                    new { stored.Round, steps = updatedSteps }, "house-holdem-cards-and-actions-v1");
                transaction.Create(resultReference, ResultData(roundId, userId, stored.Round,
                    stored.CreatedAt, nowUtc));
                transaction.Set(activeReference, ActiveRoundData(string.Empty, nowUtc));
            }
            return new HouseHoldemStoreResult(roundId, stored.Round, finalBalanceCents);
        }, cancellationToken: cancellationToken), cancellationToken);
    }

    internal static async Task<T> RunWithContentionRetryAsync<T>(Func<Task<T>> operation, CancellationToken cancellationToken)
    {
        // Firestore's transaction retry can still surface ABORTED under a burst of
        // identical requests. A fresh transaction is safe because every command
        // has a durable idempotency event that is checked before any ledger write.
        const int maxAttempts = 4;
        for (var attempt = 1; ; attempt++)
        {
            try { return await operation(); }
            catch (RpcException exception) when (exception.StatusCode == StatusCode.Aborted &&
                                                 attempt < maxAttempts && !cancellationToken.IsCancellationRequested)
            {
                var backoffMs = 40 * (1 << (attempt - 1)) + Random.Shared.Next(0, 40);
                await Task.Delay(backoffMs, cancellationToken);
            }
        }
    }

    internal static string LookupKey(string value) =>
        Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(value)));

    private DocumentReference RoundDocument(string roundId) => database.Collection("houseHoldemRounds").Document(roundId);
    private DocumentReference EventDocument(string roundId, string key) =>
        database.Collection("houseHoldemRoundEvents").Document($"{roundId}-{LookupKey(key)}");
    private DocumentReference ResultDocument(string roundId) =>
        database.Collection("cardGameResults").Document($"house-holdem-{roundId}");
    private DocumentReference ActiveRoundDocument(string userId) =>
        database.Collection("houseHoldemActiveRounds").Document(userId);
    private DocumentReference BalanceDocument(string userId) =>
        database.Collection("userBalances").Document($"{userId}_{CurrencyId}");
    private DocumentReference BalanceTransactionDocument(string transactionId) =>
        database.Collection("balanceTransactions").Document(transactionId);

    private static Dictionary<string, object> RoundData(
        string roundId, string userId, HouseHoldemRound round, IReadOnlyList<string> deck,
        string idempotencyKey, DateTimeOffset nowUtc) => new()
    {
        ["roundId"] = roundId,
        ["userId"] = userId,
        ["anteCents"] = round.AnteCents,
        ["tripsCents"] = round.TripsCents,
        ["deck"] = deck.ToArray(),
        ["steps"] = Array.Empty<string>(),
        ["phase"] = round.Phase,
        ["totalWagerCents"] = round.TotalWagerCents,
        ["payoutCents"] = 0L,
        ["startIdempotencyKey"] = idempotencyKey,
        ["createdAt"] = Timestamp.FromDateTime(nowUtc.UtcDateTime),
        ["updatedAt"] = Timestamp.FromDateTime(nowUtc.UtcDateTime),
        ["schemaVersion"] = 1L,
    };

    private static Dictionary<string, object> UpdatedRoundData(
        HouseHoldemRound round, IReadOnlyList<string> steps, DateTimeOffset nowUtc) => new()
    {
        ["steps"] = steps.ToArray(),
        ["phase"] = round.Phase,
        ["totalWagerCents"] = round.TotalWagerCents,
        ["payoutCents"] = round.PayoutCents,
        ["updatedAt"] = Timestamp.FromDateTime(nowUtc.UtcDateTime),
    };

    private static Dictionary<string, object> EventData(
        string roundId, string userId, string command, string key, DateTimeOffset nowUtc) => new()
    {
        ["roundId"] = roundId,
        ["userId"] = userId,
        ["command"] = command,
        ["idempotencyKey"] = key,
        ["createdAt"] = Timestamp.FromDateTime(nowUtc.UtcDateTime),
        ["schemaVersion"] = 1L,
    };

    private static Dictionary<string, object> ActiveRoundData(string roundId, DateTimeOffset nowUtc) => new()
    {
        ["roundId"] = roundId,
        ["updatedAt"] = Timestamp.FromDateTime(nowUtc.UtcDateTime),
    };

    private static Dictionary<string, object> BalanceData(long cents, DateTimeOffset nowUtc) => new()
    {
        ["available"] = cents / RandMoney.CentsPerRand,
        [FractionalCentsField] = cents % RandMoney.CentsPerRand,
        ["version"] = FieldValue.Increment(1),
        ["updatedAt"] = Timestamp.FromDateTime(nowUtc.UtcDateTime),
    };

    private static Dictionary<string, object> BalanceTransactionData(
        string transactionId, string userId, long amountCents, long balanceAfterCents,
        string type, string key, DateTimeOffset nowUtc) => new()
    {
        ["transactionId"] = transactionId,
        ["userId"] = userId,
        ["currencyId"] = CurrencyId,
        ["amount"] = (double)HouseHoldemMoney.ToRand(amountCents),
        ["balanceAfter"] = (double)HouseHoldemMoney.ToRand(balanceAfterCents),
        ["type"] = type,
        ["idempotencyKey"] = key,
        ["createdAt"] = Timestamp.FromDateTime(nowUtc.UtcDateTime),
    };

    private static Dictionary<string, object> ResultData(
        string roundId, string userId, HouseHoldemRound round,
        DateTimeOffset startedAt, DateTimeOffset completedAt) => new()
    {
        ["resultId"] = $"house-holdem-{roundId}",
        ["game"] = "house-holdem",
        ["mode"] = "credit",
        ["matchId"] = roundId,
        ["userId"] = userId,
        ["wagerCents"] = round.TotalWagerCents,
        ["payoutCents"] = round.PayoutCents,
        ["netCents"] = checked(round.PayoutCents - round.TotalWagerCents),
        ["outcome"] = round.Settlement!.Outcome,
        ["playerHandRank"] = round.Settlement.PlayerHandRank,
        ["dealerHandRank"] = round.Settlement.DealerHandRank,
        ["settlementStatus"] = "paid",
        ["claimStatus"] = "completed",
        ["startedAt"] = Timestamp.FromDateTime(startedAt.UtcDateTime),
        ["completedAt"] = Timestamp.FromDateTime(completedAt.UtcDateTime),
        ["schemaVersion"] = 1L,
    };

    private static StoredRound ReadRound(DocumentSnapshot snapshot)
    {
        if (!snapshot.TryGetValue<string>("roundId", out var roundId) || roundId != snapshot.Id ||
            !snapshot.TryGetValue<string>("userId", out var userId) || string.IsNullOrWhiteSpace(userId) ||
            !snapshot.TryGetValue<long>("anteCents", out var anteCents) ||
            !snapshot.TryGetValue<long>("tripsCents", out var tripsCents) ||
            !snapshot.TryGetValue<long>("schemaVersion", out var schemaVersion) || schemaVersion != 1 ||
            !snapshot.TryGetValue<Timestamp>("createdAt", out var createdAt))
            throw new InvalidOperationException("The House Hold'em round is corrupt.");
        var deck = ReadStringArray(snapshot, "deck");
        var steps = ReadStringArray(snapshot, "steps");
        HouseHoldemRound round;
        try
        {
            round = HouseHoldemRules.RestoreRound(deck, anteCents, tripsCents);
            foreach (var step in steps)
            {
                if (step == HouseHoldemCommands.Advance) HouseHoldemRules.Advance(round);
                else HouseHoldemRules.ApplyAction(round, step);
            }
        }
        catch (Exception exception) when (exception is ArgumentException or InvalidOperationException or OverflowException)
        {
            throw new InvalidOperationException("The House Hold'em round history is corrupt.", exception);
        }
        if (!snapshot.TryGetValue<string>("phase", out var phase) || phase != round.Phase ||
            !snapshot.TryGetValue<long>("totalWagerCents", out var wagerCents) || wagerCents != round.TotalWagerCents ||
            !snapshot.TryGetValue<long>("payoutCents", out var payoutCents) || payoutCents != round.PayoutCents)
            throw new InvalidOperationException("The House Hold'em round balance fields are corrupt.");
        return new StoredRound(roundId, userId, round, steps, createdAt.ToDateTimeOffset());
    }

    private static long ReadBalanceCents(DocumentSnapshot snapshot) => checked(
        ReadLong(snapshot, "available") * RandMoney.CentsPerRand +
        Math.Clamp(ReadLong(snapshot, FractionalCentsField), 0, RandMoney.CentsPerRand - 1));

    private static long ReadLong(DocumentSnapshot snapshot, string field) =>
        snapshot.Exists && snapshot.TryGetValue<long>(field, out var value) ? value : 0;

    private static string ReadString(DocumentSnapshot snapshot, string field) =>
        snapshot.TryGetValue<string>(field, out var value) ? value : string.Empty;

    private static IReadOnlyList<string> ReadStringArray(DocumentSnapshot snapshot, string field)
    {
        if (!snapshot.ToDictionary().TryGetValue(field, out var raw) || raw is not IEnumerable values)
            throw new InvalidOperationException($"The House Hold'em round {field} field is missing.");
        var entries = values.Cast<object?>().Select(value => value as string).ToArray();
        if (entries.Any(value => value is null))
            throw new InvalidOperationException($"The House Hold'em round {field} field is corrupt.");
        return entries.Select(value => value!).ToArray();
    }

    private sealed record StoredRound(
        string RoundId, string UserId, HouseHoldemRound Round,
        IReadOnlyList<string> Steps, DateTimeOffset CreatedAt);
}
