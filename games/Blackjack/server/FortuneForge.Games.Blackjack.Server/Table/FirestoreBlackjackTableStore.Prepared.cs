using System.Text.Json;
using FortuneForge.Games.Abstractions.Auditing;
using FortuneForge.Games.Abstractions.Wallet;
using FortuneForge.Server.Bots.Blackjack;
using Google.Cloud.Firestore;

namespace FortuneForge.Server.Cards.Blackjack.Table;

internal sealed partial class FirestoreBlackjackTableStore
{
    private const string PreparedCollection = BlackjackBackend.TableWalletCommands;

    private async Task<StoredTableCommand> PrepareMutationAsync(Transaction transaction, string stateId,
        BlackjackTableLobbyState state, string before, StateReferences prior, LoadedBalances balances,
        BlackjackTableJournal journal, BlackjackTableCommandGuard? commandGuard, string? guardPath, DateTime nowUtc,
        string? userId, string? key, string? requestSha, BlackjackTableStoreResult? reply,
        IReadOnlyList<BlackjackReservedManagedPlayer> reservations, CancellationToken token)
    {
        var id = key is null ? BlackjackTableIds.Hash($"advance\n{stateId}\n{Guid.NewGuid():N}") : GuardKey(userId!, key);
        var reference = PreparedCommand(id);
        var existing = await transaction.GetSnapshotAsync(reference, token);
        if (existing.Exists)
        {
            var saved = ReadPrepared(existing);
            if (saved.Terms.UserId != userId || saved.Terms.RequestSha != requestSha) throw Conflict();
            return saved;
        }
        var effects = new List<TableWalletEffect>();
        var aggregates = GameWalletJournal.Aggregate(journal.Ledger.Select(entry => (entry.UserId, entry.AmountCents)))
            .ToDictionary(entry => entry.AccountId, StringComparer.Ordinal);
        foreach (var account in balances.Current.Keys.Order(StringComparer.Ordinal))
        {
            var delta = checked(balances.Current[account] - balances.Original[account]);
            var funding = aggregates.GetValueOrDefault(account) ?? new(account, 0, 0);
            if (funding.NetCents != delta)
                throw new InvalidOperationException("The Blackjack wallet journal does not reconcile with its account effects.");
            if (delta != 0 || funding.MinimumBalanceBeforeCents > 0 || account == userId)
                effects.Add(new(account, delta, funding.MinimumBalanceBeforeCents));
        }
        // Automated progress only releases or pays prepaid stakes. A command can charge its actor.
        // This invariant lets the only refusable debit precede every credit without a partial award.
        if (effects.Count(effect => effect.MinimumBalanceBeforeCents > 0) > 1 ||
            effects.Any(effect => effect.MinimumBalanceBeforeCents > 0 && effect.AccountId != userId))
            throw new InvalidOperationException("A Blackjack table command attempted a debit outside its actor.");
        var priorFinancialEvidence = await ReadRoundFinancialEvidenceAsync(transaction, stateId, state, journal, token);
        var terms = new TableCommandTerms(id, stateId, userId, key, requestSha, BlackjackTableIds.Hash(before),
            state, prior, balances, journal, commandGuard, guardPath, before != DurableStateJson(state), nowUtc,
            reply is null ? null : StoreReply(reply), effects.ToArray(), reservations.ToArray(), GameSecurityContext.Current, priorFinancialEvidence);
        if (key is null && !terms.StateChanged && journal.Ledger.Count == 0 && journal.Results.Count == 0 &&
            journal.RoundAudits.Count == 0 && journal.ManagedPlayerReleases.Count == 0)
            return new(terms, true, true, false, [], terms.ProposedReply);

        var accountLocks = await transaction.GetAllSnapshotsAsync(balances.Current.Keys.Select(PendingAccount), token);
        foreach (var accountLock in accountLocks) ThrowIfPending(accountLock);
        await CheckManagedAdoptionsAsync(transaction, state, token);
        var prepared = new StoredTableCommand(terms, false, false, false, [], null);
        transaction.Create(reference, EncodePrepared(prepared));
        var pending = new Dictionary<string, object> { ["stateId"] = stateId, ["pendingId"] = id,
            ["status"] = "pending", ["createdAt"] = Timestamp.FromDateTime(nowUtc) };
        transaction.Set(PendingLock(stateId), pending);
        foreach (var account in balances.Current.Keys) transaction.Set(PendingAccount(account), pending);
        return prepared;
    }

    private async Task<StoredTableCommand> CompletePreparedAsync(StoredTableCommand prepared, CancellationToken token)
    {
        if (prepared.RosterDone) return prepared;
        var terms = prepared.Terms;
        if (!prepared.Finalized)
        {
            var receipts = new Dictionary<string, GameWalletSettlementReceipt>(StringComparer.Ordinal);
            var declined = false;
            foreach (var effect in terms.Effects.OrderBy(effect => effect.MinimumBalanceBeforeCents == 0).ThenBy(effect => effect.AccountId, StringComparer.Ordinal))
            {
                var request = WalletRequest(terms, effect);
                var receipt = await wallet.SettleAsync(effect.AccountId, request, token);
                if (!GameWalletReceiptVerifier.MatchesSettlement(receipt, "blackjack", effect.AccountId, request))
                    throw new GameWalletUnavailableException();
                receipts.Add(effect.AccountId, receipt with { WasAlreadySettled = false });
                if (!receipt.InsufficientFunds) continue;
                if (receipts.Count != 1 || effect.MinimumBalanceBeforeCents == 0) throw new GameWalletUnavailableException();
                declined = true;
                break;
            }
            prepared = await RunTransactionAsync(async transaction =>
            {
                var snapshots = await transaction.GetAllSnapshotsAsync(
                    [PreparedCommand(terms.Id), PendingLock(terms.StateId), StateDocument(terms.StateId)], token);
                var saved = ReadPrepared(snapshots[0]);
                if (saved.Finalized) return saved;
                if (TermsHash(saved.Terms) != TermsHash(terms) || ReadString(snapshots[1], "pendingId") != terms.Id ||
                    BlackjackTableIds.Hash(DurableStateJson(ReadState(snapshots[2]))) != terms.BeforeSha)
                    throw new GameWalletUnavailableException();
                StoredTableReply? finalReply = terms.ProposedReply;
                if (finalReply is not null && terms.UserId is { } actor && receipts.TryGetValue(actor, out var accountReceipt))
                    finalReply = finalReply with { BalanceCents = accountReceipt.BalanceAfterCents };
                var final = saved with { Finalized = true, Declined = declined, Receipts = receipts, Reply = finalReply };
                if (!declined)
                {
                    using var originalRequest = terms.Observation is { } observed ? GameSecurityContext.Push(observed) : null;
                    WriteMutation(transaction, terms.StateId, terms.After, terms.Prior, terms.Balances, ProtectedJournal(terms, receipts),
                        terms.Guard, terms.GuardPath is null ? null : database.Collection(BlackjackBackend.CommandGuards).Document(terms.GuardPath), terms.StateChanged, terms.OccurredAtUtc);
                    foreach (var entry in terms.Journal.Ledger)
                    {
                        var proof = receipts[entry.UserId];
                        transaction.Set(database.Collection(BlackjackBackend.TableLedgerEvidence).Document(entry.Id), new Dictionary<string, object>
                        {
                            ["walletMutationId"] = terms.Id, ["walletSettlementId"] = proof.SettlementId,
                            ["walletBalanceBeforeCents"] = proof.BalanceBeforeCents,
                            ["walletBalanceAfterCents"] = proof.BalanceAfterCents
                        }, SetOptions.MergeAll);
                    }
                    foreach (var table in terms.After.Tables.Values)
                        transaction.Set(TableDocument(table.TableId), new Dictionary<string, object>
                        { ["backendOwner"] = BlackjackBackend.Owner, ["walletCommandIds"] = FieldValue.ArrayUnion(terms.Id) }, SetOptions.MergeAll);
                }
                transaction.Set(PreparedCommand(terms.Id), EncodePrepared(final));
                return final;
            }, token);
        }

        var rosterState = prepared.Declined
            ? new BlackjackTableLobbyState { Tables = prepared.Terms.Prior.TableSnapshots }
            : prepared.Terms.After;
        var rosterJournal = prepared.Declined ? new BlackjackTableJournal() : prepared.Terms.Journal;
        await FinalizeManagedPlayersAsync(CaptureManagedExecution(true, rosterState, rosterJournal),
            prepared.Terms.Reservations, DateTime.UtcNow, token, prepared);
        return await RunTransactionAsync(async transaction =>
        {
            var references = new[] { PreparedCommand(terms.Id), PendingLock(terms.StateId) }
                .Concat(terms.Balances.Current.Keys.Select(PendingAccount)).ToArray();
            var snapshots = await transaction.GetAllSnapshotsAsync(references, token);
            var saved = ReadPrepared(snapshots[0]);
            if (saved.RosterDone) return saved;
            if (!saved.Finalized) throw new GameWalletUnavailableException();
            var complete = saved with { RosterDone = true };
            transaction.Set(PreparedCommand(terms.Id), EncodePrepared(complete));
            for (var index = 1; index < snapshots.Count; index++)
                if (ReadString(snapshots[index], "pendingId") == terms.Id) transaction.Delete(references[index]);
            return complete;
        }, token);
    }

    private async Task<StoredTableCommand?> ReadReplayAsync(string account, string key, string requestSha, CancellationToken token)
    {
        var snapshot = await PreparedCommand(GuardKey(account, key)).GetSnapshotAsync(token);
        if (!snapshot.Exists) return null;
        var saved = ReadPrepared(snapshot);
        if (saved.Terms.UserId != account || saved.Terms.RequestSha != requestSha) throw Conflict();
        return await CompletePreparedAsync(saved, token);
    }

    private async Task RecoverAccountAsync(string account, CancellationToken token)
    {
        var snapshot = await PendingAccount(account).GetSnapshotAsync(token);
        if (!snapshot.Exists || ReadString(snapshot, "pendingId") is not { Length: > 0 } id) return;
        var saved = ReadPrepared(await PreparedCommand(id).GetSnapshotAsync(token));
        if (!saved.Terms.Balances.Current.ContainsKey(account)) throw new GameWalletUnavailableException();
        await CompletePreparedAsync(saved, token);
    }

    private async Task RecoverShardAsync(string stateId, CancellationToken token)
    {
        var snapshot = await PendingLock(stateId).GetSnapshotAsync(token);
        if (!snapshot.Exists || ReadString(snapshot, "pendingId") is not { Length: > 0 } id) return;
        var saved = ReadPrepared(await PreparedCommand(id).GetSnapshotAsync(token));
        if (saved.Terms.StateId != stateId) throw new GameWalletUnavailableException();
        await CompletePreparedAsync(saved, token);
    }

    private async Task<long> AccountBalanceAsync(string account, CancellationToken token) =>
        (await accounts.GetStateAsync(account, token)).SlotsCreditsBalanceCents ?? throw new GameWalletUnavailableException();

    private static BlackjackTableStoreResult Reply(StoredTableCommand command)
    {
        if (!command.RosterDone || command.Reply is null) throw new GameWalletUnavailableException();
        if (command.Declined)
        {
            var account = command.Receipts.Single(pair => pair.Value.InsufficientFunds);
            var amount = command.Terms.Effects.Single(effect => effect.AccountId == account.Key).MinimumBalanceBeforeCents;
            throw new BlackjackTableInsufficientCreditsException(account.Value.BalanceBeforeCents, amount);
        }
        using var json = JsonDocument.Parse(command.Reply.SessionJson);
        var session = json.RootElement.GetProperty("kind").GetString() switch
        {
            "idle" => (BlackjackTableSessionResponse?)JsonSerializer.Deserialize<BlackjackTableIdleSessionResponse>(command.Reply.SessionJson, JsonOptions),
            "queue" => ReadQueueReply(json.RootElement),
            "table" => new BlackjackTablePlaySessionResponse(
                json.RootElement.GetProperty("table").Deserialize<BlackjackTableResponse>(JsonOptions) ?? throw new GameWalletUnavailableException(),
                json.RootElement.GetProperty("version").GetInt32()),
            _ => null,
        };
        return new(session ?? throw new GameWalletUnavailableException(), command.Reply.BalanceCents);
    }

    private static BlackjackTableQueueSessionResponse ReadQueueReply(JsonElement json) => new(
        json.GetProperty("ticketId").GetString()!, json.GetProperty("position").GetInt32(),
        json.GetProperty("joinedAtUtc").GetDateTime(), json.GetProperty("humanGraceEndsAtUtc").GetDateTime(),
        json.GetProperty("players").Deserialize<BlackjackTableSeatResponse[]>(JsonOptions) ?? throw new GameWalletUnavailableException(),
        json.GetProperty("version").GetInt32());

    private static StoredTableReply StoreReply(BlackjackTableStoreResult reply) =>
        new(JsonSerializer.Serialize(reply.Session, reply.Session.GetType(), JsonOptions), reply.BalanceCents);
    private static string RequestHash(params object[] values) => BlackjackTableIds.Hash(JsonSerializer.Serialize(values, JsonOptions));
    private static string TermsHash(TableCommandTerms terms) => BlackjackTableIds.Hash(JsonSerializer.Serialize(terms, JsonOptions));
    private static GameWalletSettlementRequest WalletRequest(TableCommandTerms terms, TableWalletEffect effect) =>
        new(BlackjackTableIds.Hash($"{terms.Id}\n{effect.AccountId}"), TermsHash(terms),
            Math.Max(0, -effect.DeltaCents), Math.Max(0, effect.DeltaCents),
            MinimumBalanceBeforeCents: effect.MinimumBalanceBeforeCents > 0 ? effect.MinimumBalanceBeforeCents : null);
    private static Dictionary<string, object> EncodePrepared(StoredTableCommand command)
    {
        var json = JsonSerializer.Serialize(command, JsonOptions);
        return new() { ["stateId"] = command.Terms.StateId, ["userId"] = command.Terms.UserId ?? "",
            ["payloadJson"] = json, ["sha256"] = BlackjackTableIds.Hash(json), ["schemaVersion"] = 1L };
    }
    private static StoredTableCommand ReadPrepared(DocumentSnapshot snapshot)
    {
        var json = ReadString(snapshot, "payloadJson");
        if (string.IsNullOrEmpty(json) || ReadString(snapshot, "sha256") != BlackjackTableIds.Hash(json)) throw new GameWalletUnavailableException();
        var saved = JsonSerializer.Deserialize<StoredTableCommand>(json, JsonOptions) ?? throw new GameWalletUnavailableException();
        if (saved.Terms.Id != snapshot.Id || saved.Terms.StateId != ReadString(snapshot, "stateId") ||
            saved.Terms.Observation is { } observation && (observation.GameId != "blackjack" || observation.UserId != saved.Terms.UserId))
            throw new GameWalletUnavailableException();
        foreach (var (account, receipt) in saved.Receipts)
        {
            var effect = saved.Terms.Effects.Single(effect => effect.AccountId == account);
            if (!GameWalletReceiptVerifier.MatchesSettlement(receipt, "blackjack", account, WalletRequest(saved.Terms, effect)))
                throw new GameWalletUnavailableException();
        }
        return saved;
    }
    private static void ThrowIfPending(DocumentSnapshot snapshot)
    {
        if (snapshot.Exists && ReadString(snapshot, "pendingId") is { Length: > 0 })
            throw new BlackjackPendingTableMutationException(ReadString(snapshot, "stateId"));
    }
    private static BlackjackTableConflictException Conflict() =>
        new("This Idempotency-Key was already used for a different Blackjack table request.");
    private CollectionReference PendingLocks() => database.Collection(BlackjackBackend.PendingShards);
    private DocumentReference PendingLock(string stateId) => PendingLocks().Document(stateId);
    private DocumentReference PendingAccount(string account) => database.Collection(BlackjackBackend.PendingAccounts).Document(BlackjackTableIds.Hash(account));
    private DocumentReference PreparedCommand(string id) => database.Collection(PreparedCollection).Document(id);

    private sealed record TableWalletEffect(string AccountId, long DeltaCents, long MinimumBalanceBeforeCents);
    private sealed record StoredTableReply(string SessionJson, long BalanceCents);
    private sealed record TableCommandTerms(string Id, string StateId, string? UserId, string? Key, string? RequestSha,
        string BeforeSha, BlackjackTableLobbyState After, StateReferences Prior, LoadedBalances Balances,
        BlackjackTableJournal Journal, BlackjackTableCommandGuard? Guard, string? GuardPath, bool StateChanged,
        DateTime OccurredAtUtc, StoredTableReply? ProposedReply, TableWalletEffect[] Effects,
        BlackjackReservedManagedPlayer[] Reservations, GameRequestObservation? Observation,
        [property: System.Text.Json.Serialization.JsonIgnore(Condition = System.Text.Json.Serialization.JsonIgnoreCondition.WhenWritingNull)]
        BlackjackFinancialSnapshot? PriorFinancialEvidence = null);
    private sealed record StoredTableCommand(TableCommandTerms Terms, bool Finalized, bool RosterDone, bool Declined,
        Dictionary<string, GameWalletSettlementReceipt> Receipts, StoredTableReply? Reply);
}

internal sealed class BlackjackPendingTableMutationException(string stateId) : Exception
{
    internal string StateId { get; } = stateId;
}
