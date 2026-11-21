using System.Text.Json;
using System.Security.Cryptography;
using FortuneForge.Server.Bots.TexasHoldem;
using FortuneForge.Server.RoundAudit;
using FortuneForge.Games.Abstractions.Analytics;
using FortuneForge.Games.Abstractions.Auditing;
using Google.Cloud.Firestore;
using Grpc.Core;

namespace FortuneForge.Server.Cards.TexasHoldem.Credit;

internal sealed class FirestoreCreditHoldemStore(
    FirestoreDb database,
    bool allowSingleHumanBotFill,
    IGameWalletBatchClient wallet,
    IGameWalletAccountReader accounts,
    IManagedPlayerRosterClient roster,
    IManagedTablePopulationDirector? managedPopulationDirector = null,
    bool allowNewRounds = true) : ICreditHoldemStore
{
    private readonly CreditHoldemMutationJournal journal = new(database, wallet, accounts, roster);
    private const string CurrencyId = "slotsCredits";
    private const int MaximumSweepBatch = 100;
    private static readonly TimeSpan ManagedLeaseHeartbeatInterval = TimeSpan.FromMinutes(5);
    private static readonly TimeSpan AutomaticInterHandDelay = TimeSpan.FromSeconds(2);
    private static readonly DateTime DormantDeadlineUtc =
        new(9998, 1, 1, 0, 0, 0, DateTimeKind.Utc);
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);
    private readonly IManagedTablePopulationDirector population =
        managedPopulationDirector ?? new ManagedTablePopulationDirector();

    public async Task<CreditHoldemStoreResult> GetSessionAsync(
        string userId,
        DateTime nowUtc,
        CancellationToken cancellationToken)
    {
        await journal.RecoverAccountAsync(userId, cancellationToken);
        var session = await SessionDocument(userId).GetSnapshotAsync(cancellationToken);
        var kind = ReadString(session, "kind");
        if (kind == CreditHoldemSessionKinds.Queue)
        {
            var ticketId = ReadString(session, "ticketId");
            if (!string.IsNullOrEmpty(ticketId)) await ExpireTicketAsync(ticketId, nowUtc, cancellationToken);
            session = await SessionDocument(userId).GetSnapshotAsync(cancellationToken);
            kind = ReadString(session, "kind");
        }
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
        string tableRuleId = CreditHoldemTableRules.StandardId,
        int? buyInCents = null)
    {
        var rule = CreditHoldemTableRules.Resolve(tableRuleId);
        var ticketId = CreditHoldemIds.Hash($"{userId}\n{idempotencyKey}");
        var partitionKey = CreditHoldemEscrow.Partition(rule.Id);
        var detail = CreditHoldemEscrow.JoinDetail(expectedVersion, rule.Id, buyInCents);
        if (await TryReadCachedReplyAsync(userId, idempotencyKey, "join", ticketId, detail, cancellationToken) is { } cached) return cached;
        await journal.RecoverAccountAsync(userId, cancellationToken);
        if (await TryReadCachedReplyAsync(userId, idempotencyKey, "join", ticketId, detail, cancellationToken) is { } recovered) return recovered;
        if (!GameWalletAccountState.IsValidPlayerName(displayName) &&
            !(await GuardDocument(userId, idempotencyKey).GetSnapshotAsync(cancellationToken)).Exists)
        {
            if (!allowNewRounds) throw new CreditHoldemPausedException();
            displayName = (await accounts.GetStateAsync(userId, cancellationToken)).PlayerName ?? "";
            if (!GameWalletAccountState.IsValidPlayerName(displayName)) throw new GameWalletUnavailableException();
        }
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
                if (!allowNewRounds) throw new CreditHoldemPausedException();
                var initial = await Task.WhenAll(
                    transaction.GetSnapshotAsync(SessionDocument(userId), cancellationToken),
                    transaction.GetSnapshotAsync(PartitionDocument(partitionKey), cancellationToken));
                if (ReadVersion(initial[0]) != expectedVersion ||
                    ReadString(initial[0], "kind") is { Length: > 0 } currentKind && currentKind != CreditHoldemSessionKinds.Idle)
                    throw new CreditHoldemConflictException("The Hold'em session changed. Reconnect before joining.");
                var balance = await transaction.BalanceAsync(userId, cancellationToken);
                var reserved = CreditHoldemEscrow.ReserveAmount(balance, rule, buyInCents);

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
                rule.Id,
                reserved);
                var activeMatchId = ReadString(initial[1], "activeMatchId");
                var eligible = await transaction.GetSnapshotAsync(database.Collection(CreditHoldemBackend.Matches)
                    .WhereEqualTo("partitionKey", partitionKey).WhereEqualTo("acceptingHumans", true)
                    .Limit(MaximumSweepBatch), cancellationToken);
                var candidateSnapshots = eligible.Documents.ToList();
                if (!string.IsNullOrEmpty(activeMatchId) && candidateSnapshots.All(snapshot => snapshot.Id != activeMatchId))
                {
                    var pointed = await transaction.GetSnapshotAsync(MatchDocument(activeMatchId), cancellationToken);
                    if (pointed.Exists) candidateSnapshots.Add(pointed);
                }
                var openMatch = candidateSnapshots.Where(snapshot => !snapshot.ContainsField("managedPlayersReleasedAt"))
                    .Select(ReadMatch).Where(CreditHoldemEscrow.CanAcceptTakeover)
                    .OrderBy(match => match.StartedAtUtc).ThenBy(match => match.MatchId, StringComparer.Ordinal).FirstOrDefault();
                if (openMatch is not null)
                {
                            activeMatchId = openMatch.MatchId;
                            var pending = ticket with { Status = "pending-next-hand", MatchId = activeMatchId };
                            openMatch.PendingTakeovers.Add(pending);
                            WriteReservation(transaction, ticket, balance, nowUtc);
                            transaction.Create(TicketDocument(ticketId), TicketData(pending));
                            transaction.Set(MatchDocument(activeMatchId), MatchData(openMatch), SetOptions.MergeAll);
                            transaction.Observe(openMatch, nowUtc, "seat-reserved");
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
                activeMatchId = string.Empty;
                var queue = ReadQueue(initial[1]);
                queue.Add(ticket);
                var selected = SelectMatch(queue, nowUtc);
                if (selected.Count == 0 || !await CreateMatchAsync(transaction, partitionKey, queue, selected, seed, nowUtc, cancellationToken))
                {
                    WriteReservation(transaction, ticket, balance, nowUtc);
                    transaction.Create(TicketDocument(ticketId), TicketData(ticket));
                    transaction.Set(PartitionDocument(partitionKey), QueueData(
                        partitionKey, queue, activeMatchId, nowUtc), SetOptions.MergeAll);
                    transaction.Set(SessionDocument(userId), SessionData(
                        userId, CreditHoldemSessionKinds.Queue, ticketId, null, partitionKey, 1, nowUtc), SetOptions.MergeAll);
                    transaction.Create(GuardDocument(userId, idempotencyKey), GuardData(
                        userId, "join", ticketId, detail, nowUtc));
                    return true;
                }
                WriteReservation(transaction, ticket, balance, nowUtc);
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
        return await ReadCommandReplyAsync(userId, idempotencyKey, nowUtc, cancellationToken);
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
        if (await TryReadCachedReplyAsync(userId, idempotencyKey, "cancel", ticketId, detail, cancellationToken) is { } cached) return cached;
        await journal.RecoverAccountAsync(userId, cancellationToken);
        if (await TryReadCachedReplyAsync(userId, idempotencyKey, "cancel", ticketId, detail, cancellationToken) is { } recovered) return recovered;
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
                if (!pendingSnapshot.Exists)
                    throw new CreditHoldemConflictException("This pending seat has already crossed the hand boundary.");
                pendingMatch = ReadMatch(pendingSnapshot);
                if (pendingMatch.PendingTakeovers.All(value => value.TicketId != ticketId))
                    throw new CreditHoldemConflictException("This pending seat has already crossed the hand boundary.");
                pendingMatch.PendingTakeovers.RemoveAll(value => value.TicketId == ticketId);
            }
            WriteTicketRefund(transaction, ticket, await transaction.BalanceAsync(userId, cancellationToken), nowUtc);
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
            {
                transaction.Set(MatchDocument(pendingMatch.MatchId), MatchData(pendingMatch), SetOptions.MergeAll);
                transaction.Observe(pendingMatch, nowUtc, "seat-cancelled");
            }
            transaction.Set(SessionDocument(userId), SessionData(
                userId, CreditHoldemSessionKinds.Idle, null, null, null, 0, nowUtc), SetOptions.MergeAll);
            transaction.Create(GuardDocument(userId, idempotencyKey), GuardData(userId, "cancel", ticketId, detail, nowUtc));
            return true;
        }, cancellationToken: cancellationToken);
        return await ReadCommandReplyAsync(userId, idempotencyKey, nowUtc, cancellationToken);
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
        if (await TryReadCachedReplyAsync(userId, idempotencyKey, "action", matchId, detail, cancellationToken) is { } cached) return cached;
        await journal.RecoverAccountAsync(userId, cancellationToken);
        if (await TryReadCachedReplyAsync(userId, idempotencyKey, "action", matchId, detail, cancellationToken) is { } recovered) return recovered;
        await RunTransactionAsync(async transaction =>
        {
            var initial = await Task.WhenAll(
                transaction.GetSnapshotAsync(GuardDocument(userId, idempotencyKey), cancellationToken),
                transaction.GetSnapshotAsync(MatchDocument(matchId), cancellationToken),
                transaction.GetSnapshotAsync(SessionDocument(userId), cancellationToken));
            if (initial[0].Exists)
            {
                VerifyGuard(initial[0], "action", matchId, detail);
                return false;
            }
            if (!initial[1].Exists) throw new CreditHoldemNotFoundException("The Hold'em table was not found.");
            var match = ReadMatch(initial[1]);
            if (match.Players.All(player => player.ActorId != userId))
                throw new CreditHoldemNotFoundException("The Hold'em table was not found.");
            RequireCurrentTableSession(initial[2], initial[1], match, userId);
            var balances = await ReadHumanBalancesAsync(transaction, match, cancellationToken);
            (match, _) = await AdvanceIfDueAsync(match, nowUtc, cancellationToken);
            if (match.Status != "active" || match.Version != request.ExpectedVersion)
                throw new CreditHoldemConflictException("The Hold'em table changed. Reconnect before acting.");
            var player = match.Players.Single(value => value.ActorId == userId);
            var required = RequiredCommitment(match, player, action, request.RaiseTo);
            var available = balances.GetValueOrDefault(userId);
            if (!match.UsesBuyInEscrow && available < required) throw new CreditHoldemInsufficientCreditsException(available, required);
            var (afterAction, committed) = await ApplyActionAsync(match, userId, action, request.RaiseTo, nowUtc, cancellationToken);
            match = afterAction;
            if (committed != required) throw new InvalidOperationException("The Hold'em commitment changed during validation.");
            if (!match.UsesBuyInEscrow && committed > 0)
            {
                WriteCommitment(transaction, match, player, committed, available, $"action-v{request.ExpectedVersion}", idempotencyKey, nowUtc);
                balances[userId] = checked(available - committed);
            }
            (match, _) = await AdvanceIfDueAsync(match, nowUtc, cancellationToken);
            await ApplyMatchWriteAsync(transaction, match, balances, nowUtc, cancellationToken);
            transaction.Create(GuardDocument(userId, idempotencyKey), GuardData(userId, "action", matchId, detail, nowUtc));
            return true;
        }, cancellationToken: cancellationToken);
        return await ReadCommandReplyAsync(userId, idempotencyKey, nowUtc, cancellationToken);
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
        if (await TryReadCachedReplyAsync(userId, idempotencyKey, "next-hand", matchId, detail, cancellationToken) is { } cached) return cached;
        await journal.RecoverAccountAsync(userId, cancellationToken);
        if (await TryReadCachedReplyAsync(userId, idempotencyKey, "next-hand", matchId, detail, cancellationToken) is { } recovered) return recovered;
        var before = await Task.WhenAll(
            GuardDocument(userId, idempotencyKey).GetSnapshotAsync(cancellationToken),
            MatchDocument(matchId).GetSnapshotAsync(cancellationToken),
            SessionDocument(userId).GetSnapshotAsync(cancellationToken));
        if (before[0].Exists)
        {
            VerifyGuard(before[0], "next-hand", matchId, detail);
            return await ReadCommandReplyAsync(userId, idempotencyKey, nowUtc, cancellationToken);
        }
        if (!allowNewRounds) throw new CreditHoldemPausedException();
        if (!before[1].Exists) throw new CreditHoldemNotFoundException("The Hold'em table was not found.");
        var beforeMatch = ReadMatch(before[1]);
        if (beforeMatch.Players.All(player => player.ActorId != userId))
            throw new CreditHoldemNotFoundException("The Hold'em table was not found.");
        RequireCurrentTableSession(before[2], before[1], beforeMatch, userId);
        if (CreditHoldemTableCoordinator.ResultSeatExpired(beforeMatch, nowUtc))
        {
            // Commit the expiry before rejecting the command; throwing inside a
            // transaction would roll back the cash-out and session release.
            await AdvanceMatchAsync(matchId, nowUtc, cancellationToken);
            throw new CreditHoldemConflictException("Your Hold'em seat expired. Reconnect before joining a table.");
        }
        if (beforeMatch.Status != "completed" || !beforeMatch.AccountingSettled || beforeMatch.Version != expectedVersion)
            throw new CreditHoldemConflictException("The hand changed or is not ready for the next deal.");
        var beforeManagedIds = beforeMatch.Players.Where(TexasHoldemManagedPlayers.IsManaged)
            .Select(player => player.ActorId).ToArray();
        RemoveBustedEscrowManagedPlayers(beforeMatch);
        _ = TexasHoldemManagedPlayers.RemoveDepartingPlayers(beforeMatch);
        var populationDecision = PopulationDecision(beforeMatch, includePendingHumans: true);
        var arrivalProfiles = populationDecision.ManagedArrivals == 0
            ? []
            : await ManagedProfilesAsync(
                matchId,
                populationDecision.ManagedArrivals,
                beforeMatch.Players.Select(player => player.ActorId).ToArray(),
                nowUtc,
                cancellationToken);
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
            RequireCurrentTableSession(initial[2], initial[1], prior, userId);
            if (prior.Status != "completed" || !prior.AccountingSettled || prior.Version != expectedVersion ||
                CreditHoldemTableCoordinator.ResultSeatExpired(prior, nowUtc))
                throw new CreditHoldemConflictException("The hand changed or is not ready for the next deal.");
            var balances = await ReadHumanBalancesAsync(transaction, prior, cancellationToken, includePending: true);
            WriteExpiredPendingRefunds(transaction, prior, balances, nowUtc);
            MarkIneligibleEscrowPlayers(transaction, prior, nowUtc);
            RemoveBustedEscrowManagedPlayers(prior);
            _ = TexasHoldemManagedPlayers.RemoveDepartingPlayers(prior);
            TexasHoldemManagedPlayers.AddPlayers(prior, arrivalProfiles);
            WriteEscrowCashouts(transaction, prior, balances, nowUtc);
            var minimumHumans = allowSingleHumanBotFill ? 1 : 2;
            var next = await StartNextHandAsync(prior, balances, seed, minimumHumans, nowUtc, cancellationToken);
            if (next is null)
            {
                if (!prior.UsesBuyInEscrow) throw new CreditHoldemConflictException("Not enough funded real players remain for the next hand.");
                WriteEscrowTableClosure(transaction, prior, balances, nowUtc);
                transaction.Create(GuardDocument(userId, idempotencyKey), GuardData(userId, "next-hand", matchId, detail, nowUtc));
                return true;
            }
            WriteUnadmittedTakeoverRefunds(transaction, prior, next, balances, nowUtc);
            WriteBlindCommitments(transaction, next, balances, idempotencyKey, nowUtc);
            foreach (var ticket in prior.PendingTakeovers.Where(ticket => !prior.UsesBuyInEscrow || next.Players.Any(player => player.EscrowTicketId == ticket.TicketId)))
                transaction.Set(TicketDocument(ticket.TicketId), TicketData(ticket with
                {
                    Status = "matched",
                    Version = checked(ticket.Version + 1)
                }));
            foreach (var human in next.Players.Where(player => player.IsAccountBacked))
                transaction.Set(SessionDocument(human.ActorId), SessionData(
                    human.ActorId, CreditHoldemSessionKinds.Match, null, matchId, null, next.Version, nowUtc), SetOptions.MergeAll);
            transaction.Set(PartitionDocument(next.PartitionKey), new Dictionary<string, object>
            {
                ["activeMatchId"] = next.Players.Count(player => player.IsAccountBacked) < CreditHoldemMoney.MaximumSeats
                    ? next.MatchId
                    : string.Empty,
                ["updatedAt"] = Timestamp.FromDateTime(nowUtc)
            }, SetOptions.MergeAll);
            transaction.Set(MatchDocument(matchId), MatchData(next), SetOptions.MergeAll);
            transaction.Observe(next, nowUtc, "hand-started");
            WriteActiveHistory(transaction, next);
            transaction.Create(GuardDocument(userId, idempotencyKey), GuardData(userId, "next-hand", matchId, detail, nowUtc));
            return true;
        }, cancellationToken: cancellationToken);
        if (beforeManagedIds.Length > 0)
        {
            var after = ReadMatch(await MatchDocument(matchId).GetSnapshotAsync(cancellationToken));
            var remaining = after.Players.Where(TexasHoldemManagedPlayers.IsManaged)
                .Select(player => player.ActorId).ToHashSet(StringComparer.Ordinal);
            var removed = beforeManagedIds.Where(id => !remaining.Contains(id)).ToArray();
            if (remaining.Count > 0)
                await roster.HeartbeatAsync(
                    ManagedAssignmentId(matchId),
                    remaining,
                    nowUtc,
                    cancellationToken);
        }
        return await ReadCommandReplyAsync(userId, idempotencyKey, nowUtc, cancellationToken);
    }

    public async Task<CreditHoldemHistoryResponse> HistoryAsync(
        string userId,
        int limit,
        CancellationToken cancellationToken)
    {
        await journal.RecoverAccountAsync(userId, cancellationToken);
        var snapshot = await database.Collection(CreditHoldemBackend.PlayerHistory)
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
        await journal.RecoverAccountAsync(userId, cancellationToken);
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
        if (await TryReadCachedReplyAsync(userId, idempotencyKey, "leave", matchId, detail, cancellationToken) is { } cached) return cached;
        await journal.RecoverAccountAsync(userId, cancellationToken);
        if (await TryReadCachedReplyAsync(userId, idempotencyKey, "leave", matchId, detail, cancellationToken) is { } recovered) return recovered;
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
            RequireCurrentTableSession(initial[2], initial[1], match, userId);
            var balances = await ReadHumanBalancesAsync(transaction, match, cancellationToken);
            (match, _) = await AdvanceIfDueAsync(match, nowUtc, cancellationToken);
            if (match.Version != expectedVersion)
                throw new CreditHoldemConflictException("The Hold'em table changed. Reconnect before leaving.");
            match = await LeaveAsync(match, userId, nowUtc, cancellationToken);
            await ApplyMatchWriteAsync(transaction, match, balances, nowUtc, cancellationToken);
            transaction.Set(SessionDocument(userId), SessionData(
                userId, CreditHoldemSessionKinds.Idle, null, null, null, 0, nowUtc), SetOptions.MergeAll);
            transaction.Create(GuardDocument(userId, idempotencyKey), GuardData(userId, "leave", matchId, detail, nowUtc));
            return true;
        }, cancellationToken: cancellationToken);
        var after = ReadMatch(await MatchDocument(matchId).GetSnapshotAsync(cancellationToken));
        await MaintainManagedAssignmentsAsync(matchId, nowUtc, cancellationToken);
        return await ReadCommandReplyAsync(userId, idempotencyKey, nowUtc, cancellationToken);
    }

    public async Task SweepAsync(DateTime nowUtc, CancellationToken cancellationToken)
    {
        await journal.RecoverAllAsync(cancellationToken);
        var expiredTickets = await database.Collection(CreditHoldemBackend.Tickets)
            .WhereLessThanOrEqualTo("expiresAt", Timestamp.FromDateTime(nowUtc))
            .Limit(MaximumSweepBatch).GetSnapshotAsync(cancellationToken);
        foreach (var ticket in expiredTickets.Documents)
            await ExpireTicketAsync(ticket.Id, nowUtc, cancellationToken);
        var due = await database.Collection(CreditHoldemBackend.Matches)
            .WhereLessThanOrEqualTo("nextDeadlineAt", Timestamp.FromDateTime(nowUtc))
            .Limit(MaximumSweepBatch)
            .GetSnapshotAsync(cancellationToken);
        foreach (var snapshot in due.Documents)
            await AdvanceMatchAsync(snapshot.Id, nowUtc, cancellationToken);
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
            return await CreateMatchAsync(transaction, partitionKey, queue, selected, seed, nowUtc, cancellationToken);
        }, cancellationToken: cancellationToken);
    }

    private async Task AdvanceMatchAsync(string matchId, DateTime nowUtc, CancellationToken cancellationToken)
    {
        string[] releasedManagedIds = [];
        await RunTransactionAsync(async transaction =>
        {
            var snapshot = await transaction.GetSnapshotAsync(MatchDocument(matchId), cancellationToken);
            if (!snapshot.Exists || TableClosed(snapshot)) return false;
            var match = ReadMatch(snapshot);
            var balances = await ReadHumanBalancesAsync(transaction, match, cancellationToken, includePending: true);
            var expiredPending = match.PendingTakeovers.Any(ticket => CreditHoldemEscrow.Expired(ticket, nowUtc));
            WriteExpiredPendingRefunds(transaction, match, balances, nowUtc);
            var priorVersion = match.Version;
            var priorStatus = match.Status;
            var priorSettlement = match.AccountingSettled;
            (match, var advance) = await AdvanceIfDueAsync(match, nowUtc, cancellationToken);
            if (match.Status == "completed" && match.AccountingSettled &&
                nowUtc >= (match.CompletedAtUtc ?? match.UpdatedAtUtc).Add(AutomaticInterHandDelay) &&
                !match.Players.Any(player => player.IsAccountBacked &&
                    !match.LeavingActorIds.Contains(player.ActorId)))
            {
                var managedBefore = match.Players.Where(TexasHoldemManagedPlayers.IsManaged)
                    .Select(player => player.ActorId).ToArray();
                RemoveBustedEscrowManagedPlayers(match);
                _ = TexasHoldemManagedPlayers.RemoveDepartingPlayers(match);
                var decision = PopulationDecision(match, includePendingHumans: true);
                var balancesWithPending = balances;
                WriteEscrowCashouts(transaction, match, balancesWithPending, nowUtc);
                foreach (var actorId in advance.ReleasedHumanActorIds)
                    transaction.Set(SessionDocument(actorId), SessionData(
                        actorId, CreditHoldemSessionKinds.Idle, null, null, null, 0, nowUtc), SetOptions.MergeAll);
                var minimumHumans = match.PendingTakeovers.Count > 0 ? 1 : 0;
                var next = decision.KeepTableOpen
                    ? await StartNextHandAsync(
                        match,
                        balancesWithPending,
                        BitConverter.ToUInt64(RandomNumberGenerator.GetBytes(sizeof(ulong))),
                        minimumHumans,
                        nowUtc, cancellationToken)
                    : null;
                if (next is not null)
                {
                    WriteUnadmittedTakeoverRefunds(transaction, match, next, balancesWithPending, nowUtc);
                    WriteBlindCommitments(transaction, next, balancesWithPending, "automatic-next-hand", nowUtc);
                    foreach (var ticket in match.PendingTakeovers.Where(ticket => !match.UsesBuyInEscrow || next.Players.Any(player => player.EscrowTicketId == ticket.TicketId)))
                        transaction.Set(TicketDocument(ticket.TicketId), TicketData(ticket with
                        {
                            Status = "matched",
                            Version = checked(ticket.Version + 1)
                        }));
                    foreach (var human in next.Players.Where(player => player.IsAccountBacked))
                        transaction.Set(SessionDocument(human.ActorId), SessionData(
                            human.ActorId, CreditHoldemSessionKinds.Match, null, matchId, null,
                            next.Version, nowUtc), SetOptions.MergeAll);
                    transaction.Set(PartitionDocument(next.PartitionKey), new Dictionary<string, object>
                    {
                        ["activeMatchId"] = next.MatchId,
                        ["updatedAt"] = Timestamp.FromDateTime(nowUtc)
                    }, SetOptions.MergeAll);
                    transaction.Set(MatchDocument(matchId), MatchData(next), SetOptions.MergeAll);
                    transaction.Observe(next, nowUtc, "hand-started-automatically");
                    WriteActiveHistory(transaction, next);
                    var remaining = next.Players.Where(TexasHoldemManagedPlayers.IsManaged)
                        .Select(player => player.ActorId).ToHashSet(StringComparer.Ordinal);
                    releasedManagedIds = managedBefore.Where(id => !remaining.Contains(id)).ToArray();
                    return true;
                }

                releasedManagedIds = managedBefore;
                population.Close(CreditHoldemBackend.GameId, matchId);
                WriteEscrowTableClosure(transaction, match, balancesWithPending, nowUtc);
                transaction.Set(PartitionDocument(match.PartitionKey), new Dictionary<string, object>
                {
                    ["activeMatchId"] = string.Empty,
                    ["updatedAt"] = Timestamp.FromDateTime(nowUtc)
                }, SetOptions.MergeAll);
                transaction.Set(MatchDocument(matchId), new Dictionary<string, object>
                {
                    ["nextDeadlineAt"] = Timestamp.FromDateTime(DormantDeadlineUtc),
                    ["managedPlayersReleasedAt"] = Timestamp.FromDateTime(nowUtc)
                }, SetOptions.MergeAll);
                return true;
            }
            if (match.Version == priorVersion && match.Status == priorStatus &&
                match.AccountingSettled == priorSettlement && !expiredPending)
                return false;
            await ApplyMatchWriteAsync(transaction, match, balances, nowUtc, cancellationToken);
            foreach (var actorId in advance.ReleasedHumanActorIds)
                transaction.Set(SessionDocument(actorId), SessionData(
                    actorId, CreditHoldemSessionKinds.Idle, null, null, null, 0, nowUtc), SetOptions.MergeAll);
            return true;
        }, cancellationToken: cancellationToken);
        await MaintainManagedAssignmentsAsync(matchId, nowUtc, cancellationToken);
    }

    private async Task MaintainManagedAssignmentsAsync(
        string matchId,
        DateTime nowUtc,
        CancellationToken cancellationToken)
    {
        var snapshot = await MatchDocument(matchId).GetSnapshotAsync(cancellationToken);
        if (!snapshot.Exists || TableClosed(snapshot)) return;
        var match = ReadMatch(snapshot);
        var managedIds = match.Players.Where(TexasHoldemManagedPlayers.IsManaged)
            .Select(player => player.ActorId).ToArray();
        if (managedIds.Length == 0) return;
        var hasActiveAccount = match.Players.Any(player =>
            player.IsAccountBacked && !match.LeavingActorIds.Contains(player.ActorId));
        if (!hasActiveAccount)
        {
            var decision = PopulationDecision(match, includePendingHumans: true);
            if (!decision.KeepTableOpen)
            {
                if (match.UsesBuyInEscrow)
                {
                    var closed = await RunTransactionAsync(async transaction =>
                    {
                        var currentSnapshot = await transaction.GetSnapshotAsync(MatchDocument(matchId), cancellationToken);
                        var current = ReadMatch(currentSnapshot);
                        if (current.PendingTakeovers.Count > 0 || current.Players.Any(player =>
                            player.IsAccountBacked && !current.LeavingActorIds.Contains(player.ActorId))) return false;
                        var balances = await ReadHumanBalancesAsync(transaction, current, cancellationToken, includePending: true);
                        WriteEscrowTableClosure(transaction, current, balances, nowUtc);
                        return true;
                    }, cancellationToken: cancellationToken);
                    if (!closed) return;
                }
                population.Close(CreditHoldemBackend.GameId, matchId);
                await WriteMaintenanceAsync(matchId, new Dictionary<string, object>
                {
                    ["nextDeadlineAt"] = Timestamp.FromDateTime(DormantDeadlineUtc),
                    ["managedPlayersReleasedAt"] = Timestamp.FromDateTime(nowUtc),
                    ["acceptingHumans"] = false
                }, cancellationToken);
                return;
            }

            await roster.HeartbeatAsync(
                ManagedAssignmentId(matchId),
                managedIds,
                nowUtc,
                cancellationToken);
            return;
        }

        var heartbeatAt = snapshot.TryGetValue<Timestamp>("managedHeartbeatAt", out var stored)
            ? stored.ToDateTime()
            : DateTime.UnixEpoch;
        if (heartbeatAt.Add(ManagedLeaseHeartbeatInterval) > nowUtc) return;
        await roster.HeartbeatAsync(
            ManagedAssignmentId(matchId),
            managedIds,
            nowUtc,
            cancellationToken);
        await WriteMaintenanceAsync(matchId, new Dictionary<string, object>
        {
            ["managedHeartbeatAt"] = Timestamp.FromDateTime(nowUtc),
            ["nextDeadlineAt"] = Timestamp.FromDateTime(NextDeadlineAt(match, nowUtc))
        }, cancellationToken);
    }

    private Task<bool> WriteMaintenanceAsync(string matchId, Dictionary<string, object> fields, CancellationToken token) =>
        RunTransactionAsync(async transaction =>
        {
            var snapshot = await transaction.GetSnapshotAsync(MatchDocument(matchId), token);
            if (!snapshot.Exists) return false;
            var current = ReadMatch(snapshot);
            if (fields.ContainsKey("managedPlayersReleasedAt") && (current.PendingTakeovers.Count > 0 ||
                current.Players.Any(player => player.IsAccountBacked && !current.LeavingActorIds.Contains(player.ActorId))))
                return false;
            if (fields.ContainsKey("managedHeartbeatAt")) fields["nextDeadlineAt"] = Timestamp.FromDateTime(NextDeadlineAt(current, DateTime.UtcNow));
            transaction.Set(MatchDocument(matchId), fields, SetOptions.MergeAll);
            return true;
        }, token);

    private async Task<T> RunTransactionAsync<T>(
        Func<CreditHoldemMutation, Task<T>> callback,
        CancellationToken cancellationToken)
    {
        const int maximumAttempts = 12;
        for (var attempt = 1; ; attempt++)
        {
            try
            {
                return await journal.RunAsync(callback, cancellationToken);
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

    private async Task<Dictionary<string, long>> ReadHumanBalancesAsync(
        CreditHoldemMutation transaction,
        CreditHoldemMatch match,
        CancellationToken cancellationToken,
        bool includePending = false)
    {
        var userIds = match.Players.Where(player => player.IsAccountBacked).Select(player => player.ActorId)
            .Concat(includePending ? match.PendingTakeovers.Select(ticket => ticket.UserId) : [])
            .Distinct(StringComparer.Ordinal)
            .ToArray();
        var values = await Task.WhenAll(userIds.Select(userId => transaction.BalanceAsync(userId, cancellationToken)));
        return userIds.Select((userId, index) => (userId, Balance: values[index]))
            .ToDictionary(value => value.userId, value => value.Balance, StringComparer.Ordinal);
    }

    private void WriteBlindCommitments(
        CreditHoldemMutation transaction,
        CreditHoldemMatch match,
        IReadOnlyDictionary<string, long> balances,
        string sourceKey,
        DateTime nowUtc)
    {
        if (match.UsesBuyInEscrow) return;
        foreach (var player in match.Players.Where(player => player.IsAccountBacked && player.CommittedHand > 0))
        {
            var available = balances.GetValueOrDefault(player.ActorId);
            if (available < player.CommittedHand)
                throw new CreditHoldemInsufficientCreditsException(available, player.CommittedHand);
            WriteCommitment(transaction, match, player, player.CommittedHand, available, "blind", sourceKey, nowUtc);
        }
    }

    private void WriteCommitment(
        CreditHoldemMutation transaction,
        CreditHoldemMatch match,
        CreditHoldemPlayer player,
        int cents,
        long available,
        string reason,
        string sourceKey,
        DateTime nowUtc)
    {
        if (match.UsesBuyInEscrow || cents <= 0 || !player.IsAccountBacked) return;
        var after = checked(available - cents);
        if (after < 0) throw new CreditHoldemInsufficientCreditsException(available, cents);
        var ledgerId = $"{match.MatchId}-hand-{match.HandNumber}-{reason}-{CreditHoldemIds.Hash(player.ActorId)}-{match.Version}";
        transaction.WalletDelta(player.ActorId, -cents, ledgerId, $"texas-holdem-{reason}", sourceKey, nowUtc);
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

    private static Task<(CreditHoldemMatch Match, int Committed)> ApplyActionAsync(CreditHoldemMatch match,
        string actorId, string action, int? raiseTo, DateTime nowUtc, CancellationToken cancellationToken) =>
        Task.FromResult((match, CreditHoldemEngine.ApplyAction(match, actorId, action, raiseTo, nowUtc)));

    private static Task<CreditHoldemMatch> LeaveAsync(CreditHoldemMatch match, string actorId,
        DateTime nowUtc, CancellationToken cancellationToken)
    {
        CreditHoldemEngine.Leave(match, actorId, nowUtc);
        return Task.FromResult(match);
    }

    private static Task<CreditHoldemMatch?> StartNextHandAsync(CreditHoldemMatch prior,
        IReadOnlyDictionary<string, long> balances, ulong seed, int minimumHumans, DateTime nowUtc,
        CancellationToken cancellationToken) =>
        Task.FromResult(CreditHoldemEngine.StartNextHand(prior, balances, seed, minimumHumans, nowUtc));

    private static Task<(CreditHoldemMatch Match, CreditHoldemTableAdvance Advance)> AdvanceIfDueAsync(
        CreditHoldemMatch match, DateTime nowUtc, CancellationToken cancellationToken) =>
        Task.FromResult((match, CreditHoldemTableCoordinator.AdvanceIfDue(match, nowUtc)));

    private async Task ApplyMatchWriteAsync(
        CreditHoldemMutation transaction,
        CreditHoldemMatch match,
        IReadOnlyDictionary<string, long> balances,
        DateTime nowUtc,
        CancellationToken cancellationToken)
    {
        if (match.Status != "active" && !match.AccountingSettled)
        {
            var settlement = CreditHoldemEngine.ApplyFinancialSettlement(match, nowUtc);
            foreach (var payout in settlement.HumanPayoutsCents.Where(value => !match.UsesBuyInEscrow && value.Value > 0))
            {
                var before = balances.GetValueOrDefault(payout.Key);
                var after = checked(before + payout.Value);
                var player = match.Players.Single(value => value.ActorId == payout.Key);
                player.AccountPayoutCents = payout.Value;
                var ledgerId = $"{match.MatchId}-hand-{match.HandNumber}-payout-{CreditHoldemIds.Hash(payout.Key)}";
                transaction.WalletDelta(payout.Key, payout.Value, ledgerId, "texas-holdem-hand-payout", match.MatchId, nowUtc);
            }
            var humanCount = match.Players.Count(player => player.IsAccountBacked);
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
                ["financialClassification"] = match.UsesBuyInEscrow ? "escrow-house-backed-v1" : "real-human-wager-v2",
                ["botFinancialContributionCents"] = match.UsesBuyInEscrow ? match.Players.Where(player => !player.IsAccountBacked).Sum(player => (long)player.CommittedHand) : 0L,
                ["recognizedAt"] = Timestamp.FromDateTime(nowUtc),
                ["settledAt"] = Timestamp.FromDateTime(nowUtc),
                ["schemaVersion"] = match.UsesBuyInEscrow ? 3L : 2L
            });
            WriteCompletedHistory(transaction, match, settlement);
            if (match.Players.Any(player => player.IsAccountBacked))
            {
                var audit = HoldemRoundAudit.Capture(match, nowUtc);
                transaction.Audit(match, nowUtc);
                foreach (var player in match.Players.Where(player => player.IsAccountBacked))
                {
                    var analytics = GameAnalyticsOutbox.Create(new("texas-holdem", revenueId, player.ActorId,
                        "paid", true, match.CompletedAtUtc ?? nowUtc, match.StartedAtUtc,
                        player.CommittedHand, player.AccountPayoutCents, "gameRoundAudits", audit.Summary.Id))!;
                    transaction.Set(database.Collection(GameAnalyticsOutbox.CollectionName).Document(analytics.Id), analytics.Fields);
                }
            }
            foreach (var human in match.Players.Where(player => player.IsAccountBacked && !match.LeavingActorIds.Contains(player.ActorId)))
                transaction.Set(SessionDocument(human.ActorId), SessionData(
                    human.ActorId, CreditHoldemSessionKinds.Result, null, match.MatchId, null, match.Version, nowUtc), SetOptions.MergeAll);
            transaction.Set(PartitionDocument(match.PartitionKey), new Dictionary<string, object>
            {
                ["activeMatchId"] = match.Players.Count(player => player.IsAccountBacked && !match.LeavingActorIds.Contains(player.ActorId)) <
                    CreditHoldemMoney.MaximumSeats ? match.MatchId : string.Empty,
                ["updatedAt"] = Timestamp.FromDateTime(nowUtc)
            }, SetOptions.MergeAll);
        }
        else if (match.Status == "active") WriteActiveHistory(transaction, match);
        else
        {
            transaction.Set(PartitionDocument(match.PartitionKey), new Dictionary<string, object>
            {
                ["activeMatchId"] = match.Players.Count(player =>
                    player.IsAccountBacked && !match.LeavingActorIds.Contains(player.ActorId)) <
                    CreditHoldemMoney.MaximumSeats && match.Players.Any(player =>
                    !match.LeavingActorIds.Contains(player.ActorId))
                        ? match.MatchId
                        : string.Empty,
                ["updatedAt"] = Timestamp.FromDateTime(nowUtc)
            }, SetOptions.MergeAll);
        }
        WriteEscrowCashouts(transaction, match, balances.ToDictionary(StringComparer.Ordinal), nowUtc);
        transaction.Set(MatchDocument(match.MatchId), MatchData(match), SetOptions.MergeAll);
        transaction.Observe(match, nowUtc);
    }

    private async Task<bool> CreateMatchAsync(
        CreditHoldemMutation transaction,
        string partitionKey,
        List<CreditHoldemTicket> queue,
        IReadOnlyList<CreditHoldemTicket> selected,
        ulong seed,
        DateTime nowUtc,
        CancellationToken cancellationToken)
    {
        var matchId = CreditHoldemIds.Hash($"{partitionKey}\n{string.Join("\n", selected.Select(ticket => ticket.TicketId))}");
        var values = await Task.WhenAll(selected.Select(ticket => transaction.BalanceAsync(ticket.UserId, cancellationToken)));
        var balances = selected.Select((ticket, index) => (ticket.UserId, Balance: values[index]))
            .ToDictionary(value => value.UserId, value => value.Balance, StringComparer.Ordinal);
        var rule = CreditHoldemTableRules.Resolve(selected[0].TableRuleId);
        var escrow = selected.All(ticket => ticket.BuyInCents > 0);
        if (!escrow && balances.Values.Any(value => value < rule.BigBlindCents))
            throw new CreditHoldemConflictException("A queued player no longer has enough credits for the big blind.");
        var populationDecision = population.Observe(new(
            CreditHoldemBackend.GameId,
            matchId,
            1,
            selected.Count,
            0,
            CreditHoldemMoney.MinimumStartPlayers,
            CreditHoldemMoney.MaximumSeats - 1));
        var managedProfiles = await ManagedProfilesAsync(
            matchId,
            populationDecision.ManagedArrivals,
            selected.Select(value => value.UserId).ToArray(),
            nowUtc,
            cancellationToken);
        if (selected.Count + managedProfiles.Count < CreditHoldemMoney.MinimumStartPlayers) return false;
        var seats = TexasHoldemManagedPlayers.BuildSeats(selected, managedProfiles, balances, seed, rule, matchId);
        var match = CreditHoldemEngine.Deal(matchId, seats, partitionKey, seed, nowUtc, rule.Id, escrow);
        WriteBlindCommitments(transaction, match, balances, "initial-deal", nowUtc);
        WriteActiveHistory(transaction, match);
        transaction.Create(MatchDocument(matchId), MatchData(match));
        transaction.Observe(match, nowUtc, "hand-dealt");
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
        return true;
    }

    private void WriteReservation(CreditHoldemMutation transaction, CreditHoldemTicket ticket, long available, DateTime nowUtc)
    {
        var after = checked(available - ticket.BuyInCents);
        if (ticket.BuyInCents <= 0 || after < 0) throw new CreditHoldemInsufficientCreditsException(available, ticket.BuyInCents);
        var ledgerId = $"{ticket.TicketId}-buy-in";
        transaction.WalletDelta(ticket.UserId, -ticket.BuyInCents, ledgerId, "texas-holdem-buy-in", ticket.TicketId, nowUtc);
    }

    private void WriteTicketRefund(CreditHoldemMutation transaction, CreditHoldemTicket ticket, long available, DateTime nowUtc)
    {
        if (ticket.BuyInCents <= 0) return;
        var after = checked(available + ticket.BuyInCents);
        var ledgerId = $"{ticket.TicketId}-buy-in-refund";
        transaction.WalletDelta(ticket.UserId, ticket.BuyInCents, ledgerId, "texas-holdem-buy-in-refund", ticket.TicketId, nowUtc);
    }

    private void WriteEscrowCashouts(CreditHoldemMutation transaction, CreditHoldemMatch match, Dictionary<string, long> balances, DateTime nowUtc)
    {
        if (!match.UsesBuyInEscrow) return;
        foreach (var player in match.Players.Where(player => player.IsAccountBacked && match.LeavingActorIds.Contains(player.ActorId)))
        {
            if (string.IsNullOrEmpty(player.EscrowTicketId)) throw new InvalidOperationException("An escrow seat is missing its reservation.");
            if (!match.EscrowCashedOutTicketIds.Add(player.EscrowTicketId)) continue;
            var after = checked(balances.GetValueOrDefault(player.ActorId) + player.Stack);
            balances[player.ActorId] = after;
            var ledgerId = $"{player.EscrowTicketId}-cash-out";
            transaction.WalletDelta(player.ActorId, player.Stack, ledgerId, "texas-holdem-cash-out", player.EscrowTicketId, nowUtc);
        }
    }

    private void MarkIneligibleEscrowPlayers(CreditHoldemMutation transaction, CreditHoldemMatch match, DateTime nowUtc)
    {
        if (!match.UsesBuyInEscrow) return;
        var minimum = CreditHoldemTableRules.Resolve(match.TableRuleId).BigBlindCents;
        foreach (var player in match.Players.Where(player => player.IsAccountBacked && !match.LeavingActorIds.Contains(player.ActorId) && player.Stack < minimum))
        {
            match.LeavingActorIds.Add(player.ActorId);
            transaction.Set(SessionDocument(player.ActorId), SessionData(player.ActorId,
                CreditHoldemSessionKinds.Idle, null, null, null, 0, nowUtc), SetOptions.MergeAll);
        }
    }

    private static void RemoveBustedEscrowManagedPlayers(CreditHoldemMatch match)
    {
        if (!match.UsesBuyInEscrow) return;
        var minimum = CreditHoldemTableRules.Resolve(match.TableRuleId).BigBlindCents;
        match.Players.RemoveAll(player => !player.IsAccountBacked && player.Stack < minimum);
    }

    private void WriteUnadmittedTakeoverRefunds(CreditHoldemMutation transaction, CreditHoldemMatch prior,
        CreditHoldemMatch next, Dictionary<string, long> balances, DateTime nowUtc)
    {
        if (!prior.UsesBuyInEscrow) return;
        foreach (var ticket in prior.PendingTakeovers.Where(ticket => next.Players.All(player => player.EscrowTicketId != ticket.TicketId)))
        {
            var before = balances.GetValueOrDefault(ticket.UserId);
            WriteTicketRefund(transaction, ticket, before, nowUtc);
            balances[ticket.UserId] = checked(before + ticket.BuyInCents);
            transaction.Set(TicketDocument(ticket.TicketId), TicketData(ticket with { Status = "cancelled", Version = checked(ticket.Version + 1) }));
            transaction.Set(SessionDocument(ticket.UserId), SessionData(ticket.UserId,
                CreditHoldemSessionKinds.Idle, null, null, null, 0, nowUtc), SetOptions.MergeAll);
        }
    }

    private void WriteExpiredPendingRefunds(CreditHoldemMutation transaction, CreditHoldemMatch match,
        Dictionary<string, long> balances, DateTime nowUtc)
    {
        foreach (var ticket in match.PendingTakeovers.Where(ticket => CreditHoldemEscrow.Expired(ticket, nowUtc)).ToArray())
        {
            var before = balances.GetValueOrDefault(ticket.UserId);
            WriteTicketRefund(transaction, ticket, before, nowUtc);
            balances[ticket.UserId] = checked(before + ticket.BuyInCents);
            transaction.Set(TicketDocument(ticket.TicketId), TicketData(ticket with { Status = "expired", Version = checked(ticket.Version + 1) }));
            transaction.Set(SessionDocument(ticket.UserId), SessionData(ticket.UserId,
                CreditHoldemSessionKinds.Idle, null, null, null, 0, nowUtc), SetOptions.MergeAll);
            match.PendingTakeovers.Remove(ticket);
        }
    }

    private void WriteEscrowTableClosure(CreditHoldemMutation transaction, CreditHoldemMatch match, Dictionary<string, long> balances, DateTime nowUtc)
    {
        if (!match.UsesBuyInEscrow) return;
        foreach (var player in match.Players.Where(player => player.IsAccountBacked && !match.LeavingActorIds.Contains(player.ActorId)))
        {
            match.LeavingActorIds.Add(player.ActorId);
            transaction.Set(SessionDocument(player.ActorId), SessionData(player.ActorId,
                CreditHoldemSessionKinds.Idle, null, null, null, 0, nowUtc), SetOptions.MergeAll);
        }
        WriteEscrowCashouts(transaction, match, balances, nowUtc);
        foreach (var ticket in match.PendingTakeovers)
        {
            var before = balances.GetValueOrDefault(ticket.UserId);
            WriteTicketRefund(transaction, ticket, before, nowUtc);
            balances[ticket.UserId] = checked(before + ticket.BuyInCents);
            transaction.Set(TicketDocument(ticket.TicketId), TicketData(ticket with { Status = "cancelled", Version = checked(ticket.Version + 1) }));
            transaction.Set(SessionDocument(ticket.UserId), SessionData(ticket.UserId,
                CreditHoldemSessionKinds.Idle, null, null, null, 0, nowUtc), SetOptions.MergeAll);
        }
        match.PendingTakeovers.Clear();
        transaction.Set(MatchDocument(match.MatchId), MatchData(match), SetOptions.MergeAll);
        transaction.Observe(match, nowUtc, "table-closed");
        transaction.Set(MatchDocument(match.MatchId), new Dictionary<string, object>
        {
            ["managedPlayersReleasedAt"] = Timestamp.FromDateTime(nowUtc),
            ["acceptingHumans"] = false,
            ["nextDeadlineAt"] = Timestamp.FromDateTime(DormantDeadlineUtc)
        }, SetOptions.MergeAll);
        transaction.Set(PartitionDocument(match.PartitionKey), new Dictionary<string, object>
        {
            ["activeMatchId"] = string.Empty, ["updatedAt"] = Timestamp.FromDateTime(nowUtc)
        }, SetOptions.MergeAll);
    }

    private async Task ExpireTicketAsync(string ticketId, DateTime nowUtc, CancellationToken cancellationToken)
    {
        await RunTransactionAsync(async transaction =>
        {
            var ticketSnapshot = await transaction.GetSnapshotAsync(TicketDocument(ticketId), cancellationToken);
            if (!ticketSnapshot.Exists) return false;
            var ticket = ReadTicket(ticketSnapshot);
            if (!CreditHoldemEscrow.Expired(ticket, nowUtc)) return false;
            var snapshots = await Task.WhenAll(
                transaction.GetSnapshotAsync(SessionDocument(ticket.UserId), cancellationToken),
                transaction.GetSnapshotAsync(PartitionDocument(ticket.PartitionKey), cancellationToken));
            CreditHoldemMatch? pendingMatch = null;
            if (ticket.MatchId is { } matchId)
            {
                var matchSnapshot = await transaction.GetSnapshotAsync(MatchDocument(matchId), cancellationToken);
                if (matchSnapshot.Exists) pendingMatch = ReadMatch(matchSnapshot);
                if (pendingMatch is not null && pendingMatch.PendingTakeovers.All(value => value.TicketId != ticketId)) return false;
            }
            WriteTicketRefund(transaction, ticket, await transaction.BalanceAsync(ticket.UserId, cancellationToken), nowUtc);
            transaction.Set(TicketDocument(ticketId), TicketData(ticket with { Status = "expired", Version = checked(ticket.Version + 1) }));
            if (ReadString(snapshots[0], "ticketId") == ticketId)
                transaction.Set(SessionDocument(ticket.UserId), SessionData(ticket.UserId,
                    CreditHoldemSessionKinds.Idle, null, null, null, 0, nowUtc), SetOptions.MergeAll);
            if (pendingMatch is not null)
            {
                pendingMatch.PendingTakeovers.RemoveAll(value => value.TicketId == ticketId);
                transaction.Set(MatchDocument(pendingMatch.MatchId), MatchData(pendingMatch), SetOptions.MergeAll);
                transaction.Observe(pendingMatch, nowUtc, "seat-expired");
            }
            else
            {
                var queue = ReadQueue(snapshots[1]);
                queue.RemoveAll(value => value.TicketId == ticketId);
                transaction.Set(PartitionDocument(ticket.PartitionKey), QueueData(ticket.PartitionKey,
                    queue, ReadString(snapshots[1], "activeMatchId"), nowUtc), SetOptions.MergeAll);
            }
            return true;
        }, cancellationToken: cancellationToken);
    }

    private Task<IReadOnlyList<ManagedPlayerRosterProfile>> ManagedProfilesAsync(
        string matchId,
        int count,
        IReadOnlyCollection<string> excludedProfileIds,
        DateTime nowUtc,
        CancellationToken cancellationToken)
    {
        if (count <= 0) return Task.FromResult<IReadOnlyList<ManagedPlayerRosterProfile>>([]);
        return roster.ReserveAsync(ManagedAssignmentId(matchId), count, excludedProfileIds, nowUtc, cancellationToken);
    }

    private ManagedTablePopulationDecision PopulationDecision(
        CreditHoldemMatch match,
        bool includePendingHumans)
    {
        var humans = match.Players.Count(player =>
            player.IsAccountBacked && !match.LeavingActorIds.Contains(player.ActorId));
        var pendingHumans = includePendingHumans ? match.PendingTakeovers.Count : 0;
        humans += pendingHumans;
        var managed = match.Players.Count(TexasHoldemManagedPlayers.IsManaged);
        managed = Math.Min(
            managed,
            Math.Max(0, CreditHoldemMoney.MaximumSeats - 1 - humans));
        return population.Observe(new(
            CreditHoldemBackend.GameId,
            match.MatchId,
            checked(match.HandNumber + 1),
            humans,
            managed,
            CreditHoldemMoney.MinimumStartPlayers,
            CreditHoldemMoney.MaximumSeats - 1));
    }

    private static string ManagedAssignmentId(string matchId) => $"texas-holdem:{matchId}";

    private async Task<CreditHoldemStoreResult> ReadSessionAsync(
        string userId,
        DateTime nowUtc,
        CancellationToken cancellationToken)
    {
        var session = await SessionDocument(userId).GetSnapshotAsync(cancellationToken);
        var balance = (await accounts.GetStateAsync(userId, cancellationToken)).SlotsCreditsBalanceCents ?? throw new GameWalletUnavailableException();
        var kind = ReadString(session, "kind");
        if (string.IsNullOrEmpty(kind) || kind == CreditHoldemSessionKinds.Idle)
            return new CreditHoldemStoreResult(new CreditHoldemIdleSessionResponse(), balance);
        if (kind == CreditHoldemSessionKinds.Queue)
            return new CreditHoldemStoreResult(await CreditHoldemSessionProjection.ReadAsync(database, userId, nowUtc, cancellationToken), balance);
        var matchId = ReadString(session, "matchId");
        var matchSnapshot = await MatchDocument(matchId).GetSnapshotAsync(cancellationToken);
        if (!matchSnapshot.Exists) throw new CreditHoldemNotFoundException("The Hold'em match was not found.");
        var match = ReadMatch(matchSnapshot);
        if (!HasCurrentSeat(matchSnapshot, match, userId))
        {
            await ReleaseStaleTableSessionAsync(userId, matchId, nowUtc, cancellationToken);
            return await ReadSessionAsync(userId, nowUtc, cancellationToken);
        }
        var response = kind == CreditHoldemSessionKinds.Result
            ? CreditHoldemProjection.Result(match, userId, nowUtc)
            : CreditHoldemProjection.Match(match, userId, nowUtc);
        return new CreditHoldemStoreResult(response, balance);
    }

    private async Task<CreditHoldemStoreResult?> TryReadCachedReplyAsync(string userId, string key, string operation,
        string target, string detail, CancellationToken token)
    {
        var guard = await GuardDocument(userId, key).GetSnapshotAsync(token);
        if (!guard.Exists) return null;
        VerifyGuard(guard, operation, target, detail);
        if (ReadString(guard, "userId") != userId) throw new InvalidOperationException("The Hold'em receipt changed owner.");
        if (guard.TryGetValue<long>("declinedAvailableCents", out var available))
            throw new CreditHoldemInsufficientCreditsException(available, guard.GetValue<long>("declinedRequiredCents"));
        return guard.TryGetValue<string>("replyJson", out var json) ? OpenReply(guard, json) : null;
    }

    private async Task<CreditHoldemStoreResult> ReadCommandReplyAsync(string userId, string key, DateTime now,
        CancellationToken token)
    {
        var document = GuardDocument(userId, key);
        var snapshot = await document.GetSnapshotAsync(token);
        if (!snapshot.Exists) throw new InvalidOperationException("The Hold'em command receipt is missing.");
        if (snapshot.TryGetValue<long>("declinedAvailableCents", out var available))
            throw new CreditHoldemInsufficientCreditsException(available, snapshot.GetValue<long>("declinedRequiredCents"));
        if (snapshot.TryGetValue<string>("replyJson", out var saved)) return OpenReply(snapshot, saved);

        // Pre-package command guards already include their financial effects. Import a frozen response without any wallet call.
        var current = await ReadSessionAsync(userId, now, token);
        var json = JsonSerializer.Serialize(new CreditHoldemFrozenReply(current.Session.Kind,
            JsonSerializer.Serialize(current.Session, JsonOptions), current.BalanceCents), JsonOptions);
        return await database.RunTransactionAsync(async transaction =>
        {
            var guard = await transaction.GetSnapshotAsync(document, token);
            if (!guard.Exists || ReadString(guard, "userId") != userId)
                throw new InvalidOperationException("The historical Hold'em command receipt changed owner.");
            if (guard.TryGetValue<string>("replyJson", out var previous)) return OpenReply(guard, previous);
            transaction.Set(document, new Dictionary<string, object>
            {
                ["replyJson"] = json, ["replySha256"] = CreditHoldemIds.Hash(json)
            }, SetOptions.MergeAll);
            return current;
        }, cancellationToken: token);
    }

    private static CreditHoldemStoreResult OpenReply(DocumentSnapshot snapshot, string json)
    {
        if (CreditHoldemIds.Hash(json) != ReadString(snapshot, "replySha256"))
            throw new InvalidOperationException("The stored Hold'em response digest is invalid.");
        return CreditHoldemMutation.RestoreReply(JsonSerializer.Deserialize<CreditHoldemFrozenReply>(json, JsonOptions)
            ?? throw new InvalidOperationException("The stored Hold'em response is empty."));
    }

    private Task ReleaseStaleTableSessionAsync(string userId, string matchId, DateTime nowUtc, CancellationToken cancellationToken) =>
        RunTransactionAsync(async transaction =>
        {
            var snapshots = await Task.WhenAll(
                transaction.GetSnapshotAsync(SessionDocument(userId), cancellationToken),
                transaction.GetSnapshotAsync(MatchDocument(matchId), cancellationToken));
            // A refresh racing a new reservation must never erase the new seat.
            if (ReadString(snapshots[0], "matchId") != matchId ||
                ReadString(snapshots[0], "kind") is not CreditHoldemSessionKinds.Match and not CreditHoldemSessionKinds.Result)
                return false;
            if (!snapshots[1].Exists) throw new CreditHoldemNotFoundException("The Hold'em match was not found.");
            var match = ReadMatch(snapshots[1]);
            if (HasCurrentSeat(snapshots[1], match, userId)) return false;
            var player = match.Players.FirstOrDefault(player => player.IsAccountBacked && player.ActorId == userId);
            if (match.UsesBuyInEscrow && player?.EscrowTicketId is { } ticketId &&
                !match.EscrowCashedOutTicketIds.Contains(ticketId))
                throw new CreditHoldemConflictException("This Hold'em table cannot be resumed while its cash-out is pending.");
            transaction.Set(SessionDocument(userId), SessionData(
                userId, CreditHoldemSessionKinds.Idle, null, null, null, 0, nowUtc), SetOptions.MergeAll);
            return true;
        }, cancellationToken: cancellationToken);

    private IReadOnlyList<CreditHoldemTicket> SelectMatch(
        List<CreditHoldemTicket> queue,
        DateTime nowUtc) =>
        MultiplayerQueuePolicy.PlanQueue(
            queue,
            new MultiplayerQueueRules(
                allowSingleHumanBotFill ? 1 : 2,
                CreditHoldemMoney.MaximumSeats),
            nowUtc,
            ticket => ticket.Status == "queued" && !CreditHoldemEscrow.Expired(ticket, nowUtc),
            ticket => ticket.JoinedAtUtc,
            ticket => ticket.GraceEndsAtUtc,
            ticket => ticket.TicketId).HumanTickets;

    private void WriteActiveHistory(CreditHoldemMutation transaction, CreditHoldemMatch match)
    {
        foreach (var human in match.Players.Where(value => value.IsAccountBacked))
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
        CreditHoldemMutation transaction,
        CreditHoldemMatch match,
        CreditHoldemFinancialSettlement settlement)
    {
        foreach (var human in match.Players.Where(value => value.IsAccountBacked))
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
        database.Collection(CreditHoldemBackend.Sessions).Document(CreditHoldemIds.Hash(userId));
    private DocumentReference TicketDocument(string id) => database.Collection(CreditHoldemBackend.Tickets).Document(id);
    private DocumentReference PartitionDocument(string id) => database.Collection(CreditHoldemBackend.QueuePartitions).Document(id);
    private DocumentReference MatchDocument(string id) => database.Collection(CreditHoldemBackend.Matches).Document(id);
    private DocumentReference GuardDocument(string userId, string key) =>
        database.Collection(CreditHoldemBackend.CommandGuards).Document(CreditHoldemIds.Hash($"{userId}\n{key}"));
    private DocumentReference RevenueDocument(string id) => database.Collection(CreditHoldemBackend.MatchRevenue).Document(id);
    private DocumentReference HistoryDocument(string userId, string eventId) =>
        database.Collection(CreditHoldemBackend.PlayerHistory)
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
        ["status"] = ticket.Status,
        ["expiresAt"] = Timestamp.FromDateTime(ticket.BuyInCents > 0 && ticket.Status is "queued" or "pending-next-hand" ? ticket.JoinedAtUtc.Add(CreditHoldemEscrow.QueueLifetime) : DormantDeadlineUtc),
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
        ["partitionKey"] = match.PartitionKey,
        ["usesBuyInEscrow"] = match.UsesBuyInEscrow,
        ["acceptingHumans"] = CreditHoldemEscrow.CanAcceptTakeover(match),
        ["matchJson"] = JsonSerializer.Serialize(match, JsonOptions),
        ["status"] = match.Status,
        ["version"] = match.Version,
        ["nextDeadlineAt"] = Timestamp.FromDateTime(NextDeadlineAt(match, match.UpdatedAtUtc)),
        ["schemaVersion"] = 1L
    };

    private static DateTime NextDeadlineAt(CreditHoldemMatch match, DateTime nowUtc)
    {
        var managedIds = match.Players.Where(TexasHoldemManagedPlayers.IsManaged)
            .Select(player => player.ActorId).ToHashSet(StringComparer.Ordinal);
        var hasActiveHuman = match.Players.Any(player =>
            player.IsAccountBacked && !match.LeavingActorIds.Contains(player.ActorId));
        var hasManagedPlayers = managedIds.Count > 0;
        if (!hasActiveHuman && !hasManagedPlayers && match.PendingTakeovers.Count == 0)
            return DormantDeadlineUtc;
        if (match.Status == "completed")
        {
            if (!hasActiveHuman)
                return (match.CompletedAtUtc ?? match.UpdatedAtUtc).Add(AutomaticInterHandDelay);
            var resultExpiry = (match.CompletedAtUtc ?? match.UpdatedAtUtc)
                .Add(CreditHoldemTableCoordinator.ResultInactivityDuration);
            return managedIds.Count > 0
                ? new[] { resultExpiry, nowUtc.Add(ManagedLeaseHeartbeatInterval) }.Min()
                : resultExpiry;
        }
        if (match.Status != "active") return DormantDeadlineUtc;
        var candidates = new List<DateTime> { match.MatchDeadlineAtUtc };
        if (match.ActionDeadlineAtUtc is { } actionDeadline) candidates.Add(actionDeadline);
        var active = match.Players.Single(player => player.Seat == match.ActiveSeat);
        if (managedIds.Contains(active.ActorId))
            candidates.Add(TexasHoldemManagedPlayers.PrivateActionDueAt(match, active));
        if (managedIds.Count > 0) candidates.Add(nowUtc.Add(ManagedLeaseHeartbeatInterval));
        return candidates.Min();
    }

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
        CreditHoldemMatchPersistence.Deserialize(ReadString(snapshot, "matchJson"));

    private static CreditHoldemHistoryRecord ReadHistory(DocumentSnapshot snapshot) =>
        JsonSerializer.Deserialize<CreditHoldemHistoryRecord>(ReadString(snapshot, "historyJson"), JsonOptions)
        ?? throw new InvalidOperationException("A stored Hold'em history item is invalid.");

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
        match.Players.Count(player => player.IsAccountBacked && !match.LeavingActorIds.Contains(player.ActorId)) +
        match.PendingTakeovers.Count < CreditHoldemMoney.MaximumSeats;

    private static bool TableClosed(DocumentSnapshot snapshot) => snapshot.ContainsField("managedPlayersReleasedAt");

    private static bool HasCurrentSeat(DocumentSnapshot snapshot, CreditHoldemMatch match, string userId) =>
        !TableClosed(snapshot) && !match.LeavingActorIds.Contains(userId) &&
        match.Players.Any(player => player.IsAccountBacked && player.ActorId == userId);

    private static void RequireCurrentTableSession(DocumentSnapshot session, DocumentSnapshot snapshot, CreditHoldemMatch match, string userId)
    {
        if (ReadString(session, "matchId") != match.MatchId ||
            ReadString(session, "kind") is not CreditHoldemSessionKinds.Match and not CreditHoldemSessionKinds.Result ||
            !HasCurrentSeat(snapshot, match, userId))
            throw new CreditHoldemConflictException("You no longer have a seat at this Hold'em table.");
    }
    private static ulong NewSeed() => BitConverter.ToUInt64(System.Security.Cryptography.RandomNumberGenerator.GetBytes(8));
}

internal sealed class CreditHoldemPausedException() : Exception("Texas Hold'em is not accepting new paid hands.");
