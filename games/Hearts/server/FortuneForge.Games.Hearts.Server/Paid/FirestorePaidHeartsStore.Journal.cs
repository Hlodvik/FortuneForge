using System.Text.Json;
using FortuneForge.Games.Abstractions.Auditing;
using FortuneForge.Games.Abstractions.Wallet;
using FortuneForge.Games.Firestore;
using Google.Cloud.Firestore;
using Grpc.Core;

namespace FortuneForge.Server.Games.Hearts;

internal sealed partial class FirestorePaidHeartsStore
{
    internal const string PreparedCollection = "heartsPaidPreparedMutations";
    internal const string LocksCollection = "heartsPaidMutationLocks";
    internal const string WalletJournalCollection = "heartsPaidWalletJournal";
    internal const string AccountLanesCollection = "heartsPaidAccountLanes";
    private string? pendingSweepCursor;
    private DocumentReference Prepared(string id) => database.Collection(PreparedCollection).Document(id);
    private DocumentReference Lock(string path) => database.Collection(LocksCollection).Document(PaidHeartsIds.Hash(path));
    private DocumentReference AccountLane(string account) => database.Collection(AccountLanesCollection).Document(PaidHeartsIds.Hash(account));

    private async Task<T> TransactionAsync<T>(Func<PaidHeartsMutation, Task<T>> action, CancellationToken ct,
        string? actorId = null, DateTime? now = null)
    {
        for (var attempt = 0; attempt < 20; attempt++)
        {
            try
            {
                var staged = await database.RunTransactionAsync(async transaction =>
                {
                    var mutation = new PaidHeartsMutation(transaction, database);
                    var result = await action(mutation);
                    if (!mutation.HasChanges) return (Result: result, Plan: (PaidHeartsPlan?)null);
                    var actor = actorId ?? GameSecurityContext.Current?.UserId ?? mutation.Deltas.FirstOrDefault()?.AccountId ??
                        mutation.ResponseMatch?.Players.FirstOrDefault(value => value.IsAccountBacked)?.UserId ?? "hearts-table-worker";
                    var at = now ?? DateTime.UtcNow;
                    var replies = new List<PaidHeartsReply>();
                    foreach (var guard in mutation.Changes.Where(value => value.Kind == PaidHeartsDocumentKind.Command))
                    {
                        if (mutation.ResponseMatch is not { } match) throw new InvalidOperationException("A Hearts command has no frozen response.");
                        var policy = await mutation.GetSnapshotAsync(database.Document(FirestoreManagedPlayerPolicy.StatePath), ct);
                        var canHouse = match.Status == "queue" && match.Players.Count == 1 && at >= match.NextAdmissionAtUtc &&
                            options.AllowManagedPlayers && FirestoreManagedPlayerPolicy.AllowsBots(policy, HeartsBackend.GameId);
                        var response = Response(match, actor, 0, at, canHouse);
                        if (response.Notice is null && mutation.Notice is { } notice) response = response with { Notice = notice };
                        replies.Add(new(guard.Path, actor, JsonSerializer.Serialize(response, Json)));
                    }
                    foreach (var change in mutation.Changes.ToArray())
                    {
                        var prior = await mutation.GetSnapshotAsync(database.Document(change.Path), ct);
                        if (change.Create && prior.Exists) throw new PaidHeartsConflictException("This Hearts receipt already exists.");
                    }
                    foreach (var delta in mutation.Deltas)
                    {
                        var modern = await mutation.GetSnapshotAsync(database.Collection(WalletJournalCollection).Document(delta.JournalId), ct);
                        var legacy = await mutation.GetSnapshotAsync(database.Collection("balanceTransactions").Document(delta.JournalId), ct);
                        if (modern.Exists || legacy.Exists) throw new PaidHeartsConflictException("This Hearts financial operation was already accepted.");
                    }
                    var evidence = await PaidHeartsFinancialProof.ReadPriorAsync(database, mutation, ct);
                    var affected = (mutation.ResponseMatch?.Players.Where(value => value.IsAccountBacked).Select(value => value.UserId) ?? [])
                        .Concat(mutation.Deltas.Select(value => value.AccountId)).Concat(replies.Select(value => value.AccountId))
                        .Distinct(StringComparer.Ordinal).ToArray();
                    // One direct account lock makes charged/credited pending work discoverable without a bounded global scan.
                    foreach (var account in affected) await mutation.GetSnapshotAsync(AccountLane(account), ct);
                    var reads = mutation.Reads.Where(value => IsAuthoritativeRead(value.Key, mutation.Changes)).Select(value => new PaidHeartsRead(value.Key, value.Value.Exists,
                        PaidHeartsMutation.Version(value.Value))).OrderBy(value => value.Path, StringComparer.Ordinal).ToArray();
                    var locks = await transaction.GetAllSnapshotsAsync(reads.Select(value => Lock(value.Path)), ct);
                    var held = locks.FirstOrDefault(value => value.Exists);
                    if (held is not null) throw new PaidHeartsPendingException(held.GetValue<string>("mutationId"));
                    var plan = new PaidHeartsPlan(Guid.NewGuid().ToString("N"), actor, at, reads, mutation.Changes.ToArray(),
                        mutation.Deltas.ToArray(), mutation.Observations.ToArray(), mutation.Settlements.ToArray(), replies.ToArray(), evidence,
                        GameSecurityContext.Current);
                    var payload = JsonSerializer.Serialize(plan, Json);
                    _ = plan.Request();
                    transaction.Create(Prepared(plan.Id), new Dictionary<string, object>
                    {
                        ["status"] = "pending", ["payloadJson"] = payload, ["payloadSha256"] = PaidHeartsIds.Hash(payload),
                        ["preparedAt"] = Timestamp.FromDateTime(at)
                    });
                    foreach (var read in reads) transaction.Create(Lock(read.Path), new Dictionary<string, object> { ["mutationId"] = plan.Id });
                    return (Result: result, Plan: plan);
                }, cancellationToken: ct);
                if (staged.Plan is not null)
                {
                    var receipt = await CompleteAsync(staged.Plan, ct);
                    if (receipt?.BatchDeclined == true) throw new PaidHeartsInsufficientFundsException();
                }
                return staged.Result;
            }
            catch (PaidHeartsPendingException pending) { await RecoverAsync(pending.Id, ct); }
            catch (RpcException error) when (error.StatusCode == StatusCode.Aborted && attempt < 19)
            { await Task.Delay(20 * (attempt + 1) + Random.Shared.Next(10, 60), ct); }
        }
        throw new PaidHeartsConflictException("The Hearts table is busy. Retry the original request.");
    }

    internal async Task RecoverPendingAsync(CancellationToken ct)
    {
        // Pending settlement is recovered even when new paid admission is paused.
        Query query = database.Collection(PreparedCollection).WhereEqualTo("status", "pending").OrderBy(FieldPath.DocumentId);
        if (pendingSweepCursor is { } cursor) query = query.StartAfter(cursor);
        var pending = await query.Limit(100).GetSnapshotAsync(ct);
        foreach (var document in pending.Documents)
        {
            try { await RecoverAsync(document.Id, ct); }
            catch (OperationCanceledException) when (ct.IsCancellationRequested) { throw; }
            catch (Exception error) { logger?.LogError(error, "Hearts pending mutation {MutationId} will retry recovery.", document.Id); }
        }
        pendingSweepCursor = pending.Count == 100 ? pending.Documents[^1].Id : null;
    }
    private async Task RecoverAccountAsync(string account, CancellationToken ct)
    {
        await RecoverLockAsync(AccountLane(account), ct);
        // Pre-lane frozen joins already fence this legacy session even when the session document does not exist yet.
        await RecoverLockAsync(Session(account), ct);
        var session = await Session(account).GetSnapshotAsync(ct);
        if (Text(session, "matchId") is { Length: > 0 } id) await RecoverMatchAsync(id, ct);
    }
    private Task RecoverMatchAsync(string id, CancellationToken ct) => RecoverLockAsync(Match(id), ct);
    private async Task RecoverLockAsync(DocumentReference reference, CancellationToken ct)
    {
        for (var attempt = 0; attempt < 20; attempt++)
        {
            var held = await Lock(PaidHeartsMutation.Relative(reference)).GetSnapshotAsync(ct);
            if (!held.Exists) return;
            await RecoverAsync(held.GetValue<string>("mutationId"), ct);
        }
        throw new PaidHeartsConflictException("The Hearts table is busy. Retry the original request.");
    }
    private async Task RecoverAsync(string id, CancellationToken ct)
    {
        var saved = await Prepared(id).GetSnapshotAsync(ct);
        if (!saved.Exists) throw new InvalidOperationException("The pending Hearts mutation is missing.");
        if (saved.GetValue<string>("status") == "pending") await CompleteAsync(Decode(saved), ct);
    }
    internal static PaidHeartsPlan Decode(DocumentSnapshot snapshot)
    {
        var payload = snapshot.GetValue<string>("payloadJson");
        var hash = snapshot.GetValue<string>("payloadSha256");
        if (PaidHeartsIds.Hash(payload) != hash) throw new GameWalletUnavailableException();
        var plan = JsonSerializer.Deserialize<PaidHeartsPlan>(payload, Json) ?? throw new GameWalletUnavailableException();
        if (plan.Id != snapshot.Id) throw new GameWalletUnavailableException();
        return plan with { FrozenSha256 = hash };
    }

    private async Task<GameWalletBatchReceipt?> CompleteAsync(PaidHeartsPlan plan, CancellationToken ct)
    {
        var request = plan.Request();
        var receipt = request.Effects.Count == 0 ? null : await wallet.SettleBatchAsync(plan.ActorId, request, ct);
        if (receipt is not null && !GameWalletBatchVerifier.MatchesSettlement(receipt, HeartsBackend.GameId, plan.ActorId, request))
            throw new GameWalletUnavailableException();
        if (receipt is not null) receipt = receipt with { WasAlreadySettled = false };
        // Only the game publication is retried. The exact confirmed wallet receipt and frozen outcome stay unchanged.
        for (var attempt = 0; ; attempt++)
        {
            try { return await FinalizeAsync(); }
            catch (RpcException error) when (error.StatusCode == StatusCode.Aborted && attempt < 9)
            { await Task.Delay(20 * (attempt + 1) + Random.Shared.Next(10, 100), ct); }
        }

        Task<GameWalletBatchReceipt?> FinalizeAsync() => database.RunTransactionAsync(async transaction =>
        {
            var saved = await transaction.GetSnapshotAsync(Prepared(plan.Id), ct);
            if (Decode(saved).Request().OutcomeSha256 != request.OutcomeSha256) throw new GameWalletUnavailableException();
            if (saved.GetValue<string>("status") != "pending")
            {
                var original = saved.TryGetValue<string>("walletJson", out var json) ? JsonSerializer.Deserialize<GameWalletBatchReceipt>(json, Json) : null;
                if (JsonSerializer.Serialize(original, Json) != JsonSerializer.Serialize(receipt, Json)) throw new GameWalletUnavailableException();
                return original;
            }
            // Prepare reads coordinator state before game locks; use the same order to avoid the inverse SDK lock wait.
            var current = await transaction.GetAllSnapshotsAsync(plan.Reads.Select(value => database.Document(value.Path)), ct);
            var locks = await transaction.GetAllSnapshotsAsync(plan.Reads.Select(value => Lock(value.Path)), ct);
            for (var index = 0; index < plan.Reads.Length; index++)
                if (!locks[index].Exists || locks[index].GetValue<string>("mutationId") != plan.Id ||
                    IsAuthoritativeRead(plan.Reads[index].Path, plan.Changes) &&
                    (current[index].Exists != plan.Reads[index].Exists || PaidHeartsMutation.Version(current[index]) != plan.Reads[index].Version))
                    throw new GameWalletUnavailableException();
            var declined = receipt?.BatchDeclined == true;
            foreach (var change in plan.Changes)
            {
                if (declined && change.Kind != PaidHeartsDocumentKind.Command) continue;
                var reference = database.Document(change.Path);
                if (change.Fields is null) { transaction.Delete(reference); continue; }
                var fields = FirestoreFieldCodec.Deserialize(change.Fields);
                if (change.Kind == PaidHeartsDocumentKind.Command)
                {
                    fields["actorId"] = plan.ActorId;
                    fields["walletBatchId"] = receipt?.BatchId ?? "";
                    if (declined) fields["declined"] = true;
                    else
                    {
                        var reply = plan.Replies.Single(value => value.GuardPath == change.Path);
                        var response = JsonSerializer.Deserialize<PaidHeartsSessionResponse>(reply.ResponseJson, Json)!;
                        var json = JsonSerializer.Serialize(response with
                        { BalanceCredits = receipt!.Accounts.Single(value => value.AccountId == reply.AccountId).BalanceAfterCents / 100m }, Json);
                        fields["replyJson"] = json; fields["replySha256"] = PaidHeartsIds.Hash(json);
                    }
                }
                if (change.Create) transaction.Create(reference, fields);
                else transaction.Set(reference, fields, change.Merge ? SetOptions.MergeAll : SetOptions.Overwrite);
            }
            if (!declined)
            {
                using var scope = plan.ObservationContext is { } observed ? GameSecurityContext.Push(observed) : null;
                foreach (var delta in plan.Money) transaction.Create(database.Collection(WalletJournalCollection).Document(delta.JournalId), new Dictionary<string, object>
                {
                    ["accountId"] = delta.AccountId, ["deltaCents"] = delta.Cents, ["kind"] = delta.Kind, ["source"] = delta.Source,
                    ["occurredAt"] = Timestamp.FromDateTime(delta.AtUtc), ["mutationId"] = plan.Id, ["walletBatchId"] = receipt!.BatchId
                });
                foreach (var observation in plan.Observations)
                {
                    var match = JsonSerializer.Deserialize<PaidHeartsMatch>(observation.MatchJson, Json)!;
                    FirestoreGameAudit.WriteObservation(transaction, database, HeartsBackend.GameId, match.Id, match.Version, observation.AtUtc,
                        match, match.Players.Where(value => value.IsAccountBacked).Select(value => value.UserId));
                    foreach (var player in match.Players.Where(value => value.IsAccountBacked))
                        FirestoreGameAudit.WriteAcceptedParticipation(transaction, database, HeartsBackend.GameId, player.UserId, observation.AtUtc);
                }
                foreach (var settlement in plan.Settlements)
                {
                    var match = JsonSerializer.Deserialize<PaidHeartsMatch>(settlement.MatchJson, Json)!;
                    var capture = PaidHeartsFinancialProof.Protect(PaidHeartsRoundAudit.Capture(match, settlement.Abandoned, settlement.AtUtc),
                        plan, receipt, match.Id);
                    FirestoreGameAudit.WriteCompletion(transaction, database, capture);
                }
            }
            transaction.Update(Prepared(plan.Id), new Dictionary<string, object>
            { ["status"] = declined ? "declined" : "applied", ["walletJson"] = JsonSerializer.Serialize(receipt, Json) });
            foreach (var read in plan.Reads) transaction.Delete(Lock(read.Path));
            return receipt;
        }, cancellationToken: ct);
    }

    private async Task<PaidHeartsSessionResponse?> ReplayAsync(string user, string key, string operation, string target, string detail, CancellationToken ct)
    {
        var guard = await Guard(user, key).GetSnapshotAsync(ct);
        if (!guard.Exists) return null;
        Verify(guard, operation, target, detail);
        if (guard.TryGetValue<bool>("declined", out var declined) && declined) throw new PaidHeartsInsufficientFundsException();
        if (!guard.TryGetValue<string>("replyJson", out var json)) return null; // Historical guards retain their original projection semantics.
        if (Text(guard, "actorId") != user || PaidHeartsIds.Hash(json) != Text(guard, "replySha256")) throw new GameWalletUnavailableException();
        return JsonSerializer.Deserialize<PaidHeartsSessionResponse>(json, Json) ?? throw new GameWalletUnavailableException();
    }
    private async Task<PaidHeartsSessionResponse> SavedOrSessionAsync(string user, string key, DateTime now, CancellationToken ct)
    {
        var guard = await Guard(user, key).GetSnapshotAsync(ct);
        return await ReplayAsync(user, key, Text(guard, "operation"), Text(guard, "target"), Text(guard, "detail"), ct)
            ?? await SessionAsync(user, now, ct);
    }
    private sealed class PaidHeartsPendingException(string id) : Exception { internal string Id { get; } = id; }
    private static bool IsAuthoritativeRead(string path, IReadOnlyCollection<PaidHeartsChange> changes) =>
        changes.Any(value => value.Path == path) || path.Split('/')[0] is
            "heartsPaidMatches" or "heartsPaidSessions" or "heartsPaidQueuePartitions" or "heartsPaidCommands" or
            "heartsPaidFunding" or "heartsPaidFundingTransfers" or AccountLanesCollection;
}
