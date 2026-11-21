using System.Text.Json;
using System.Text.Json.Serialization;
using FortuneForge.Games.Abstractions.Auditing;
using FortuneForge.Games.Abstractions.Wallet;
using FortuneForge.Server.Bots.ThreeCardPoker;
using Google.Cloud.Firestore;

namespace FortuneForge.Server.Cards.ThreeCardPoker.Table;

internal sealed partial class FirestoreThreeCardPokerTableStore
{
    private async Task<ThreeCardPokerTableStoreResult?> ExecuteShardAsync(string stateId, string? userId, string? key,
        CommandIdentity? identity,
        Func<ThreeCardPokerTableCoordinator, ThreeCardPokerTableLobbyState, IDictionary<string, long>, ValueTask<ThreeCardPokerTableCoordinatorResult>>? command,
        DateTime nowUtc, CancellationToken token)
    {
        for (var attempt = 0; attempt < 12; attempt++)
        {
            if (userId is not null) await RecoverAccountAsync(userId, token);
            if (await PendingShard(stateId).GetSnapshotAsync(token) is { Exists: true } pending)
                await ResumeAsync(pending.GetValue<string>("operationId"), token);
            if (key is not null && userId is not null &&
                await CommandReceipt(userId, key).GetSnapshotAsync(token) is { Exists: true } receipt)
            {
                ValidateIdentity(receipt, identity!);
                return await ResumeAsync(receipt.GetValue<string>("operationId"), token);
            }
            var beforeSnapshot = await StateDocument(stateId).GetSnapshotAsync(token);
            var before = ReadState(beforeSnapshot);
            var loaded = await ReadBalancesAsync(before, userId, token);
            var actors = loaded.Original.Keys.Order(StringComparer.Ordinal).ToArray();
            if (actors.Length > ThreeCardPokerTableEngine.Capacity)
                throw new InvalidOperationException("A Three Card Poker shard exceeded five human accounts.");
            var operationId = key is null ? Guid.NewGuid().ToString("N") : ThreeCardPokerTableIds.Hash($"{userId}\n{key}");
            try
            {
                var staged = await RunTransactionAsync(async transaction =>
                {
                    var documents = new List<DocumentReference> { StateDocument(stateId), PendingShard(stateId) };
                    documents.AddRange(actors.Select(PendingAccount));
                    if (userId is not null) documents.Add(SessionDocument(userId));
                    if (key is not null && userId is not null) documents.Add(CommandReceipt(userId, key));
                    var snapshots = await transaction.GetAllSnapshotsAsync(documents, token);
                    if (snapshots[1].Exists) throw new PendingMutationException(snapshots[1].GetValue<string>("operationId"));
                    foreach (var accountLock in snapshots.Skip(2).Take(actors.Length))
                        if (accountLock.Exists) throw new PendingMutationException(accountLock.GetValue<string>("operationId"));
                    if (SourceHash(snapshots[0]) != SourceHash(beforeSnapshot)) throw new RetryPrepareException();
                    if (key is not null && userId is not null && snapshots[^1].Exists)
                    {
                        ValidateIdentity(snapshots[^1], identity!);
                        throw new PendingMutationException(snapshots[^1].GetValue<string>("operationId"));
                    }
                    if (userId is not null)
                    {
                        var route = snapshots[2 + actors.Length];
                        if (ReadString(route, "stateId") is { Length: > 0 } routed && routed != stateId)
                            throw new ThreeCardPokerTableShardChangedException(routed);
                    }
                    var state = ReadState(snapshots[0]);
                    var prior = StateReferences.Capture(state, userId);
                    DocumentReference? guardReference = null;
                    if (key is not null && userId is not null)
                    {
                        guardReference = GuardDocument(userId, key);
                        var guard = await transaction.GetSnapshotAsync(guardReference, token);
                        if (guard.Exists) state.Guards[GuardKey(userId, key)] = ReadGuard(guard);
                    }
                    var originalState = DurableStateJson(state);
                    var balances = new LoadedBalances(new(loaded.Original, StringComparer.Ordinal), new(loaded.Original, StringComparer.Ordinal));
                    var coordinator = new ThreeCardPokerTableCoordinator(deckFactory, seedFactory, population);
                    ThreeCardPokerTableCoordinatorResult? result;
                    ThreeCardPokerTableJournal journal;
                    if (command is null)
                    {
                        result = null;
                        journal = await coordinator.SweepAsync(state, balances.Current, nowUtc);
                    }
                    else
                    {
                        result = await command(coordinator, state, balances.Current);
                        journal = result.Journal;
                    }
                    var guardValue = userId is not null && key is not null && state.Guards.TryGetValue(GuardKey(userId, key), out var accepted) ? accepted : null;
                    state.Guards.Clear();
                    Normalize(state);
                    var changed = originalState != DurableStateJson(state);
                    if (key is null && !changed && journal.Ledger.Count == 0 && journal.Results.Count == 0 && journal.ManagedPlayerReleases.Count == 0)
                        return new PrepareResult(null, result?.Store);
                    var funding = FundingFor(journal.Ledger, balances.Original, balances.Current);
                    var deltas = funding.ToDictionary(effect => effect.AccountId, effect => effect.NetCents, StringComparer.Ordinal);
                    var minimums = funding.ToDictionary(effect => effect.AccountId, effect => effect.MinimumBalanceBeforeCents, StringComparer.Ordinal);
                    // Only the manual wager/play actor can require funds before this mutation's credits.
                    if (minimums.Any(pair => pair.Value > 0 && pair.Key != userId))
                        throw new InvalidOperationException("A Three Card Poker automated transition attempted to debit another account.");
                    var observation = GameSecurityContext.Current;
                    if (observation is not null && userId is not null && (observation.UserId != userId || observation.GameId != ThreeCardPokerModule.Descriptor.Id))
                        throw new ArgumentException("The authenticated action belongs to another account or game.");
                    var financialEvidence = await ReadFinancialEvidenceAsync(transaction, stateId, journal, token);
                    var prepared = new FrozenMutation(operationId, stateId, userId, key, identity, SourceHash(snapshots[0]), actors,
                        state, prior.TableSnapshots.ToDictionary(pair => pair.Key, pair => pair.Value, StringComparer.Ordinal),
                        prior.TicketIds.ToArray(), prior.UserIds.ToArray(), balances.Original, balances.Current, deltas, minimums,
                        FrozenJournal.From(journal), guardValue, result?.Store, changed, nowUtc,
                        observation is null ? null : observation with { SessionId = null, RequestJson = null }, financialEvidence);
                    var fields = new Dictionary<string, object>
                    {
                        ["preparedJson"] = Serialize(prepared), ["preparedSha256"] = Hash(prepared),
                        ["status"] = "prepared", ["stateId"] = stateId, ["schemaVersion"] = 1L,
                        ["createdAt"] = Timestamp.FromDateTime(nowUtc)
                    };
                    transaction.Create(Operation(operationId), fields);
                    var pendingFields = new Dictionary<string, object> { ["operationId"] = operationId, ["stateId"] = stateId };
                    transaction.Create(PendingShard(stateId), pendingFields);
                    foreach (var actor in actors) transaction.Create(PendingAccount(actor), pendingFields);
                    if (key is not null && userId is not null)
                        transaction.Create(CommandReceipt(userId, key), new Dictionary<string, object>
                        { ["operationId"] = operationId, ["requestSha256"] = Hash(identity!), ["stateId"] = stateId });
                    return new PrepareResult(operationId, null);
                }, token);
                if (staged.OperationId is not null) return await ResumeAsync(staged.OperationId, token);
                await MaintainRosterAsync(before, [], token);
                return staged.Result;
            }
            catch (RetryPrepareException) { }
            catch (PendingMutationException pendingMutation) { await ResumeAsync(pendingMutation.OperationId, token); }
        }
        throw new ThreeCardPokerTableConflictException("The Three Card Poker table changed. Reconnect and retry the same operation.");
    }

    private async Task RecoverAccountAsync(string userId, CancellationToken token)
    {
        var pending = await PendingAccount(userId).GetSnapshotAsync(token);
        if (!pending.Exists) return;
        try { await ResumeAsync(pending.GetValue<string>("operationId"), token); }
        catch (ThreeCardPokerTableInsufficientCreditsException) { }
    }

    private async Task<ThreeCardPokerTableStoreResult?> ResumeAsync(string operationId, CancellationToken token)
    {
        var snapshot = await Operation(operationId).GetSnapshotAsync(token);
        var frozen = ReadFrozen(snapshot);
        var status = ReadString(snapshot, "status");
        if (status == "declined") throw Decline(snapshot);
        if (status == "completed") return ReadResult(snapshot);
        if (status == "prepared")
        {
            var financialEvidence = frozen.PriorFinancialEvidence ?? await RunTransactionAsync(
                transaction => ReadFinancialEvidenceAsync(transaction, frozen.StateId, frozen.Journal.ToJournal(), token), token);
            var financialActors = FinancialActors(frozen);
            var receipts = new Dictionary<string, GameWalletSettlementReceipt>(StringComparer.Ordinal);
            foreach (var actor in financialActors)
            {
                var request = WalletRequest(frozen, actor);
                var receiptReference = WalletReceipt(operationId, actor);
                var prior = await receiptReference.GetSnapshotAsync(token);
                GameWalletSettlementReceipt receipt;
                if (prior.Exists) receipt = Deserialize<GameWalletSettlementReceipt>(prior.GetValue<string>("json"));
                else
                {
                    try { receipt = await wallet.SettleAsync(actor, request, token); }
                    catch (GameWalletConflictException error) { throw new GameWalletUnavailableException(error); }
                    if (!GameWalletReceiptVerifier.MatchesSettlement(receipt, ThreeCardPokerModule.Descriptor.Id, actor, request))
                        throw new GameWalletUnavailableException();
                    receipt = receipt with { WasAlreadySettled = false };
                    await RunTransactionAsync(async transaction =>
                    {
                        var current = await transaction.GetSnapshotAsync(receiptReference, token);
                        if (current.Exists)
                        {
                            if (Deserialize<GameWalletSettlementReceipt>(current.GetValue<string>("json")) != receipt) throw new GameWalletUnavailableException();
                        }
                        else transaction.Create(receiptReference, new Dictionary<string, object> { ["json"] = Serialize(receipt), ["outcomeSha256"] = Hash(frozen) });
                        return true;
                    }, token);
                }
                if (!GameWalletReceiptVerifier.MatchesSettlement(receipt, ThreeCardPokerModule.Descriptor.Id, actor, request))
                    throw new GameWalletUnavailableException();
                receipts[actor] = receipt;
                if (receipt.InsufficientFunds)
                {
                    await DeclineAsync(frozen, receipt, frozen.MinimumBalances[actor], token);
                    throw new ThreeCardPokerTableInsufficientCreditsException(receipt.BalanceBeforeCents, frozen.MinimumBalances[actor]);
                }
            }
            await ApplyAsync(frozen, receipts, financialEvidence, token);
        }
        await FinalizeRosterAsync(frozen, token);
        return await CompleteAsync(frozen, token);
    }

    private async Task ApplyAsync(FrozenMutation frozen, IReadOnlyDictionary<string, GameWalletSettlementReceipt> receipts,
        FinancialSnapshot financialEvidence, CancellationToken token) =>
        await RunTransactionAsync(async transaction =>
        {
            var snapshots = await transaction.GetAllSnapshotsAsync([Operation(frozen.OperationId), StateDocument(frozen.StateId), PendingShard(frozen.StateId)], token);
            var current = ReadFrozen(snapshots[0]);
            if (Hash(current) != Hash(frozen)) throw new GameWalletUnavailableException();
            if (ReadString(snapshots[0], "status") != "prepared") return true;
            if (!snapshots[2].Exists || ReadString(snapshots[2], "operationId") != frozen.OperationId || SourceHash(snapshots[1]) != frozen.BeforeSha256)
                throw new GameWalletUnavailableException();
            var pointers = await transaction.GetAllSnapshotsAsync(frozen.Users.Select(PendingAccount), token);
            if (pointers.Any(pointer => !pointer.Exists || ReadString(pointer, "operationId") != frozen.OperationId)) throw new GameWalletUnavailableException();
            using var observed = frozen.Observation is { } observation ? GameSecurityContext.Push(observation) : null;
            var before = new StateReferences(frozen.BeforeTables.Keys.ToHashSet(StringComparer.Ordinal), frozen.BeforeTickets.ToHashSet(StringComparer.Ordinal),
                frozen.BeforeUsers.ToHashSet(StringComparer.Ordinal), frozen.BeforeTables);
            var journal = ProtectedJournal(frozen, receipts, financialEvidence);
            WriteMutation(transaction, frozen.StateId, frozen.After, before, new(frozen.AfterBalances, frozen.BeforeBalances), journal,
                frozen.Guard, frozen.UserId is not null && frozen.Key is not null ? GuardDocument(frozen.UserId, frozen.Key) : null,
                frozen.StateChanged, frozen.OccurredAtUtc, frozen.OperationId, receipts);
            var reply = frozen.Result;
            if (reply is not null && frozen.UserId is not null && receipts.TryGetValue(frozen.UserId, out var actorReceipt))
                reply = reply with { BalanceCents = actorReceipt.BalanceAfterCents };
            transaction.Update(Operation(frozen.OperationId), new Dictionary<string, object>
            { ["status"] = "applied", ["resultJson"] = Serialize(reply), ["appliedAt"] = Timestamp.GetCurrentTimestamp() });
            return true;
        }, token);

    private Task FinalizeRosterAsync(FrozenMutation frozen, CancellationToken token) => MaintainRosterAsync(frozen.After, frozen.Journal.Releases, token);

    private async Task MaintainRosterAsync(ThreeCardPokerTableLobbyState state, IEnumerable<ThreeCardPokerManagedPlayerRelease> releases, CancellationToken token)
    {
        var now = DateTime.UtcNow;
        foreach (var table in state.Tables.Values)
        {
            var profiles = table.Players.Where(ThreeCardPokerManagedSeat.IsManaged).Select(player => player.ActorId).Distinct(StringComparer.Ordinal).ToArray();
            if (profiles.Length > 0) await roster.HeartbeatAsync(table.TableId, profiles, now, token);
        }
        foreach (var group in releases.DistinctBy(release => $"{release.AssignmentId}\n{release.ProfileId}").GroupBy(release => release.AssignmentId))
            await roster.ReleaseAsync(group.Key, group.Select(release => release.ProfileId).ToArray(), now, token);
    }

    private Task<ThreeCardPokerTableStoreResult?> CompleteAsync(FrozenMutation frozen, CancellationToken token) =>
        RunTransactionAsync(async transaction =>
        {
            var snapshot = await transaction.GetSnapshotAsync(Operation(frozen.OperationId), token);
            if (ReadString(snapshot, "status") == "completed") return ReadResult(snapshot);
            if (ReadString(snapshot, "status") != "applied") throw new GameWalletUnavailableException();
            var locks = await transaction.GetAllSnapshotsAsync(frozen.Users.Select(PendingAccount).Append(PendingShard(frozen.StateId)), token);
            if (locks.Any(pointer => !pointer.Exists || ReadString(pointer, "operationId") != frozen.OperationId)) throw new GameWalletUnavailableException();
            foreach (var pointer in locks) transaction.Delete(pointer.Reference);
            transaction.Update(Operation(frozen.OperationId), new Dictionary<string, object>
            { ["status"] = "completed", ["rosterFinalizedAt"] = Timestamp.GetCurrentTimestamp() });
            return ReadResult(snapshot);
        }, token);

    private Task<bool> DeclineAsync(FrozenMutation frozen, GameWalletSettlementReceipt receipt, long required, CancellationToken token) =>
        RunTransactionAsync(async transaction =>
        {
            var snapshot = await transaction.GetSnapshotAsync(Operation(frozen.OperationId), token);
            if (ReadString(snapshot, "status") == "declined") return true;
            if (ReadString(snapshot, "status") != "prepared") throw new GameWalletUnavailableException();
            var pointers = await transaction.GetAllSnapshotsAsync(frozen.Users.Select(PendingAccount).Append(PendingShard(frozen.StateId)), token);
            if (pointers.Any(pointer => !pointer.Exists || ReadString(pointer, "operationId") != frozen.OperationId)) throw new GameWalletUnavailableException();
            foreach (var pointer in pointers) transaction.Delete(pointer.Reference);
            transaction.Update(Operation(frozen.OperationId), new Dictionary<string, object>
            { ["status"] = "declined", ["availableCents"] = receipt.BalanceBeforeCents, ["requiredCents"] = required });
            return true;
        }, token);

    private static GameWalletSettlementRequest WalletRequest(FrozenMutation frozen, string actor) => new(
        ThreeCardPokerTableIds.Hash($"{frozen.OperationId}\n{actor}"), Hash(frozen), Math.Max(0, -frozen.Deltas[actor]), Math.Max(0, frozen.Deltas[actor]),
        MinimumBalanceBeforeCents: frozen.MinimumBalances[actor]);

    private static string[] FinancialActors(FrozenMutation frozen) => frozen.Journal.Ledger.Select(entry => entry.UserId)
        .Concat(frozen.Deltas.Where(pair => pair.Value != 0).Select(pair => pair.Key)).Distinct(StringComparer.Ordinal)
        .OrderBy(actor => frozen.MinimumBalances[actor] > 0 ? 0 : 1).ThenBy(actor => actor, StringComparer.Ordinal).ToArray();

    internal static IReadOnlyList<GameWalletJournalBalance> FundingFor(IEnumerable<ThreeCardPokerTableLedgerEntry> ledger,
        IReadOnlyDictionary<string, long> before, IReadOnlyDictionary<string, long> after)
    {
        var journal = GameWalletJournal.Aggregate(ledger.Select(entry => (entry.UserId, entry.AmountCents)))
            .ToDictionary(effect => effect.AccountId, StringComparer.Ordinal);
        if (journal.Keys.Any(user => !before.ContainsKey(user)))
            throw new InvalidOperationException("The Three Card Poker ledger contains an account outside its frozen shard.");
        var effects = new List<GameWalletJournalBalance>();
        foreach (var (user, balance) in before)
        {
            var effect = journal.GetValueOrDefault(user) ?? new(user, 0, 0);
            if (!after.TryGetValue(user, out var final) || checked(final - balance) != effect.NetCents)
                throw new InvalidOperationException("The Three Card Poker ledger does not reconcile with its frozen account effects.");
            effects.Add(effect);
        }
        return effects;
    }
    private static string SourceHash(DocumentSnapshot snapshot) => ThreeCardPokerTableIds.Hash(ReadString(snapshot, "stateJson"));
    private static string Hash<T>(T value) => ThreeCardPokerTableIds.Hash(Serialize(value));
    private static string Serialize<T>(T value) => JsonSerializer.Serialize(value, JsonOptions);
    private static T Deserialize<T>(string json) => JsonSerializer.Deserialize<T>(json, JsonOptions) ?? throw new InvalidOperationException("The recorded Three Card Poker operation is invalid.");
    private static FrozenMutation ReadFrozen(DocumentSnapshot snapshot)
    {
        if (!snapshot.Exists) throw new GameWalletUnavailableException();
        var frozen = Deserialize<FrozenMutation>(snapshot.GetValue<string>("preparedJson"));
        if (frozen.OperationId != snapshot.Id || Hash(frozen) != snapshot.GetValue<string>("preparedSha256")) throw new GameWalletUnavailableException();
        return frozen;
    }
    private static ThreeCardPokerTableStoreResult? ReadResult(DocumentSnapshot snapshot) =>
        JsonSerializer.Deserialize<ThreeCardPokerTableStoreResult>(snapshot.GetValue<string>("resultJson"), JsonOptions);
    private static ThreeCardPokerTableInsufficientCreditsException Decline(DocumentSnapshot snapshot) =>
        new(snapshot.GetValue<long>("availableCents"), snapshot.GetValue<long>("requiredCents"));
    private static void ValidateIdentity(DocumentSnapshot snapshot, CommandIdentity identity)
    {
        if (ReadString(snapshot, "requestSha256") != Hash(identity))
            throw new ThreeCardPokerTableConflictException("This Idempotency-Key was already used for a different Three Card Poker request.");
    }

    private sealed record CommandIdentity(string Operation, string Target, string Detail);
    private sealed record PrepareResult(string? OperationId, ThreeCardPokerTableStoreResult? Result);
    private sealed class PendingMutationException(string operationId) : Exception { public string OperationId { get; } = operationId; }
    private sealed class RetryPrepareException : Exception { }
    private sealed record FrozenMutation(string OperationId, string StateId, string? UserId, string? Key, CommandIdentity? Identity,
        string BeforeSha256, string[] Users, ThreeCardPokerTableLobbyState After,
        Dictionary<string, ThreeCardPokerTableState> BeforeTables, string[] BeforeTickets, string[] BeforeUsers,
        Dictionary<string, long> BeforeBalances, Dictionary<string, long> AfterBalances, Dictionary<string, long> Deltas, Dictionary<string, long> MinimumBalances,
        FrozenJournal Journal, ThreeCardPokerTableCommandGuard? Guard, ThreeCardPokerTableStoreResult? Result,
        bool StateChanged, DateTime OccurredAtUtc, GameRequestObservation? Observation,
        [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] FinancialSnapshot? PriorFinancialEvidence = null);
    private sealed record FrozenJournal(RoundAuditCapture[] Audits, ThreeCardPokerTableLedgerEntry[] Ledger,
        ThreeCardPokerTableRevenueEntry[] Revenue, ThreeCardPokerTableResultEntry[] Results, ThreeCardPokerManagedPlayerRelease[] Releases)
    {
        internal static FrozenJournal From(ThreeCardPokerTableJournal journal) =>
            new(journal.RoundAudits.ToArray(), journal.Ledger.ToArray(), journal.Revenue.ToArray(), journal.Results.ToArray(), journal.ManagedPlayerReleases.ToArray());
        internal ThreeCardPokerTableJournal ToJournal()
        {
            var journal = new ThreeCardPokerTableJournal();
            journal.RoundAudits.AddRange(Audits); journal.Ledger.AddRange(Ledger); journal.Revenue.AddRange(Revenue);
            journal.Results.AddRange(Results); journal.ManagedPlayerReleases.AddRange(Releases);
            return journal;
        }
    }
}
