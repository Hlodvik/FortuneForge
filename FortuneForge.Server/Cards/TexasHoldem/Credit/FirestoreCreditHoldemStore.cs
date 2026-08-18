using System.Text.Json;
using Google.Cloud.Firestore;
using Grpc.Core;

namespace FortuneForge.Server.Cards.TexasHoldem.Credit;

internal sealed class FirestoreCreditHoldemStore(
    FirestoreDb database,
    bool allowSingleHumanBotFill = false) : ICreditHoldemStore
{
    private const string CurrencyId = "slotsCredits";
    private const string FractionField = "availableFractionalCents";
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);

    public async Task<CreditHoldemStoreResult> GetSessionAsync(
        string userId,
        DateTime nowUtc,
        CancellationToken cancellationToken)
    {
        var session = await SessionDocument(userId).GetSnapshotAsync(cancellationToken);
        var kind = ReadString(session, "kind");
        if (kind == CreditHoldemSessionKinds.Queue)
        {
            var pendingMatchId = ReadString(session, "matchId");
            if (!string.IsNullOrEmpty(pendingMatchId))
                await AdvanceMatchAsync(pendingMatchId, nowUtc, cancellationToken);
            else await TryMatchAsync(ReadString(session, "partitionKey"), NewSeed(), nowUtc, cancellationToken);
            session = await SessionDocument(userId).GetSnapshotAsync(cancellationToken);
            kind = ReadString(session, "kind");
        }
        if (kind is CreditHoldemSessionKinds.Match or CreditHoldemSessionKinds.Result)
        {
            var matchId = ReadString(session, "matchId");
            await AdvanceMatchAsync(matchId, nowUtc, cancellationToken);
        }
        return await ReadSessionAsync(userId, nowUtc, cancellationToken);
    }

    public async Task<CreditHoldemStoreResult> JoinAsync(
        string userId,
        string displayName,
        int expectedVersion,
        string idempotencyKey,
        ulong seed,
        DateTime nowUtc,
        CancellationToken cancellationToken,
        string tableRuleId = CreditHoldemTableRules.StandardId)
    {
        var rule = CreditHoldemTableRules.Resolve(tableRuleId);
        var ticketId = CreditHoldemIds.Hash($"{userId}\n{idempotencyKey}");
        var partitionKey = CreditHoldemTableRules.Partition(rule.Id);
        var detail = $"{expectedVersion}:{rule.Id}";
        try
        {
            await RunTransactionAsync(async transaction =>
            {
                var guard = await transaction.GetSnapshotAsync(
                    GuardDocument(userId, idempotencyKey), cancellationToken);
                if (guard.Exists)
                {
                    VerifyGuard(guard, "join", ticketId, detail);
                    return false;
                }
                var initial = await Task.WhenAll(
                    transaction.GetSnapshotAsync(SessionDocument(userId), cancellationToken),
                    transaction.GetSnapshotAsync(PartitionDocument(partitionKey), cancellationToken),
                    transaction.GetSnapshotAsync(BalanceDocument(userId), cancellationToken));
                if (ReadVersion(initial[0]) != expectedVersion ||
                    ReadString(initial[0], "kind") is { Length: > 0 } currentKind && currentKind != CreditHoldemSessionKinds.Idle)
                    throw new CreditHoldemConflictException("The Hold'em session changed. Reconnect before joining.");
                var balance = ReadBalance(initial[2]);
                if (balance < rule.BigBlindCents)
                    throw new CreditHoldemInsufficientCreditsException(balance, rule.BigBlindCents);

                var ticket = new CreditHoldemTicket(
                ticketId,
                userId,
                $"seat_{Guid.NewGuid():N}",
                displayName,
                partitionKey,
                "queued",
                1,
                nowUtc,
                nowUtc.Add(CreditHoldemEngine.HumanGrace),
                null,
                rule.Id);
                var activeMatchId = ReadString(initial[1], "activeMatchId");
                if (!string.IsNullOrEmpty(activeMatchId))
                {
                    var activeSnapshot = await transaction.GetSnapshotAsync(MatchDocument(activeMatchId), cancellationToken);
                    if (activeSnapshot.Exists)
                    {
                        var activeMatch = ReadMatch(activeSnapshot);
                        if (CanAcceptTakeover(activeMatch))
                        {
                            var pending = ticket with { Status = "pending-next-hand", MatchId = activeMatchId };
                            activeMatch.PendingTakeovers.Add(pending);
                            transaction.Create(TicketDocument(ticketId), TicketData(pending));
                            transaction.Set(MatchDocument(activeMatchId), MatchData(activeMatch));
                            transaction.Set(SessionDocument(userId), SessionData(
                                userId,
                                CreditHoldemSessionKinds.Queue,
                                ticketId,
                                activeMatchId,
                                partitionKey,
                                1,
                                nowUtc), SetOptions.MergeAll);
                            transaction.Create(GuardDocument(userId, idempotencyKey), GuardData(
                                userId, "join", ticketId, detail, nowUtc));
                            return true;
                        }
                    }
                    activeMatchId = string.Empty;
                }
                var queue = ReadQueue(initial[1]);
                queue.Add(ticket);
                var selected = SelectMatch(queue, nowUtc);
                if (selected.Count == 0)
                {
                    transaction.Create(TicketDocument(ticketId), TicketData(ticket));
                    transaction.Set(PartitionDocument(partitionKey), QueueData(
                        partitionKey, queue, activeMatchId, nowUtc), SetOptions.MergeAll);
                    transaction.Set(SessionDocument(userId), SessionData(
                        userId, CreditHoldemSessionKinds.Queue, ticketId, null, partitionKey, 1, nowUtc), SetOptions.MergeAll);
                    transaction.Create(GuardDocument(userId, idempotencyKey), GuardData(
                        userId, "join", ticketId, detail, nowUtc));
                    return true;
                }
                await CreateMatchAsync(transaction, partitionKey, queue, selected, seed, nowUtc, cancellationToken);
                transaction.Create(GuardDocument(userId, idempotencyKey), GuardData(
                    userId, "join", ticketId, detail, nowUtc));
                return true;
            }, cancellationToken: cancellationToken);
        }
        catch (RpcException exception) when (exception.StatusCode == StatusCode.Aborted)
        {
            var committedGuard = await GuardDocument(userId, idempotencyKey).GetSnapshotAsync(cancellationToken);
            if (!committedGuard.Exists) throw;
            VerifyGuard(committedGuard, "join", ticketId, detail);
        }
        return await ReadSessionAsync(userId, nowUtc, cancellationToken);
    }

    public async Task<CreditHoldemStoreResult> CancelAsync(
        string userId,
        string ticketId,
        int expectedVersion,
        string idempotencyKey,
        DateTime nowUtc,
        CancellationToken cancellationToken)
    {
        var detail = expectedVersion.ToString(System.Globalization.CultureInfo.InvariantCulture);
        await RunTransactionAsync(async transaction =>
        {
            var initial = await Task.WhenAll(
                transaction.GetSnapshotAsync(GuardDocument(userId, idempotencyKey), cancellationToken),
                transaction.GetSnapshotAsync(TicketDocument(ticketId), cancellationToken),
                transaction.GetSnapshotAsync(SessionDocument(userId), cancellationToken));
            if (initial[0].Exists)
            {
                VerifyGuard(initial[0], "cancel", ticketId, detail);
                return false;
            }
            if (!initial[1].Exists) throw new CreditHoldemNotFoundException("The Hold'em queue ticket was not found.");
            var ticket = ReadTicket(initial[1]);
            if (ticket.UserId != userId) throw new CreditHoldemNotFoundException("The Hold'em queue ticket was not found.");
            if (ticket.Status is not "queued" and not "pending-next-hand" || ticket.Version != expectedVersion ||
                ReadString(initial[2], "kind") != CreditHoldemSessionKinds.Queue ||
                ReadString(initial[2], "ticketId") != ticketId)
                throw new CreditHoldemConflictException("This queue ticket changed, matched, or was already cancelled.");
            DocumentSnapshot? partitionSnapshot = null;
            CreditHoldemMatch? pendingMatch = null;
            if (ticket.Status == "queued")
                partitionSnapshot = await transaction.GetSnapshotAsync(PartitionDocument(ticket.PartitionKey), cancellationToken);
            else if (ticket.MatchId is { } pendingMatchId)
            {
                var pendingSnapshot = await transaction.GetSnapshotAsync(MatchDocument(pendingMatchId), cancellationToken);
                if (!pendingSnapshot.Exists || (pendingMatch = ReadMatch(pendingSnapshot)).AccountingSettled)
                    throw new CreditHoldemConflictException("This pending seat has already crossed the hand boundary.");
                pendingMatch.PendingTakeovers.RemoveAll(value => value.TicketId == ticketId);
            }
            transaction.Set(TicketDocument(ticketId), TicketData(ticket with
            {
                Status = "cancelled",
                Version = checked(ticket.Version + 1)
            }));
            if (partitionSnapshot is not null)
            {
                var queue = ReadQueue(partitionSnapshot);
                queue.RemoveAll(value => value.TicketId == ticketId);
                transaction.Set(PartitionDocument(ticket.PartitionKey), QueueData(
                    ticket.PartitionKey,
                    queue,
                    ReadString(partitionSnapshot, "activeMatchId"),
                    nowUtc), SetOptions.MergeAll);
            }
            else if (pendingMatch is not null)
                transaction.Set(MatchDocument(pendingMatch.MatchId), MatchData(pendingMatch));
            transaction.Set(SessionDocument(userId), SessionData(
                userId, CreditHoldemSessionKinds.Idle, null, null, null, 0, nowUtc), SetOptions.MergeAll);
            transaction.Create(GuardDocument(userId, idempotencyKey), GuardData(userId, "cancel", ticketId, detail, nowUtc));
            return true;
        }, cancellationToken: cancellationToken);
        return await ReadSessionAsync(userId, nowUtc, cancellationToken);
    }

    public async Task<CreditHoldemStoreResult> ActionAsync(
        string userId,
        string matchId,
        CreditHoldemActionRequest request,
        string idempotencyKey,
        DateTime nowUtc,
        CancellationToken cancellationToken)
    {
        var action = request.Type.Trim().ToLowerInvariant();
        var detail = $"{action}:{request.ExpectedVersion}:{request.RaiseTo?.ToString() ?? string.Empty}";
        await RunTransactionAsync(async transaction =>
        {
            var initial = await Task.WhenAll(
                transaction.GetSnapshotAsync(GuardDocument(userId, idempotencyKey), cancellationToken),
                transaction.GetSnapshotAsync(MatchDocument(matchId), cancellationToken));
            if (initial[0].Exists)
            {
                VerifyGuard(initial[0], "action", matchId, detail);
                return false;
            }
            if (!initial[1].Exists) throw new CreditHoldemNotFoundException("The Hold'em table was not found.");
            var match = ReadMatch(initial[1]);
            if (match.Players.All(player => player.ActorId != userId))
                throw new CreditHoldemNotFoundException("The Hold'em table was not found.");
            var balances = await ReadHumanBalancesAsync(transaction, match, cancellationToken);
            _ = CreditHoldemEngine.AdvanceAutomatedTurn(match, nowUtc);
            if (match.Status != "active" || match.Version != request.ExpectedVersion)
                throw new CreditHoldemConflictException("The Hold'em table changed. Reconnect before acting.");
            var player = match.Players.Single(value => value.ActorId == userId);
            var required = RequiredCommitment(match, player, action, request.RaiseTo);
            var available = balances.GetValueOrDefault(userId);
            if (available < required) throw new CreditHoldemInsufficientCreditsException(available, required);
            var committed = CreditHoldemEngine.ApplyAction(match, userId, action, request.RaiseTo, nowUtc);
            if (committed != required) throw new InvalidOperationException("The Hold'em commitment changed during validation.");
            if (committed > 0)
            {
                WriteCommitment(transaction, match, player, committed, available, $"action-v{request.ExpectedVersion}", idempotencyKey, nowUtc);
                balances[userId] = checked(available - committed);
            }
            _ = CreditHoldemEngine.AdvanceAutomatedTurn(match, nowUtc);
            ApplyMatchWrite(transaction, match, balances, nowUtc);
            transaction.Create(GuardDocument(userId, idempotencyKey), GuardData(userId, "action", matchId, detail, nowUtc));
            return true;
        }, cancellationToken: cancellationToken);
        return await ReadSessionAsync(userId, nowUtc, cancellationToken);
    }

    public async Task<CreditHoldemStoreResult> NextHandAsync(
        string userId,
        string matchId,
        int expectedVersion,
        string idempotencyKey,
        ulong seed,
        DateTime nowUtc,
        CancellationToken cancellationToken)
    {
        var detail = expectedVersion.ToString(System.Globalization.CultureInfo.InvariantCulture);
        await RunTransactionAsync(async transaction =>
        {
            var initial = await Task.WhenAll(
                transaction.GetSnapshotAsync(GuardDocument(userId, idempotencyKey), cancellationToken),
                transaction.GetSnapshotAsync(MatchDocument(matchId), cancellationToken),
                transaction.GetSnapshotAsync(SessionDocument(userId), cancellationToken));
            if (initial[0].Exists)
            {
                VerifyGuard(initial[0], "next-hand", matchId, detail);
                return false;
            }
            if (!initial[1].Exists) throw new CreditHoldemNotFoundException("The Hold'em table was not found.");
            var prior = ReadMatch(initial[1]);
            if (prior.Players.All(player => player.ActorId != userId))
                throw new CreditHoldemNotFoundException("The Hold'em table was not found.");
            if (prior.Status != "completed" || !prior.AccountingSettled || prior.Version != expectedVersion ||
                ReadString(initial[2], "matchId") != matchId)
                throw new CreditHoldemConflictException("The hand changed or is not ready for the next deal.");
            var balances = await ReadHumanBalancesAsync(transaction, prior, cancellationToken, includePending: true);
            var minimumHumans = allowSingleHumanBotFill ? 1 : 2;
            var next = CreditHoldemEngine.StartNextHand(prior, balances, seed, minimumHumans, nowUtc)
                ?? throw new CreditHoldemConflictException("Not enough funded real players remain for the next hand.");
            WriteBlindCommitments(transaction, next, balances, idempotencyKey, nowUtc);
            foreach (var ticket in prior.PendingTakeovers)
                transaction.Set(TicketDocument(ticket.TicketId), TicketData(ticket with
                {
                    Status = "matched",
                    Version = checked(ticket.Version + 1)
                }));
            foreach (var human in next.Players.Where(player => !player.IsBot))
                transaction.Set(SessionDocument(human.ActorId), SessionData(
                    human.ActorId, CreditHoldemSessionKinds.Match, null, matchId, null, next.Version, nowUtc), SetOptions.MergeAll);
            transaction.Set(PartitionDocument(next.PartitionKey), new Dictionary<string, object>
            {
                ["activeMatchId"] = next.Players.Count(player => !player.IsBot) < CreditHoldemMoney.MaximumSeats
                    ? next.MatchId
                    : string.Empty,
                ["updatedAt"] = Timestamp.FromDateTime(nowUtc)
            }, SetOptions.MergeAll);
            transaction.Set(MatchDocument(matchId), MatchData(next));
            WriteActiveHistory(transaction, next);
            transaction.Create(GuardDocument(userId, idempotencyKey), GuardData(userId, "next-hand", matchId, detail, nowUtc));
            return true;
        }, cancellationToken: cancellationToken);
        return await ReadSessionAsync(userId, nowUtc, cancellationToken);
    }

    public async Task<CreditHoldemHistoryResponse> HistoryAsync(
        string userId,
        int limit,
        CancellationToken cancellationToken)
    {
        var snapshot = await database.Collection("creditHoldemPlayerHistory")
            .Document(CreditHoldemIds.Hash(userId))
            .Collection("events")
            .OrderByDescending("startedAt")
            .Limit(Math.Clamp(limit, 1, 50))
            .GetSnapshotAsync(cancellationToken);
        var items = snapshot.Documents.Select(ReadHistory)
            .OrderByDescending(value => value.StartedAtUtc)
            .ThenByDescending(value => value.HandNumber)
            .Take(Math.Clamp(limit, 1, 50))
            .Select(CreditHoldemProjection.History)
            .ToArray();
        return new CreditHoldemHistoryResponse(items);
    }

    public async Task<CreditHoldemHistoryItemResponse> MarkHistorySeenAsync(
        string userId,
        string eventId,
        DateTime nowUtc,
        CancellationToken cancellationToken)
    {
        CreditHoldemHistoryRecord? updated = null;
        await RunTransactionAsync(async transaction =>
        {
            var snapshot = await transaction.GetSnapshotAsync(HistoryDocument(userId, eventId), cancellationToken);
            if (!snapshot.Exists || (updated = ReadHistory(snapshot)).UserId != userId)
                throw new CreditHoldemNotFoundException("The Hold'em history item was not found.");
            updated = updated with { Seen = true };
            transaction.Set(HistoryDocument(userId, eventId), HistoryData(updated), SetOptions.MergeAll);
            if (updated.CompletedAtUtc is not null)
                transaction.Set(CardGameResultDocument(eventId), new Dictionary<string, object>
                {
                    ["seenAt"] = Timestamp.FromDateTime(nowUtc)
                }, SetOptions.MergeAll);
            return true;
        }, cancellationToken: cancellationToken);
        return CreditHoldemProjection.History(updated!);
    }

    public async Task<CreditHoldemStoreResult> LeaveAsync(
        string userId,
        string matchId,
        int expectedVersion,
        string idempotencyKey,
        DateTime nowUtc,
        CancellationToken cancellationToken)
    {
        var detail = expectedVersion.ToString(System.Globalization.CultureInfo.InvariantCulture);
        await RunTransactionAsync(async transaction =>
        {
            var initial = await Task.WhenAll(
                transaction.GetSnapshotAsync(GuardDocument(userId, idempotencyKey), cancellationToken),
                transaction.GetSnapshotAsync(MatchDocument(matchId), cancellationToken),
                transaction.GetSnapshotAsync(SessionDocument(userId), cancellationToken));
            if (initial[0].Exists)
            {
                VerifyGuard(initial[0], "leave", matchId, detail);
                return false;
            }
            if (!initial[1].Exists) throw new CreditHoldemNotFoundException("The Hold'em table was not found.");
            var match = ReadMatch(initial[1]);
            if (match.Players.All(player => player.ActorId != userId) || ReadString(initial[2], "matchId") != matchId)
                throw new CreditHoldemNotFoundException("The Hold'em table was not found.");
            var balances = await ReadHumanBalancesAsync(transaction, match, cancellationToken);
            _ = CreditHoldemEngine.AdvanceAutomatedTurn(match, nowUtc);
            if (match.Version != expectedVersion)
                throw new CreditHoldemConflictException("The Hold'em table changed. Reconnect before leaving.");
            CreditHoldemEngine.Leave(match, userId, nowUtc);
            ApplyMatchWrite(transaction, match, balances, nowUtc);
            transaction.Set(SessionDocument(userId), SessionData(
                userId, CreditHoldemSessionKinds.Idle, null, null, null, 0, nowUtc), SetOptions.MergeAll);
            transaction.Create(GuardDocument(userId, idempotencyKey), GuardData(userId, "leave", matchId, detail, nowUtc));
            return true;
        }, cancellationToken: cancellationToken);
        return await ReadSessionAsync(userId, nowUtc, cancellationToken);
    }

    private async Task TryMatchAsync(
        string partitionKey,
        ulong seed,
        DateTime nowUtc,
        CancellationToken cancellationToken)
    {
        if (string.IsNullOrEmpty(partitionKey)) return;
        await RunTransactionAsync(async transaction =>
        {
            var snapshot = await transaction.GetSnapshotAsync(PartitionDocument(partitionKey), cancellationToken);
            var queue = ReadQueue(snapshot);
            var selected = SelectMatch(queue, nowUtc);
            if (selected.Count == 0) return false;
            await CreateMatchAsync(transaction, partitionKey, queue, selected, seed, nowUtc, cancellationToken);
            return true;
        }, cancellationToken: cancellationToken);
    }

    private async Task AdvanceMatchAsync(string matchId, DateTime nowUtc, CancellationToken cancellationToken)
    {
        await RunTransactionAsync(async transaction =>
        {
            var snapshot = await transaction.GetSnapshotAsync(MatchDocument(matchId), cancellationToken);
            if (!snapshot.Exists) throw new CreditHoldemNotFoundException("The Hold'em match was not found.");
            var match = ReadMatch(snapshot);
            var balances = await ReadHumanBalancesAsync(transaction, match, cancellationToken);
            var priorVersion = match.Version;
            var priorStatus = match.Status;
            var priorSettlement = match.AccountingSettled;
            _ = CreditHoldemEngine.AdvanceAutomatedTurn(match, nowUtc);
            if (match.Version == priorVersion && match.Status == priorStatus &&
                match.AccountingSettled == priorSettlement)
                return false;
            ApplyMatchWrite(transaction, match, balances, nowUtc);
            return true;
        }, cancellationToken: cancellationToken);
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
                exception.StatusCode == StatusCode.Aborted && attempt < maximumAttempts)
            {
                var exponential = Math.Min(500, 20 * (1 << Math.Min(attempt - 1, 5)));
                var backoffMilliseconds = exponential + Random.Shared.Next(25, 126);
                await Task.Delay(backoffMilliseconds, cancellationToken);
            }
        }
    }

    private async Task<Dictionary<string, long>> ReadHumanBalancesAsync(
        Transaction transaction,
        CreditHoldemMatch match,
        CancellationToken cancellationToken,
        bool includePending = false)
    {
        var userIds = match.Players.Where(player => !player.IsBot).Select(player => player.ActorId)
            .Concat(includePending ? match.PendingTakeovers.Select(ticket => ticket.UserId) : [])
            .Distinct(StringComparer.Ordinal)
            .ToArray();
        var snapshots = await Task.WhenAll(userIds.Select(userId =>
            transaction.GetSnapshotAsync(BalanceDocument(userId), cancellationToken)));
        return userIds.Select((userId, index) => (userId, Balance: ReadBalance(snapshots[index])))
            .ToDictionary(value => value.userId, value => value.Balance, StringComparer.Ordinal);
    }

    private void WriteBlindCommitments(
        Transaction transaction,
        CreditHoldemMatch match,
        IReadOnlyDictionary<string, long> balances,
        string sourceKey,
        DateTime nowUtc)
    {
        foreach (var player in match.Players.Where(player => !player.IsBot && player.CommittedHand > 0))
        {
            var available = balances.GetValueOrDefault(player.ActorId);
            if (available < player.CommittedHand)
                throw new CreditHoldemInsufficientCreditsException(available, player.CommittedHand);
            WriteCommitment(transaction, match, player, player.CommittedHand, available, "blind", sourceKey, nowUtc);
        }
    }

    private void WriteCommitment(
        Transaction transaction,
        CreditHoldemMatch match,
        CreditHoldemPlayer player,
        int cents,
        long available,
        string reason,
        string sourceKey,
        DateTime nowUtc)
    {
        if (cents <= 0 || player.IsBot) return;
        var after = checked(available - cents);
        if (after < 0) throw new CreditHoldemInsufficientCreditsException(available, cents);
        var ledgerId = $"{match.MatchId}-hand-{match.HandNumber}-{reason}-{CreditHoldemIds.Hash(player.ActorId)}-{match.Version}";
        transaction.Set(BalanceDocument(player.ActorId), BalanceData(after, nowUtc), SetOptions.MergeAll);
        transaction.Create(LedgerDocument(ledgerId), LedgerData(
            ledgerId, player.ActorId, -cents, after, $"texas-holdem-{reason}", sourceKey, nowUtc));
    }

    private static int RequiredCommitment(
        CreditHoldemMatch match,
        CreditHoldemPlayer player,
        string action,
        int? raiseTo) => action switch
    {
        CreditHoldemActions.Call => Math.Min(Math.Max(0, match.CurrentBet - player.CommittedRound), player.Stack),
        CreditHoldemActions.Raise when raiseTo is { } target => Math.Max(0, target - player.CommittedRound),
        _ => 0
    };

    private void ApplyMatchWrite(
        Transaction transaction,
        CreditHoldemMatch match,
        IReadOnlyDictionary<string, long> balances,
        DateTime nowUtc)
    {
        if (match.Status != "active" && !match.AccountingSettled)
        {
            var settlement = CreditHoldemEngine.ApplyFinancialSettlement(match);
            foreach (var payout in settlement.HumanPayoutsCents.Where(value => value.Value > 0))
            {
                var before = balances.GetValueOrDefault(payout.Key);
                var after = checked(before + payout.Value);
                var player = match.Players.Single(value => value.ActorId == payout.Key);
                player.AccountPayoutCents = payout.Value;
                transaction.Set(BalanceDocument(payout.Key), BalanceData(after, nowUtc), SetOptions.MergeAll);
                var ledgerId = $"{match.MatchId}-hand-{match.HandNumber}-payout-{CreditHoldemIds.Hash(payout.Key)}";
                transaction.Create(LedgerDocument(ledgerId), LedgerData(
                    ledgerId, payout.Key, payout.Value, after, "texas-holdem-hand-payout", match.MatchId, nowUtc));
            }
            var humanCount = match.Players.Count(player => !player.IsBot);
            var revenueId = $"{match.MatchId}-hand-{match.HandNumber}";
            transaction.Create(RevenueDocument(revenueId), new Dictionary<string, object>
            {
                ["matchId"] = match.MatchId,
                ["handNumber"] = match.HandNumber,
                ["humanWagerCents"] = settlement.HumanCommittedCents,
                ["humanPayoutCents"] = settlement.HumanPayoutCents,
                ["houseNetCents"] = settlement.HouseNetCents,
                ["humanPlayerCount"] = humanCount,
                ["currencyId"] = CurrencyId,
                ["financialClassification"] = "real-human-wager-v2",
                ["botFinancialContributionCents"] = 0L,
                ["recognizedAt"] = Timestamp.FromDateTime(nowUtc),
                ["settledAt"] = Timestamp.FromDateTime(nowUtc),
                ["schemaVersion"] = 2L
            });
            WriteCompletedHistory(transaction, match, settlement);
            foreach (var human in match.Players.Where(player => !player.IsBot && !match.LeavingActorIds.Contains(player.ActorId)))
                transaction.Set(SessionDocument(human.ActorId), SessionData(
                    human.ActorId, CreditHoldemSessionKinds.Result, null, match.MatchId, null, match.Version, nowUtc), SetOptions.MergeAll);
            transaction.Set(PartitionDocument(match.PartitionKey), new Dictionary<string, object>
            {
                ["activeMatchId"] = match.Players.Count(player => !player.IsBot && !match.LeavingActorIds.Contains(player.ActorId)) <
                    CreditHoldemMoney.MaximumSeats ? match.MatchId : string.Empty,
                ["updatedAt"] = Timestamp.FromDateTime(nowUtc)
            }, SetOptions.MergeAll);
        }
        else WriteActiveHistory(transaction, match);
        transaction.Set(MatchDocument(match.MatchId), MatchData(match));
    }

    private async Task CreateMatchAsync(
        Transaction transaction,
        string partitionKey,
        List<CreditHoldemTicket> queue,
        IReadOnlyList<CreditHoldemTicket> selected,
        ulong seed,
        DateTime nowUtc,
        CancellationToken cancellationToken)
    {
        var matchId = CreditHoldemIds.Hash($"{partitionKey}\n{string.Join("\n", selected.Select(ticket => ticket.TicketId))}");
        var occupiedSeats = Math.Max(CreditHoldemMoney.MinimumStartPlayers, selected.Count);
        var snapshots = await Task.WhenAll(selected.Select(ticket =>
            transaction.GetSnapshotAsync(BalanceDocument(ticket.UserId), cancellationToken)));
        var balances = selected.Select((ticket, index) => (ticket.UserId, Balance: ReadBalance(snapshots[index])))
            .ToDictionary(value => value.UserId, value => value.Balance, StringComparer.Ordinal);
        var rule = CreditHoldemTableRules.Resolve(selected[0].TableRuleId);
        if (balances.Values.Any(value => value < rule.BigBlindCents))
            throw new CreditHoldemConflictException("A queued player no longer has enough credits for the big blind.");
        var match = CreditHoldemEngine.Deal(
            matchId, selected, occupiedSeats, partitionKey, seed, balances, nowUtc, rule.Id);
        WriteBlindCommitments(transaction, match, balances, "initial-deal", nowUtc);
        WriteActiveHistory(transaction, match);
        transaction.Create(MatchDocument(matchId), MatchData(match));
        var ids = selected.Select(ticket => ticket.TicketId).ToHashSet(StringComparer.Ordinal);
        foreach (var ticket in selected)
        {
            transaction.Set(TicketDocument(ticket.TicketId), TicketData(ticket with
            {
                Status = "matched",
                MatchId = matchId,
                Version = checked(ticket.Version + 1)
            }));
            transaction.Set(SessionDocument(ticket.UserId), SessionData(
                ticket.UserId, CreditHoldemSessionKinds.Match, null, matchId, null, match.Version, nowUtc), SetOptions.MergeAll);
        }
        queue.RemoveAll(ticket => ids.Contains(ticket.TicketId));
        transaction.Set(PartitionDocument(partitionKey), QueueData(
            partitionKey,
            queue,
            selected.Count < CreditHoldemMoney.MaximumSeats ? matchId : string.Empty,
            nowUtc), SetOptions.MergeAll);
    }

    private async Task<CreditHoldemStoreResult> ReadSessionAsync(
        string userId,
        DateTime nowUtc,
        CancellationToken cancellationToken)
    {
        var session = await SessionDocument(userId).GetSnapshotAsync(cancellationToken);
        var balance = ReadBalance(await BalanceDocument(userId).GetSnapshotAsync(cancellationToken));
        var kind = ReadString(session, "kind");
        if (string.IsNullOrEmpty(kind) || kind == CreditHoldemSessionKinds.Idle)
            return new CreditHoldemStoreResult(new CreditHoldemIdleSessionResponse(), balance);
        if (kind == CreditHoldemSessionKinds.Queue)
        {
            var ticketSnapshot = await TicketDocument(ReadString(session, "ticketId")).GetSnapshotAsync(cancellationToken);
            if (!ticketSnapshot.Exists) throw new CreditHoldemNotFoundException("The Hold'em queue ticket was not found.");
            var ticket = ReadTicket(ticketSnapshot);
            if (ticket.Status == "pending-next-hand")
            {
                var pendingSeat = new CreditHoldemSeatResponse(
                    ticket.PublicSeatId,
                    ticket.DisplayName,
                    0,
                    0,
                    0,
                    0,
                    0,
                    "pending-next-hand",
                    null,
                    [],
                    null,
                    true);
                return new CreditHoldemStoreResult(new CreditHoldemQueueSessionResponse(
                    ticket.TicketId,
                    1,
                    ticket.JoinedAtUtc,
                    ticket.GraceEndsAtUtc,
                    [pendingSeat],
                    ticket.Version,
                    CreditHoldemTableRules.Resolve(ticket.TableRuleId).Public), balance);
            }
            var queue = ReadQueue(await PartitionDocument(ticket.PartitionKey).GetSnapshotAsync(cancellationToken))
                .Where(value => value.Status == "queued")
                .OrderBy(value => value.JoinedAtUtc)
                .ThenBy(value => value.TicketId, StringComparer.Ordinal)
                .ToArray();
            var seats = queue.Select((value, index) => new CreditHoldemSeatResponse(
                value.PublicSeatId, value.DisplayName, index,
                0, 0, 0, 0, "queued", null, [], null,
                value.UserId == userId)).ToArray();
            return new CreditHoldemStoreResult(new CreditHoldemQueueSessionResponse(
                ticket.TicketId,
                Array.FindIndex(queue, value => value.UserId == userId) + 1,
                ticket.JoinedAtUtc,
                ticket.GraceEndsAtUtc,
                seats,
                ticket.Version,
                CreditHoldemTableRules.Resolve(ticket.TableRuleId).Public), balance);
        }
        var matchSnapshot = await MatchDocument(ReadString(session, "matchId")).GetSnapshotAsync(cancellationToken);
        if (!matchSnapshot.Exists) throw new CreditHoldemNotFoundException("The Hold'em match was not found.");
        var match = ReadMatch(matchSnapshot);
        var response = kind == CreditHoldemSessionKinds.Result
            ? CreditHoldemProjection.Result(match, userId, nowUtc)
            : CreditHoldemProjection.Match(match, userId, nowUtc);
        return new CreditHoldemStoreResult(response, balance);
    }

    private IReadOnlyList<CreditHoldemTicket> SelectMatch(List<CreditHoldemTicket> queue, DateTime nowUtc)
    {
        var eligible = queue.Where(ticket => ticket.Status == "queued")
            .OrderBy(ticket => ticket.JoinedAtUtc)
            .ThenBy(ticket => ticket.TicketId, StringComparer.Ordinal)
            .ToArray();
        var minimumHumans = allowSingleHumanBotFill ? 1 : 2;
        if (eligible.Length < minimumHumans || nowUtc < eligible[0].GraceEndsAtUtc) return [];
        return eligible.Take(CreditHoldemMoney.MaximumSeats).ToArray();
    }

    private void WriteActiveHistory(Transaction transaction, CreditHoldemMatch match)
    {
        foreach (var human in match.Players.Where(value => !value.IsBot))
        {
            var eventId = HistoryId(human.ActorId, match.MatchId, match.HandNumber);
            transaction.Set(HistoryDocument(human.ActorId, eventId), HistoryData(new CreditHoldemHistoryRecord(
                eventId,
                human.ActorId,
                match.MatchId,
                match.HandNumber,
                "active",
                true,
                match.StartedAtUtc,
                null,
                human.CommittedHand,
                0)), SetOptions.MergeAll);
        }
    }

    private void WriteCompletedHistory(
        Transaction transaction,
        CreditHoldemMatch match,
        CreditHoldemFinancialSettlement settlement)
    {
        foreach (var human in match.Players.Where(value => !value.IsBot))
        {
            var eventId = HistoryId(human.ActorId, match.MatchId, match.HandNumber);
            var payout = settlement.HumanPayoutsCents.GetValueOrDefault(human.ActorId);
            var completed = match.CompletedAtUtc ?? match.UpdatedAtUtc;
            transaction.Set(HistoryDocument(human.ActorId, eventId), HistoryData(new CreditHoldemHistoryRecord(
                eventId,
                human.ActorId,
                match.MatchId,
                match.HandNumber,
                "completed",
                false,
                match.StartedAtUtc,
                completed,
                human.CommittedHand,
                payout)), SetOptions.MergeAll);
            transaction.Set(CardGameResultDocument(eventId), new Dictionary<string, object>
            {
                ["resultId"] = eventId,
                ["game"] = "texas-holdem",
                ["mode"] = "credit-table",
                ["matchId"] = match.MatchId,
                ["tableId"] = match.MatchId,
                ["handId"] = $"{match.MatchId}-hand-{match.HandNumber}",
                ["userId"] = human.ActorId,
                ["claimStatus"] = "completed",
                ["settlementStatus"] = "paid",
                ["handNumber"] = match.HandNumber,
                ["wagerCents"] = human.CommittedHand,
                ["payoutCents"] = payout,
                ["netCents"] = payout - human.CommittedHand,
                ["startedAt"] = Timestamp.FromDateTime(match.StartedAtUtc),
                ["completedAt"] = Timestamp.FromDateTime(completed),
                ["seenAt"] = null!,
                ["schemaVersion"] = 1L
            });
        }
    }

    private static string HistoryId(string userId, string matchId, int handNumber) =>
        CreditHoldemIds.Hash($"{userId}\n{matchId}\n{handNumber}");

    private DocumentReference SessionDocument(string userId) =>
        database.Collection("creditHoldemSessions").Document(CreditHoldemIds.Hash(userId));
    private DocumentReference TicketDocument(string id) => database.Collection("creditHoldemTickets").Document(id);
    private DocumentReference PartitionDocument(string id) => database.Collection("creditHoldemQueuePartitions").Document(id);
    private DocumentReference MatchDocument(string id) => database.Collection("creditHoldemMatches").Document(id);
    private DocumentReference GuardDocument(string userId, string key) =>
        database.Collection("creditHoldemCommandGuards").Document(CreditHoldemIds.Hash($"{userId}\n{key}"));
    private DocumentReference BalanceDocument(string userId) => database.Collection("userBalances").Document($"{userId}_{CurrencyId}");
    private DocumentReference LedgerDocument(string id) => database.Collection("balanceTransactions").Document(id);
    private DocumentReference RevenueDocument(string id) => database.Collection("creditHoldemMatchRevenue").Document(id);
    private DocumentReference HistoryDocument(string userId, string eventId) =>
        database.Collection("creditHoldemPlayerHistory")
            .Document(CreditHoldemIds.Hash(userId))
            .Collection("events")
            .Document(eventId);
    private DocumentReference CardGameResultDocument(string id) => database.Collection("cardGameResults").Document(id);

    private static Dictionary<string, object> SessionData(
        string userId, string kind, string? ticketId, string? matchId, string? partitionKey, int version, DateTime nowUtc) => new()
    {
        ["userId"] = userId,
        ["kind"] = kind,
        ["ticketId"] = ticketId ?? string.Empty,
        ["matchId"] = matchId ?? string.Empty,
        ["partitionKey"] = partitionKey ?? string.Empty,
        ["version"] = version,
        ["updatedAt"] = Timestamp.FromDateTime(nowUtc)
    };

    private static Dictionary<string, object> TicketData(CreditHoldemTicket ticket) => new()
    {
        ["ticketJson"] = JsonSerializer.Serialize(ticket, JsonOptions),
        ["schemaVersion"] = 1L
    };

    private static Dictionary<string, object> QueueData(
        string key,
        List<CreditHoldemTicket> queue,
        string activeMatchId,
        DateTime nowUtc) => new()
    {
        ["partitionKey"] = key,
        ["queueJson"] = JsonSerializer.Serialize(queue, JsonOptions),
        ["activeMatchId"] = activeMatchId,
        ["updatedAt"] = Timestamp.FromDateTime(nowUtc),
        ["schemaVersion"] = 1L
    };

    private static Dictionary<string, object> MatchData(CreditHoldemMatch match) => new()
    {
        ["matchId"] = match.MatchId,
        ["matchJson"] = JsonSerializer.Serialize(match, JsonOptions),
        ["status"] = match.Status,
        ["version"] = match.Version,
        ["schemaVersion"] = 1L
    };

    private static Dictionary<string, object> GuardData(
        string userId, string operation, string target, string detail, DateTime nowUtc) => new()
    {
        ["userId"] = userId,
        ["operation"] = operation,
        ["target"] = target,
        ["detail"] = detail,
        ["createdAt"] = Timestamp.FromDateTime(nowUtc)
    };

    private static Dictionary<string, object> HistoryData(CreditHoldemHistoryRecord value) => new()
    {
        ["userId"] = value.UserId,
        ["historyJson"] = JsonSerializer.Serialize(value, JsonOptions),
        ["status"] = value.Status,
        ["seen"] = value.Seen,
        ["startedAt"] = Timestamp.FromDateTime(value.StartedAtUtc),
        ["schemaVersion"] = 1L
    };

    private static Dictionary<string, object> BalanceData(long cents, DateTime nowUtc) => new()
    {
        ["available"] = cents / CreditHoldemMoney.CentsPerCredit,
        [FractionField] = cents % CreditHoldemMoney.CentsPerCredit,
        ["version"] = FieldValue.Increment(1),
        ["updatedAt"] = Timestamp.FromDateTime(nowUtc)
    };

    private static Dictionary<string, object> LedgerData(
        string id, string userId, long amount, long balanceAfter, string type, string idempotencyKey, DateTime nowUtc) => new()
    {
        ["transactionId"] = id,
        ["userId"] = userId,
        ["currencyId"] = CurrencyId,
        ["amount"] = (double)CreditHoldemMoney.ToCredits(amount),
        ["balanceAfter"] = (double)CreditHoldemMoney.ToCredits(balanceAfter),
        ["type"] = type,
        ["idempotencyKey"] = idempotencyKey,
        ["createdAt"] = Timestamp.FromDateTime(nowUtc)
    };

    private static CreditHoldemTicket ReadTicket(DocumentSnapshot snapshot) =>
        JsonSerializer.Deserialize<CreditHoldemTicket>(ReadString(snapshot, "ticketJson"), JsonOptions)
        ?? throw new InvalidOperationException("A stored Hold'em ticket is invalid.");

    private static List<CreditHoldemTicket> ReadQueue(DocumentSnapshot snapshot)
    {
        var json = ReadString(snapshot, "queueJson");
        return string.IsNullOrEmpty(json)
            ? []
            : JsonSerializer.Deserialize<List<CreditHoldemTicket>>(json, JsonOptions)
              ?? throw new InvalidOperationException("A stored Hold'em queue is invalid.");
    }

    private static CreditHoldemMatch ReadMatch(DocumentSnapshot snapshot) =>
        JsonSerializer.Deserialize<CreditHoldemMatch>(ReadString(snapshot, "matchJson"), JsonOptions)
        ?? throw new InvalidOperationException("A stored Hold'em match is invalid.");

    private static CreditHoldemHistoryRecord ReadHistory(DocumentSnapshot snapshot) =>
        JsonSerializer.Deserialize<CreditHoldemHistoryRecord>(ReadString(snapshot, "historyJson"), JsonOptions)
        ?? throw new InvalidOperationException("A stored Hold'em history item is invalid.");

    private static long ReadBalance(DocumentSnapshot snapshot) => checked(
        ReadLong(snapshot, "available") * CreditHoldemMoney.CentsPerCredit +
        Math.Clamp(ReadLong(snapshot, FractionField), 0, 99));
    private static int ReadVersion(DocumentSnapshot snapshot) => checked((int)ReadLong(snapshot, "version"));
    private static long ReadLong(DocumentSnapshot snapshot, string field) =>
        snapshot.Exists && snapshot.TryGetValue<long>(field, out var value) ? value : 0;
    private static string ReadString(DocumentSnapshot snapshot, string field) =>
        snapshot.Exists && snapshot.TryGetValue<string>(field, out var value) ? value : string.Empty;

    private static void VerifyGuard(DocumentSnapshot snapshot, string operation, string target, string detail)
    {
        if (ReadString(snapshot, "operation") != operation || ReadString(snapshot, "target") != target ||
            ReadString(snapshot, "detail") != detail)
            throw new CreditHoldemConflictException("This Idempotency-Key was already used for a different Hold'em request.");
    }

    private static bool CanAcceptTakeover(CreditHoldemMatch match) =>
        match.Status is "active" or "completed" &&
        match.Players.Count(player => !player.IsBot && !match.LeavingActorIds.Contains(player.ActorId)) +
        match.PendingTakeovers.Count < CreditHoldemMoney.MaximumSeats;
    private static ulong NewSeed() => BitConverter.ToUInt64(System.Security.Cryptography.RandomNumberGenerator.GetBytes(8));
}
