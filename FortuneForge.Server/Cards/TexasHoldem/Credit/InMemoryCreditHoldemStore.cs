using FortuneForge.Server.Bots;
using FortuneForge.Server.Bots.TexasHoldem;
using FortuneForge.Server.Matchmaking;
using FortuneForge.Server.RoundAudit;
using FortuneForge.Games.Abstractions.Auditing;

namespace FortuneForge.Server.Cards.TexasHoldem.Credit;

internal sealed class InMemoryCreditHoldemStore(
    bool allowSingleHumanBotFill = false,
    IMultiplayerMatchmaker? multiplayerMatchmaker = null,
    IManagedTablePopulationDirector? managedPopulationDirector = null) : ICreditHoldemStore
{
    private static readonly TimeSpan AutomaticInterHandDelay = TimeSpan.FromSeconds(2);
    private sealed record SessionPointer(string Kind, string? TicketId, string? MatchId);
    private readonly object gate = new();
    private readonly Dictionary<string, SessionPointer> sessions = new(StringComparer.Ordinal);
    private readonly Dictionary<string, CreditHoldemTicket> tickets = new(StringComparer.Ordinal);
    private readonly List<string> queue = [];
    private readonly Dictionary<string, CreditHoldemMatch> matches = new(StringComparer.Ordinal);
    private readonly Dictionary<string, (string Operation, string Target, string Detail)> guards = new(StringComparer.Ordinal);
    private readonly Dictionary<string, long> balances = new(StringComparer.Ordinal);
    private readonly HashSet<string> ledgerIds = new(StringComparer.Ordinal);
    private readonly HashSet<string> revenueIds = new(StringComparer.Ordinal);
    private readonly Dictionary<string, CreditHoldemHistoryRecord> history = new(StringComparer.Ordinal);
    private readonly Dictionary<string, string> activeMatchIds = new(StringComparer.Ordinal);
    private readonly HashSet<string> closedEscrowMatchIds = new(StringComparer.Ordinal);
    private readonly Dictionary<string, RoundAuditCapture> roundAudits = new(StringComparer.Ordinal);
    private readonly IMultiplayerMatchmaker matchmaking =
        multiplayerMatchmaker ?? new MultiplayerMatchmaker();
    private readonly IManagedTablePopulationDirector population =
        managedPopulationDirector ?? new ManagedTablePopulationDirector();

    internal void SetBalance(string userId, long cents)
    {
        lock (gate) balances[userId] = cents;
    }

    internal long Balance(string userId)
    {
        lock (gate) return balances.GetValueOrDefault(userId);
    }

    internal int LedgerCount { get { lock (gate) return ledgerIds.Count; } }
    internal int RevenueCount { get { lock (gate) return revenueIds.Count; } }
    internal CreditHoldemMatch MatchForTest(string matchId) { lock (gate) return matches[matchId]; }
    internal IReadOnlyList<RoundAuditCapture> AuditsForTest() { lock (gate) return roundAudits.Values.ToArray(); }

    public Task SweepAsync(DateTime nowUtc, CancellationToken cancellationToken)
    {
        lock (gate)
        {
            cancellationToken.ThrowIfCancellationRequested();
            ExpireQueueReservations(nowUtc);
            foreach (var match in matches.Values.Where(match =>
                         match.Status is "active" or "completed").ToArray())
            {
                AdvanceMatch(match, nowUtc);
            }
            return Task.CompletedTask;
        }
    }

    public Task<CreditHoldemStoreResult> GetSessionAsync(
        string userId,
        DateTime nowUtc,
        CancellationToken cancellationToken)
    {
        lock (gate)
        {
            cancellationToken.ThrowIfCancellationRequested();
            ExpireQueueReservations(nowUtc);
            AdvanceForUser(userId, nowUtc);
            return Task.FromResult(Project(userId, nowUtc));
        }
    }

    public Task<CreditHoldemStoreResult> JoinAsync(
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
        lock (gate)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var ticketId = CreditHoldemIds.Hash($"{userId}\n{idempotencyKey}");
            ExpireQueueReservations(nowUtc);
            var rule = CreditHoldemTableRules.Resolve(tableRuleId);
            var partitionKey = CreditHoldemEscrow.Partition(rule.Id);
            var detail = CreditHoldemEscrow.JoinDetail(expectedVersion, rule.Id, buyInCents);
            if (Replay(userId, idempotencyKey, "join", ticketId, detail)) return Task.FromResult(Project(userId, nowUtc));
            var current = Project(userId, nowUtc).Session;
            if (current.Version != expectedVersion || current.Kind != CreditHoldemSessionKinds.Idle)
                throw new CreditHoldemConflictException("The Hold'em session changed. Reconnect before joining.");
            var available = balances.GetValueOrDefault(userId);
            var reserved = CreditHoldemEscrow.ReserveAmount(available, rule, buyInCents);

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
            balances[userId] = checked(available - reserved);
            ledgerIds.Add($"{ticketId}-buy-in");
            var openMatch = matches.Values.Where(match => !closedEscrowMatchIds.Contains(match.MatchId) && match.PartitionKey == partitionKey && CreditHoldemEscrow.CanAcceptTakeover(match))
                .OrderBy(match => match.StartedAtUtc).ThenBy(match => match.MatchId, StringComparer.Ordinal).FirstOrDefault();
            if (openMatch is not null)
            {
                var pending = ticket with { Status = "pending-next-hand", MatchId = openMatch.MatchId };
                tickets.Add(ticketId, pending);
                openMatch.PendingTakeovers.Add(pending);
                sessions[userId] = new(CreditHoldemSessionKinds.Queue, ticketId, openMatch.MatchId);
            }
            else
            {
                activeMatchIds.Remove(partitionKey);
                tickets.Add(ticketId, ticket);
                queue.Add(ticketId);
                sessions[userId] = new(CreditHoldemSessionKinds.Queue, ticketId, null);
                TryMatch(partitionKey, seed, nowUtc);
            }
            guards[Guard(userId, idempotencyKey)] = ("join", ticketId, detail);
            return Task.FromResult(Project(userId, nowUtc));
        }
    }

    public Task<CreditHoldemStoreResult> CancelAsync(
        string userId,
        string ticketId,
        int expectedVersion,
        string idempotencyKey,
        DateTime nowUtc,
        CancellationToken cancellationToken)
    {
        lock (gate)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var detail = expectedVersion.ToString(System.Globalization.CultureInfo.InvariantCulture);
            if (Replay(userId, idempotencyKey, "cancel", ticketId, detail)) return Task.FromResult(Project(userId, nowUtc));
            if (!tickets.TryGetValue(ticketId, out var ticket) || ticket.UserId != userId)
                throw new CreditHoldemNotFoundException("The Hold'em queue ticket was not found.");
            if (ticket.Version != expectedVersion || ticket.Status is not "queued" and not "pending-next-hand" ||
                !sessions.TryGetValue(userId, out var session) || session.Kind != CreditHoldemSessionKinds.Queue)
                throw new CreditHoldemConflictException("This queue ticket changed, matched, or was already cancelled.");
            tickets[ticketId] = ticket with { Status = "cancelled", Version = checked(ticket.Version + 1) };
            RefundTicket(ticket);
            queue.Remove(ticketId);
            if (ticket.MatchId is { } matchId && matches.TryGetValue(matchId, out var match))
                match.PendingTakeovers.RemoveAll(value => value.TicketId == ticketId);
            sessions[userId] = new(CreditHoldemSessionKinds.Idle, null, null);
            guards[Guard(userId, idempotencyKey)] = ("cancel", ticketId, detail);
            return Task.FromResult(Project(userId, nowUtc));
        }
    }

    public Task<CreditHoldemStoreResult> ActionAsync(
        string userId,
        string matchId,
        CreditHoldemActionRequest request,
        string idempotencyKey,
        DateTime nowUtc,
        CancellationToken cancellationToken)
    {
        lock (gate)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var action = request.Type.Trim().ToLowerInvariant();
            var detail = $"{action}:{request.ExpectedVersion}:{request.RaiseTo?.ToString() ?? string.Empty}";
            if (Replay(userId, idempotencyKey, "action", matchId, detail)) return Task.FromResult(Project(userId, nowUtc));
            RequireCurrentTableSession(userId, matchId);
            var match = MatchForUser(matchId, userId);
            AdvanceMatch(match, nowUtc);
            if (match.Status != "active" || match.Version != request.ExpectedVersion)
                throw new CreditHoldemConflictException("The Hold'em table changed. Reconnect before acting.");
            var player = match.Players.Single(value => value.ActorId == userId);
            var required = RequiredCommitment(match, player, action, request.RaiseTo);
            var available = balances.GetValueOrDefault(userId);
            if (!match.UsesBuyInEscrow && available < required) throw new CreditHoldemInsufficientCreditsException(available, required);
            var committed = CreditHoldemEngine.ApplyAction(match, userId, action, request.RaiseTo, nowUtc);
            if (committed != required) throw new InvalidOperationException("The Hold'em commitment changed during validation.");
            DebitCommitment(match, player, committed, $"action-v{request.ExpectedVersion}", idempotencyKey, nowUtc);
            guards[Guard(userId, idempotencyKey)] = ("action", matchId, detail);
            AdvanceMatch(match, nowUtc);
            return Task.FromResult(Project(userId, nowUtc));
        }
    }

    public Task<CreditHoldemStoreResult> NextHandAsync(
        string userId,
        string matchId,
        int expectedVersion,
        string idempotencyKey,
        ulong seed,
        DateTime nowUtc,
        CancellationToken cancellationToken)
    {
        lock (gate)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var detail = expectedVersion.ToString(System.Globalization.CultureInfo.InvariantCulture);
            if (Replay(userId, idempotencyKey, "next-hand", matchId, detail)) return Task.FromResult(Project(userId, nowUtc));
            RequireCurrentTableSession(userId, matchId);
            ExpireQueueReservations(nowUtc);
            var prior = MatchForUser(matchId, userId);
            SettleOnce(prior, nowUtc);
            if (prior.Status != "completed" || prior.Version != expectedVersion || !prior.AccountingSettled)
                throw new CreditHoldemConflictException("The hand changed or is not ready for the next deal.");
            CashOutReleasedPlayers(prior);
            if (prior.LeavingActorIds.Contains(userId))
                throw new CreditHoldemConflictException("You have left this Hold'em table.");
            RemoveBustedEscrowSeats(prior);
            _ = TexasHoldemManagedPlayers.RemoveDepartingPlayers(prior);
            var populationDecision = PopulationDecision(prior, includePendingHumans: true);
            if (!populationDecision.KeepTableOpen)
            {
                if (!prior.UsesBuyInEscrow) throw new CreditHoldemConflictException("This Hold'em table has closed.");
                CloseEscrowTable(prior);
                guards[Guard(userId, idempotencyKey)] = ("next-hand", matchId, detail);
                return Task.FromResult(Project(userId, nowUtc));
            }
            TexasHoldemManagedPlayers.AddPlayers(
                prior,
                TexasHoldemManagedPlayers.CreateLocalProfiles(
                    populationDecision.ManagedArrivals,
                    nowUtc));
            var balanceMap = prior.Players.Where(value => value.IsAccountBacked)
                .Concat(prior.PendingTakeovers.Select(ticket => new CreditHoldemPlayer
                {
                    ActorId = ticket.UserId,
                    PublicSeatId = ticket.PublicSeatId,
                    DisplayName = ticket.DisplayName,
                    IsAccountBacked = true,
                    Seat = 0,
                    StartingStack = 0,
                    HoleCards = []
                }))
                .Select(value => value.ActorId)
                .Distinct(StringComparer.Ordinal)
                .ToDictionary(value => value, value => balances.GetValueOrDefault(value), StringComparer.Ordinal);
            var minimumHumans = allowSingleHumanBotFill ? 1 : 2;
            var next = CreditHoldemEngine.StartNextHand(prior, balanceMap, seed, minimumHumans, nowUtc);
            if (next is null)
            {
                if (!prior.UsesBuyInEscrow) throw new CreditHoldemConflictException("Not enough funded real players remain for the next hand.");
                CloseEscrowTable(prior);
                guards[Guard(userId, idempotencyKey)] = ("next-hand", matchId, detail);
                return Task.FromResult(Project(userId, nowUtc));
            }
            RefundUnadmittedTakeovers(prior, next);
            ApplyBlindCommitments(next, idempotencyKey, nowUtc);
            matches[matchId] = next;
            foreach (var ticket in prior.PendingTakeovers.Where(ticket => !prior.UsesBuyInEscrow || next.Players.Any(player => player.EscrowTicketId == ticket.TicketId)))
                tickets[ticket.TicketId] = ticket with { Status = "matched", Version = checked(ticket.Version + 1) };
            foreach (var human in next.Players.Where(value => value.IsAccountBacked))
                sessions[human.ActorId] = new(CreditHoldemSessionKinds.Match, null, matchId);
            WriteActiveHistory(next);
            if (next.Players.Count(value => value.IsAccountBacked) < CreditHoldemMoney.MaximumSeats)
                activeMatchIds[next.PartitionKey] = matchId;
            else activeMatchIds.Remove(next.PartitionKey);
            guards[Guard(userId, idempotencyKey)] = ("next-hand", matchId, detail);
            return Task.FromResult(Project(userId, nowUtc));
        }
    }

    public Task<CreditHoldemStoreResult> LeaveAsync(
        string userId,
        string matchId,
        int expectedVersion,
        string idempotencyKey,
        DateTime nowUtc,
        CancellationToken cancellationToken)
    {
        lock (gate)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var detail = expectedVersion.ToString(System.Globalization.CultureInfo.InvariantCulture);
            if (Replay(userId, idempotencyKey, "leave", matchId, detail)) return Task.FromResult(Project(userId, nowUtc));
            RequireCurrentTableSession(userId, matchId);
            var match = MatchForUser(matchId, userId);
            AdvanceMatch(match, nowUtc);
            if (match.Version != expectedVersion)
                throw new CreditHoldemConflictException("The Hold'em table changed. Reconnect before leaving.");
            CreditHoldemEngine.Leave(match, userId, nowUtc);
            SettleOnce(match, nowUtc);
            CashOutReleasedPlayers(match);
            sessions[userId] = new(CreditHoldemSessionKinds.Idle, null, null);
            if (match.Players.Where(value => value.IsAccountBacked).All(value => match.LeavingActorIds.Contains(value.ActorId)))
                activeMatchIds.Remove(match.PartitionKey);
            guards[Guard(userId, idempotencyKey)] = ("leave", matchId, detail);
            return Task.FromResult(Project(userId, nowUtc));
        }
    }

    public Task<CreditHoldemHistoryResponse> HistoryAsync(
        string userId,
        int limit,
        CancellationToken cancellationToken)
    {
        lock (gate)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var items = history.Values.Where(value => value.UserId == userId)
                .OrderByDescending(value => value.StartedAtUtc)
                .ThenByDescending(value => value.HandNumber)
                .Take(Math.Clamp(limit, 1, 50))
                .Select(CreditHoldemProjection.History)
                .ToArray();
            return Task.FromResult(new CreditHoldemHistoryResponse(items));
        }
    }

    public Task<CreditHoldemHistoryItemResponse> MarkHistorySeenAsync(
        string userId,
        string eventId,
        DateTime nowUtc,
        CancellationToken cancellationToken)
    {
        lock (gate)
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (!history.TryGetValue(eventId, out var item) || item.UserId != userId)
                throw new CreditHoldemNotFoundException("The Hold'em history item was not found.");
            item = item with { Seen = true };
            history[eventId] = item;
            return Task.FromResult(CreditHoldemProjection.History(item));
        }
    }

    private void AdvanceForUser(string userId, DateTime nowUtc)
    {
        if (!sessions.TryGetValue(userId, out var session)) return;
        if (session.Kind == CreditHoldemSessionKinds.Queue && session.TicketId is { } ticketId)
        {
            var ticket = tickets[ticketId];
            if (ticket.Status == "queued") TryMatch(ticket.PartitionKey, NewSeed(), nowUtc);
            else if (ticket.MatchId is { } pendingMatchId)
            {
                var pending = matches[pendingMatchId];
                AdvanceMatch(pending, nowUtc);
            }
        }
        if (sessions.TryGetValue(userId, out session) && session.MatchId is { } matchId)
        {
            var match = matches[matchId];
            AdvanceMatch(match, nowUtc);
        }
    }

    private void AdvanceMatch(CreditHoldemMatch match, DateTime nowUtc)
    {
        var advance = CreditHoldemTableCoordinator.AdvanceIfDue(match, nowUtc);
        SettleOnce(match, nowUtc);
        CashOutReleasedPlayers(match);
        foreach (var actorId in advance.ReleasedHumanActorIds)
            sessions[actorId] = new(CreditHoldemSessionKinds.Idle, null, null);
        if (advance.ReleasedHumanActorIds.Count > 0 &&
            match.Players.Where(player => player.IsAccountBacked)
                .All(player => match.LeavingActorIds.Contains(player.ActorId)))
            activeMatchIds.Remove(match.PartitionKey);
        TryStartAutomaticNextHand(match, nowUtc);
    }

    private void TryStartAutomaticNextHand(CreditHoldemMatch prior, DateTime nowUtc)
    {
        if (prior.Status != "completed" || !prior.AccountingSettled ||
            nowUtc < (prior.CompletedAtUtc ?? prior.UpdatedAtUtc).Add(AutomaticInterHandDelay) ||
            prior.Players.Any(player => player.IsAccountBacked &&
                !prior.LeavingActorIds.Contains(player.ActorId)))
            return;

        RemoveBustedEscrowSeats(prior);
        _ = TexasHoldemManagedPlayers.RemoveDepartingPlayers(prior);
        var decision = PopulationDecision(prior, includePendingHumans: true);
        if (!decision.KeepTableOpen)
        {
            population.Close(ManagedPlayerGames.TexasHoldem, prior.MatchId);
            activeMatchIds.Remove(prior.PartitionKey);
            CloseEscrowTable(prior);
            return;
        }

        TexasHoldemManagedPlayers.AddPlayers(
            prior,
            TexasHoldemManagedPlayers.CreateLocalProfiles(decision.ManagedArrivals, nowUtc));
        var balanceMap = prior.PendingTakeovers
            .Select(ticket => ticket.UserId)
            .Distinct(StringComparer.Ordinal)
            .ToDictionary(userId => userId, userId => balances.GetValueOrDefault(userId), StringComparer.Ordinal);
        var minimumHumans = prior.PendingTakeovers.Count > 0 ? 1 : 0;
        var next = CreditHoldemEngine.StartNextHand(
            prior, balanceMap, NewSeed(), minimumHumans, nowUtc);
        if (next is null)
        {
            population.Close(ManagedPlayerGames.TexasHoldem, prior.MatchId);
            activeMatchIds.Remove(prior.PartitionKey);
            CloseEscrowTable(prior);
            return;
        }

        ApplyBlindCommitments(next, "automatic-next-hand", nowUtc);
        RefundUnadmittedTakeovers(prior, next);
        matches[prior.MatchId] = next;
        foreach (var ticket in prior.PendingTakeovers.Where(ticket => !prior.UsesBuyInEscrow || next.Players.Any(player => player.EscrowTicketId == ticket.TicketId)))
            tickets[ticket.TicketId] = ticket with
            {
                Status = "matched",
                Version = checked(ticket.Version + 1)
            };
        foreach (var human in next.Players.Where(player => player.IsAccountBacked))
            sessions[human.ActorId] = new(CreditHoldemSessionKinds.Match, null, next.MatchId);
        WriteActiveHistory(next);
        activeMatchIds[next.PartitionKey] = next.MatchId;
    }

    private void TryMatch(string partitionKey, ulong seed, DateTime nowUtc)
    {
        var selected = matchmaking.PlanQueue(
            queue.Select(id => tickets[id]),
            new MultiplayerQueueRules(
                allowSingleHumanBotFill ? 1 : 2,
                CreditHoldemMoney.MaximumSeats),
            nowUtc,
            value => value.Status == "queued" && value.PartitionKey == partitionKey && !CreditHoldemEscrow.Expired(value, nowUtc),
            value => value.JoinedAtUtc,
            value => value.GraceEndsAtUtc,
            value => value.TicketId).HumanTickets;
        if (selected.Count == 0) return;
        var rule = CreditHoldemTableRules.Resolve(selected[0].TableRuleId);
        var escrow = selected.All(ticket => ticket.BuyInCents > 0);
        var balancesForHand = selected.ToDictionary(value => value.UserId, value => escrow ? value.BuyInCents : balances.GetValueOrDefault(value.UserId), StringComparer.Ordinal);
        if (balancesForHand.Values.Any(value => value < rule.BigBlindCents)) return;
        var matchId = CreditHoldemIds.Hash($"{partitionKey}\n{string.Join("\n", selected.Select(value => value.TicketId))}");
        var populationDecision = population.Observe(new(
            ManagedPlayerGames.TexasHoldem,
            matchId,
            1,
            selected.Count,
            0,
            CreditHoldemMoney.MinimumStartPlayers,
            CreditHoldemMoney.MaximumSeats - 1));
        var profiles = TexasHoldemManagedPlayers.CreateLocalProfiles(
            populationDecision.ManagedArrivals,
            nowUtc);
        var seats = TexasHoldemManagedPlayers.BuildSeats(selected, profiles, balancesForHand, seed, rule, matchId);
        var match = CreditHoldemEngine.Deal(
            matchId, seats, partitionKey, seed, nowUtc, rule.Id, escrow);
        ApplyBlindCommitments(match, "initial-deal", nowUtc);
        matches.Add(matchId, match);
        foreach (var ticket in selected)
        {
            tickets[ticket.TicketId] = ticket with { Status = "matched", MatchId = matchId, Version = checked(ticket.Version + 1) };
            sessions[ticket.UserId] = new(CreditHoldemSessionKinds.Match, null, matchId);
            queue.Remove(ticket.TicketId);
        }
        WriteActiveHistory(match);
        if (selected.Count < CreditHoldemMoney.MaximumSeats) activeMatchIds[partitionKey] = matchId;
        else activeMatchIds.Remove(partitionKey);
    }

    private void ApplyBlindCommitments(CreditHoldemMatch match, string sourceKey, DateTime nowUtc)
    {
        if (match.UsesBuyInEscrow) return;
        foreach (var human in match.Players.Where(value => value.IsAccountBacked && value.CommittedHand > 0))
        {
            var available = balances.GetValueOrDefault(human.ActorId);
            if (available < human.CommittedHand)
                throw new CreditHoldemInsufficientCreditsException(available, human.CommittedHand);
            DebitCommitment(match, human, human.CommittedHand, "blind", sourceKey, nowUtc);
        }
    }

    private void DebitCommitment(
        CreditHoldemMatch match,
        CreditHoldemPlayer player,
        int cents,
        string reason,
        string sourceKey,
        DateTime nowUtc)
    {
        if (match.UsesBuyInEscrow || cents <= 0 || !player.IsAccountBacked) return;
        var before = balances.GetValueOrDefault(player.ActorId);
        var after = checked(before - cents);
        if (after < 0) throw new CreditHoldemInsufficientCreditsException(before, cents);
        var ledgerId = $"{match.MatchId}-hand-{match.HandNumber}-{reason}-{CreditHoldemIds.Hash(player.ActorId)}-{match.Version}";
        if (!ledgerIds.Add(ledgerId)) return;
        balances[player.ActorId] = after;
    }

    private void SettleOnce(CreditHoldemMatch match, DateTime nowUtc)
    {
        if (match.Status == "active" || match.AccountingSettled) return;
        var settlement = CreditHoldemEngine.ApplyFinancialSettlement(match);
        foreach (var payout in settlement.HumanPayoutsCents.Where(value => !match.UsesBuyInEscrow && value.Value > 0))
        {
            var player = match.Players.Single(value => value.ActorId == payout.Key);
            var ledgerId = $"{match.MatchId}-hand-{match.HandNumber}-payout-{CreditHoldemIds.Hash(payout.Key)}";
            if (!ledgerIds.Add(ledgerId)) continue;
            balances[payout.Key] = checked(balances.GetValueOrDefault(payout.Key) + payout.Value);
            player.AccountPayoutCents = payout.Value;
        }
        revenueIds.Add($"{match.MatchId}-hand-{match.HandNumber}");
        WriteCompletedHistory(match, settlement);
        if (match.Players.Any(player => player.IsAccountBacked))
        {
            var capture = HoldemRoundAudit.Capture(match, nowUtc);
            roundAudits.TryAdd(capture.Summary.Id, capture);
        }
        foreach (var human in match.Players.Where(value => value.IsAccountBacked && !match.LeavingActorIds.Contains(value.ActorId)))
            sessions[human.ActorId] = new(CreditHoldemSessionKinds.Result, null, match.MatchId);
        if (match.Players.Count(value => value.IsAccountBacked && !match.LeavingActorIds.Contains(value.ActorId)) < CreditHoldemMoney.MaximumSeats)
            activeMatchIds[match.PartitionKey] = match.MatchId;
        else activeMatchIds.Remove(match.PartitionKey);
    }

    private void RefundTicket(CreditHoldemTicket ticket)
    {
        if (ticket.BuyInCents <= 0 || !ledgerIds.Add($"{ticket.TicketId}-buy-in-refund")) return;
        balances[ticket.UserId] = checked(balances.GetValueOrDefault(ticket.UserId) + ticket.BuyInCents);
    }

    private void ExpireQueueReservations(DateTime nowUtc)
    {
        foreach (var ticket in tickets.Values.Where(ticket => CreditHoldemEscrow.Expired(ticket, nowUtc)).ToArray())
        {
            RefundTicket(ticket);
            tickets[ticket.TicketId] = ticket with { Status = "expired", Version = checked(ticket.Version + 1) };
            queue.Remove(ticket.TicketId);
            if (ticket.MatchId is { } matchId && matches.TryGetValue(matchId, out var match))
                match.PendingTakeovers.RemoveAll(value => value.TicketId == ticket.TicketId);
            if (sessions.TryGetValue(ticket.UserId, out var session) && session.TicketId == ticket.TicketId)
                sessions[ticket.UserId] = new(CreditHoldemSessionKinds.Idle, null, null);
        }
    }

    private void CashOutReleasedPlayers(CreditHoldemMatch match)
    {
        if (!match.UsesBuyInEscrow) return;
        foreach (var player in match.Players.Where(player => player.IsAccountBacked && match.LeavingActorIds.Contains(player.ActorId)))
        {
            if (string.IsNullOrEmpty(player.EscrowTicketId)) throw new InvalidOperationException("An escrow seat is missing its reservation.");
            if (!match.EscrowCashedOutTicketIds.Add(player.EscrowTicketId)) continue;
            if (!ledgerIds.Add($"{player.EscrowTicketId}-cash-out")) continue;
            balances[player.ActorId] = checked(balances.GetValueOrDefault(player.ActorId) + player.Stack);
        }
    }

    private void RemoveBustedEscrowSeats(CreditHoldemMatch match)
    {
        if (!match.UsesBuyInEscrow) return;
        var minimum = CreditHoldemTableRules.Resolve(match.TableRuleId).BigBlindCents;
            foreach (var player in match.Players.Where(player => player.IsAccountBacked && !match.LeavingActorIds.Contains(player.ActorId) && player.Stack < minimum))
        {
            match.LeavingActorIds.Add(player.ActorId);
            sessions[player.ActorId] = new(CreditHoldemSessionKinds.Idle, null, null);
        }
        CashOutReleasedPlayers(match);
        match.Players.RemoveAll(player => !player.IsAccountBacked && player.Stack < minimum);
    }

    private void RefundUnadmittedTakeovers(CreditHoldemMatch prior, CreditHoldemMatch next)
    {
        foreach (var ticket in prior.PendingTakeovers.Where(ticket => next.Players.All(player => player.EscrowTicketId != ticket.TicketId)))
        {
            RefundTicket(ticket);
            tickets[ticket.TicketId] = ticket with { Status = "cancelled", Version = checked(ticket.Version + 1) };
            sessions[ticket.UserId] = new(CreditHoldemSessionKinds.Idle, null, null);
        }
    }

    private void CloseEscrowTable(CreditHoldemMatch match)
    {
        if (!match.UsesBuyInEscrow) return;
        closedEscrowMatchIds.Add(match.MatchId);
        foreach (var player in match.Players.Where(player => player.IsAccountBacked && !match.LeavingActorIds.Contains(player.ActorId)))
        {
            match.LeavingActorIds.Add(player.ActorId);
            sessions[player.ActorId] = new(CreditHoldemSessionKinds.Idle, null, null);
        }
        CashOutReleasedPlayers(match);
        foreach (var ticket in match.PendingTakeovers)
        {
            RefundTicket(ticket);
            tickets[ticket.TicketId] = ticket with { Status = "cancelled", Version = checked(ticket.Version + 1) };
            sessions[ticket.UserId] = new(CreditHoldemSessionKinds.Idle, null, null);
        }
        match.PendingTakeovers.Clear();
        activeMatchIds.Remove(match.PartitionKey);
    }

    private CreditHoldemMatch MatchForUser(string matchId, string userId)
    {
        if (!matches.TryGetValue(matchId, out var match) || match.Players.All(value => value.ActorId != userId))
            throw new CreditHoldemNotFoundException("The Hold'em table was not found.");
        return match;
    }

    private void RequireCurrentTableSession(string userId, string matchId)
    {
        if (!sessions.TryGetValue(userId, out var session) || session.MatchId != matchId ||
            session.Kind is not CreditHoldemSessionKinds.Match and not CreditHoldemSessionKinds.Result ||
            !matches.TryGetValue(matchId, out var match) || match.LeavingActorIds.Contains(userId))
            throw new CreditHoldemConflictException("You no longer have a seat at this Hold'em table.");
    }

    private void WriteActiveHistory(CreditHoldemMatch match)
    {
        foreach (var human in match.Players.Where(value => value.IsAccountBacked))
        {
            var eventId = CreditHoldemIds.Hash($"{human.ActorId}\n{match.MatchId}\n{match.HandNumber}");
            history[eventId] = new CreditHoldemHistoryRecord(
                eventId, human.ActorId, match.MatchId, match.HandNumber, "active", true,
                match.StartedAtUtc, null, human.CommittedHand, 0);
        }
    }

    private void WriteCompletedHistory(CreditHoldemMatch match, CreditHoldemFinancialSettlement settlement)
    {
        foreach (var human in match.Players.Where(value => value.IsAccountBacked))
        {
            var eventId = CreditHoldemIds.Hash($"{human.ActorId}\n{match.MatchId}\n{match.HandNumber}");
            history[eventId] = new CreditHoldemHistoryRecord(
                eventId, human.ActorId, match.MatchId, match.HandNumber, "completed", false,
                match.StartedAtUtc, match.CompletedAtUtc ?? match.UpdatedAtUtc,
                human.CommittedHand, settlement.HumanPayoutsCents.GetValueOrDefault(human.ActorId));
        }
    }

    private CreditHoldemStoreResult Project(string userId, DateTime nowUtc)
    {
        var balance = balances.GetValueOrDefault(userId);
        if (!sessions.TryGetValue(userId, out var session) || session.Kind == CreditHoldemSessionKinds.Idle)
            return new(new CreditHoldemIdleSessionResponse(), balance);
        if (session.Kind == CreditHoldemSessionKinds.Queue && session.TicketId is { } ticketId)
        {
            var ticket = tickets[ticketId];
            var people = ticket.Status == "pending-next-hand"
                ? [ticket]
                : queue.Select(id => tickets[id]).Where(value => value.Status == "queued" && value.PartitionKey == ticket.PartitionKey)
                    .OrderBy(value => value.JoinedAtUtc).ThenBy(value => value.TicketId, StringComparer.Ordinal).ToArray();
            var seats = people.Select((value, index) => new CreditHoldemSeatResponse(
                value.PublicSeatId,
                value.DisplayName,
                index,
                value.BuyInCents,
                value.BuyInCents,
                0,
                0,
                value.Status,
                null,
                [],
                null,
                value.UserId == userId)).ToArray();
            return new(new CreditHoldemQueueSessionResponse(
                ticket.TicketId,
                Array.FindIndex(people, value => value.UserId == userId) + 1,
                ticket.JoinedAtUtc,
                ticket.GraceEndsAtUtc,
                seats,
                ticket.Version,
                CreditHoldemTableRules.Resolve(ticket.TableRuleId).Public), balance);
        }
        var match = matches[session.MatchId!];
        var response = session.Kind == CreditHoldemSessionKinds.Result
            ? CreditHoldemProjection.Result(match, userId, nowUtc)
            : CreditHoldemProjection.Match(match, userId, nowUtc);
        return new(response, balance);
    }

    private bool Replay(string userId, string key, string operation, string target, string detail)
    {
        if (!guards.TryGetValue(Guard(userId, key), out var prior)) return false;
        if (prior != (operation, target, detail))
            throw new CreditHoldemConflictException("This Idempotency-Key was already used for a different Hold'em request.");
        return true;
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

    private static bool CanAcceptTakeover(CreditHoldemMatch match) =>
        match.Players.Count(value => value.IsAccountBacked && !match.LeavingActorIds.Contains(value.ActorId)) +
        match.PendingTakeovers.Count < CreditHoldemMoney.MaximumSeats;

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
            ManagedPlayerGames.TexasHoldem,
            match.MatchId,
            checked(match.HandNumber + 1),
            humans,
            managed,
            CreditHoldemMoney.MinimumStartPlayers,
            CreditHoldemMoney.MaximumSeats - 1));
    }
    private static string Guard(string userId, string key) => $"{userId}\n{key}";
    private static ulong NewSeed() => BitConverter.ToUInt64(System.Security.Cryptography.RandomNumberGenerator.GetBytes(8));
}
