using System.Text.Json;
using Google.Cloud.Firestore;

namespace FortuneForge.Games.Mines.Server;

/// <summary>
/// Server-only collections hold private boards and command receipts. One Firestore transaction
/// commits wallet debit/credit, immutable command receipt, private round and resume pointer.
/// No production balance is synthesized. Host Firestore rules must deny client reads/writes.
/// </summary>
public sealed class FirestoreMinesStore(FirestoreDb database) : IMinesStore
{
    public bool Practice => false;
    private const string CurrencyId = "slotsCredits";
    private void WriteRoundRecord(Transaction transaction, FortuneForge.Games.Abstractions.Analytics.SinglePlayerRoundRecord record)
    {
        transaction.Create(database.Collection(FortuneForge.Games.Abstractions.Analytics.GameAnalyticsOutbox.CollectionName).Document(record.Settlement.Id), record.Settlement.Fields);
        var documents = FortuneForge.Games.Abstractions.Auditing.RoundAuditDocuments.Create(record.Audit);
        transaction.Create(database.Collection(FortuneForge.Games.Abstractions.Auditing.RoundAuditDocuments.Summaries).Document(record.Audit.Summary.Id), documents.Summary);
        transaction.Create(database.Collection(FortuneForge.Games.Abstractions.Auditing.RoundAuditDocuments.Evidence).Document(record.Audit.Summary.Id), documents.Evidence);
    }


    public Task<MinesTransactionSnapshot> ReadAsync(string userId, CancellationToken cancellationToken = default) =>
        database.RunTransactionAsync(async transaction =>
        {
            var snapshots = await transaction.GetAllSnapshotsAsync([Balance(userId), Session(userId)], cancellationToken);
            var latest = await ReadLatest(transaction, userId, snapshots[1], cancellationToken);
            return new MinesTransactionSnapshot(ReadBalance(snapshots[0]), latest, null, null);
        }, cancellationToken: cancellationToken);

    public Task<MinesSessionResponse> ExecuteAsync(string userId, string commandId, string fingerprint, string? roundId,
        Func<MinesTransactionSnapshot, MinesMutation> action, CancellationToken cancellationToken = default) =>
        database.RunTransactionAsync(async transaction =>
        {
            var snapshots = await transaction.GetAllSnapshotsAsync([Balance(userId), Session(userId), Command(commandId)], cancellationToken);
            var balance = ReadBalance(snapshots[0]);
            var latest = await ReadLatest(transaction, userId, snapshots[1], cancellationToken);
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
            var receipt = snapshots[2].Exists ? ReadReceipt(snapshots[2], userId) : null;
            var mutation = action(new(balance, latest, target, receipt));
            if (mutation.Replay) return mutation.Response;
            var round = mutation.Round ?? throw new InvalidOperationException("A Mines command must save a round.");
            round.Validate(userId);
            if (mutation.Response.Practice != Practice || mutation.Response.BalanceCents != checked(balance + mutation.BalanceDeltaCents) || mutation.Response.BalanceCents < 0)
                throw new InvalidOperationException("The Mines wallet mutation is invalid.");
            var ledgerReference = Ledger(commandId);
            var ledgerSnapshot = await transaction.GetSnapshotAsync(ledgerReference, cancellationToken);
            if (ledgerSnapshot.Exists) throw new InvalidOperationException("A Mines wallet ledger exists without its command receipt.");
            var result = round.State.Status == MinesRoundStatus.Active ? null : Result(round.RoundId);
            if (result is not null && (await transaction.GetSnapshotAsync(result, cancellationToken)).Exists)
                throw new InvalidOperationException("A Mines result already exists before this settlement.");
            // All reads above precede all writes, including on transaction retries.
            var observation = FortuneForge.Games.Abstractions.Auditing.GameSecurityEventDocuments.Capture(
                "mines", round.RoundId, round.Version, round.UpdatedAt.UtcDateTime, round, [userId]);
            transaction.Set(database.Collection(FortuneForge.Games.Abstractions.Auditing.GameSecurityEventDocuments.CollectionName).Document(observation.Id), observation.Fields);
            transaction.Set(database.Collection(FortuneForge.Games.Abstractions.Auditing.GameSecurityEventDocuments.ManifestCollectionName)
                .Document(FortuneForge.Games.Abstractions.Auditing.GameSecurityEventDocuments.ManifestId("mines", round.RoundId)),
                new Dictionary<string, object> { ["gameId"] = "mines", ["instanceId"] = round.RoundId,
                    ["eventIds"] = FieldValue.ArrayUnion(observation.Id), ["schemaVersion"] = 1L }, SetOptions.MergeAll);
            var participation = FortuneForge.Games.Abstractions.Analytics.AcceptedGameParticipationDocuments.Create(userId, "mines", round.UpdatedAt.UtcDateTime);
            transaction.Set(database.Collection("catalogPlayerActivity").Document(participation.DocOwnerKey)
                .Collection("games").Document(participation.GameId), participation.Fields, SetOptions.MergeAll);
            if (result is not null)
            {
                var analytics = FortuneForge.Games.Abstractions.Analytics.SinglePlayerRoundRecord.Create(
                    "mines", round.RoundId, userId, round.State.WagerCents, round.State.PayoutCents,
                    round.CreatedAt.UtcDateTime, round.UpdatedAt.UtcDateTime, "minesRounds", round.RoundId,
                    round, "mines-private-board-and-reveals-v1",
                    MinesOutcomeAudit.Verify(round, mutation.BalanceDeltaCents), MinesOutcomeAudit.Version);
                WriteRoundRecord(transaction, analytics);
            }
            if (mutation.BalanceDeltaCents != 0)
            {
                transaction.Set(Balance(userId), new Dictionary<string, object>
                {
                    ["available"] = mutation.Response.BalanceCents / 100,
                    ["availableFractionalCents"] = mutation.Response.BalanceCents % 100,
                    ["version"] = FieldValue.Increment(1),
                    ["updatedAt"] = Timestamp.FromDateTime(round.UpdatedAt.UtcDateTime),
                }, SetOptions.MergeAll);
                transaction.Create(ledgerReference, new Dictionary<string, object>
                {
                    ["transactionId"] = ledgerReference.Id, ["userId"] = userId, ["currencyId"] = CurrencyId,
                    ["amount"] = mutation.BalanceDeltaCents / 100d, ["balanceAfter"] = mutation.Response.BalanceCents / 100d,
                    ["amountCents"] = mutation.BalanceDeltaCents, ["balanceAfterCents"] = mutation.Response.BalanceCents,
                    ["type"] = mutation.LedgerType!, ["idempotencyKey"] = commandId, ["roundId"] = round.RoundId,
                    ["createdAt"] = Timestamp.FromDateTime(round.UpdatedAt.UtcDateTime),
                });
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
            transaction.Create(Command(commandId), new Dictionary<string, object>
            {
                ["userId"] = userId, ["fingerprint"] = fingerprint, ["schemaVersion"] = 1L,
                ["responseJson"] = JsonSerializer.Serialize(mutation.Response, MinesService.StorageJson),
                ["createdAt"] = Timestamp.FromDateTime(round.UpdatedAt.UtcDateTime),
            });
            return mutation.Response;
        }, options: TransactionOptions.ForMaxAttempts(20), cancellationToken: cancellationToken);

    private async Task<MinesStoredRound?> ReadLatest(Transaction transaction, string userId, DocumentSnapshot snapshot, CancellationToken cancellationToken)
    {
        if (!snapshot.Exists) return null;
        if (RequiredString(snapshot, "userId") != userId || RequiredLong(snapshot, "schemaVersion") != 1)
            throw new InvalidOperationException("The Mines resume pointer is corrupt.");
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
            return new(userId, RequiredString(snapshot, "fingerprint"), response);
        }
        catch (JsonException exception) { throw new InvalidOperationException("The Mines command receipt is corrupt.", exception); }
    }
    private static long ReadBalance(DocumentSnapshot snapshot)
    {
        if (!snapshot.Exists) return 0;
        var whole = RequiredLong(snapshot, "available");
        var fraction = snapshot.TryGetValue<long>("availableFractionalCents", out var value) ? value : 0;
        if (whole < 0 || fraction is < 0 or > 99) throw new InvalidOperationException("The account wallet balance is corrupt.");
        return checked(whole * 100 + fraction);
    }
    private DocumentReference Balance(string userId) => database.Collection("userBalances").Document($"{userId}_{CurrencyId}");
    private DocumentReference Session(string userId) => database.Collection("minesSessions").Document(MinesService.Hash(userId));
    private DocumentReference Round(string id) => database.Collection("minesRounds").Document(id);
    private DocumentReference Command(string id) => database.Collection("minesCommands").Document(id);
    private DocumentReference Ledger(string id) => database.Collection("balanceTransactions").Document($"mines-{id}");
    private DocumentReference Result(string id) => database.Collection("cardGameResults").Document($"mines-{id}");
    private static string RequiredString(DocumentSnapshot snapshot, string field) => snapshot.TryGetValue<string>(field, out var value) && !string.IsNullOrWhiteSpace(value)
        ? value : throw new InvalidOperationException($"The Mines record is missing {field}.");
    private static long RequiredLong(DocumentSnapshot snapshot, string field) => snapshot.TryGetValue<long>(field, out var value)
        ? value : throw new InvalidOperationException($"The Mines record is missing {field}.");
}
