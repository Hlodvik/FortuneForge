using System.Text;
using System.Text.Json;
using FortuneForge.Games.Abstractions.Auditing;
using FortuneForge.Games.Abstractions.Wallet;
using FortuneForge.Games.Firestore;
using Google.Cloud.Firestore;

namespace FortuneForge.Server.Cards.Solitaire;

internal sealed partial class FirestoreCompetitiveSolitaireStore
{
    internal const string FinancialOperations = "solitairePreparedWalletActions";
    internal const string FinancialReplies = "solitaireWalletActionReplies";
    internal const string BackendOwner = "solitaire-package-v1";
    // One compatibility shard covers every legacy cohort mutation during wallet uncertainty.
    // State reads and the sweeper recover it; narrowing it requires partitioning all legacy mutators together.
    private DocumentReference FinancialGate() => database.Collection("solitaireWalletShards").Document("legacy-cohorts-v1");
    private DocumentReference FinancialOperation(string id) => database.Collection(FinancialOperations).Document(id);
    private DocumentReference FinancialReply(string id) => database.Collection(FinancialReplies).Document(id);
    private static string FinancialId(string user, string key) => CreateLookupKey($"{user}\n{key}");
    private static string JoinFinancialDetail(int players, long cents, int draw) => $"{players}:{cents}:{draw}";

    internal async Task<SolitaireStoreSession?> TryReplayJoinAsync(string account, JoinSolitaireQueueRequest request, CancellationToken token)
    {
        var terms = CompetitiveSolitaireService.ValidateJoin(request);
        var target = CreateLookupKey($"{account}\n{request.IdempotencyKey}");
        var detail = JoinFinancialDetail(request.PlayerCount, terms.BuyInCents, terms.DrawCount);
        if (await ReplayFinancialAsync(account, request.IdempotencyKey, "join", target, detail, token) is { } replay) return replay;
        var legacy = await ActionDocument(account, request.IdempotencyKey).GetSnapshotAsync(token);
        if (!legacy.Exists) return null;
        VerifyAction(legacy, "join", target, detail);
        return await RunFinancialAsync(account, request.IdempotencyKey, "join", target, detail, DateTime.UtcNow,
            (_, _, _) => Task.FromResult(false), token);
    }

    private async Task<SolitaireStoreSession?> ReplayFinancialAsync(string account, string key, string operation,
        string target, string detail, CancellationToken token)
    {
        var snapshot = await FinancialOperation(FinancialId(account, key)).GetSnapshotAsync(token);
        if (!snapshot.Exists) return null;
        var saved = ReadFinancial(snapshot);
        VerifyFinancial(saved.Plan, account, key, operation, target, detail);
        return await CompleteFinancialAsync(saved, token);
    }

    private async Task<SolitaireStoreSession> RunFinancialAsync<T>(string account, string key, string operation,
        string target, string detail, DateTime now, Func<Transaction, SolitaireFinancialBuffer, long, Task<T>> build,
        CancellationToken token)
    {
        if (now.Kind != DateTimeKind.Utc) throw new ArgumentException("A Solitaire action requires server UTC time.");
        var id = FinancialId(account, key);
        if (await ReplayFinancialAsync(account, key, operation, target, detail, token) is { } replay) return replay;
        await RecoverFinancialOperationAsync(token);
        var balance = await ReadWalletBalanceAsync(account, token);
        var observation = GameSecurityContext.Current;
        if (observation is not null && (observation.UserId != account || observation.GameId != ComputeGameId))
            throw new SolitaireConflictException("The signed Solitaire request belongs to another account or game.");
        var prepared = await database.RunTransactionAsync(async transaction =>
        {
            var snapshots = await transaction.GetAllSnapshotsAsync([FinancialOperation(id), FinancialGate()], token);
            if (snapshots[0].Exists)
            {
                var saved = ReadFinancial(snapshots[0]);
                VerifyFinancial(saved.Plan, account, key, operation, target, detail);
                return saved;
            }
            if (ReadString(snapshots[1], "operationId").Length > 0) throw new SolitaireFinancialPendingException();
            var buffer = new SolitaireFinancialBuffer();
            _ = await build(transaction, buffer, balance);
            var plan = buffer.Freeze(id, account, key, operation, target, detail, now, observation);
            if (plan.WagerCents < 0 || plan.PayoutCents < 0 || plan.WagerCents > 0 && plan.PayoutCents > 0)
                throw new InvalidOperationException("The Solitaire wallet action has invalid net effects.");
            var json = JsonSerializer.Serialize(plan, JsonOptions);
            // Reserve space below the shared observation limit for the confirmed one-account
            // receipt and claim-proof envelope, which are added only after the wallet replies.
            if (Encoding.UTF8.GetByteCount(json) > 760_000)
                throw new SolitaireConflictException("This Solitaire action exceeds the durable evidence budget.");
            var digest = GameSecurityEventDocuments.Hash(json);
            transaction.Create(FinancialOperation(id), new Dictionary<string, object>
            {
                ["payloadJson"] = json, ["payloadSha256"] = digest, ["stage"] = "prepared",
                ["userId"] = account, ["operation"] = operation, ["schemaVersion"] = 1L,
            });
            transaction.Set(FinancialGate(), new Dictionary<string, object>
            {
                ["operationId"] = id, ["userId"] = account, ["preparedAt"] = Timestamp.FromDateTime(now),
            });
            return new SolitaireFinancialState(plan, digest, "prepared", null);
        }, cancellationToken: token);
        return await CompleteFinancialAsync(prepared, token);
    }

    private async Task RecoverFinancialOperationAsync(CancellationToken token)
    {
        var gate = await FinancialGate().GetSnapshotAsync(token);
        var id = ReadString(gate, "operationId");
        if (id.Length == 0) return;
        try { _ = await CompleteFinancialAsync(ReadFinancial(await FinancialOperation(id).GetSnapshotAsync(token)), token); }
        catch (SolitaireInsufficientCreditsException) { /* A confirmed decline releases the shard without changing game state. */ }
    }

    private async Task<SolitaireStoreSession> CompleteFinancialAsync(SolitaireFinancialState state, CancellationToken token)
    {
        var plan = state.Plan;
        var reply = await FinancialReply(plan.Id).GetSnapshotAsync(token);
        if (reply.Exists) return ReadFinancialReply(reply);
        var request = FinancialRequest(state);
        var receipt = state.Wallet ?? await wallet.SettleBatchAsync(plan.AccountId, request, token);
        if (!GameWalletBatchVerifier.MatchesSettlement(receipt, ComputeGameId, plan.AccountId, request))
            throw new GameWalletUnavailableException();
        receipt = receipt with { WasAlreadySettled = false };
        var published = await database.RunTransactionAsync(async transaction =>
        {
            var snapshots = await transaction.GetAllSnapshotsAsync([FinancialOperation(plan.Id), FinancialGate()], token);
            var current = ReadFinancial(snapshots[0]);
            if (current.Digest != state.Digest) throw new GameWalletUnavailableException();
            if (current.Stage is "published" or "complete" or "declined") return current;
            if (ReadString(snapshots[1], "operationId") != plan.Id)
                throw new InvalidOperationException("The frozen Solitaire action lost its game-shard gate.");
            if (!receipt.BatchDeclined)
            {
                using var scope = plan.Observation is { } original ? GameSecurityContext.Push(original) : null;
                foreach (var write in plan.Writes)
                {
                    var reference = database.Document(write.Path);
                    var fields = FirestoreFieldCodec.Deserialize(write.Fields);
                    if (reference.Parent.Id == "solitaireCommandGuards")
                    {
                        fields["backendOwner"] = BackendOwner;
                        fields["walletBatchId"] = receipt.BatchId;
                        fields["walletOperationId"] = plan.Id;
                    }
                    var financialPrefix = reference.Parent.Id == "solitaireTickets"
                        ? plan.Operation == "join" ? "buyIn" : plan.Operation == "cancel" ? "refund" : null
                        : reference.Parent.Id == "cardGameResults" && plan.Operation == "claim" ? "claim" : null;
                    if (financialPrefix is not null)
                    {
                        fields["backendOwner"] = BackendOwner;
                        fields[financialPrefix + "WalletBatchId"] = receipt.BatchId;
                        fields[financialPrefix + "WalletOperationId"] = plan.Id;
                    }
                    switch (write.Mode)
                    {
                        case "create": transaction.Create(reference, fields); break;
                        case "update": transaction.Update(reference, fields); break;
                        case "merge": transaction.Set(reference, fields, SetOptions.MergeAll); break;
                        case "set": transaction.Set(reference, fields); break;
                        default: throw new InvalidOperationException("The frozen Solitaire write mode is corrupt.");
                    }
                }
                foreach (var journal in plan.Journals)
                {
                    if (journal.Kind == "payout-claimed")
                        WriteClaimSecurityState(transaction, new(plan, state.Digest, "published", receipt), journal);
                    else
                        WriteSecurityState(transaction, journal.Match, journal.Players, journal.OccurredAtUtc, journal.Kind);
                }
                foreach (var invalidation in plan.Invalidations)
                    transaction.Update(database.Document(invalidation.Path), new Dictionary<string, object>
                    {
                        ["securityAuditPending"] = true,
                        ["securityAuditNextAttemptAtUtc"] = Timestamp.FromDateTime(invalidation.OccurredAtUtc),
                        ["securityAuditGeneration"] = FieldValue.Increment(1),
                    });
            }
            var stage = receipt.BatchDeclined ? "declined" : "published";
            var walletJson = JsonSerializer.Serialize(receipt, JsonOptions);
            transaction.Update(FinancialOperation(plan.Id), new Dictionary<string, object>
            {
                ["stage"] = stage, ["walletJson"] = walletJson,
                ["walletSha256"] = GameSecurityEventDocuments.Hash(walletJson),
            });
            if (receipt.BatchDeclined) transaction.Delete(FinancialGate());
            return new SolitaireFinancialState(plan, state.Digest, stage, receipt);
        }, cancellationToken: token);
        var confirmed = published.Wallet ?? throw new GameWalletUnavailableException();
        if (confirmed.BatchDeclined)
            throw new SolitaireInsufficientCreditsException(confirmed.Accounts[0].BalanceBeforeCents, plan.WagerCents);
        if (published.Stage == "complete")
            return ReadFinancialReply(await FinancialReply(plan.Id).GetSnapshotAsync(token));
        // The shard gate remains held after publication until the exact response is durable.
        var result = await ReadSessionCoreAsync(plan.AccountId, plan.PreparedAtUtc, false,
            confirmed.Accounts[0].BalanceAfterCents, token);
        var body = JsonSerializer.Serialize(result.Session, result.Session.GetType(), JsonOptions);
        await database.RunTransactionAsync(async transaction =>
        {
            var snapshots = await transaction.GetAllSnapshotsAsync([FinancialOperation(plan.Id), FinancialGate(), FinancialReply(plan.Id)], token);
            if (snapshots[2].Exists) return false;
            if (ReadString(snapshots[1], "operationId") != plan.Id || ReadFinancial(snapshots[0]).Digest != state.Digest)
                throw new InvalidOperationException("The Solitaire response no longer owns the financial gate.");
            transaction.Create(FinancialReply(plan.Id), new Dictionary<string, object>
            {
                ["kind"] = result.Session.Kind, ["json"] = body, ["sha256"] = ReplyDigest(result.Session.Kind, body, result.BalanceCents),
                ["balanceCents"] = result.BalanceCents,
            });
            transaction.Update(FinancialOperation(plan.Id), new Dictionary<string, object> { ["stage"] = "complete" });
            transaction.Delete(FinancialGate());
            return true;
        }, cancellationToken: token);
        return ReadFinancialReply(await FinancialReply(plan.Id).GetSnapshotAsync(token));
    }

    private static SolitaireFinancialState ReadFinancial(DocumentSnapshot snapshot)
    {
        var json = ReadString(snapshot, "payloadJson");
        var digest = ReadString(snapshot, "payloadSha256");
        if (!snapshot.Exists || GameSecurityEventDocuments.Hash(json) != digest)
            throw new InvalidOperationException("The prepared Solitaire action evidence is corrupt.");
        var plan = JsonSerializer.Deserialize<SolitaireFinancialPlan>(json, JsonOptions)
            ?? throw new InvalidOperationException("The prepared Solitaire action is missing.");
        var stage = ReadString(snapshot, "stage");
        if (plan.Id != snapshot.Id || plan.Id != FinancialId(plan.AccountId, plan.Key) ||
            plan.AccountId != ReadString(snapshot, "userId") || plan.PreparedAtUtc.Kind != DateTimeKind.Utc ||
            stage is not ("prepared" or "published" or "complete" or "declined"))
            throw new InvalidOperationException("The prepared Solitaire action identity is corrupt.");
        var walletJson = ReadString(snapshot, "walletJson");
        if (walletJson.Length > 0 && GameSecurityEventDocuments.Hash(walletJson) != ReadString(snapshot, "walletSha256"))
            throw new InvalidOperationException("The prepared Solitaire wallet receipt digest is corrupt.");
        var receipt = walletJson.Length == 0 ? null : JsonSerializer.Deserialize<GameWalletBatchReceipt>(walletJson, JsonOptions);
        if ((stage == "prepared") != (receipt is null))
            throw new InvalidOperationException("The prepared Solitaire action stage is corrupt.");
        return new(plan, digest, stage, receipt);
    }

    private static SolitaireStoreSession ReadFinancialReply(DocumentSnapshot snapshot)
    {
        var json = ReadString(snapshot, "json");
        var kind = ReadString(snapshot, "kind");
        var balance = ReadLong(snapshot, "balanceCents", -1);
        if (!snapshot.Exists || ReplyDigest(kind, json, balance) != ReadString(snapshot, "sha256"))
            throw new InvalidOperationException("The saved Solitaire action response is corrupt.");
        SolitaireSessionResponse session = kind switch
        {
            SolitaireSessionKinds.Idle => JsonSerializer.Deserialize<SolitaireIdleSessionResponse>(json, JsonOptions)!,
            SolitaireSessionKinds.Queued => JsonSerializer.Deserialize<SolitaireQueueSessionResponse>(json, JsonOptions)!,
            SolitaireSessionKinds.Match => JsonSerializer.Deserialize<SolitaireMatchSessionResponse>(json, JsonOptions)!,
            SolitaireSessionKinds.Result => JsonSerializer.Deserialize<SolitaireResultSessionResponse>(json, JsonOptions)!,
            _ => throw new InvalidOperationException("The saved Solitaire response kind is corrupt."),
        };
        return session is not null && balance >= 0 ? new(session, balance)
            : throw new InvalidOperationException("The saved Solitaire response is corrupt.");
    }

    private static string ReplyDigest(string kind, string json, long balance) => GameSecurityEventDocuments.Hash(
        kind + "\n" + balance.ToString(System.Globalization.CultureInfo.InvariantCulture) + "\n" + json);

    private static void VerifyFinancial(SolitaireFinancialPlan plan, string account, string key,
        string operation, string target, string detail)
    {
        if (plan.AccountId != account || plan.Key != key || plan.Operation != operation || plan.Target != target || plan.Detail != detail)
            throw new SolitaireConflictException("This idempotency key belongs to a different Solitaire request.");
    }

    private async Task<long> ReadWalletBalanceAsync(string account, CancellationToken token) =>
        (await accounts.GetStateAsync(account, token)).SlotsCreditsBalanceCents is { } balance && balance >= 0
            ? balance : throw new GameWalletUnavailableException();
}
