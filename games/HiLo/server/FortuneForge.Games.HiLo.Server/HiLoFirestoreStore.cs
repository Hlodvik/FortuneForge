using System.Text.Json;
using FortuneForge.Games.Cards;
using Google.Cloud.Firestore;

namespace FortuneForge.Games.HiLo.Server;

/// <summary>Canonical slotsCredits wallet, revealed game state, operation journal, event and debit/credit ledger commit in one Firestore transaction.</summary>
public sealed class HiLoFirestoreStore(FirestoreDb database) : IHiLoStore
{
    private const string Currency = "slotsCredits";
    public bool Practice => false;
    public Task<HiLoSessionResponse> GetSessionAsync(string userId, CancellationToken cancellationToken = default) =>
        database.RunTransactionAsync(async transaction =>
        {
            var snapshots = await transaction.GetAllSnapshotsAsync([Head(userId), Wallet(userId)], cancellationToken);
            return HiLoMutations.Snapshot(ReadState(snapshots[0], snapshots[1], userId), false);
        }, cancellationToken: cancellationToken);

    public Task<HiLoSessionResponse> ExecuteAsync(string userId, HiLoCommand command, Func<PlayingCard> draw,
        DateTimeOffset now, CancellationToken cancellationToken = default, bool allowNewMutations = true)
    {
        HiLoService.ValidateCommand(command);
        var key = HiLoService.Hash(userId + "\n" + command.OperationId);
        var operation = database.Collection("hiLoOperations").Document(key);
        var ledger = database.Collection("balanceTransactions").Document("hi-lo-" + key);
        var eventReference = database.Collection("hiLoEvents").Document(key);
        var fingerprint = HiLoService.Fingerprint(command);
        return database.RunTransactionAsync(async transaction =>
        {
            var documents = await transaction.GetAllSnapshotsAsync([Head(userId), Wallet(userId), operation, ledger, eventReference], cancellationToken);
            var state = ReadState(documents[0], documents[1], userId);
            if (documents[2].Exists)
            {
                var record = documents[2];
                if (Required<string>(record, "userId") != userId || Required<string>(record, "operationId") != command.OperationId || Required<long>(record, "schemaVersion") != 1)
                    throw new InvalidOperationException("The Hi-Lo operation identity is corrupt.");
                if (Required<string>(record, "fingerprint") != fingerprint) throw new HiLoException("operation_conflict", "This operationId was already used for a different request.");
                var response = JsonSerializer.Deserialize<HiLoSessionResponse>(Required<string>(record, "responseJson")) ?? throw new InvalidOperationException("The Hi-Lo operation response is corrupt.");
                var delta = Required<long>(record, "walletDeltaCents");
                if (!documents[4].Exists || Required<string>(documents[4], "userId") != userId || Required<string>(documents[4], "fingerprint") != fingerprint ||
                    Required<string>(documents[4], "roundId") != response.Round!.RoundId || documents[3].Exists != (delta != 0))
                    throw new InvalidOperationException("The Hi-Lo operation audit is corrupt.");
                if (delta != 0) ValidateLedger(documents[3], userId, command.OperationId, delta, response.WalletBalanceCents,
                    delta < 0 ? "hi-lo-wager" : "hi-lo-payout");
                if (response.Round!.Status != "active")
                    ValidateHistory(await transaction.GetSnapshotAsync(Result(response.Round.RoundId), cancellationToken), userId, response.Round);
                return response;
            }
            if (documents[3].Exists || documents[4].Exists) throw new InvalidOperationException("A Hi-Lo ledger or event exists without its operation.");
            var mutation = HiLoMutations.Apply(state, command, draw, false, allowNewMutations);
            var publicRound = mutation.Response.Round!;
            var startedAt = command.Kind == "start" ? Timestamp.FromDateTime(now.UtcDateTime)
                : documents[0].TryGetValue<Timestamp>("roundStartedAt", out var savedStart) ? savedStart
                : documents[0].GetValue<Timestamp>("updatedAt");
            var settled = publicRound.Status != "active";
            var result = settled ? Result(publicRound.RoundId) : null;
            if (result is not null && (await transaction.GetSnapshotAsync(result, cancellationToken)).Exists)
                throw new InvalidOperationException("A Hi-Lo history result already exists before this settlement.");
            transaction.Set(Head(userId), new Dictionary<string, object>
            {
                ["schemaVersion"] = 1L, ["userId"] = userId, ["roundJson"] = JsonSerializer.Serialize(mutation.State.Round),
                ["updatedAt"] = Timestamp.FromDateTime(now.UtcDateTime),
                ["roundStartedAt"] = startedAt,
            });
            if (result is not null)
                transaction.Create(result, new Dictionary<string, object>
                {
                    ["resultId"] = result.Id, ["game"] = "hi-lo", ["mode"] = "credit", ["userId"] = userId,
                    ["matchId"] = publicRound.RoundId, ["wagerCents"] = publicRound.WagerCents,
                    ["payoutCents"] = publicRound.CurrentReturnCents,
                    ["netCents"] = checked(publicRound.CurrentReturnCents - publicRound.WagerCents),
                    ["outcome"] = publicRound.Status, ["settlementStatus"] = "paid", ["claimStatus"] = "completed",
                    ["startedAt"] = startedAt, ["completedAt"] = Timestamp.FromDateTime(now.UtcDateTime), ["schemaVersion"] = 1L,
                });
            if (mutation.WalletDeltaCents != 0)
            {
                transaction.Set(Wallet(userId), new Dictionary<string, object>
                {
                    ["available"] = mutation.State.BalanceCents / 100, ["availableFractionalCents"] = mutation.State.BalanceCents % 100,
                    ["version"] = FieldValue.Increment(1), ["updatedAt"] = Timestamp.FromDateTime(now.UtcDateTime),
                }, SetOptions.MergeAll);
                transaction.Create(ledger, new Dictionary<string, object>
                {
                    ["transactionId"] = ledger.Id, ["userId"] = userId, ["currencyId"] = Currency,
                    ["amount"] = mutation.WalletDeltaCents / 100d, ["balanceAfter"] = mutation.State.BalanceCents / 100d,
                    ["amountCents"] = mutation.WalletDeltaCents, ["balanceAfterCents"] = mutation.State.BalanceCents,
                    ["type"] = mutation.LedgerType!, ["idempotencyKey"] = command.OperationId,
                    ["roundId"] = mutation.Response.Round!.RoundId, ["createdAt"] = Timestamp.FromDateTime(now.UtcDateTime),
                });
            }
            transaction.Create(operation, new Dictionary<string, object>
            {
                ["schemaVersion"] = 1L, ["userId"] = userId, ["operationId"] = command.OperationId, ["fingerprint"] = fingerprint,
                ["responseJson"] = JsonSerializer.Serialize(mutation.Response), ["walletDeltaCents"] = mutation.WalletDeltaCents,
                ["createdAt"] = Timestamp.FromDateTime(now.UtcDateTime),
            });
            transaction.Create(eventReference, new Dictionary<string, object>
            {
                ["schemaVersion"] = 1L, ["userId"] = userId, ["roundId"] = mutation.Response.Round!.RoundId,
                ["operationId"] = command.OperationId, ["fingerprint"] = fingerprint, ["choice"] = command.Choice ?? "start",
                ["revision"] = mutation.Response.Round.Revision, ["createdAt"] = Timestamp.FromDateTime(now.UtcDateTime),
            });
            return mutation.Response;
        }, options: TransactionOptions.ForMaxAttempts(20), cancellationToken: cancellationToken);
    }
    private DocumentReference Head(string userId) => database.Collection("hiLoPlayers").Document(HiLoService.Hash(userId));
    private DocumentReference Wallet(string userId) => database.Collection("userBalances").Document(userId + "_" + Currency);
    private DocumentReference Result(string roundId) => database.Collection("cardGameResults").Document("hi-lo-" + roundId);
    private static void ValidateHistory(DocumentSnapshot result, string userId, HiLoRoundResponse round)
    {
        if (!result.Exists || Required<string>(result, "userId") != userId || Required<string>(result, "game") != "hi-lo" ||
            Required<string>(result, "matchId") != round.RoundId || Required<long>(result, "wagerCents") != round.WagerCents ||
            Required<long>(result, "payoutCents") != round.CurrentReturnCents ||
            Required<long>(result, "netCents") != checked(round.CurrentReturnCents - round.WagerCents))
            throw new InvalidOperationException("The Hi-Lo settled history is corrupt.");
    }
    private static HiLoStoredState ReadState(DocumentSnapshot head, DocumentSnapshot wallet, string userId)
    {
        var balance = 0L;
        if (wallet.Exists)
        {
            var whole = Required<long>(wallet, "available");
            var fraction = wallet.TryGetValue<long>("availableFractionalCents", out var value) ? value : 0;
            if (whole < 0 || fraction is < 0 or > 99) throw new InvalidOperationException("The account wallet is corrupt.");
            balance = checked(whole * 100 + fraction);
        }
        if (!head.Exists) return new(balance, null);
        if (Required<string>(head, "userId") != userId || Required<long>(head, "schemaVersion") != 1) throw new InvalidOperationException("The Hi-Lo player identity is corrupt.");
        try
        {
            var round = JsonSerializer.Deserialize<HiLoRound>(Required<string>(head, "roundJson")) ?? throw new InvalidOperationException("The Hi-Lo round is missing.");
            ValidateRound(round);
            return new(balance, round);
        }
        catch (Exception exception) when (exception is JsonException or ArgumentException or OverflowException or HiLoRuleException)
        { throw new InvalidOperationException("The persisted Hi-Lo round is corrupt.", exception); }
    }
    internal static void ValidateRound(HiLoRound round)
    {
        if (round.History.IsDefaultOrEmpty || round.History.Length > 153) throw new InvalidOperationException("The Hi-Lo history is corrupt.");
        var first = round.History[0];
        if (first.Kind != HiLoHistoryKind.Initial) throw new InvalidOperationException("The Hi-Lo initial card is missing.");
        var rebuilt = HiLoEngine.Start(round.Id, round.Wager, first.Card);
        foreach (var entry in round.History.Skip(1))
            rebuilt = entry.Kind == HiLoHistoryKind.Skip ? HiLoEngine.Skip(rebuilt, entry.Card)
                : entry.Kind is HiLoHistoryKind.Win or HiLoHistoryKind.Loss && entry.Direction is { } direction
                    ? HiLoEngine.Guess(rebuilt, direction, entry.Card) : throw new InvalidOperationException("Invalid Hi-Lo history action.");
        if (round.Status == HiLoRoundStatus.CashedOut) rebuilt = HiLoEngine.CashOut(rebuilt);
        if (JsonSerializer.Serialize(rebuilt) != JsonSerializer.Serialize(round)) throw new InvalidOperationException("The Hi-Lo round differs from its revealed history.");
    }
    private static T Required<T>(DocumentSnapshot document, string field) => document.TryGetValue<T>(field, out var value)
        ? value : throw new InvalidOperationException("The Hi-Lo record is missing " + field + ".");
    private static void ValidateLedger(DocumentSnapshot ledger, string userId, string key, long amount, long balance, string type)
    {
        if (Required<string>(ledger, "transactionId") != ledger.Id || Required<string>(ledger, "userId") != userId ||
            Required<string>(ledger, "currencyId") != Currency || Required<string>(ledger, "idempotencyKey") != key ||
            Required<string>(ledger, "type") != type || Required<long>(ledger, "amountCents") != amount ||
            Required<long>(ledger, "balanceAfterCents") != balance || Required<double>(ledger, "amount") != amount / 100d ||
            Required<double>(ledger, "balanceAfter") != balance / 100d)
            throw new InvalidOperationException("The Hi-Lo wallet ledger is corrupt.");
    }
}
