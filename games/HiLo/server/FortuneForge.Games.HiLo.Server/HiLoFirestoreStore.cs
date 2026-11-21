using System.Text.Json;
using FortuneForge.Games.Abstractions.Analytics;
using FortuneForge.Games.Abstractions.Auditing;
using FortuneForge.Games.Abstractions.Wallet;
using FortuneForge.Games.Cards;
using FortuneForge.Games.Firestore;
using Google.Cloud.Firestore;

namespace FortuneForge.Games.HiLo.Server;

/// <summary>Owns admission, revealed state, recovery and history. Only the signed wallet writes money.</summary>
public sealed class HiLoFirestoreStore(FirestoreDb database, IGameWalletClient wallet, IGameWalletAccountReader accounts) : IHiLoStore
{
    public bool Practice => false;
    private DocumentReference Head(string user) => database.Collection("hiLoPlayers").Document(HiLoService.Hash(user));
    private DocumentReference Operation(string user, string operation) => database.Collection("hiLoOperations").Document(HiLoService.Hash(user + "\n" + operation));
    private DocumentReference Result(string round) => database.Collection("cardGameResults").Document("hi-lo-" + round);

    public async Task<HiLoSessionResponse> GetSessionAsync(string userId, CancellationToken cancellationToken = default)
    {
        var head = await Head(userId).GetSnapshotAsync(cancellationToken);
        if (head.TryGetValue<string>("pendingOperationId", out var operation) && operation.Length != 0)
        {
            var pending = await database.RunTransactionAsync(transaction => ReadAsync(transaction, userId, operation, null, cancellationToken), cancellationToken: cancellationToken) ?? throw Corrupt();
            try { await ResolveAsync(userId, pending, cancellationToken); }
            catch (HiLoException error) when (error.Code == "insufficient_funds") { }
            head = await Head(userId).GetSnapshotAsync(cancellationToken);
        }
        return HiLoMutations.Snapshot(ReadState(head, await BalanceAsync(userId, cancellationToken), userId), false);
    }

    public async Task<HiLoSessionResponse> ExecuteAsync(string userId, HiLoCommand command, Func<PlayingCard> draw,
        DateTimeOffset now, CancellationToken cancellationToken = default, bool allowNewMutations = true)
    {
        HiLoService.ValidateCommand(command);
        var existing = await database.RunTransactionAsync(transaction => ReadAsync(transaction, userId, command.OperationId, command, cancellationToken), cancellationToken: cancellationToken);
        if (existing is not null) return await ResolveAsync(userId, existing, cancellationToken);
        var balance = await BalanceAsync(userId, cancellationToken);
        var prepared = await database.RunTransactionAsync(async transaction =>
        {
            var prior = await ReadAsync(transaction, userId, command.OperationId, command, cancellationToken);
            if (prior is not null) return prior;
            var head = await transaction.GetSnapshotAsync(Head(userId), cancellationToken);
            if (head.TryGetValue<string>("pendingOperationId", out var pending) && pending.Length != 0)
                throw new HiLoException("operation_pending", "Restore the pending Hi-Lo action before choosing again.");
            var fields = new Dictionary<string, object>
            {
                ["schemaVersion"] = 2L, ["userId"] = userId, ["operationId"] = command.OperationId,
                ["fingerprint"] = HiLoService.Fingerprint(command), ["status"] = "pending",
                ["createdAt"] = Timestamp.FromDateTime(now.UtcDateTime),
            };
            HiLoMutation mutation;
            try { mutation = HiLoMutations.Apply(ReadState(head, balance, userId), command, draw, false, allowNewMutations); }
            catch (HiLoException error) when (error.Code == "insufficient_funds")
            {
                var rejection = new HiLoErrorResponse(error.Code, error.Message);
                fields["status"] = "rejected";
                fields["commandJson"] = JsonSerializer.Serialize(command);
                fields["balanceAtRejectionCents"] = balance;
                fields["errorJson"] = JsonSerializer.Serialize(rejection);
                transaction.Create(Operation(userId, command.OperationId), fields);
                return new HiLoPaidOperation(null, null, rejection);
            }
            var round = mutation.State.Round!;
            if (round.Status != HiLoRoundStatus.Active && (await transaction.GetSnapshotAsync(Result(round.Id.ToString("N")), cancellationToken)).Exists) throw Corrupt();
            var started = command.Kind == "start" ? now : head.TryGetValue<Timestamp>("roundStartedAt", out var start)
                ? new DateTimeOffset(start.ToDateTime()) : new DateTimeOffset(Required<Timestamp>(head, "updatedAt").ToDateTime());
            var reservationId = command.Kind == "start" ? "round-" + round.Id.ToString("N")
                : head.TryGetValue<string>("walletReservationOperationId", out var reserved) ? reserved : null;
            var digest = HiLoService.Hash(JsonSerializer.Serialize(new { command, round }));
            var reservationHash = command.Kind == "start" ? digest : head.TryGetValue<string>("walletReservationSha256", out var hash) ? hash : null;
            if ((reservationId is null) != (reservationHash is null)) throw Corrupt();
            var request = WalletRequest(command, round, reservationId, reservationHash);
            var observation = GameSecurityContext.Current;
            if (observation is not null && (observation.UserId != userId || observation.GameId != "hi-lo")) throw Corrupt();
            var intent = new HiLoPaidIntent(command, mutation, now, started, reservationId, reservationHash, request, observation);
            fields["intentJson"] = JsonSerializer.Serialize(intent);
            transaction.Create(Operation(userId, command.OperationId), fields);
            transaction.Set(Head(userId), new Dictionary<string, object>
            {
                ["schemaVersion"] = 1L, ["userId"] = userId, ["pendingOperationId"] = command.OperationId,
            }, SetOptions.MergeAll);
            return new HiLoPaidOperation(intent, null, null);
        }, options: TransactionOptions.ForMaxAttempts(20), cancellationToken: cancellationToken);
        return await ResolveAsync(userId, prepared, cancellationToken);
    }

    private async Task<long> BalanceAsync(string user, CancellationToken token) =>
        (await accounts.GetStateAsync(user, token)).SlotsCreditsBalanceCents is { } balance && balance >= 0 ? balance : throw new GameWalletUnavailableException();

    private async Task<HiLoSessionResponse> ResolveAsync(string user, HiLoPaidOperation operation, CancellationToken token)
    {
        if (operation.Response is { } replay) return replay;
        if (operation.Error is { } error) throw new HiLoException(error.Code, error.Message);
        var intent = operation.Intent ?? throw Corrupt();
        ValidateIntent(intent);
        var receipt = await wallet.SettleAsync(user, intent.Wallet, token);
        ValidateWallet(user, intent.Wallet, receipt);
        receipt = receipt with { WasAlreadySettled = false };
        var result = await database.RunTransactionAsync(async transaction =>
        {
            var saved = await ReadAsync(transaction, user, intent.Command.OperationId, intent.Command, token) ?? throw Corrupt();
            if (saved.Response is not null || saved.Error is not null) return saved;
            if (JsonSerializer.Serialize(saved.Intent) != JsonSerializer.Serialize(intent)) throw Corrupt();
            var head = await transaction.GetSnapshotAsync(Head(user), token);
            if (Required<string>(head, "pendingOperationId") != intent.Command.OperationId) throw Corrupt();
            var response = intent.Mutation.Response with { WalletBalanceCents = receipt.BalanceAfterCents };
            var round = response.Round!;
            if (!receipt.InsufficientFunds && round.Status != "active" && (await transaction.GetSnapshotAsync(Result(round.RoundId), token)).Exists) throw Corrupt();
            var fields = new Dictionary<string, object>
            {
                ["status"] = receipt.InsufficientFunds ? "declined" : "completed", ["walletReceiptJson"] = JsonSerializer.Serialize(receipt),
                ["walletDeltaCents"] = intent.Mutation.WalletDeltaCents,
            };
            transaction.Set(Head(user), new Dictionary<string, object> { ["pendingOperationId"] = FieldValue.Delete }, SetOptions.MergeAll);
            if (receipt.InsufficientFunds)
            {
                var declined = new HiLoErrorResponse("insufficient_funds", "The wallet has insufficient credits for this wager.");
                fields["errorJson"] = JsonSerializer.Serialize(declined);
                transaction.Update(Operation(user, intent.Command.OperationId), fields);
                return new HiLoPaidOperation(intent, null, declined);
            }
            fields["responseJson"] = JsonSerializer.Serialize(response);
            transaction.Update(Operation(user, intent.Command.OperationId), fields);
            transaction.Set(Head(user), new Dictionary<string, object>
            {
                ["roundJson"] = JsonSerializer.Serialize(intent.Mutation.State.Round),
                ["updatedAt"] = Timestamp.FromDateTime(intent.OccurredAtUtc.UtcDateTime),
                ["roundStartedAt"] = Timestamp.FromDateTime(intent.StartedAtUtc.UtcDateTime),
                ["walletReservationOperationId"] = (object?)intent.ReservationOperationId ?? FieldValue.Delete,
                ["walletReservationSha256"] = (object?)intent.ReservationSha256 ?? FieldValue.Delete,
            }, SetOptions.MergeAll);
            WriteAccepted(transaction, user, intent, response, receipt);
            return new HiLoPaidOperation(intent, response, null);
        }, options: TransactionOptions.ForMaxAttempts(20), cancellationToken: token);
        if (result.Error is { } failure) throw new HiLoException(failure.Code, failure.Message);
        return result.Response ?? throw Corrupt();
    }

    private void WriteAccepted(Transaction transaction, string user, HiLoPaidIntent intent, HiLoSessionResponse response, GameWalletSettlementReceipt receipt)
    {
        var round = response.Round!;
        using var scope = intent.Observation is { } observation ? GameSecurityContext.Push(observation) : null;
        FirestoreGameAudit.WriteObservation(transaction, database, "hi-lo", round.RoundId, round.Revision,
            intent.OccurredAtUtc.UtcDateTime, intent.Mutation.State.Round!, [user], intent.Command.Kind);
        FirestoreGameAudit.WriteAcceptedParticipation(transaction, database, "hi-lo", user, intent.OccurredAtUtc.UtcDateTime);
        transaction.Create(database.Collection("hiLoEvents").Document(Operation(user, intent.Command.OperationId).Id), new Dictionary<string, object>
        {
            ["schemaVersion"] = 2L, ["userId"] = user, ["roundId"] = round.RoundId,
            ["operationId"] = intent.Command.OperationId, ["fingerprint"] = HiLoService.Fingerprint(intent.Command),
            ["choice"] = intent.Command.Choice ?? "start", ["revision"] = round.Revision,
            ["walletSettlementId"] = receipt.SettlementId, ["createdAt"] = Timestamp.FromDateTime(intent.OccurredAtUtc.UtcDateTime),
        });
        if (round.Status == "active") return;
        var result = Result(round.RoundId);
        var record = SinglePlayerRoundRecord.Create("hi-lo", round.RoundId, user, round.WagerCents, round.CurrentReturnCents,
            intent.StartedAtUtc.UtcDateTime, intent.OccurredAtUtc.UtcDateTime, "cardGameResults", result.Id,
            intent.Mutation.State.Round!, "hi-lo-revealed-cards-and-choices-v1");
        transaction.Create(database.Collection(GameAnalyticsOutbox.CollectionName).Document(record.Settlement.Id), record.Settlement.Fields);
        FirestoreGameAudit.WriteCompletion(transaction, database, record.Audit);
        transaction.Create(result, new Dictionary<string, object>
        {
            ["resultId"] = result.Id, ["game"] = "hi-lo", ["mode"] = "credit", ["userId"] = user,
            ["matchId"] = round.RoundId, ["wagerCents"] = round.WagerCents, ["payoutCents"] = round.CurrentReturnCents,
            ["netCents"] = checked(round.CurrentReturnCents - round.WagerCents), ["outcome"] = round.Status,
            ["settlementStatus"] = "paid", ["claimStatus"] = "completed", ["schemaVersion"] = 2L,
            ["startedAt"] = Timestamp.FromDateTime(intent.StartedAtUtc.UtcDateTime),
            ["completedAt"] = Timestamp.FromDateTime(intent.OccurredAtUtc.UtcDateTime),
        });
    }

    private async Task<HiLoPaidOperation?> ReadAsync(Transaction transaction, string user, string operationId, HiLoCommand? command, CancellationToken token)
    {
        var reference = Operation(user, operationId);
        var document = await transaction.GetSnapshotAsync(reference, token);
        if (!document.Exists)
        {
            var witnesses = await transaction.GetAllSnapshotsAsync([
                database.Collection("balanceTransactions").Document("hi-lo-" + reference.Id),
                database.Collection("hiLoEvents").Document(reference.Id)], token);
            if (witnesses.Any(witness => witness.Exists)) throw Corrupt();
            return null;
        }
        if (Required<string>(document, "userId") != user || Required<string>(document, "operationId") != operationId) throw Corrupt();
        if (command is not null && Required<string>(document, "fingerprint") != HiLoService.Fingerprint(command))
            throw new HiLoException("operation_conflict", "This operationId was already used for a different request.");
        if (Required<long>(document, "schemaVersion") == 1) return await ReadLegacyAsync(transaction, document, user, operationId, token);
        if (Required<long>(document, "schemaVersion") != 2) throw Corrupt();
        if (Required<string>(document, "status") == "rejected")
        {
            var rejected = Deserialize<HiLoCommand>(document, "commandJson");
            var error = Deserialize<HiLoErrorResponse>(document, "errorJson");
            var balance = Required<long>(document, "balanceAtRejectionCents");
            HiLoRules.ValidateExistingWager(rejected.WagerCents / 100m);
            if (rejected.Kind != "start" || rejected.OperationId != operationId || HiLoService.Fingerprint(rejected) != Required<string>(document, "fingerprint") ||
                balance < 0 || balance >= rejected.WagerCents ||
                error.Code != "insufficient_funds" || string.IsNullOrWhiteSpace(error.Message)) throw Corrupt();
            return new(null, null, error);
        }
        var intent = Deserialize<HiLoPaidIntent>(document, "intentJson");
        ValidateIntent(intent);
        if (intent.Command.OperationId != operationId || HiLoService.Fingerprint(intent.Command) != Required<string>(document, "fingerprint")) throw Corrupt();
        if (Required<string>(document, "status") == "pending") return new(intent, null, null);
        var receipt = Deserialize<GameWalletSettlementReceipt>(document, "walletReceiptJson");
        ValidateWallet(user, intent.Wallet, receipt);
        if (Required<long>(document, "walletDeltaCents") != intent.Mutation.WalletDeltaCents) throw Corrupt();
        if (Required<string>(document, "status") == "declined")
        {
            if (!receipt.InsufficientFunds) throw Corrupt();
            return new(intent, null, Deserialize<HiLoErrorResponse>(document, "errorJson"));
        }
        if (Required<string>(document, "status") != "completed" || receipt.InsufficientFunds) throw Corrupt();
        var response = Deserialize<HiLoSessionResponse>(document, "responseJson");
        if (JsonSerializer.Serialize(response) != JsonSerializer.Serialize(intent.Mutation.Response with { WalletBalanceCents = receipt.BalanceAfterCents })) throw Corrupt();
        var eventDocument = await transaction.GetSnapshotAsync(database.Collection("hiLoEvents").Document(reference.Id), token);
        if (!eventDocument.Exists || Required<string>(eventDocument, "userId") != user || Required<string>(eventDocument, "fingerprint") != HiLoService.Fingerprint(intent.Command) ||
            Required<string>(eventDocument, "walletSettlementId") != receipt.SettlementId || Required<string>(eventDocument, "operationId") != operationId ||
            Required<string>(eventDocument, "roundId") != response.Round!.RoundId || Required<long>(eventDocument, "revision") != response.Round.Revision) throw Corrupt();
        if (response.Round!.Status != "active") ValidateHistory(await transaction.GetSnapshotAsync(Result(response.Round.RoundId), token), user, response.Round);
        return new(intent, response, null);
    }

    private async Task<HiLoPaidOperation> ReadLegacyAsync(Transaction transaction, DocumentSnapshot document, string user, string operation, CancellationToken token)
    {
        var response = Deserialize<HiLoSessionResponse>(document, "responseJson");
        var delta = Required<long>(document, "walletDeltaCents");
        var ledger = database.Collection("balanceTransactions").Document("hi-lo-" + document.Id);
        var records = await transaction.GetAllSnapshotsAsync([ledger, database.Collection("hiLoEvents").Document(document.Id)], token);
        var observation = records[1];
        if (!observation.Exists || Required<string>(observation, "userId") != user ||
            Required<string>(observation, "fingerprint") != Required<string>(document, "fingerprint") ||
            Required<string>(observation, "roundId") != response.Round!.RoundId || records[0].Exists != (delta != 0)) throw Corrupt();
        if (delta != 0)
        {
            var saved = records[0];
            if (Required<string>(saved, "transactionId") != ledger.Id || Required<string>(saved, "userId") != user ||
                Required<string>(saved, "currencyId") != "slotsCredits" || Required<string>(saved, "idempotencyKey") != operation ||
                Required<string>(saved, "type") != (delta < 0 ? "hi-lo-wager" : "hi-lo-payout") ||
                Required<long>(saved, "amountCents") != delta || Required<long>(saved, "balanceAfterCents") != response.WalletBalanceCents ||
                Required<double>(saved, "amount") != delta / 100d || Required<double>(saved, "balanceAfter") != response.WalletBalanceCents / 100d) throw Corrupt();
        }
        if (response.Round.Status != "active") ValidateHistory(await transaction.GetSnapshotAsync(Result(response.Round.RoundId), token), user, response.Round);
        return new(null, response, null);
    }

    private static HiLoStoredState ReadState(DocumentSnapshot head, long balance, string user)
    {
        if (!head.Exists) return new(balance, null);
        if (Required<string>(head, "userId") != user || Required<long>(head, "schemaVersion") != 1) throw Corrupt();
        if (!head.ContainsField("roundJson")) return new(balance, null);
        var round = Deserialize<HiLoRound>(head, "roundJson");
        ValidateSavedRound(round);
        return new(balance, round);
    }

    private static GameWalletSettlementRequest WalletRequest(HiLoCommand command, HiLoRound round, string? reservation, string? reservationHash)
    {
        var hash = HiLoService.Hash(JsonSerializer.Serialize(new { command, round }));
        return command.Kind == "start"
            ? new("round-" + round.Id.ToString("N"), hash, HiLoService.Cents(round.Wager), 0, Phase: GameWalletSettlementPhase.Reserve)
            : round.Status != HiLoRoundStatus.Active && reservation is not null
                ? new(reservation, hash, 0, HiLoService.Cents(round.Payout), Phase: GameWalletSettlementPhase.CompleteReservation, ReservationSha256: reservationHash)
                : new("action-" + command.OperationId, hash, 0, round.Status != HiLoRoundStatus.Active ? HiLoService.Cents(round.Payout) : 0);
    }

    private static void ValidateIntent(HiLoPaidIntent intent)
    {
        try { ValidateIntentCore(intent); }
        catch (Exception error) when (error is HiLoException or ArgumentException or OverflowException)
        { throw Corrupt(); }
    }

    private static void ValidateIntentCore(HiLoPaidIntent intent)
    {
        ArgumentNullException.ThrowIfNull(intent.Command);
        ArgumentNullException.ThrowIfNull(intent.Mutation);
        ArgumentNullException.ThrowIfNull(intent.Mutation.State);
        ArgumentNullException.ThrowIfNull(intent.Mutation.Response);
        HiLoService.ValidateCommand(intent.Command);
        var round = intent.Mutation.State.Round ?? throw Corrupt();
        HiLoEngine.ValidateRevealedHistory(round);
        var expected = WalletRequest(intent.Command, round, intent.ReservationOperationId, intent.ReservationSha256);
        var delta = intent.Command.Kind == "start" ? -HiLoService.Cents(round.Wager) : round.Status == HiLoRoundStatus.Active ? 0 : HiLoService.Cents(round.Payout);
        if (intent.Wallet != expected || (intent.ReservationOperationId is null) != (intent.ReservationSha256 is null) ||
            intent.Mutation.WalletDeltaCents != delta || intent.Command.Kind == "start" && round.Version != 0 ||
            intent.Command.Kind == "action" && (round.Id.ToString("N") != intent.Command.RoundId || round.Version != intent.Command.ExpectedRevision + 1) ||
            intent.Command.Kind == "start" && (intent.ReservationSha256 != expected.OutcomeSha256 || intent.ReservationOperationId != expected.OperationId || intent.Command.WagerCents != expected.WagerCents) ||
            JsonSerializer.Serialize(intent.Mutation.Response) != JsonSerializer.Serialize(HiLoMutations.Snapshot(intent.Mutation.State, false) with { Enabled = intent.Mutation.Response.Enabled })) throw Corrupt();
    }

    private static void ValidateWallet(string user, GameWalletSettlementRequest request, GameWalletSettlementReceipt receipt)
    {
        if (!GameWalletReceiptVerifier.MatchesSettlement(receipt, "hi-lo", user, request) ||
            receipt.InsufficientLegacyFreeGames || receipt.LegacyFreeGamesCountMismatch || receipt.LegacyFreeGameTransactionId is not null) throw Corrupt();
    }

    private static void ValidateHistory(DocumentSnapshot result, string user, HiLoRoundResponse round)
    {
        if (!result.Exists || Required<string>(result, "userId") != user || Required<string>(result, "game") != "hi-lo" ||
            Required<string>(result, "matchId") != round.RoundId || Required<long>(result, "wagerCents") != round.WagerCents ||
            Required<long>(result, "payoutCents") != round.CurrentReturnCents || Required<long>(result, "netCents") != checked(round.CurrentReturnCents - round.WagerCents)) throw Corrupt();
    }
    private static void ValidateSavedRound(HiLoRound round)
    {
        try { HiLoEngine.ValidateRevealedHistory(round); }
        catch (Exception error) when (error is ArgumentException or OverflowException) { throw Corrupt(); }
    }
    private static T Deserialize<T>(DocumentSnapshot document, string field)
    {
        try { return JsonSerializer.Deserialize<T>(Required<string>(document, field)) ?? throw Corrupt(); }
        catch (Exception error) when (error is JsonException or ArgumentException or OverflowException) { throw Corrupt(); }
    }
    private static T Required<T>(DocumentSnapshot document, string field) => document.TryGetValue<T>(field, out var value) ? value : throw Corrupt();
    private static InvalidOperationException Corrupt() => new("The persisted Hi-Lo operation, state or wallet receipt is invalid.");
}

internal sealed record HiLoPaidIntent(HiLoCommand Command, HiLoMutation Mutation, DateTimeOffset OccurredAtUtc,
    DateTimeOffset StartedAtUtc, string? ReservationOperationId, string? ReservationSha256,
    GameWalletSettlementRequest Wallet, GameRequestObservation? Observation);
internal sealed record HiLoPaidOperation(HiLoPaidIntent? Intent, HiLoSessionResponse? Response, HiLoErrorResponse? Error);
