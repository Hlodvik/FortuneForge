using System.Text.Json;
using FortuneForge.Games.Abstractions.Auditing;
using FortuneForge.Games.Firestore;
using FortuneForge.Server.RoundAudit;
using Google.Cloud.Firestore;

namespace FortuneForge.Server.Cards.TexasHoldem.Credit;

/// <summary>Fences affected Hold'em documents until the immutable multi-account wallet receipt and table state agree.</summary>
internal sealed class CreditHoldemMutationJournal(FirestoreDb database, IGameWalletBatchClient wallet, IGameWalletAccountReader accounts,
    IManagedPlayerRosterClient roster)
{
    private DocumentReference Prepared(string id) => database.Collection(CreditHoldemBackend.Mutations).Document(id);
    private DocumentReference Lock(string path) => database.Collection(CreditHoldemBackend.Locks).Document(CreditHoldemIds.Hash(path));

    internal async Task<T> RunAsync<T>(Func<CreditHoldemMutation, Task<T>> action, CancellationToken token)
    {
        await DrainRosterAsync(token);
        while (true)
        {
            var id = Guid.NewGuid().ToString("N");
            T? result = default;
            try
            {
                var plan = await database.RunTransactionAsync(async transaction =>
                {
                    var prior = await transaction.GetSnapshotAsync(Prepared(id), token);
                    if (prior.Exists) return Decode(prior);
                    var mutation = new CreditHoldemMutation(transaction, database, accounts);
                    result = await action(mutation);
                    if (!mutation.HasChanges) return null;
                    var frozen = await mutation.FreezeAsync(id, token);
                    var locks = await transaction.GetAllSnapshotsAsync(frozen.Reads.Select(read => Lock(read.Path)), token);
                    foreach (var held in locks)
                        if (held.Exists && held.TryGetValue<string>("mutationId", out var pending) && pending.Length > 0)
                            throw new CreditHoldemPendingMutationException(pending);
                    var json = JsonSerializer.Serialize(frozen, CreditHoldemMutation.Json);
                    transaction.Create(Prepared(id), new Dictionary<string, object>
                    {
                        ["payloadJson"] = json, ["payloadSha256"] = CreditHoldemIds.Hash(json), ["status"] = "pending",
                        ["accountIds"] = AffectedAccounts(frozen),
                        ["schemaVersion"] = 1L, ["preparedAt"] = Timestamp.FromDateTime(frozen.PreparedAt)
                    });
                    foreach (var read in frozen.Reads)
                        transaction.Set(Lock(read.Path), new Dictionary<string, object> { ["path"] = read.Path, ["mutationId"] = id });
                    return frozen;
                }, cancellationToken: token);
                if (plan is not null)
                {
                    var receipt = await CompleteAsync(plan, token);
                    if (receipt?.BatchDeclined == true) ThrowDeclined(plan, receipt);
                }
                return result!;
            }
            catch (CreditHoldemPendingMutationException pending)
            {
                await RecoverAsync(pending.Id, token);
            }
        }
    }

    internal async Task RecoverAccountAsync(string account, CancellationToken token)
    {
        await DrainRosterAsync(token);
        var pending = await database.Collection(CreditHoldemBackend.Mutations).WhereEqualTo("status", "pending")
            .WhereArrayContains("accountIds", account).Limit(100).GetSnapshotAsync(token);
        foreach (var snapshot in pending.Documents) await RecoverAsync(snapshot.Id, token);
    }

    internal async Task RecoverAllAsync(CancellationToken token)
    {
        await DrainRosterAsync(token);
        var pending = await database.Collection(CreditHoldemBackend.Mutations).WhereEqualTo("status", "pending").Limit(100).GetSnapshotAsync(token);
        foreach (var snapshot in pending.Documents) await RecoverAsync(snapshot.Id, token);
    }

    private async Task RecoverAsync(string id, CancellationToken token)
    {
        var snapshot = await Prepared(id).GetSnapshotAsync(token);
        if (!snapshot.Exists) throw new InvalidOperationException("The pending Hold'em mutation is missing.");
        if (snapshot.GetValue<string>("status") == "pending") await CompleteAsync(Decode(snapshot), token);
    }

    private async Task<GameWalletBatchReceipt?> CompleteAsync(CreditHoldemMutationPlan plan, CancellationToken token)
    {
        var request = plan.WalletRequest();
        GameWalletBatchReceipt? receipt = request.Effects.Count == 0 ? null :
            await wallet.SettleBatchAsync(plan.ActorAccountId, request, token);
        if (receipt is not null && !GameWalletBatchVerifier.MatchesSettlement(receipt, CreditHoldemBackend.GameId, plan.ActorAccountId, request))
            throw new GameWalletUnavailableException();
        if (receipt is not null) receipt = receipt with { WasAlreadySettled = false };
        var finalReceipt = await database.RunTransactionAsync(async transaction =>
        {
            var saved = await transaction.GetSnapshotAsync(Prepared(plan.Id), token);
            if (Decode(saved).WalletRequest() is not { } original || original.OutcomeSha256 != request.OutcomeSha256)
                throw new InvalidOperationException("The frozen Hold'em mutation changed before finalization.");
            if (saved.GetValue<string>("status") != "pending")
            {
                var previous = saved.TryGetValue<string>("walletJson", out var completed) ?
                    JsonSerializer.Deserialize<GameWalletBatchReceipt>(completed, CreditHoldemMutation.Json) : null;
                if (JsonSerializer.Serialize(previous, CreditHoldemMutation.Json) != JsonSerializer.Serialize(receipt, CreditHoldemMutation.Json))
                    throw new GameWalletUnavailableException();
                return previous;
            }
            var current = await transaction.GetAllSnapshotsAsync(plan.Reads.Select(read => database.Document(read.Path)), token);
            var locks = await transaction.GetAllSnapshotsAsync(plan.Reads.Select(read => Lock(read.Path)), token);
            for (var index = 0; index < plan.Reads.Length; index++)
                if (!locks[index].Exists || locks[index].GetValue<string>("mutationId") != plan.Id ||
                    CreditHoldemMutation.IsAuthoritativeRead(plan.Reads[index].Path, plan.Changes) &&
                    (current[index].Exists != plan.Reads[index].Exists || CreditHoldemMutation.Version(current[index]) != plan.Reads[index].UpdatedAt))
                    throw new InvalidOperationException("The pending Hold'em mutation no longer owns its original table/session state.");
            var declined = receipt?.BatchDeclined == true;
            var financialEvidence = declined || plan.Audits.Length == 0 ? CreditHoldemFinancialSnapshot.Empty : plan.FinancialEvidence ??
                await CreditHoldemFinancialProof.ReadPriorAsync(database, document => transaction.GetSnapshotAsync(document, token),
                    query => transaction.GetSnapshotAsync(query, token), plan.Audits, plan.Wallet);
            foreach (var change in plan.Changes)
            {
                var fields = FirestoreFieldCodec.Deserialize(change.Fields);
                var isGuard = change.Path.StartsWith(CreditHoldemBackend.CommandGuards + "/", StringComparison.Ordinal);
                if (declined && !isGuard) continue;
                if (isGuard)
                {
                    if (declined)
                    {
                        var insufficient = receipt!.Accounts.First(value => value.InsufficientFunds);
                        fields["declinedAvailableCents"] = insufficient.BalanceBeforeCents;
                        var effect = request.Effects.Single(value => value.AccountId == insufficient.AccountId);
                        fields["declinedRequiredCents"] = GameWalletBalanceRequirement.RequiredCents(effect.WagerCents, effect.MinimumBalanceBeforeCents);
                    }
                    else if (plan.Replies.SingleOrDefault(value => value.GuardPath == change.Path) is { } reply)
                    {
                        var account = receipt!.Accounts.Single(value => value.AccountId == reply.AccountId);
                        var json = JsonSerializer.Serialize(new CreditHoldemFrozenReply(reply.Kind, reply.SessionJson, account.BalanceAfterCents), CreditHoldemMutation.Json);
                        fields["replyJson"] = json;
                        fields["replySha256"] = CreditHoldemIds.Hash(json);
                    }
                    fields["walletBatchId"] = receipt?.BatchId ?? string.Empty;
                }
                if (change.Create) transaction.Create(database.Document(change.Path), fields);
                else transaction.Set(database.Document(change.Path), fields, change.Merge ? SetOptions.MergeAll : SetOptions.Overwrite);
            }
            if (!declined)
            {
                using var scope = plan.Request is { } observed ? GameSecurityContext.Push(observed) : null;
                foreach (var delta in plan.Wallet)
                    transaction.Create(database.Collection(CreditHoldemBackend.WalletJournal).Document(delta.JournalId), new Dictionary<string, object>
                    {
                        ["accountId"] = delta.AccountId, ["deltaCents"] = delta.Cents, ["reason"] = delta.Reason,
                        ["sourceKey"] = delta.SourceKey, ["occurredAt"] = Timestamp.FromDateTime(delta.OccurredAt),
                        ["walletBatchId"] = receipt!.BatchId, ["mutationId"] = plan.Id
                    });
                foreach (var observation in plan.Observations)
                {
                    var match = CreditHoldemMatchPersistence.Deserialize(observation.MatchJson);
                    var humans = match.Players.Where(value => value.IsAccountBacked).ToArray();
                    FirestoreGameJournal.Write(transaction, database, CreditHoldemBackend.GameId, $"{match.MatchId}-hand-{match.HandNumber}",
                        match.Version, observation.OccurredAt, match, humans.Select(value => value.ActorId), observation.Kind, match.MatchId);
                    foreach (var human in humans.Where(value => value.CommittedHand > 0))
                        FirestoreGameAudit.WriteAcceptedParticipation(transaction, database, CreditHoldemBackend.GameId, human.ActorId, observation.OccurredAt);
                }
                foreach (var audit in plan.Audits)
                {
                    var match = CreditHoldemMatchPersistence.Deserialize(audit.MatchJson);
                    FirestoreGameAudit.WriteCompletion(transaction, database, CreditHoldemFinancialProof.Protect(
                        HoldemRoundAudit.Capture(match, audit.SettledAt), match, plan, receipt, financialEvidence));
                }
                foreach (var release in plan.RosterReleases ?? [])
                    transaction.Create(database.Collection(CreditHoldemBackend.RosterOutbox).Document(CreditHoldemIds.Hash($"{plan.Id}\n{release.AssignmentId}")),
                        new Dictionary<string, object>
                        {
                            ["assignmentId"] = release.AssignmentId, ["profileIds"] = release.ProfileIds,
                            ["occurredAt"] = Timestamp.FromDateTime(plan.PreparedAt), ["status"] = "pending", ["mutationId"] = plan.Id
                        });
            }
            foreach (var read in plan.Reads) transaction.Delete(Lock(read.Path));
            var final = new Dictionary<string, object>
            {
                ["status"] = declined ? "declined" : "applied", ["completedAt"] = Timestamp.GetCurrentTimestamp()
            };
            if (receipt is not null) final["walletJson"] = JsonSerializer.Serialize(receipt, CreditHoldemMutation.Json);
            transaction.Set(Prepared(plan.Id), final, SetOptions.MergeAll);
            return receipt;
        }, cancellationToken: token);
        await DrainRosterAsync(token);
        return finalReceipt;
    }

    private async Task DrainRosterAsync(CancellationToken token)
    {
        var pending = await database.Collection(CreditHoldemBackend.RosterOutbox).WhereEqualTo("status", "pending").Limit(100).GetSnapshotAsync(token);
        foreach (var entry in pending.Documents)
        {
            await roster.ReleaseAsync(entry.GetValue<string>("assignmentId"), entry.GetValue<string[]>("profileIds"),
                DateTime.UtcNow, token);
            await entry.Reference.SetAsync(new Dictionary<string, object> { ["status"] = "complete" }, SetOptions.MergeAll, token);
        }
    }

    private static string[] AffectedAccounts(CreditHoldemMutationPlan plan)
    {
        var accounts = plan.Wallet.Select(value => value.AccountId).Concat(plan.Replies.Select(value => value.AccountId))
            .ToHashSet(StringComparer.Ordinal);
        foreach (var change in plan.Changes)
        {
            var fields = FirestoreFieldCodec.Deserialize(change.Fields);
            if (fields.GetValueOrDefault("userId") is string account) accounts.Add(account);
            if (change.Path.StartsWith(CreditHoldemBackend.Matches + "/", StringComparison.Ordinal) &&
                fields.GetValueOrDefault("matchJson") is string json)
            {
                var match = CreditHoldemMatchPersistence.Deserialize(json);
                foreach (var player in match.Players.Where(value => value.IsAccountBacked)) accounts.Add(player.ActorId);
                foreach (var ticket in match.PendingTakeovers) accounts.Add(ticket.UserId);
            }
        }
        return accounts.Order(StringComparer.Ordinal).ToArray();
    }

    internal static CreditHoldemMutationPlan Decode(DocumentSnapshot snapshot)
    {
        if (!snapshot.Exists || snapshot.GetValue<long>("schemaVersion") != 1)
            throw new InvalidOperationException("The prepared Hold'em mutation schema is invalid.");
        var json = snapshot.GetValue<string>("payloadJson");
        if (CreditHoldemIds.Hash(json) != snapshot.GetValue<string>("payloadSha256"))
            throw new InvalidOperationException("The prepared Hold'em mutation digest is invalid.");
        var plan = JsonSerializer.Deserialize<CreditHoldemMutationPlan>(json, CreditHoldemMutation.Json)
            ?? throw new InvalidOperationException("The prepared Hold'em mutation is empty.");
        if (plan.Id != snapshot.Id) throw new InvalidOperationException("The prepared Hold'em mutation identity is invalid.");
        return plan with { FrozenSha256 = snapshot.GetValue<string>("payloadSha256") };
    }

    private static void ThrowDeclined(CreditHoldemMutationPlan plan, GameWalletBatchReceipt receipt)
    {
        var shortAccount = receipt.Accounts.First(value => value.InsufficientFunds);
        var effect = plan.WalletRequest().Effects.Single(value => value.AccountId == shortAccount.AccountId);
        throw new CreditHoldemInsufficientCreditsException(shortAccount.BalanceBeforeCents,
            GameWalletBalanceRequirement.RequiredCents(effect.WagerCents, effect.MinimumBalanceBeforeCents));
    }
}

internal sealed class CreditHoldemPendingMutationException(string id) : Exception
{
    internal string Id { get; } = id;
}
