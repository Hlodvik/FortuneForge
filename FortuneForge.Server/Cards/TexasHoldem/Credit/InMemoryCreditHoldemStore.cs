namespace FortuneForge.Server.Cards.TexasHoldem.Credit;

internal sealed class InMemoryCreditHoldemStore(bool allowSingleHumanBotFill = false) : ICreditHoldemStore
{
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

    public Task<CreditHoldemStoreResult> GetSessionAsync(
        string userId,
        DateTime nowUtc,
        CancellationToken cancellationToken)
    {
        lock (gate)
        {
            cancellationToken.ThrowIfCancellationRequested();
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
        string tableRuleId = CreditHoldemTableRules.StandardId)
    {
        lock (gate)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var ticketId = CreditHoldemIds.Hash($"{userId}\n{idempotencyKey}");
            var rule = CreditHoldemTableRules.Resolve(tableRuleId);
            var partitionKey = CreditHoldemTableRules.Partition(rule.Id);
            var detail = $"{expectedVersion}:{rule.Id}";
            if (Replay(userId, idempotencyKey, "join", ticketId, detail)) return Task.FromResult(Project(userId, nowUtc));
            var current = Project(userId, nowUtc).Session;
            if (current.Version != expectedVersion || current.Kind != CreditHoldemSessionKinds.Idle)
                throw new CreditHoldemConflictException("The Hold'em session changed. Reconnect before joining.");
            var available = balances.GetValueOrDefault(userId);
            if (available < rule.BigBlindCents)
                throw new CreditHoldemInsufficientCreditsException(available, rule.BigBlindCents);

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
            if (activeMatchIds.TryGetValue(partitionKey, out var openMatchId) &&
                matches.TryGetValue(openMatchId, out var openMatch) && CanAcceptTakeover(openMatch))
            {
                var pending = ticket with { Status = "pending-next-hand", MatchId = openMatchId };
                tickets.Add(ticketId, pending);
                openMatch.PendingTakeovers.Add(pending);
                sessions[userId] = new(CreditHoldemSessionKinds.Queue, ticketId, openMatchId);
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
            var match = MatchForUser(matchId, userId);
            _ = CreditHoldemEngine.AdvanceAutomatedTurn(match, nowUtc);
            SettleOnce(match, nowUtc);
            if (match.Status != "active" || match.Version != request.ExpectedVersion)
                throw new CreditHoldemConflictException("The Hold'em table changed. Reconnect before acting.");
            var player = match.Players.Single(value => value.ActorId == userId);
            var required = RequiredCommitment(match, player, action, request.RaiseTo);
            var available = balances.GetValueOrDefault(userId);
            if (available < required) throw new CreditHoldemInsufficientCreditsException(available, required);
            var committed = CreditHoldemEngine.ApplyAction(match, userId, action, request.RaiseTo, nowUtc);
            if (committed != required) throw new InvalidOperationException("The Hold'em commitment changed during validation.");
            DebitCommitment(match, player, committed, $"action-v{request.ExpectedVersion}", idempotencyKey, nowUtc);
            guards[Guard(userId, idempotencyKey)] = ("action", matchId, detail);
            _ = CreditHoldemEngine.AdvanceAutomatedTurn(match, nowUtc);
            SettleOnce(match, nowUtc);
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
            var prior = MatchForUser(matchId, userId);
            SettleOnce(prior, nowUtc);
            if (prior.Status != "completed" || prior.Version != expectedVersion || !prior.AccountingSettled)
                throw new CreditHoldemConflictException("The hand changed or is not ready for the next deal.");
            var balanceMap = prior.Players.Where(value => !value.IsBot)
                .Concat(prior.PendingTakeovers.Select(ticket => new CreditHoldemPlayer
                {
                    ActorId = ticket.UserId,
                    PublicSeatId = ticket.PublicSeatId,
                    DisplayName = ticket.DisplayName,
                    IsBot = false,
                    BotSkillLevel = null,
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
                throw new CreditHoldemConflictException("Not enough funded real players remain for the next hand.");
            ApplyBlindCommitments(next, idempotencyKey, nowUtc);
            matches[matchId] = next;
            foreach (var ticket in prior.PendingTakeovers)
                tickets[ticket.TicketId] = ticket with { Status = "matched", Version = checked(ticket.Version + 1) };
            foreach (var human in next.Players.Where(value => !value.IsBot))
                sessions[human.ActorId] = new(CreditHoldemSessionKinds.Match, null, matchId);
            WriteActiveHistory(next);
            if (next.Players.Count(value => !value.IsBot) < CreditHoldemMoney.MaximumSeats)
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
            var match = MatchForUser(matchId, userId);
            _ = CreditHoldemEngine.AdvanceAutomatedTurn(match, nowUtc);
            SettleOnce(match, nowUtc);
            if (match.Version != expectedVersion)
                throw new CreditHoldemConflictException("The Hold'em table changed. Reconnect before leaving.");
            CreditHoldemEngine.Leave(match, userId, nowUtc);
            SettleOnce(match, nowUtc);
            sessions[userId] = new(CreditHoldemSessionKinds.Idle, null, null);
            if (match.Players.Where(value => !value.IsBot).All(value => match.LeavingActorIds.Contains(value.ActorId)))
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
                _ = CreditHoldemEngine.AdvanceAutomatedTurn(pending, nowUtc);
                SettleOnce(pending, nowUtc);
            }
        }
        if (sessions.TryGetValue(userId, out session) && session.MatchId is { } matchId)
        {
            var match = matches[matchId];
            _ = CreditHoldemEngine.AdvanceAutomatedTurn(match, nowUtc);
            SettleOnce(match, nowUtc);
        }
    }

    private void TryMatch(string partitionKey, ulong seed, DateTime nowUtc)
    {
        var eligible = queue.Select(id => tickets[id])
            .Where(value => value.Status == "queued" && value.PartitionKey == partitionKey)
            .OrderBy(value => value.JoinedAtUtc)
            .ThenBy(value => value.TicketId, StringComparer.Ordinal)
            .ToArray();
        var minimumHumans = allowSingleHumanBotFill ? 1 : 2;
        if (eligible.Length < minimumHumans || nowUtc < eligible[0].GraceEndsAtUtc) return;
        var selected = eligible.Take(CreditHoldemMoney.MaximumSeats).ToArray();
        var rule = CreditHoldemTableRules.Resolve(selected[0].TableRuleId);
        var balancesForHand = selected.ToDictionary(value => value.UserId, value => balances.GetValueOrDefault(value.UserId), StringComparer.Ordinal);
        if (balancesForHand.Values.Any(value => value < rule.BigBlindCents)) return;
        var matchId = CreditHoldemIds.Hash($"{partitionKey}\n{string.Join("\n", selected.Select(value => value.TicketId))}");
        var occupied = Math.Max(CreditHoldemMoney.MinimumStartPlayers, selected.Length);
        var match = CreditHoldemEngine.Deal(
            matchId, selected, occupied, partitionKey, seed, balancesForHand, nowUtc, rule.Id);
        ApplyBlindCommitments(match, "initial-deal", nowUtc);
        matches.Add(matchId, match);
        foreach (var ticket in selected)
        {
            tickets[ticket.TicketId] = ticket with { Status = "matched", MatchId = matchId, Version = checked(ticket.Version + 1) };
            sessions[ticket.UserId] = new(CreditHoldemSessionKinds.Match, null, matchId);
            queue.Remove(ticket.TicketId);
        }
        WriteActiveHistory(match);
        if (selected.Length < CreditHoldemMoney.MaximumSeats) activeMatchIds[partitionKey] = matchId;
        else activeMatchIds.Remove(partitionKey);
    }

    private void ApplyBlindCommitments(CreditHoldemMatch match, string sourceKey, DateTime nowUtc)
    {
        foreach (var human in match.Players.Where(value => !value.IsBot && value.CommittedHand > 0))
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
        if (cents <= 0 || player.IsBot) return;
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
        foreach (var payout in settlement.HumanPayoutsCents.Where(value => value.Value > 0))
        {
            var player = match.Players.Single(value => value.ActorId == payout.Key);
            var ledgerId = $"{match.MatchId}-hand-{match.HandNumber}-payout-{CreditHoldemIds.Hash(payout.Key)}";
            if (!ledgerIds.Add(ledgerId)) continue;
            balances[payout.Key] = checked(balances.GetValueOrDefault(payout.Key) + payout.Value);
            player.AccountPayoutCents = payout.Value;
        }
        revenueIds.Add($"{match.MatchId}-hand-{match.HandNumber}");
        WriteCompletedHistory(match, settlement);
        foreach (var human in match.Players.Where(value => !value.IsBot && !match.LeavingActorIds.Contains(value.ActorId)))
            sessions[human.ActorId] = new(CreditHoldemSessionKinds.Result, null, match.MatchId);
        if (match.Players.Count(value => !value.IsBot && !match.LeavingActorIds.Contains(value.ActorId)) < CreditHoldemMoney.MaximumSeats)
            activeMatchIds[match.PartitionKey] = match.MatchId;
        else activeMatchIds.Remove(match.PartitionKey);
    }

    private CreditHoldemMatch MatchForUser(string matchId, string userId)
    {
        if (!matches.TryGetValue(matchId, out var match) || match.Players.All(value => value.ActorId != userId))
            throw new CreditHoldemNotFoundException("The Hold'em table was not found.");
        return match;
    }

    private void WriteActiveHistory(CreditHoldemMatch match)
    {
        foreach (var human in match.Players.Where(value => !value.IsBot))
        {
            var eventId = CreditHoldemIds.Hash($"{human.ActorId}\n{match.MatchId}\n{match.HandNumber}");
            history[eventId] = new CreditHoldemHistoryRecord(
                eventId, human.ActorId, match.MatchId, match.HandNumber, "active", true,
                match.StartedAtUtc, null, human.CommittedHand, 0);
        }
    }

    private void WriteCompletedHistory(CreditHoldemMatch match, CreditHoldemFinancialSettlement settlement)
    {
        foreach (var human in match.Players.Where(value => !value.IsBot))
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
                0,
                0,
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
        match.Players.Count(value => !value.IsBot && !match.LeavingActorIds.Contains(value.ActorId)) +
        match.PendingTakeovers.Count < CreditHoldemMoney.MaximumSeats;
    private static string Guard(string userId, string key) => $"{userId}\n{key}";
    private static ulong NewSeed() => BitConverter.ToUInt64(System.Security.Cryptography.RandomNumberGenerator.GetBytes(8));
}
