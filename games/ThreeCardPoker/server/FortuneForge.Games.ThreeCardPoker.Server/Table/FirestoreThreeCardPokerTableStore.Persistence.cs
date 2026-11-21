using System.Text.Json;
using FortuneForge.Games.Abstractions.ManagedPlayers;
using FortuneForge.Games.Abstractions.Wallet;
using FortuneForge.Games.Firestore;
using FortuneForge.Games.ThreeCardPoker.Server;
using FortuneForge.Server.Bots.ThreeCardPoker;
using Google.Cloud.Firestore;
using Grpc.Core;

namespace FortuneForge.Server.Cards.ThreeCardPoker.Table;

internal sealed partial class FirestoreThreeCardPokerTableStore
{
    private void WriteMutation(
        Transaction transaction,
        string stateId,
        ThreeCardPokerTableLobbyState state,
        StateReferences prior,
        LoadedBalances balances,
        ThreeCardPokerTableJournal journal,
        ThreeCardPokerTableCommandGuard? commandGuard,
        DocumentReference? guardReference,
        bool stateChanged,
        DateTime nowUtc,
        string operationId,
        IReadOnlyDictionary<string, GameWalletSettlementReceipt> receipts)
    {
        if (stateChanged)
        {
            if (IsEmpty(state))
            {
                transaction.Delete(StateDocument(stateId));
            }
            else
            {
                var stateData = new Dictionary<string, object>
                {
                    ["stateJson"] = DurableStateJson(state),
                    ["acceptingHumans"] = false,
                    ["humanCount"] = ActiveHumanCount(state),
                    ["tableCount"] = state.Tables.Count,
                    ["queuedTicketCount"] = state.Tickets.Count,
                    ["createdAt"] = Timestamp.FromDateTime(StateCreatedAt(state, nowUtc)),
                    ["updatedAt"] = Timestamp.FromDateTime(nowUtc),
                    ["schemaVersion"] = 2L,
                    ["walletMutationId"] = operationId
                };
                if (NextDeadline(state) is { } deadline)
                    stateData["nextDeadlineAt"] = Timestamp.FromDateTime(deadline);
                else
                    stateData["nextDeadlineAt"] = FieldValue.Delete;
                transaction.Set(StateDocument(stateId), stateData, SetOptions.MergeAll);
            }

            foreach (var tableId in prior.TableIds.Except(state.Tables.Keys, StringComparer.Ordinal))
            {
                var previous = prior.TableSnapshots[tableId];
                FirestoreGameJournal.Write(transaction, database, "three-card-poker",
                    $"{tableId}-round-{previous.RoundNumber}", previous.Version, nowUtc,
                    new { previous, removed = true }, previous.Players.Where(player => !ThreeCardPokerManagedSeat.IsManaged(player)).Select(player => player.ActorId),
                    "table-removed", tableId);
                transaction.Delete(TableDocument(tableId));
            }
            foreach (var table in state.Tables.Values)
            {
                FirestoreGameJournal.Write(transaction, database, "three-card-poker",
                    $"{table.TableId}-round-{table.RoundNumber}", table.Version, nowUtc, table,
                    table.Players.Where(player => !ThreeCardPokerManagedSeat.IsManaged(player)).Select(player => player.ActorId), "engine-state", table.TableId);
                transaction.Set(TableDocument(table.TableId), new Dictionary<string, object>
                {
                    ["stateId"] = stateId,
                    ["tableId"] = table.TableId,
                    ["tableJson"] = JsonSerializer.Serialize(table, JsonOptions),
                    ["walletMutationId"] = operationId,
                    ["phase"] = table.Phase,
                    ["round"] = table.RoundNumber,
                    ["version"] = table.Version,
                    ["updatedAt"] = Timestamp.FromDateTime(table.UpdatedAtUtc),
                    ["schemaVersion"] = 2L
                }, SetOptions.MergeAll);
            }

            foreach (var ticketId in prior.TicketIds.Except(
                         state.Tickets.Select(ticket => ticket.TicketId), StringComparer.Ordinal))
                transaction.Delete(TicketDocument(ticketId));
            foreach (var ticket in state.Tickets)
            {
                transaction.Set(TicketDocument(ticket.TicketId), new Dictionary<string, object>
                {
                    ["stateId"] = stateId,
                    ["ticketId"] = ticket.TicketId,
                    ["ticketJson"] = JsonSerializer.Serialize(ticket, JsonOptions),
                    ["status"] = ticket.Status,
                    ["version"] = ticket.Version,
                    ["expiresAt"] = Timestamp.FromDateTime(ticket.GraceEndsAtUtc.AddHours(1)),
                    ["schemaVersion"] = 2L
                }, SetOptions.MergeAll);
            }

            var routeUsers = prior.UserIds.Union(state.Sessions.Keys, StringComparer.Ordinal);
            foreach (var routeUserId in routeUsers)
            {
                if (!state.Sessions.TryGetValue(routeUserId, out var link) ||
                    link.Kind == ThreeCardPokerTableSessionKinds.Idle)
                {
                    transaction.Delete(SessionDocument(routeUserId));
                    continue;
                }
                transaction.Set(SessionDocument(routeUserId), new Dictionary<string, object>
                {
                    ["stateId"] = stateId,
                    ["kind"] = link.Kind,
                    ["ticketId"] = link.TicketId ?? string.Empty,
                    ["tableId"] = link.TableId ?? string.Empty,
                    ["updatedAt"] = Timestamp.FromDateTime(nowUtc),
                    ["schemaVersion"] = 2L
                }, SetOptions.MergeAll);
            }
        }

        foreach (var table in state.Tables.Values)
            foreach (var player in table.Players.Where(player => !ThreeCardPokerManagedSeat.IsManaged(player) && player.TotalWagerCents > 0 && player.Cards.Count == 3))
                FirestoreGameAudit.WriteAcceptedParticipation(transaction, database, ThreeCardPokerModule.Descriptor.Id, player.ActorId, nowUtc);

        foreach (var entry in journal.Ledger)
        {
            var fields = LedgerData(entry);
            fields["walletMutationId"] = operationId;
            fields["backendOwner"] = ThreeCardPokerBackend.Owner;
            fields["walletSettlementId"] = receipts[entry.UserId].SettlementId;
            transaction.Create(LedgerDocument(entry.Id), fields);
        }
        foreach (var entry in journal.Revenue)
        {
            var fields = RevenueData(entry);
            fields["walletMutationId"] = operationId;
            transaction.Create(RevenueDocument(entry.RoundId), fields);
        }
        foreach (var audit in journal.RoundAudits)
        {
            FirestoreGameAudit.WriteCompletion(transaction, database, audit);
            var startKnown = !audit.Summary.Findings.Any(finding => finding.Code == "round-start-unavailable");
            foreach (var player in audit.Summary.Participants.Where(player => player.IsAccountBacked))
            {
                var analytics = FortuneForge.Games.Abstractions.Analytics.GameAnalyticsOutbox.Create(new(
                    audit.Summary.GameId, audit.Summary.RoundId, player.ActorId, "paid", true,
                    audit.Summary.CompletedAtUtc, startKnown ? audit.Summary.StartedAtUtc : null,
                    player.WagerCents, player.PayoutCents, "gameRoundAudits", audit.Summary.Id))!;
                transaction.Set(database.Collection(FortuneForge.Games.Abstractions.Analytics.GameAnalyticsOutbox.CollectionName)
                    .Document(analytics.Id), analytics.Fields);
            }
        }
        foreach (var result in journal.Results)
        {
            var fields = CardGameResultData(result);
            fields["walletMutationId"] = operationId;
            transaction.Create(CardGameResultDocument(result.ResultId), fields);
            if (result.IsManagedPlayer)
            {
                transaction.Set(ManagedProfileDocument(result.UserId), new Dictionary<string, object>
                {
                    ["cardMatchesPlayed"] = FieldValue.Increment(1),
                    ["cardWins"] = FieldValue.Increment(result.PayoutCents > result.WagerCents ? 1 : 0),
                    ["cardLosses"] = FieldValue.Increment(result.PayoutCents < result.WagerCents ? 1 : 0),
                    ["cardPushes"] = FieldValue.Increment(result.PayoutCents == result.WagerCents ? 1 : 0),
                    ["updatedAt"] = Timestamp.FromDateTime(result.CompletedAtUtc)
                }, SetOptions.MergeAll);
            }
        }
        if (guardReference is not null && commandGuard is not null)
        {
            transaction.Set(guardReference, new Dictionary<string, object>
            {
                ["stateId"] = stateId,
                ["guardJson"] = JsonSerializer.Serialize(commandGuard, JsonOptions),
                ["createdAt"] = Timestamp.FromDateTime(commandGuard.CreatedAtUtc),
                ["expiresAt"] = Timestamp.FromDateTime(commandGuard.CreatedAtUtc.Add(GuardRetention)),
                ["schemaVersion"] = 2L
            }, SetOptions.MergeAll);
        }
    }

    private async Task<string?> ResolveStateIdAsync(
        string userId,
        string idempotencyKey,
        CancellationToken cancellationToken)
    {
        var snapshots = await Task.WhenAll(
            SessionDocument(userId).GetSnapshotAsync(cancellationToken),
            GuardDocument(userId, idempotencyKey).GetSnapshotAsync(cancellationToken));
        var sessionState = ReadString(snapshots[0], "stateId");
        if (!string.IsNullOrEmpty(sessionState)) return sessionState;
        var guardState = ReadString(snapshots[1], "stateId");
        return string.IsNullOrEmpty(guardState) ? null : guardState;
    }

    private async Task<T> RunTransactionAsync<T>(
        Func<Transaction, Task<T>> callback,
        CancellationToken cancellationToken)
    {
        const int maximumAttempts = 12;
        for (var attempt = 1; ; attempt++)
        {
            try
            {
                return await database.RunTransactionAsync(callback, cancellationToken: cancellationToken);
            }
            catch (RpcException exception) when (
                (exception.StatusCode == StatusCode.Aborted ||
                 exception.StatusCode == StatusCode.InvalidArgument &&
                 exception.Status.Detail.Contains("Transaction is invalid or closed", StringComparison.OrdinalIgnoreCase)) &&
                attempt < maximumAttempts)
            {
                var exponential = Math.Min(500, 20 * (1 << Math.Min(attempt - 1, 5)));
                var backoffMilliseconds = exponential + Random.Shared.Next(25, 126);
                await Task.Delay(backoffMilliseconds, cancellationToken);
            }
        }
    }

    private static void Normalize(ThreeCardPokerTableLobbyState state)
    {
        foreach (var table in state.Tables.Values.Where(value => value.Phase == ThreeCardPokerTablePhases.Closed).ToArray())
        {
            foreach (var ticket in state.Tickets.Where(value =>
                         value.Status == "queued" && value.TargetTableId == table.TableId).ToArray())
            {
                var index = state.Tickets.FindIndex(value => value.TicketId == ticket.TicketId);
                state.Tickets[index] = ticket with { TargetTableId = null, EligibleAfterRound = 0 };
            }
            state.Tables.Remove(table.TableId);
        }
        state.Tickets.RemoveAll(ticket => ticket.Status != "queued");
        foreach (var userId in state.Sessions
                     .Where(pair => pair.Value.Kind == ThreeCardPokerTableSessionKinds.Idle)
                     .Select(pair => pair.Key)
                     .ToArray())
            state.Sessions.Remove(userId);
    }

    private static int ActiveHumanCount(ThreeCardPokerTableLobbyState state) => state.Sessions.Count(pair =>
        pair.Value.Kind is ThreeCardPokerTableSessionKinds.Queue or ThreeCardPokerTableSessionKinds.Table);

    private static bool IsEmpty(ThreeCardPokerTableLobbyState state) =>
        state.Sessions.Count == 0 && state.Tickets.Count == 0 && state.Tables.Count == 0;

    private static DateTime? NextDeadline(ThreeCardPokerTableLobbyState state)
    {
        var deadlines = state.Tickets.Where(ticket => ticket.Status == "queued" && ticket.TargetTableId is null)
            .Select(ticket => (DateTime?)ticket.GraceEndsAtUtc)
            .Concat(state.Tables.Values.SelectMany(table => new DateTime?[]
            {
                table.ActionDeadlineAtUtc,
                table.WagerDeadlineAtUtc,
                table.NextTransitionAtUtc
            }.Concat(table.Players.SelectMany(player => new DateTime?[]
            {
                ThreeCardPokerManagedSeat.ActionReadyAt(player),
                ThreeCardPokerManagedSeat.WagerReadyAt(player)
            }))))
            .Where(value => value is not null)
            .Select(value => value!.Value)
            .ToArray();
        return deadlines.Length == 0 ? null : deadlines.Min();
    }

    private static DateTime StateCreatedAt(ThreeCardPokerTableLobbyState state, DateTime fallback) =>
        state.Tickets.Select(ticket => ticket.JoinedAtUtc)
            .Concat(state.Tables.Values.Select(table => table.CreatedAtUtc))
            .DefaultIfEmpty(fallback)
            .Min();

    private static string DurableStateJson(ThreeCardPokerTableLobbyState state) =>
        JsonSerializer.Serialize(state, JsonOptions);

    private static string GuardKey(string userId, string idempotencyKey) =>
        ThreeCardPokerTableIds.Hash($"{userId}\n{idempotencyKey}");

    private CollectionReference StateCollection() => database.Collection(ThreeCardPokerBackend.TableState);
    private DocumentReference StateDocument(string stateId) => StateCollection().Document(stateId);
    private DocumentReference LeaseDocument() =>
        database.Collection("threeCardPokerTableLeases").Document("deadline-worker");
    private DocumentReference TableDocument(string tableId) =>
        database.Collection("threeCardPokerTables").Document(tableId);
    private DocumentReference TicketDocument(string ticketId) =>
        database.Collection("threeCardPokerTableTickets").Document(ticketId);
    private DocumentReference SessionDocument(string userId) =>
        database.Collection("threeCardPokerTableSessions").Document(ThreeCardPokerTableIds.Hash(userId));
    private DocumentReference GuardDocument(string userId, string idempotencyKey) =>
        database.Collection("threeCardPokerTableCommandGuards").Document(GuardKey(userId, idempotencyKey));
    private DocumentReference LedgerDocument(string id) =>
        database.Collection(ThreeCardPokerBackend.TableLedgerEvidence).Document(id);
    private DocumentReference RevenueDocument(string id) =>
        database.Collection("threeCardPokerTableRoundRevenue").Document(ThreeCardPokerTableIds.Hash(id));
    private DocumentReference CardGameResultDocument(string resultId) =>
        database.Collection("cardGameResults").Document(resultId);
    private DocumentReference ManagedProfileDocument(string userId) =>
        database.Collection("threeCardPokerManagedStatistics").Document(userId);

    private static ThreeCardPokerTableLobbyState ReadState(DocumentSnapshot snapshot)
    {
        var json = ReadString(snapshot, "stateJson");
        return string.IsNullOrEmpty(json)
            ? new ThreeCardPokerTableLobbyState()
            : JsonSerializer.Deserialize<ThreeCardPokerTableLobbyState>(json, JsonOptions)
              ?? throw new InvalidOperationException("The durable Three Card Poker table shard state is invalid.");
    }

    private static ThreeCardPokerTableCommandGuard ReadGuard(DocumentSnapshot snapshot)
    {
        var json = ReadString(snapshot, "guardJson");
        return JsonSerializer.Deserialize<ThreeCardPokerTableCommandGuard>(json, JsonOptions)
            ?? throw new InvalidOperationException("The durable Three Card Poker table command guard is invalid.");
    }

    private static Dictionary<string, object> LedgerData(ThreeCardPokerTableLedgerEntry entry) => new()
    {
        ["transactionId"] = entry.Id,
        ["userId"] = entry.UserId,
        ["currencyId"] = CurrencyId,
        ["amount"] = (double)ThreeCardPokerMoney.ToRand(entry.AmountCents),
        ["balanceAfter"] = (double)ThreeCardPokerMoney.ToRand(entry.BalanceAfterCents),
        ["type"] = entry.Type,
        ["idempotencyKey"] = entry.Reference,
        ["createdAt"] = Timestamp.FromDateTime(entry.CreatedAtUtc)
    };

    private static Dictionary<string, object> RevenueData(ThreeCardPokerTableRevenueEntry entry) => new()
    {
        ["roundId"] = entry.RoundId,
        ["tableId"] = entry.TableId,
        ["round"] = entry.RoundNumber,
        ["humanWagerCents"] = entry.HumanWagerCents,
        ["humanPayoutCents"] = entry.HumanPayoutCents,
        ["houseNetCents"] = checked(entry.HumanWagerCents - entry.HumanPayoutCents),
        ["humanPlayerCount"] = entry.HumanPlayerCount,
        ["currencyId"] = CurrencyId,
        ["financialClassification"] = "real-human-dealer-counterparty-v1",
        ["botFinancialContributionCents"] = 0L,
        ["recognizedAt"] = Timestamp.FromDateTime(entry.SettledAtUtc),
        ["settledAt"] = Timestamp.FromDateTime(entry.SettledAtUtc),
        ["schemaVersion"] = 1L
    };

    private static Dictionary<string, object> CardGameResultData(ThreeCardPokerTableResultEntry entry) => new()
    {
        ["resultId"] = entry.ResultId,
        ["game"] = "three-card-poker",
        ["mode"] = "credit-table",
        ["matchId"] = entry.TableId,
        ["tableId"] = entry.TableId,
        ["round"] = entry.RoundNumber,
        ["userId"] = entry.UserId,
        ["userHash"] = ThreeCardPokerTableIds.Hash(entry.UserId),
        ["userGameHash"] = ThreeCardPokerTableIds.Hash($"{entry.UserId}\nthree-card-poker\ncredit-table"),
        ["claimStatus"] = "completed",
        ["settlementStatus"] = entry.IsManagedPlayer ? "recorded" : "paid",
        ["wagerCents"] = entry.WagerCents,
        ["payoutCents"] = entry.PayoutCents,
        ["netCents"] = checked(entry.PayoutCents - entry.WagerCents),
        ["completedAt"] = Timestamp.FromDateTime(entry.CompletedAtUtc),
        ["seenAt"] = null!,
        ["financialClassification"] = entry.IsManagedPlayer
            ? "managed-player-virtual-v1"
            : "real-human-dealer-counterparty-v1",
        ["schemaVersion"] = 1L
    };

    private static ThreeCardPokerTableHistoryItemResponse HistoryResponse(
        DocumentSnapshot snapshot,
        DateTime? seenOverride = null)
    {
        var seenAt = seenOverride;
        if (seenAt is null && snapshot.Exists &&
            snapshot.ToDictionary().TryGetValue("seenAt", out var seenValue) && seenValue is Timestamp timestamp)
            seenAt = timestamp.ToDateTime();
        return new(
            ReadString(snapshot, "resultId"),
            ReadString(snapshot, "game"),
            ReadString(snapshot, "mode"),
            ReadString(snapshot, "matchId"),
            ReadString(snapshot, "tableId"),
            checked((int)ReadLong(snapshot, "round")),
            ThreeCardPokerMoney.ToRand(ReadLong(snapshot, "wagerCents")),
            ThreeCardPokerMoney.ToRand(ReadLong(snapshot, "payoutCents")),
            ThreeCardPokerMoney.ToRand(ReadLong(snapshot, "netCents")),
            ReadString(snapshot, "claimStatus"),
            ReadString(snapshot, "settlementStatus"),
            ReadTimestamp(snapshot, "completedAt"),
            seenAt is not null,
            seenAt);
    }

    private static long ReadLong(DocumentSnapshot snapshot, string field) =>
        snapshot.Exists && snapshot.TryGetValue<long>(field, out var value) ? value : 0;
    private static string ReadString(DocumentSnapshot snapshot, string field) =>
        snapshot.Exists && snapshot.TryGetValue<string>(field, out var value) ? value : string.Empty;
    private static DateTime ReadTimestamp(DocumentSnapshot snapshot, string field) =>
        snapshot.Exists && snapshot.TryGetValue<Timestamp>(field, out var value)
            ? value.ToDateTime()
            : DateTime.MaxValue;

    private sealed record LoadedBalances(
        Dictionary<string, long> Current,
        Dictionary<string, long> Original);

    private sealed record StateReferences(
        IReadOnlySet<string> TableIds,
        IReadOnlySet<string> TicketIds,
        IReadOnlySet<string> UserIds,
        IReadOnlyDictionary<string, ThreeCardPokerTableState> TableSnapshots)
    {
        public static StateReferences Capture(ThreeCardPokerTableLobbyState state, string? requestedUserId) => new(
            state.Tables.Keys.ToHashSet(StringComparer.Ordinal),
            state.Tickets.Select(ticket => ticket.TicketId).ToHashSet(StringComparer.Ordinal),
            state.Sessions.Keys.Append(requestedUserId ?? string.Empty)
                .Where(value => !string.IsNullOrEmpty(value))
                .ToHashSet(StringComparer.Ordinal),
            state.Tables.ToDictionary(pair => pair.Key,
                pair => JsonSerializer.Deserialize<ThreeCardPokerTableState>(JsonSerializer.Serialize(pair.Value, JsonOptions), JsonOptions)!, StringComparer.Ordinal));
    }
}

internal sealed class ThreeCardPokerTableShardChangedException(string stateId) : Exception
{
    public string StateId { get; } = stateId;
}
