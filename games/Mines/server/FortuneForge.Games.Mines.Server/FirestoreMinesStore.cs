using System.Text.Json;
using FortuneForge.Games.Abstractions.Wallet;
using Google.Cloud.Firestore;
using FortuneForge.Games.Firestore;
using FortuneForge.Games.Abstractions.Auditing;

namespace FortuneForge.Games.Mines.Server;

/// <summary>Private command journal serializes account actions around the signed wallet RPC.</summary>
public sealed class FirestoreMinesStore(FirestoreDb database, IGameWalletClient wallet,
    IGameWalletAccountReader accounts, Func<bool>? admitNewRound = null) : IMinesStore
{
    public bool Practice => false;
    private sealed record Pending(string UserId, string Fingerprint, MinesMutation Mutation,
        GameWalletSettlementRequest? WalletRequest, GameRequestObservation? Observation = null);
    private sealed record Prepared(string CommandId, Pending? Pending = null, MinesSessionResponse? Response = null,
        bool RequestedCommand = true, bool Declined = false);

    public async Task<MinesTransactionSnapshot> ReadAsync(string userId, CancellationToken cancellationToken = default)
    {
        var pointer = await Session(userId).GetSnapshotAsync(cancellationToken);
        if (pointer.TryGetValue<string>("pendingCommandId", out var commandId))
            await FinishAsync(userId, commandId, cancellationToken);
        var state = await accounts.GetStateAsync(userId, cancellationToken);
        return await database.RunTransactionAsync(async transaction =>
        {
            var current = await transaction.GetSnapshotAsync(Session(userId), cancellationToken);
            var latest = await ReadLatest(transaction, userId, current, cancellationToken);
            return new MinesTransactionSnapshot(Balance(state), latest, null, null);
        }, cancellationToken: cancellationToken);
    }

    public async Task<MinesSessionResponse> ExecuteAsync(string userId, string commandId, string fingerprint, string? roundId,
        Func<MinesTransactionSnapshot, MinesMutation> action, CancellationToken cancellationToken = default)
    {
        // An immutable reply needs no live account call, including during a wallet outage.
        var existing = await Command(commandId).GetSnapshotAsync(cancellationToken);
        if (existing.Exists)
        {
            if (RequiredString(existing, "userId") != userId || RequiredString(existing, "fingerprint") != fingerprint)
                throw new MinesConflictException("This requestId was already used for a different action or payload.");
            if (existing.ContainsField("insufficientFunds")) { ValidateDecline(existing, userId); throw new MinesInsufficientFundsException(); }
            if (existing.ContainsField("responseJson")) return ReadReceipt(existing, userId).Response;
            return await FinishAsync(userId, commandId, cancellationToken);
        }
        for (var attempt = 0; attempt < 20; attempt++)
        {
            var balance = Balance(await accounts.GetStateAsync(userId, cancellationToken));
            var prepared = await database.RunTransactionAsync(async transaction =>
            {
                var reads = await transaction.GetAllSnapshotsAsync([Command(commandId), Session(userId)], cancellationToken);
                if (reads[0].Exists)
                {
                    if (RequiredString(reads[0], "userId") != userId || RequiredString(reads[0], "fingerprint") != fingerprint)
                        throw new MinesConflictException("This requestId was already used for a different action or payload.");
                    if (reads[0].ContainsField("insufficientFunds")) { ValidateDecline(reads[0], userId); throw new MinesInsufficientFundsException(); }
                    if (reads[0].ContainsField("responseJson")) return new Prepared(commandId, Response: ReadReceipt(reads[0], userId).Response);
                    return new Prepared(commandId, ReadPending(reads[0], userId));
                }
                if (reads[1].TryGetValue<string>("pendingCommandId", out var busy))
                    return new Prepared(busy, RequestedCommand: false);
                var latest = await ReadLatest(transaction, userId, reads[1], cancellationToken);
                MinesStoredRound? target = null;
                if (roundId is not null)
                {
                    if (latest?.RoundId == roundId) target = latest;
                    else
                    {
                        var targetSnapshot = await transaction.GetSnapshotAsync(Round(roundId), cancellationToken);
                        if (targetSnapshot.Exists && RequiredString(targetSnapshot, "userId") == userId) target = ReadRound(targetSnapshot, userId);
                    }
                }
                if (roundId is null && admitNewRound?.Invoke() == false) throw new MinesAdmissionClosedException();
                MinesMutation mutation;
                try { mutation = action(new(balance, latest, target, null)); }
                catch (MinesInsufficientFundsException)
                {
                    var required = StartWager(fingerprint);
                    if (required <= balance) throw new InvalidOperationException("Invalid Mines admission decline.");
                    transaction.Create(Command(commandId), new Dictionary<string, object>
                    {
                        ["userId"] = userId, ["fingerprint"] = fingerprint, ["schemaVersion"] = 1L,
                        ["insufficientFunds"] = true, ["preflightInsufficientFunds"] = true,
                        ["balanceBeforeCents"] = balance, ["requiredWagerCents"] = required,
                    });
                    return new Prepared(commandId, Declined: true);
                }
                var round = mutation.Round ?? throw new InvalidOperationException("A Mines command must save a round.");
                round.Validate(userId);
                if (mutation.Response.Practice || mutation.Response.BalanceCents != checked(balance + mutation.BalanceDeltaCents))
                    throw new InvalidOperationException("Invalid Mines wallet projection.");
                GameWalletSettlementRequest? request = null;
                if (mutation.BalanceDeltaCents < 0)
                {
                    var reservation = MinesService.Hash(JsonSerializer.Serialize(round, MinesService.StorageJson));
                    round = round with { ReservationSha256 = reservation };
                    mutation = mutation with { Round = round };
                    request = new(round.RoundId, reservation, round.State.WagerCents, 0, Phase: GameWalletSettlementPhase.Reserve);
                }
                else if (round.State.Status != MinesRoundStatus.Active)
                {
                    var digest = MinesService.Hash(JsonSerializer.Serialize(round, MinesService.StorageJson));
                    request = round.ReservationSha256 is { } reservation
                        ? new(round.RoundId, digest, 0, round.State.PayoutCents, Phase: GameWalletSettlementPhase.CompleteReservation, ReservationSha256: reservation)
                        : new(commandId, digest, 0, round.State.PayoutCents);
                }
                var pending = new Pending(userId, fingerprint, mutation, request, GameSecurityContext.Current);
                transaction.Create(Command(commandId), new Dictionary<string, object>
                {
                    ["userId"] = userId, ["fingerprint"] = fingerprint, ["schemaVersion"] = 1L,
                    ["pendingJson"] = JsonSerializer.Serialize(pending, MinesService.StorageJson),
                });
                transaction.Set(Session(userId), new Dictionary<string, object>
                {
                    ["userId"] = userId, ["pendingCommandId"] = commandId, ["schemaVersion"] = 1L,
                }, SetOptions.MergeAll);
                return new Prepared(commandId, pending);
            }, options: TransactionOptions.ForMaxAttempts(20), cancellationToken: cancellationToken);
            if (prepared.Declined) throw new MinesInsufficientFundsException();
            if (prepared.Response is { } reply) return reply;
            var result = await FinishAsync(userId, prepared.CommandId, cancellationToken);
            if (prepared.RequestedCommand) return result;
        }
        throw new GameWalletUnavailableException();
    }

    private async Task<MinesSessionResponse> FinishAsync(string userId, string commandId, CancellationToken token)
    {
        var snapshot = await Command(commandId).GetSnapshotAsync(token);
        if (snapshot.ContainsField("responseJson")) return ReadReceipt(snapshot, userId).Response;
        if (snapshot.ContainsField("insufficientFunds")) { ValidateDecline(snapshot, userId); throw new MinesInsufficientFundsException(); }
        var pending = ReadPending(snapshot, userId);
        using var originalRequest = pending.Observation is { } observation ? GameSecurityContext.Push(observation) : null;
        var mutation = pending.Mutation;
        var round = mutation.Round ?? throw new InvalidOperationException("Missing prepared Mines round.");
        if (round.State.Status != MinesRoundStatus.Active)
        {
            var completed = await database.RunTransactionAsync(async transaction =>
            {
                var reads = await transaction.GetAllSnapshotsAsync([Command(commandId), Result(round.RoundId)], token);
                if (reads[0].ContainsField("responseJson")) return ReadReceipt(reads[0], userId).Response;
                if (reads[1].Exists) throw new InvalidOperationException("The Mines round already has a terminal history record.");
                return null;
            }, cancellationToken: token);
            if (completed is not null) return completed;
        }
        var receipt = pending.WalletRequest is { } request ? await wallet.SettleAsync(userId, request, token) : null;
        if (receipt is not null && !GameWalletReceiptVerifier.MatchesSettlement(receipt, "mines", userId, pending.WalletRequest!))
            throw new GameWalletUnavailableException();
        if (receipt?.InsufficientFunds == true)
        {
            await database.RunTransactionAsync(async transaction =>
            {
                var pointer = await transaction.GetSnapshotAsync(Session(userId), token);
                var command = await transaction.GetSnapshotAsync(Command(commandId), token);
                if (command.ContainsField("responseJson")) return;
                transaction.Update(Command(commandId), new Dictionary<string, object>
                {
                    ["insufficientFunds"] = true, ["walletReceiptJson"] = JsonSerializer.Serialize(receipt),
                });
                if (pointer.TryGetValue<string>("pendingCommandId", out var busy) && busy == commandId)
                    transaction.Update(Session(userId), "pendingCommandId", FieldValue.Delete);
            }, cancellationToken: token);
            throw new MinesInsufficientFundsException();
        }
        var response = mutation.Response with { BalanceCents = receipt?.BalanceAfterCents ?? mutation.Response.BalanceCents };
        mutation = mutation with { Response = response };
        return await database.RunTransactionAsync(async transaction =>
        {
            var reads = await transaction.GetAllSnapshotsAsync([Command(commandId), Session(userId)], token);
            if (reads[0].ContainsField("responseJson")) return ReadReceipt(reads[0], userId).Response;
            var original = ReadPending(reads[0], userId);
            if (JsonSerializer.Serialize(original) != JsonSerializer.Serialize(pending) ||
                RequiredString(reads[1], "pendingCommandId") != commandId)
                throw new InvalidOperationException("The Mines command journal changed during settlement.");
            var result = round.State.Status == MinesRoundStatus.Active ? null : Result(round.RoundId);
            if (result is not null && (await transaction.GetSnapshotAsync(result, token)).Exists)
                throw new InvalidOperationException("A Mines result already exists before settlement.");
            Commit(transaction, userId, commandId, pending.Fingerprint, round, mutation, result);
            var proof = new Dictionary<string, object>
            {
                ["walletProtocol"] = 1L, ["pendingJson"] = JsonSerializer.Serialize(pending, MinesService.StorageJson),
            };
            if (receipt is not null) proof["walletReceiptJson"] = JsonSerializer.Serialize(receipt with { WasAlreadySettled = false });
            transaction.Update(Command(commandId), proof);
            return response;
        }, options: TransactionOptions.ForMaxAttempts(20), cancellationToken: token);
    }

    private static Pending ReadPending(DocumentSnapshot snapshot, string userId)
    {
        try { return ReadPendingCore(snapshot, userId); }
        catch (Exception error) when (error is ArgumentException or JsonException or OverflowException)
        { throw new InvalidOperationException("The persisted Mines command is corrupt.", error); }
    }

    private static Pending ReadPendingCore(DocumentSnapshot snapshot, string userId)
    {
        if (RequiredString(snapshot, "userId") != userId) throw new MinesNotFoundException();
        var pending = JsonSerializer.Deserialize<Pending>(RequiredString(snapshot, "pendingJson"), MinesService.StorageJson)
            ?? throw new InvalidOperationException("Empty Mines command journal.");
        if (pending.UserId != userId || pending.Fingerprint != RequiredString(snapshot, "fingerprint"))
            throw new InvalidOperationException("Corrupt Mines command journal.");
        var round = pending.Mutation.Round ?? throw new InvalidOperationException("Missing Mines round.");
        round.Validate(userId);
        GameWalletSettlementRequest? expected = pending.Mutation.BalanceDeltaCents < 0
            ? new(round.RoundId, MinesService.Hash(JsonSerializer.Serialize(round with { ReservationSha256 = null }, MinesService.StorageJson)),
                round.State.WagerCents, 0, Phase: GameWalletSettlementPhase.Reserve)
            : round.State.Status != MinesRoundStatus.Active
                ? round.ReservationSha256 is { } reservation
                    ? new(round.RoundId, MinesService.Hash(JsonSerializer.Serialize(round, MinesService.StorageJson)), 0, round.State.PayoutCents,
                        Phase: GameWalletSettlementPhase.CompleteReservation, ReservationSha256: reservation)
                    : new(snapshot.Id, MinesService.Hash(JsonSerializer.Serialize(round, MinesService.StorageJson)), 0, round.State.PayoutCents)
                : null;
        if (pending.WalletRequest != expected || expected?.Phase == GameWalletSettlementPhase.Reserve && round.ReservationSha256 != expected.OutcomeSha256)
            throw new InvalidOperationException("Corrupt Mines wallet request.");
        return pending;
    }

    private static long Balance(GameWalletAccountState state) => state.SlotsCreditsBalanceCents ?? throw new GameWalletUnavailableException();

    private void WriteRoundRecord(Transaction transaction, FortuneForge.Games.Abstractions.Analytics.SinglePlayerRoundRecord record)
    {
        transaction.Create(database.Collection(FortuneForge.Games.Abstractions.Analytics.GameAnalyticsOutbox.CollectionName).Document(record.Settlement.Id), record.Settlement.Fields);
        FirestoreGameAudit.WriteCompletion(transaction, database, record.Audit);
    }

    private void Commit(Transaction transaction, string userId, string commandId, string fingerprint,
        MinesStoredRound round, MinesMutation mutation, DocumentReference? result)
    {
        FirestoreGameAudit.WriteObservation(transaction, database, "mines", round.RoundId, round.Version,
            round.UpdatedAt.UtcDateTime, round, [userId]);
        FirestoreGameAudit.WriteAcceptedParticipation(transaction, database, "mines", userId, round.UpdatedAt.UtcDateTime);
        if (result is not null)
        {
            var analytics = FortuneForge.Games.Abstractions.Analytics.SinglePlayerRoundRecord.Create(
                "mines", round.RoundId, userId, round.State.WagerCents, round.State.PayoutCents,
                round.CreatedAt.UtcDateTime, round.UpdatedAt.UtcDateTime, "minesRounds", round.RoundId,
                round, "mines-private-board-and-reveals-v1",
                MinesOutcomeAudit.Verify(round, mutation.BalanceDeltaCents), MinesOutcomeAudit.Version);
            WriteRoundRecord(transaction, analytics);
        }
        if (result is not null)
            transaction.Create(result, new Dictionary<string, object>
            {
                ["resultId"] = result.Id, ["game"] = "mines", ["mode"] = "credit", ["userId"] = userId,
                ["matchId"] = round.RoundId, ["wagerCents"] = round.State.WagerCents, ["payoutCents"] = round.State.PayoutCents,
                ["netCents"] = checked(round.State.PayoutCents - round.State.WagerCents),
                ["houseEdgeBasisPoints"] = round.State.HouseEdgeBasisPoints, ["payoutPolicyVersion"] = round.State.PayoutPolicyVersion,
                ["outcome"] = mutation.Response.Round!.Status, ["settlementStatus"] = "paid", ["claimStatus"] = "completed",
                ["startedAt"] = Timestamp.FromDateTime(round.CreatedAt.UtcDateTime),
                ["completedAt"] = Timestamp.FromDateTime(round.UpdatedAt.UtcDateTime), ["schemaVersion"] = 1L,
            });
        transaction.Set(Round(round.RoundId), new Dictionary<string, object>
        {
            ["roundId"] = round.RoundId, ["userId"] = userId, ["version"] = round.Version,
            ["privateRoundJson"] = JsonSerializer.Serialize(round, MinesService.StorageJson), ["schemaVersion"] = 1L,
            ["updatedAt"] = Timestamp.FromDateTime(round.UpdatedAt.UtcDateTime),
        });
        transaction.Set(Session(userId), new Dictionary<string, object> { ["userId"] = userId, ["latestRoundId"] = round.RoundId, ["schemaVersion"] = 1L });
        transaction.Set(Command(commandId), new Dictionary<string, object>
        {
            ["userId"] = userId, ["fingerprint"] = fingerprint, ["schemaVersion"] = 1L,
            ["responseJson"] = JsonSerializer.Serialize(mutation.Response, MinesService.StorageJson),
            ["createdAt"] = Timestamp.FromDateTime(round.UpdatedAt.UtcDateTime),
        });
    }

    private async Task<MinesStoredRound?> ReadLatest(Transaction transaction, string userId, DocumentSnapshot snapshot, CancellationToken cancellationToken)
    {
        if (!snapshot.Exists) return null;
        if (RequiredString(snapshot, "userId") != userId || RequiredLong(snapshot, "schemaVersion") != 1)
            throw new InvalidOperationException("The Mines resume pointer is corrupt.");
        if (!snapshot.ContainsField("latestRoundId")) return null;
        var round = await transaction.GetSnapshotAsync(Round(RequiredString(snapshot, "latestRoundId")), cancellationToken);
        return ReadRound(round, userId);
    }
    private static MinesStoredRound ReadRound(DocumentSnapshot snapshot, string userId)
    {
        try
        {
            if (!snapshot.Exists || RequiredString(snapshot, "userId") != userId || RequiredLong(snapshot, "schemaVersion") != 1)
                throw new InvalidOperationException("The Mines private round is missing or corrupt.");
            var round = JsonSerializer.Deserialize<MinesStoredRound>(RequiredString(snapshot, "privateRoundJson"), MinesService.StorageJson)
                ?? throw new InvalidOperationException("The Mines private round is empty.");
            round.Validate(userId);
            if (round.RoundId != snapshot.Id || round.RoundId != RequiredString(snapshot, "roundId") || round.Version != RequiredLong(snapshot, "version"))
                throw new InvalidOperationException("The Mines private round identity is corrupt.");
            return round;
        }
        catch (Exception exception) when (exception is ArgumentException or JsonException or OverflowException)
        { throw new InvalidOperationException("The persisted Mines round is corrupt.", exception); }
    }
    private static MinesCommandReceipt ReadReceipt(DocumentSnapshot snapshot, string userId)
    {
        try
        {
            if (RequiredString(snapshot, "userId") != userId || RequiredLong(snapshot, "schemaVersion") != 1)
                throw new InvalidOperationException("The Mines command receipt identity is corrupt.");
            var response = JsonSerializer.Deserialize<MinesSessionResponse>(RequiredString(snapshot, "responseJson"), MinesService.StorageJson)
                ?? throw new InvalidOperationException("The Mines command receipt is empty.");
            if (snapshot.ContainsField("walletProtocol"))
            {
                var pending = ReadPending(snapshot, userId);
                var expected = pending.Mutation.Response;
                if (pending.WalletRequest is { } request)
                {
                    var receipt = JsonSerializer.Deserialize<GameWalletSettlementReceipt>(RequiredString(snapshot, "walletReceiptJson"))
                        ?? throw new InvalidOperationException("Empty Mines wallet receipt.");
                    if (!GameWalletReceiptVerifier.MatchesSettlement(receipt, "mines", userId, request) || receipt.InsufficientFunds)
                        throw new InvalidOperationException("Corrupt Mines wallet receipt.");
                    expected = expected with { BalanceCents = receipt.BalanceAfterCents };
                }
                if (JsonSerializer.Serialize(response) != JsonSerializer.Serialize(expected))
                    throw new InvalidOperationException("The Mines response differs from its accepted command.");
            }
            return new(userId, RequiredString(snapshot, "fingerprint"), response);
        }
        catch (JsonException exception) { throw new InvalidOperationException("The Mines command receipt is corrupt.", exception); }
    }
    private static void ValidateDecline(DocumentSnapshot snapshot, string userId)
    {
        if (snapshot.ContainsField("preflightInsufficientFunds"))
        {
            if (RequiredString(snapshot, "userId") != userId || RequiredLong(snapshot, "schemaVersion") != 1 ||
                RequiredLong(snapshot, "balanceBeforeCents") < 0 ||
                RequiredLong(snapshot, "requiredWagerCents") != StartWager(RequiredString(snapshot, "fingerprint")) ||
                RequiredLong(snapshot, "balanceBeforeCents") >= RequiredLong(snapshot, "requiredWagerCents"))
                throw new InvalidOperationException("Corrupt Mines admission decline.");
            return;
        }
        var pending = ReadPending(snapshot, userId);
        var receipt = JsonSerializer.Deserialize<GameWalletSettlementReceipt>(RequiredString(snapshot, "walletReceiptJson"))
            ?? throw new InvalidOperationException("Empty declined Mines wallet receipt.");
        if (pending.WalletRequest is not { } request || !receipt.InsufficientFunds ||
            !GameWalletReceiptVerifier.MatchesSettlement(receipt, "mines", userId, request))
            throw new InvalidOperationException("Corrupt declined Mines wallet receipt.");
    }

    private static long StartWager(string fingerprint)
    {
        var fields = fingerprint.Split(':');
        return fields is ["start", var wager, _] && long.TryParse(wager, System.Globalization.CultureInfo.InvariantCulture, out var cents) && cents > 0
            ? cents : throw new InvalidOperationException("Invalid Mines admission fingerprint.");
    }

    private DocumentReference Session(string userId) => database.Collection("minesSessions").Document(MinesService.Hash(userId));
    private DocumentReference Round(string id) => database.Collection("minesRounds").Document(id);
    private DocumentReference Command(string id) => database.Collection("minesCommands").Document(id);
    private DocumentReference Result(string id) => database.Collection("cardGameResults").Document($"mines-{id}");
    private static string RequiredString(DocumentSnapshot snapshot, string field) => snapshot.TryGetValue<string>(field, out var value) && !string.IsNullOrWhiteSpace(value)
        ? value : throw new InvalidOperationException($"The Mines record is missing {field}.");
    private static long RequiredLong(DocumentSnapshot snapshot, string field) => snapshot.TryGetValue<long>(field, out var value)
        ? value : throw new InvalidOperationException($"The Mines record is missing {field}.");
}
