using System.Text.Json;
using FortuneForge.Games.Abstractions.Auditing;
using FortuneForge.Games.Abstractions.Wallet;
using FortuneForge.Games.Cards;
using FortuneForge.Games.VideoPoker;
using Google.Cloud.Firestore;

namespace FortuneForge.Server.Cards.VideoPoker;

/// <summary>Owns paid rounds and frozen action receipts; the wallet owns all account funds.</summary>
public sealed partial class VideoPokerFirestoreStore(FirestoreDb database, IGameWalletClient wallet,
    IGameWalletAccountReader accounts) : IVideoPokerStore
{
    public const string RoundCollection = VideoPokerBackend.RoundCollection;
    public const string OperationCollection = VideoPokerBackend.OperationCollection;
    private static readonly JsonSerializerOptions Json = RoundAuditCapture.Json;

    public async Task<bool> HasStartAsync(string userId, string idempotencyKey, CancellationToken token)
    {
        var snapshots = await Task.WhenAll(Operation(userId, idempotencyKey).GetSnapshotAsync(token),
            Round(CreateLookupKey($"{userId}\n{idempotencyKey}")).GetSnapshotAsync(token));
        return snapshots.Any(snapshot => snapshot.Exists && snapshot.GetValue<string>("userId") == userId);
    }

    public async Task<VideoPokerStoreResult> StartAsync(string userId, string idempotencyKey, int coinsWagered,
        int handCount, IReadOnlyList<IReadOnlyList<PlayingCard>> shuffledDecks, DateTimeOffset nowUtc,
        CancellationToken cancellationToken)
    {
        var roundId = CreateLookupKey($"{userId}\n{idempotencyKey}");
        var hash = VideoPokerPreparedOperation.RequestHash("deal", roundId, coinsWagered, handCount);
        var saved = await database.RunTransactionAsync(async transaction =>
        {
            var snapshots = await transaction.GetAllSnapshotsAsync([Operation(userId, idempotencyKey), Round(roundId)], cancellationToken);
            if (snapshots[0].Exists) return OpenOperation(snapshots[0], userId, hash);
            if (snapshots[1].Exists)
            {
                var existing = OpenRound(snapshots[1], userId);
                if (existing.Round.CoinsWagered != coinsWagered || existing.Round.HandCount != handCount)
                    throw Conflict();
                if (PendingKey(snapshots[1]) is not null)
                    throw new InvalidOperationException("A prepared Video Poker deal is missing its operation receipt.");
                await ValidateLegacyLedgerAsync(transaction, snapshots[1], existing.Round, true, cancellationToken);
                return new StoredOperation(null, new(existing.RoundId, existing.Round, 0), null);
            }

            var round = VideoPokerRoundEngine.Deal(shuffledDecks, coinsWagered);
            var operation = Prepare(userId, roundId, idempotencyKey, "deal", hash, round, nowUtc, nowUtc);
            var fields = VideoPokerRoundCodec.RoundData(roundId, userId, round, idempotencyKey, nowUtc);
            fields["pendingOperationKey"] = idempotencyKey;
            fields["backendOwner"] = VideoPokerBackend.Owner;
            transaction.Create(Round(roundId), fields);
            transaction.Create(Operation(userId, idempotencyKey), EncodeOperation(operation));
            return new StoredOperation(operation, null, null);
        }, cancellationToken: cancellationToken);
        return await FinishAsync(saved, userId, cancellationToken);
    }

    public async Task<VideoPokerStoreResult?> GetAsync(string userId, string roundId, CancellationToken cancellationToken)
    {
        var snapshot = await Round(roundId).GetSnapshotAsync(cancellationToken);
        if (!snapshot.Exists || snapshot.GetValue<string>("userId") != userId) return null;
        if (PendingKey(snapshot) is { } key)
        {
            var pending = await Operation(userId, key).GetSnapshotAsync(cancellationToken);
            if (!pending.Exists) throw new InvalidOperationException("The Video Poker pending action receipt is missing.");
            var saved = OpenOperation(pending, userId);
            if (saved.Prepared?.RoundId != roundId)
                throw new InvalidOperationException("The pending action belongs to a different Video Poker round.");
            return await FinishAsync(saved, userId, cancellationToken);
        }
        var round = OpenRound(snapshot, userId);
        return new(roundId, round.Round, await BalanceAsync(userId, cancellationToken));
    }

    public async Task<VideoPokerStoreResult> DrawAsync(string userId, string roundId, string idempotencyKey,
        VideoPokerHeldCardPositions heldPositions, DateTimeOffset nowUtc, CancellationToken cancellationToken)
    {
        // An interrupted deal or draw must finish before another action can observe the round.
        var before = await Round(roundId).GetSnapshotAsync(cancellationToken);
        if (!before.Exists || before.GetValue<string>("userId") != userId) throw new VideoPokerRoundNotFoundException();
        if (PendingKey(before) is not null)
            _ = await GetAsync(userId, roundId, cancellationToken) ?? throw new VideoPokerRoundNotFoundException();
        var positions = heldPositions.Positions.Select(position => (int)position).ToArray();
        var saved = await database.RunTransactionAsync(async transaction =>
        {
            var snapshots = await transaction.GetAllSnapshotsAsync([Operation(userId, idempotencyKey), Round(roundId)], cancellationToken);
            var stored = OpenRound(snapshots[1], userId);
            var hash = VideoPokerPreparedOperation.RequestHash("draw", roundId, stored.Round.CoinsWagered, stored.Round.HandCount, positions);
            if (snapshots[0].Exists) return OpenOperation(snapshots[0], userId, hash);
            await ValidateLegacyLedgerAsync(transaction, snapshots[1], stored.Round, false, cancellationToken);
            if (stored.Round.Status == VideoPokerRoundStatus.Completed)
            {
                if (!stored.Round.HeldCardPositions!.Positions.SequenceEqual(heldPositions.Positions)) throw Conflict();
                var existingReply = Text(snapshots[1], "drawReplyJson");
                return new StoredOperation(null, existingReply is null ? new(stored.RoundId, stored.Round, 0)
                    : Deserialize<VideoPokerStoreResult>(existingReply), null, existingReply is not null);
            }
            if (PendingKey(snapshots[1]) is { } pendingKey)
            {
                var pending = await transaction.GetSnapshotAsync(Operation(userId, pendingKey), cancellationToken);
                return OpenOperation(pending, userId, hash);
            }
            var completed = VideoPokerRoundEngine.Draw(stored.Round, heldPositions);
            var started = snapshots[1].GetValue<Timestamp>("createdAt").ToDateTimeOffset();
            var financialEvidence = await ReadOpeningFinancialEvidenceAsync(transaction, snapshots[1], stored.Round, cancellationToken);
            var operation = Prepare(userId, roundId, idempotencyKey, "draw", hash, completed, started, nowUtc) with { PriorFinancialEvidence = financialEvidence };
            transaction.Update(Round(roundId), new Dictionary<string, object> { ["pendingOperationKey"] = idempotencyKey });
            transaction.Create(Operation(userId, idempotencyKey), EncodeOperation(operation));
            return new StoredOperation(operation, null, null);
        }, cancellationToken: cancellationToken);
        return await FinishAsync(saved, userId, cancellationToken);
    }

    public async Task<IReadOnlyList<VideoPokerRoundResponse>> HistoryAsync(string userId, int limit,
        CancellationToken cancellationToken)
    {
        if (limit is < 1 or > 100) throw new ArgumentOutOfRangeException(nameof(limit));
        var snapshots = await database.Collection(RoundCollection).WhereEqualTo("userId", userId)
            .OrderByDescending("createdAt").Limit(limit).GetSnapshotAsync(cancellationToken);
        var result = new List<VideoPokerStoreResult>();
        foreach (var snapshot in snapshots.Documents)
        {
            if (PendingKey(snapshot) is null)
                result.Add(new(snapshot.Id, OpenRound(snapshot, userId).Round, 0));
            else
            {
                try
                {
                    if (await GetAsync(userId, snapshot.Id, cancellationToken) is { } round) result.Add(round);
                }
                catch (VideoPokerInsufficientCreditsException) { /* A declined prepared deal has no paid round to show. */ }
            }
        }
        var balance = await BalanceAsync(userId, cancellationToken);
        return result.Select(round => VideoPokerService.ToResponse(round with { BalanceCents = balance })).ToArray();
    }

    private async Task<VideoPokerStoreResult> FinishAsync(StoredOperation saved, string userId, CancellationToken token)
    {
        if (saved.Prepared is null)
            return saved.ExactResult ? saved.Final! : saved.Final! with { BalanceCents = await BalanceAsync(userId, token) };
        if (saved.Receipt?.InsufficientFunds == true)
            throw new VideoPokerInsufficientCreditsException(saved.Receipt.BalanceBeforeCents, saved.Prepared.WagerCents);
        if (saved.Final is { } final) return final;
        var operation = saved.Prepared;
        var financialEvidence = operation.Kind == "draw" ? operation.PriorFinancialEvidence ?? await RecoverFinancialEvidenceAsync(operation, token)
            : VideoPokerFinancialSnapshot.Empty;
        GameWalletSettlementReceipt receipt;
        try { receipt = await wallet.SettleAsync(operation.UserId, operation.WalletRequest, token); }
        catch (GameWalletConflictException exception) { throw new GameWalletUnavailableException(exception); }
        ValidateReceipt(operation, receipt);
        receipt = receipt with { WasAlreadySettled = false };
        var result = await database.RunTransactionAsync(async transaction =>
        {
            var reference = Operation(operation.UserId, operation.IdempotencyKey);
            var snapshots = await transaction.GetAllSnapshotsAsync([reference, Round(operation.RoundId)], token);
            var current = OpenOperation(snapshots[0], operation.UserId, operation.RequestSha256);
            if (current.Prepared != operation && current.Prepared?.OutcomeSha256 != operation.OutcomeSha256)
                throw new GameWalletUnavailableException();
            if (current.Receipt is not null)
            {
                if (current.Receipt != receipt) throw new GameWalletUnavailableException();
                return current.Final;
            }
            if (!snapshots[1].Exists || snapshots[1].GetValue<string>("userId") != operation.UserId ||
                PendingKey(snapshots[1]) != operation.IdempotencyKey)
                throw new GameWalletUnavailableException();
            var fields = new Dictionary<string, object> { ["receiptJson"] = Serialize(receipt), ["status"] = receipt.InsufficientFunds ? "declined" : "completed" };
            if (receipt.InsufficientFunds)
            {
                transaction.Delete(Round(operation.RoundId));
                transaction.Update(reference, fields);
                return null;
            }
            var reply = new VideoPokerStoreResult(operation.RoundId, operation.Round, receipt.BalanceAfterCents);
            fields["resultJson"] = Serialize(reply);
            transaction.Update(reference, fields);
            var completed = receipt.SettledAtUtc < operation.OccurredAtUtc ? operation.OccurredAtUtc : receipt.SettledAtUtc;
            var roundFields = operation.Kind == "draw"
                ? VideoPokerRoundCodec.CompletedRoundData(operation.Round, operation.IdempotencyKey, completed)
                : new Dictionary<string, object>();
            roundFields["pendingOperationKey"] = FieldValue.Delete;
            roundFields[operation.Kind + "ReplyJson"] = Serialize(reply);
            roundFields["backendOwner"] = VideoPokerBackend.Owner;
            roundFields[operation.Kind + "WalletSettlementId"] = receipt.SettlementId;
            roundFields[operation.Kind + "OutcomeSha256"] = operation.OutcomeSha256;
            transaction.Update(Round(operation.RoundId), roundFields);
            transaction.Create(database.Collection("videoPokerRoundEvents").Document($"{operation.RoundId}-{operation.Kind}"),
                new Dictionary<string, object>
                {
                    ["roundId"] = operation.RoundId, ["userId"] = operation.UserId, ["event"] = operation.Kind,
                    ["idempotencyKey"] = operation.IdempotencyKey, ["createdAt"] = Timestamp.FromDateTime(completed.UtcDateTime),
                    ["schemaVersion"] = 1L, ["walletSettlementId"] = receipt.SettlementId
                });
            VideoPokerRoundRecording.Write(transaction, database, operation, completed.UtcDateTime, financialEvidence, receipt);
            return reply;
        }, cancellationToken: token);
        return result ?? throw new VideoPokerInsufficientCreditsException(receipt.BalanceBeforeCents, operation.WagerCents);
    }

    private async Task<long> BalanceAsync(string userId, CancellationToken token)
    {
        var state = await accounts.GetStateAsync(userId, token);
        return state.SlotsCreditsBalanceCents is >= 0 ? state.SlotsCreditsBalanceCents.Value : throw new GameWalletUnavailableException();
    }

    private static VideoPokerPreparedOperation Prepare(string userId, string roundId, string key, string kind,
        string hash, VideoPokerRound round, DateTimeOffset started, DateTimeOffset now)
    {
        var observation = GameSecurityContext.Current;
        if (observation is not null && (observation.UserId != userId || observation.GameId != VideoPokerBackend.GameId))
            throw new ArgumentException("The request belongs to another account or game.");
        return new(userId, roundId, key, kind, hash, round, started, now,
            observation is null ? null : observation with { SessionId = null, RequestJson = null });
    }

    private static void ValidateReceipt(VideoPokerPreparedOperation operation, GameWalletSettlementReceipt receipt)
    {
        if (!GameWalletReceiptVerifier.MatchesSettlement(receipt, VideoPokerBackend.GameId, operation.UserId, operation.WalletRequest))
            throw new GameWalletUnavailableException();
    }

    private async Task ValidateLegacyLedgerAsync(Transaction transaction, DocumentSnapshot snapshot,
        VideoPokerRound round, bool requireOpening, CancellationToken token)
    {
        if (Text(snapshot, "backendOwner") == VideoPokerBackend.Owner) return;
        if (requireOpening)
        {
            var opening = await transaction.GetAllSnapshotsAsync([
                database.Collection("balanceTransactions").Document($"video-poker-{snapshot.Id}-wager"),
                database.Collection("videoPokerRoundEvents").Document($"{snapshot.Id}-deal")], token);
            if (opening.Any(document => !document.Exists))
                throw new InvalidOperationException("The legacy Video Poker deal is missing its recorded opening ledger.");
            return;
        }
        var completion = await transaction.GetAllSnapshotsAsync([
            database.Collection("balanceTransactions").Document($"video-poker-{snapshot.Id}-payout"),
            database.Collection("videoPokerRoundEvents").Document($"{snapshot.Id}-draw")], token);
        var completed = round.Status == VideoPokerRoundStatus.Completed;
        if (!completed && completion.Any(document => document.Exists) ||
            completed && (!completion[1].Exists || completion[0].Exists != round.Results.Any(hand => hand.PaytableOutcome.CreditsWon > 0)))
            throw new InvalidOperationException("The legacy Video Poker completion state and payout ledger do not agree.");
    }

    private static Dictionary<string, object> EncodeOperation(VideoPokerPreparedOperation operation) => new()
    {
        ["userId"] = operation.UserId, ["roundId"] = operation.RoundId, ["kind"] = operation.Kind,
        ["requestSha256"] = operation.RequestSha256, ["preparedJson"] = Serialize(operation),
        ["preparedSha256"] = operation.OutcomeSha256, ["status"] = "prepared", ["schemaVersion"] = 1L
    };

    private static StoredOperation OpenOperation(DocumentSnapshot snapshot, string userId, string? requestHash = null)
    {
        if (!snapshot.Exists || snapshot.GetValue<string>("userId") != userId) throw new VideoPokerRoundNotFoundException();
        var operation = Deserialize<VideoPokerPreparedOperation>(snapshot.GetValue<string>("preparedJson"));
        if (operation.UserId != userId || operation.OutcomeSha256 != snapshot.GetValue<string>("preparedSha256"))
            throw new InvalidOperationException("The prepared Video Poker action is corrupt.");
        if (requestHash is not null && operation.RequestSha256 != requestHash) throw Conflict();
        return new(operation, Text(snapshot, "resultJson") is { } result ? Deserialize<VideoPokerStoreResult>(result) : null,
            Text(snapshot, "receiptJson") is { } receipt ? Deserialize<GameWalletSettlementReceipt>(receipt) : null, true);
    }

    private static VideoPokerRoundCodec.StoredRound OpenRound(DocumentSnapshot snapshot, string userId)
    {
        if (!snapshot.Exists || snapshot.GetValue<string>("userId") != userId) throw new VideoPokerRoundNotFoundException();
        return VideoPokerRoundCodec.ReadRound(snapshot);
    }

    public static string CreateLookupKey(string value) => GameSecurityEventDocuments.Hash(value);
    private DocumentReference Round(string id) => database.Collection(RoundCollection).Document(id);
    private DocumentReference Operation(string user, string key) => database.Collection(OperationCollection).Document(CreateLookupKey($"{user}\n{key}"));
    private static string? PendingKey(DocumentSnapshot snapshot) => Text(snapshot, "pendingOperationKey");
    private static string? Text(DocumentSnapshot snapshot, string field) => snapshot.TryGetValue<string>(field, out var value) && !string.IsNullOrEmpty(value) ? value : null;
    private static string Serialize<T>(T value) => JsonSerializer.Serialize(value, Json);
    private static T Deserialize<T>(string json) => JsonSerializer.Deserialize<T>(json, Json) ?? throw new InvalidOperationException("The stored Video Poker action is corrupt.");
    private static VideoPokerRoundConflictException Conflict() => new("This operation or Video Poker round was already used with different terms.");
    private sealed record StoredOperation(VideoPokerPreparedOperation? Prepared, VideoPokerStoreResult? Final,
        GameWalletSettlementReceipt? Receipt, bool ExactResult = false);
}
