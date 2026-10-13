using System.Globalization;
using System.Security.Cryptography;
using FortuneForge.Server.Bots;
using FortuneForge.Server.Bots.ThreeCardPoker;
using FortuneForge.Server.Matchmaking;

namespace FortuneForge.Server.Cards.ThreeCardPoker.Table;

internal sealed record ThreeCardPokerTableCoordinatorResult(ThreeCardPokerTableStoreResult Store, ThreeCardPokerTableJournal Journal);

internal sealed class ThreeCardPokerTableCoordinator(
    Func<IReadOnlyList<string>>? deckFactory = null,
    Func<ulong>? seedFactory = null,
    ThreeCardPokerManagedPlayerSupply? managedPlayerSupply = null,
    IMultiplayerMatchmaker? multiplayerMatchmaker = null,
    IManagedTablePopulationDirector? managedPopulationDirector = null)
{
    private static readonly TimeSpan MinimumManagedTurnPause = TimeSpan.FromMilliseconds(1_500);
    private static readonly TimeSpan MaximumManagedTurnPause = TimeSpan.FromMilliseconds(5_500);
    private static readonly TimeSpan MinimumManagedWagerPause = TimeSpan.FromMilliseconds(700);
    private static readonly TimeSpan MaximumManagedWagerPause = TimeSpan.FromMilliseconds(1_800);
    private readonly Func<IReadOnlyList<string>> createDeck = deckFactory ?? ThreeCardPokerRules.CreateShuffledDeck;
    private readonly Func<ulong> createSeed = seedFactory ??
        (() => BitConverter.ToUInt64(RandomNumberGenerator.GetBytes(sizeof(ulong))));
    private readonly ThreeCardPokerManagedPlayerSupply managedPlayers = managedPlayerSupply ?? new(generateWhenEmpty: true);
    private readonly IMultiplayerMatchmaker matchmaking =
        multiplayerMatchmaker ?? new MultiplayerMatchmaker();
    private readonly IManagedTablePopulationDirector population =
        managedPopulationDirector ?? new ManagedTablePopulationDirector();

    public ThreeCardPokerTableCoordinatorResult Get(
        ThreeCardPokerTableLobbyState state,
        IDictionary<string, long> balances,
        string userId,
        DateTime nowUtc)
    {
        var journal = new ThreeCardPokerTableJournal();
        Advance(state, balances, journal, nowUtc);
        return Result(state, balances, userId, nowUtc, journal);
    }

    public ThreeCardPokerTableCoordinatorResult Join(
        ThreeCardPokerTableLobbyState state,
        IDictionary<string, long> balances,
        string userId,
        string displayName,
        int expectedVersion,
        string idempotencyKey,
        DateTime nowUtc)
    {
        var journal = new ThreeCardPokerTableJournal();
        Advance(state, balances, journal, nowUtc);
        var ticketId = ThreeCardPokerTableIds.Hash($"{userId}\n{idempotencyKey}");
        var detail = expectedVersion.ToString(CultureInfo.InvariantCulture);
        if (Replay(state, userId, idempotencyKey, "join", ticketId, detail))
            return Result(state, balances, userId, nowUtc, journal);
        var current = ThreeCardPokerTableProjection.Session(state, userId, nowUtc);
        if (current.Kind != ThreeCardPokerTableSessionKinds.Idle || current.Version != expectedVersion)
            throw new ThreeCardPokerTableConflictException("The Three Card Poker table session changed. Reconnect before joining.");
        var target = matchmaking.FindLiveTable(
            state.Tables.Values,
            table => table.Phase != ThreeCardPokerTablePhases.Closed &&
                table.Players.Count(player => !ThreeCardPokerManagedSeat.IsManaged(player)) < ThreeCardPokerManagedTablePolicy.MaximumOccupiedSeats &&
                (table.Players.Count < ThreeCardPokerTableEngine.Capacity || table.Players.Any(ThreeCardPokerManagedSeat.IsManaged)),
            table => table.CreatedAtUtc,
            table => table.TableId);
        var ticket = new ThreeCardPokerTableTicket(
            ticketId,
            userId,
            $"seat_{Guid.NewGuid():N}",
            displayName.Trim(),
            target?.TableId,
            target?.RoundNumber ?? 0,
            "queued",
            1,
            nowUtc,
            nowUtc.Add(ThreeCardPokerTableEngine.HumanGrace));
        state.Tickets.Add(ticket);
        state.Sessions[userId] = new(ThreeCardPokerTableSessionKinds.Queue, ticketId, null);
        state.Guards[GuardKey(userId, idempotencyKey)] = new("join", ticketId, detail, nowUtc);
        if (target?.Phase == ThreeCardPokerTablePhases.Betting)
        {
            AdmitQueuedHumansAtBoundary(state, target, journal);
            target.UpdatedAtUtc = nowUtc;
            StartIfReady(target, nowUtc);
        }
        else if (target is not null && target.Players.Count < ThreeCardPokerTableEngine.Capacity)
        {
            var seat = NextOpenSeatAfterPlayers(target);
            var joining = Human(ticket, seat, target.RoundNumber);
            joining.Status = "joining-next-round";
            target.Players.Add(joining);
            MarkTicketMatched(state, ticket);
            state.Sessions[userId] = new(ThreeCardPokerTableSessionKinds.Table, null, target.TableId);
            ScheduleGracefulManagedDeparture(target);
            target.UpdatedAtUtc = nowUtc;
        }
        else if (target is not null)
        {
            ScheduleGracefulManagedDeparture(target);
        }
        Advance(state, balances, journal, nowUtc);
        return Result(state, balances, userId, nowUtc, journal);
    }

    public ThreeCardPokerTableCoordinatorResult Cancel(
        ThreeCardPokerTableLobbyState state,
        IDictionary<string, long> balances,
        string userId,
        string ticketId,
        int expectedVersion,
        string idempotencyKey,
        DateTime nowUtc)
    {
        var journal = new ThreeCardPokerTableJournal();
        Advance(state, balances, journal, nowUtc);
        var detail = expectedVersion.ToString(CultureInfo.InvariantCulture);
        if (Replay(state, userId, idempotencyKey, "cancel", ticketId, detail))
            return Result(state, balances, userId, nowUtc, journal);
        var index = state.Tickets.FindIndex(ticket => ticket.TicketId == ticketId && ticket.UserId == userId);
        if (index < 0) throw new ThreeCardPokerTableNotFoundException("The Three Card Poker table queue ticket was not found.");
        var ticket = state.Tickets[index];
        if (ticket.Status != "queued" || ticket.Version != expectedVersion ||
            !state.Sessions.TryGetValue(userId, out var session) || session.TicketId != ticketId)
            throw new ThreeCardPokerTableConflictException("This Three Card Poker queue ticket changed or was already matched.");
        state.Tickets[index] = ticket with { Status = "cancelled", Version = checked(ticket.Version + 1) };
        state.Sessions[userId] = new(ThreeCardPokerTableSessionKinds.Idle, null, null);
        state.Guards[GuardKey(userId, idempotencyKey)] = new("cancel", ticketId, detail, nowUtc);
        return Result(state, balances, userId, nowUtc, journal);
    }

    public ThreeCardPokerTableCoordinatorResult Wager(
        ThreeCardPokerTableLobbyState state,
        IDictionary<string, long> balances,
        string userId,
        string tableId,
        long wagerCents,
        long pairPlusCents,
        int expectedVersion,
        string idempotencyKey,
        DateTime nowUtc)
    {
        _ = ThreeCardPokerMoney.ToWagerCents(ThreeCardPokerMoney.ToRand(wagerCents));
        if (pairPlusCents != 0) _ = ThreeCardPokerMoney.ToWagerCents(ThreeCardPokerMoney.ToRand(pairPlusCents));
        var journal = new ThreeCardPokerTableJournal();
        Advance(state, balances, journal, nowUtc);
        var detail = $"{wagerCents}:{pairPlusCents}:{expectedVersion}";
        if (Replay(state, userId, idempotencyKey, "wager", tableId, detail))
            return Result(state, balances, userId, nowUtc, journal);
        var table = OwnedTable(state, userId, tableId);
        if (table.Phase != ThreeCardPokerTablePhases.Betting ||
            table.Transition != "human-wager" ||
            table.ActiveSeat != table.Players.Single(value => value.ActorId == userId).Seat ||
            table.Version != expectedVersion)
            throw new ThreeCardPokerTableConflictException("The Three Card Poker table changed or is not accepting its next wager.");
        var player = table.Players.Single(value => value.ActorId == userId);
        var priorWager = checked(player.NextWagerCents + player.NextPairPlusCents);
        var difference = checked(wagerCents + pairPlusCents - priorWager);
        if (difference > 0) Debit(balances, userId, difference);
        else if (difference < 0) Credit(balances, userId, checked(-difference));
        player.NextWagerCents = wagerCents;
        player.NextPairPlusCents = pairPlusCents;
        player.ConsecutiveMissedRounds = 0;
        player.Status = "ready";
        table.ActiveSeat = null;
        table.PendingSeat = null;
        table.WagerDeadlineAtUtc = null;
        table.Transition = null;
        table.NextTransitionAtUtc = null;
        table.Version = checked(table.Version + 1);
        table.UpdatedAtUtc = nowUtc;
        state.Guards[GuardKey(userId, idempotencyKey)] = new("wager", tableId, detail, nowUtc);
        var roundReference = $"{tableId}-round-{table.RoundNumber + 1}";
        if (difference != 0)
        {
            journal.Ledger.Add(new(
                $"three-card-poker-table-wager-{ThreeCardPokerTableIds.Hash($"{userId}\n{idempotencyKey}")}",
                userId,
                checked(-difference),
                balances[userId],
                priorWager == 0 ? "three-card-poker-table-wager" : "three-card-poker-table-wager-adjustment",
                roundReference,
                nowUtc));
        }
        StartIfReady(table, nowUtc);
        return Result(state, balances, userId, nowUtc, journal);
    }

    public ThreeCardPokerTableCoordinatorResult SitOut(
        ThreeCardPokerTableLobbyState state,
        IDictionary<string, long> balances,
        string userId,
        string tableId,
        int expectedVersion,
        string idempotencyKey,
        DateTime nowUtc)
    {
        var journal = new ThreeCardPokerTableJournal();
        Advance(state, balances, journal, nowUtc);
        var detail = expectedVersion.ToString(System.Globalization.CultureInfo.InvariantCulture);
        if (Replay(state, userId, idempotencyKey, "sit-out", tableId, detail))
            return Result(state, balances, userId, nowUtc, journal);
        var table = OwnedTable(state, userId, tableId);
        var player = table.Players.Single(value => value.ActorId == userId);
        if (table.Phase != ThreeCardPokerTablePhases.Betting ||
            table.Transition != "human-wager" ||
            table.ActiveSeat != player.Seat ||
            table.Version != expectedVersion)
            throw new ThreeCardPokerTableConflictException("The Three Card Poker table changed or is not accepting a sit-out request.");

        player.NextWagerCents = 0;
        player.ConsecutiveMissedRounds = 0;
        player.Status = "sitting-out";
        table.ActiveSeat = null;
        table.PendingSeat = null;
        table.WagerDeadlineAtUtc = null;
        table.Transition = null;
        table.NextTransitionAtUtc = null;
        table.Version = checked(table.Version + 1);
        table.UpdatedAtUtc = nowUtc;
        state.Guards[GuardKey(userId, idempotencyKey)] = new("sit-out", tableId, detail, nowUtc);
        StartIfReady(table, nowUtc);
        return Result(state, balances, userId, nowUtc, journal);
    }

    public ThreeCardPokerTableCoordinatorResult Action(
        ThreeCardPokerTableLobbyState state,
        IDictionary<string, long> balances,
        string userId,
        string tableId,
        string action,
        int expectedVersion,
        string idempotencyKey,
        DateTime nowUtc)
    {
        var journal = new ThreeCardPokerTableJournal();
        Advance(state, balances, journal, nowUtc);
        action = action.Trim().ToLowerInvariant();
        var detail = $"{action}:{expectedVersion}";
        if (Replay(state, userId, idempotencyKey, "action", tableId, detail))
            return Result(state, balances, userId, nowUtc, journal);
        var table = OwnedTable(state, userId, tableId);
        if (table.Version != expectedVersion)
            throw new ThreeCardPokerTableConflictException("The Three Card Poker table changed. Reconnect before acting.");
        var player = table.Players.Single(value => value.ActorId == userId);
        if (!ThreeCardPokerTableEngine.LegalActions(table, player).Contains(action))
            throw new ThreeCardPokerTableIllegalActionException("That Three Card Poker action is not legal now.");
        var isPlayAction = table.Phase == ThreeCardPokerTablePhases.Active;
        var additionalWager = ThreeCardPokerTableEngine.AdditionalWagerFor(player, action);
        if (additionalWager > 0)
        {
            Debit(balances, userId, additionalWager);
            var wagerKind = "three-card-poker-table-play";
            journal.Ledger.Add(new(
                $"{wagerKind}-{ThreeCardPokerTableIds.Hash($"{userId}\n{idempotencyKey}")}",
                userId,
                -additionalWager,
                balances[userId],
                wagerKind,
                $"{tableId}-round-{table.RoundNumber}",
                nowUtc));
        }
        ThreeCardPokerTableEngine.ApplyAction(table, userId, action, nowUtc);
        if (isPlayAction)
        {
            player.ConsecutiveMissedActionRounds = 0;
            player.LastMissedActionRound = 0;
        }
        state.Guards[GuardKey(userId, idempotencyKey)] = new("action", tableId, detail, nowUtc);
        return Result(state, balances, userId, nowUtc, journal);
    }

    public ThreeCardPokerTableCoordinatorResult Leave(
        ThreeCardPokerTableLobbyState state,
        IDictionary<string, long> balances,
        string userId,
        string tableId,
        int expectedVersion,
        string idempotencyKey,
        DateTime nowUtc)
    {
        var journal = new ThreeCardPokerTableJournal();
        Advance(state, balances, journal, nowUtc);
        var detail = expectedVersion.ToString(CultureInfo.InvariantCulture);
        if (Replay(state, userId, idempotencyKey, "leave", tableId, detail))
            return Result(state, balances, userId, nowUtc, journal);
        var table = OwnedTable(state, userId, tableId);
        if (table.Version != expectedVersion)
            throw new ThreeCardPokerTableConflictException("The Three Card Poker table changed before the seat could be left.");
        var player = table.Players.Single(value => value.ActorId == userId);
        if (table.Phase == ThreeCardPokerTablePhases.Betting)
        {
            if (player.NextWagerCents > 0)
            {
                Credit(balances, userId, checked(player.NextWagerCents + player.NextPairPlusCents));
                journal.Ledger.Add(new(
                    $"three-card-poker-table-leave-{ThreeCardPokerTableIds.Hash($"{userId}\n{idempotencyKey}")}",
                    userId,
                    checked(player.NextWagerCents + player.NextPairPlusCents),
                    balances[userId],
                    "three-card-poker-table-wager-release",
                    tableId,
                    nowUtc));
            }
            player.NextWagerCents = 0;
            player.NextPairPlusCents = 0;
            table.Players.Remove(player);
            table.Version = checked(table.Version + 1);
            table.UpdatedAtUtc = nowUtc;
            if (ApplyPopulationDecision(state, table, journal, nowUtc))
                StartIfReady(table, nowUtc);
        }
        else
        {
            ThreeCardPokerTableEngine.MarkLeaving(table, player, nowUtc);
        }
        state.Sessions[userId] = new(ThreeCardPokerTableSessionKinds.Idle, null, null);
        state.Guards[GuardKey(userId, idempotencyKey)] = new("leave", tableId, detail, nowUtc);
        return Result(state, balances, userId, nowUtc, journal);
    }

    public ThreeCardPokerTableJournal Sweep(
        ThreeCardPokerTableLobbyState state,
        IDictionary<string, long> balances,
        DateTime nowUtc)
    {
        var journal = new ThreeCardPokerTableJournal();
        Advance(state, balances, journal, nowUtc);
        return journal;
    }

    private void Advance(
        ThreeCardPokerTableLobbyState state,
        IDictionary<string, long> balances,
        ThreeCardPokerTableJournal journal,
        DateTime nowUtc)
    {
        foreach (var table in state.Tables.Values.OrderBy(value => value.CreatedAtUtc).ToArray())
        {
            if (table.Phase is ThreeCardPokerTablePhases.Dealing or ThreeCardPokerTablePhases.Active or ThreeCardPokerTablePhases.Dealer)
                ThreeCardPokerTableEngine.AdvanceAutomatedTurns(table, nowUtc);
            AdvanceManagedPlayerTurns(table, nowUtc);
            if (table.Phase == ThreeCardPokerTablePhases.Settlement) CompleteSettlement(state, balances, journal, table, nowUtc);
            if (table.Phase == ThreeCardPokerTablePhases.Betting && table.Transition == "next-round-countdown" &&
                table.NextTransitionAtUtc is { } countdownEndsAt && nowUtc >= countdownEndsAt)
            {
                table.Transition = null;
                table.NextTransitionAtUtc = null;
                table.UpdatedAtUtc = countdownEndsAt;
                StartIfReady(table, countdownEndsAt);
            }
            AdvanceManagedWagerTurns(table, nowUtc);
            if (table.Phase == ThreeCardPokerTablePhases.Betting && table.Transition == "wager-lock" &&
                table.NextTransitionAtUtc is { } readyAt && nowUtc >= readyAt)
            {
                table.Transition = null;
                table.NextTransitionAtUtc = null;
                StartIfReady(table, nowUtc, adjustmentElapsed: true);
            }
            if (table.Phase == ThreeCardPokerTablePhases.Betting &&
                table.WagerDeadlineAtUtc is { } deadline && nowUtc >= deadline)
            {
                ApplyWagerDeadline(state, table, journal, nowUtc);
            }
        }
        StartNewTables(state, balances, journal, nowUtc);
    }

    private static void AdvanceManagedPlayerTurns(ThreeCardPokerTableState table, DateTime nowUtc)
    {
        if (table.Phase is not (ThreeCardPokerTablePhases.Active) ||
            table.ActiveSeat is not { } activeSeat)
            return;
        var player = table.Players.SingleOrDefault(value => value.Seat == activeSeat);
        if (player is null || table.Transition == "action-settle") return;

        if (ThreeCardPokerManagedSeat.IsManaged(player))
        {
            // Migrate any live table that still carries the former shared, publicly visible pause.
            if (table.Transition == "turn-pause")
            {
                table.Transition = null;
                table.NextTransitionAtUtc = null;
                table.ActionDeadlineAtUtc = null;
            }
            if (table.Transition is not null) return;

            var decisionAt = ThreeCardPokerManagedSeat.ActionReadyAt(player);
            if (decisionAt is null)
            {
                ThreeCardPokerManagedSeat.SetActionReadyAt(player, nowUtc.Add(ManagedPause(
                    MinimumManagedTurnPause,
                    MaximumManagedTurnPause)));
                // A managed player's internal thinking clock is never projected as a table timer.
                table.ActionDeadlineAtUtc = null;
                table.Version = checked(table.Version + 1);
                table.UpdatedAtUtc = nowUtc;
                return;
            }
            table.ActionDeadlineAtUtc = null;
            if (nowUtc < decisionAt) return;
            ThreeCardPokerManagedSeat.ClearActionReadyAt(player);
            var available = ThreeCardPokerTableEngine.LegalActions(table, player);
            var action = ThreeCardPokerManagedActionPolicy.Choose(table, player, available);
            ThreeCardPokerTableEngine.ApplyAction(table, player.ActorId, action, nowUtc);
            return;
        }

        if (table.Transition is not null ||
            table.ActionDeadlineAtUtc is not { } deadline || nowUtc < deadline)
            return;
        if (player.LastMissedActionRound != table.RoundNumber)
        {
            player.ConsecutiveMissedActionRounds = player.LastMissedActionRound == table.RoundNumber - 1
                ? checked(player.ConsecutiveMissedActionRounds + 1)
                : 1;
            player.LastMissedActionRound = table.RoundNumber;
        }
        var releaseAfterRound = player.ConsecutiveMissedActionRounds >= 2;
        ThreeCardPokerTableEngine.ApplyAction(table, player.ActorId, ThreeCardPokerActions.Fold, nowUtc);
        if (releaseAfterRound) player.LeavingAfterRound = true;
    }

    private static TimeSpan ManagedPause(TimeSpan minimum, TimeSpan maximum)
    {
        var range = checked((int)(maximum - minimum).TotalMilliseconds + 1);
        return minimum.Add(TimeSpan.FromMilliseconds(RandomNumberGenerator.GetInt32(range)));
    }

    private void StartNewTables(
        ThreeCardPokerTableLobbyState state,
        IDictionary<string, long> balances,
        ThreeCardPokerTableJournal journal,
        DateTime nowUtc)
    {
        while (true)
        {
            var selected = matchmaking.PlanQueue(
                state.Tickets,
                new MultiplayerQueueRules(
                    1,
                    ThreeCardPokerManagedTablePolicy.MaximumOccupiedSeats),
                nowUtc,
                ticket => ticket.Status == "queued" && ticket.TargetTableId is null,
                ticket => ticket.JoinedAtUtc,
                ticket => ticket.GraceEndsAtUtc,
                ticket => ticket.TicketId).HumanTickets.ToArray();
            if (selected.Length == 0) return;
            var tableId = ThreeCardPokerTableIds.Hash(string.Join("\n", selected.Select(ticket => ticket.TicketId)));
            var initialSeed = createSeed();
            var startingSeats = RandomizedInitialSeats(selected.Length, initialSeed);
            var players = selected.Select((ticket, index) => Human(ticket, startingSeats[index], 0)).ToList();
            var table = new ThreeCardPokerTableState
            {
                TableId = tableId,
                Players = players,
                CreatedAtUtc = nowUtc,
                UpdatedAtUtc = nowUtc,
                Phase = ThreeCardPokerTablePhases.Betting,
                Version = 1,
                RoundNumber = 0,
                WagerDeadlineAtUtc = null
            };
            ApplyPopulationDecision(state, table, journal, nowUtc);
            state.Tables[tableId] = table;
            foreach (var ticket in selected)
            {
                MarkTicketMatched(state, ticket);
                state.Sessions[ticket.UserId] = new(ThreeCardPokerTableSessionKinds.Table, null, tableId);
            }
            StartIfReady(table, nowUtc);
        }
    }

    private void ApplyWagerDeadline(
        ThreeCardPokerTableLobbyState state,
        ThreeCardPokerTableState table,
        ThreeCardPokerTableJournal journal,
        DateTime nowUtc)
    {
        if (table.Transition is not ("human-wager" or "managed-wager")) return;
        table.Version = checked(table.Version + 1);
        table.UpdatedAtUtc = nowUtc;
        var player = table.ActiveSeat is { } activeSeat
            ? table.Players.SingleOrDefault(value => value.Seat == activeSeat)
            : null;
        if (player is { NextWagerCents: 0 } && !ThreeCardPokerManagedSeat.IsManaged(player))
        {
            player.ConsecutiveMissedRounds = checked(player.ConsecutiveMissedRounds + 1);
            player.Status = "sitting-out";
            if (player.ConsecutiveMissedRounds >= 2)
            {
                table.Players.Remove(player);
                state.Sessions[player.ActorId] = new(ThreeCardPokerTableSessionKinds.Idle, null, null);
            }
        }
        table.ActiveSeat = null;
        table.PendingSeat = null;
        table.WagerDeadlineAtUtc = null;
        table.Transition = null;
        table.NextTransitionAtUtc = null;
        if (ApplyPopulationDecision(state, table, journal, nowUtc))
            StartIfReady(table, nowUtc);
    }

    private void CompleteSettlement(
        ThreeCardPokerTableLobbyState state,
        IDictionary<string, long> balances,
        ThreeCardPokerTableJournal journal,
        ThreeCardPokerTableState table,
        DateTime nowUtc)
    {
        if (table.RoundAccountingSettled)
        {
            if (table.Transition == "settlement-display" &&
                table.NextTransitionAtUtc is { } displayEndsAt && nowUtc >= displayEndsAt)
                BeginNextRoundCountdown(state, journal, table, displayEndsAt);
            return;
        }
        var roundId = $"{table.TableId}-round-{table.RoundNumber}";
        long humanWagers = 0;
        long humanPayouts = 0;
        var humanCount = 0;
        var participants = table.Players.Where(player => player.TotalWagerCents > 0).ToArray();
        foreach (var player in participants)
        {
            var committed = ThreeCardPokerTableEngine.TotalCommitted(player);
            player.SessionWagerCents = checked(player.SessionWagerCents + committed);
            player.SessionPayoutCents = checked(player.SessionPayoutCents + player.PayoutCents);
            player.SessionRoundsPlayed = checked(player.SessionRoundsPlayed + 1);
            if (ThreeCardPokerManagedSeat.IsManaged(player)) UpdateManagedWagerHabit(player, committed);
            if (!ThreeCardPokerManagedSeat.IsManaged(player))
            {
                humanCount++;
                humanWagers = checked(humanWagers + committed);
                humanPayouts = checked(humanPayouts + player.PayoutCents);
                if (player.PayoutCents > 0)
                {
                    Credit(balances, player.ActorId, player.PayoutCents);
                    journal.Ledger.Add(new(
                        $"three-card-poker-table-payout-{roundId}-{ThreeCardPokerTableIds.Hash(player.ActorId)}",
                        player.ActorId,
                        player.PayoutCents,
                        balances[player.ActorId],
                        "three-card-poker-table-payout",
                        roundId,
                        nowUtc));
                }
            }
            journal.Results.Add(new(
                ThreeCardPokerTableIds.Hash($"{roundId}\n{player.ActorId}"),
                player.ActorId,
                table.TableId,
                table.RoundNumber,
                committed,
                player.PayoutCents,
                ThreeCardPokerManagedSeat.IsManaged(player),
                nowUtc));
        }
        if (humanCount > 0)
        {
            journal.Revenue.Add(new(
                roundId,
                table.TableId,
                table.RoundNumber,
                humanWagers,
                humanPayouts,
                humanCount,
                nowUtc));
        }
        table.RoundAccountingSettled = true;
        table.ActiveSeat = null;
        table.PendingSeat = null;
        table.ActionDeadlineAtUtc = null;
        table.WagerDeadlineAtUtc = null;
        table.Transition = "settlement-display";
        table.NextTransitionAtUtc = nowUtc.Add(ThreeCardPokerTableEngine.SettlementDisplayDuration);
        table.Version = checked(table.Version + 1);
        table.UpdatedAtUtc = nowUtc;
    }

    private void BeginNextRoundCountdown(
        ThreeCardPokerTableLobbyState state,
        ThreeCardPokerTableJournal journal,
        ThreeCardPokerTableState table,
        DateTime nowUtc)
    {
        foreach (var player in table.Players.Where(player => !ThreeCardPokerManagedSeat.IsManaged(player) && player.LeavingAfterRound).ToArray())
        {
            table.Players.Remove(player);
            state.Sessions[player.ActorId] = new(ThreeCardPokerTableSessionKinds.Idle, null, null);
        }
        foreach (var player in table.Players.Where(player => ThreeCardPokerManagedSeat.IsManaged(player) &&
                     (ThreeCardPokerManagedSeat.DepartureRound(player) is { } departureRound && departureRound <= table.RoundNumber ||
                      nowUtc - player.SessionStartedAtUtc >= TimeSpan.FromHours(1))).ToArray())
        {
            table.Players.Remove(player);
            ReleaseManagedPlayer(table, player, journal);
        }
        table.Phase = ThreeCardPokerTablePhases.Betting;
        table.ActiveSeat = null;
        table.PendingSeat = null;
        table.ActionDeadlineAtUtc = null;
        table.Transition = "next-round-countdown";
        table.NextTransitionAtUtc = nowUtc.Add(ThreeCardPokerTableEngine.NextRoundCountdownDuration);
        table.WagerDeadlineAtUtc = null;
        AdmitQueuedHumansAtBoundary(state, table, journal);
        if (!ApplyPopulationDecision(state, table, journal, nowUtc)) return;
        ThreeCardPokerTableEngine.PrepareForBetting(table);
        foreach (var player in table.Players)
        {
            player.NextWagerCents = 0;
            player.NextPairPlusCents = 0;
            player.Status = ThreeCardPokerManagedSeat.IsManaged(player) ? "waiting-to-wager" : "awaiting-wager";
        }
        table.Version = checked(table.Version + 1);
        table.UpdatedAtUtc = nowUtc;
    }

    private void AdmitQueuedHumansAtBoundary(
        ThreeCardPokerTableLobbyState state,
        ThreeCardPokerTableState table,
        ThreeCardPokerTableJournal journal)
    {
        var waiting = state.Tickets
            .Where(ticket => ticket.Status == "queued" && ticket.TargetTableId == table.TableId &&
                ticket.EligibleAfterRound <= table.RoundNumber)
            .OrderBy(ticket => ticket.JoinedAtUtc)
            .ThenBy(ticket => ticket.TicketId, StringComparer.Ordinal)
            .ToArray();
        foreach (var ticket in waiting)
        {
            if (table.Players.Count < ThreeCardPokerTableEngine.Capacity)
            {
                var emptySeat = NextOpenSeatAfterPlayers(table);
                table.Players.Add(Human(ticket, emptySeat, table.RoundNumber));
                if (table.Players.Count == ThreeCardPokerTableEngine.Capacity)
                    ScheduleGracefulManagedDeparture(table);
            }
            else
            {
                ScheduleGracefulManagedDeparture(table);
                break;
            }
            MarkTicketMatched(state, ticket);
            state.Sessions[ticket.UserId] = new(ThreeCardPokerTableSessionKinds.Table, null, table.TableId);
        }
    }

    private void StartIfReady(
        ThreeCardPokerTableState table,
        DateTime nowUtc,
        bool adjustmentElapsed = false)
    {
        if (table.Phase != ThreeCardPokerTablePhases.Betting) return;
        if (table.Transition == "next-round-countdown") return;
        if (table.Transition == "wager-lock")
        {
            if (adjustmentElapsed)
                ThreeCardPokerTableEngine.BeginDeal(table, createDeck(), createSeed(), nowUtc);
            return;
        }
        if (table.Transition is "human-wager" or "managed-wager") return;
        if (ScheduleNextWagerTurn(table, nowUtc)) return;
        if (!table.Players.Any(player => player.NextWagerCents > 0))
        {
            if (table.Players.Count > 0 && table.Players.All(player => player.Status == "sitting-out"))
            {
                ThreeCardPokerTableEngine.PrepareForBetting(table);
                foreach (var player in table.Players)
                    player.Status = ThreeCardPokerManagedSeat.IsManaged(player) ? "waiting-to-wager" : "awaiting-wager";
                table.Transition = "next-round-countdown";
                table.NextTransitionAtUtc = nowUtc.Add(ThreeCardPokerTableEngine.NextRoundCountdownDuration);
                table.Version = checked(table.Version + 1);
                table.UpdatedAtUtc = nowUtc;
            }
            return;
        }
        if (!adjustmentElapsed)
        {
            table.ActiveSeat = null;
            table.PendingSeat = null;
            table.WagerDeadlineAtUtc = null;
            table.Transition = "wager-lock";
            table.NextTransitionAtUtc = nowUtc.Add(ThreeCardPokerTableEngine.WagerAdjustmentDuration);
            table.UpdatedAtUtc = nowUtc;
            return;
        }
        ThreeCardPokerTableEngine.BeginDeal(table, createDeck(), createSeed(), nowUtc);
    }

    private bool ApplyPopulationDecision(
        ThreeCardPokerTableLobbyState state,
        ThreeCardPokerTableState table,
        ThreeCardPokerTableJournal journal,
        DateTime nowUtc)
    {
        var decision = population.Observe(PopulationObservation(table));
        if (!decision.KeepTableOpen)
        {
            CloseTable(state, table, journal);
            return false;
        }
        for (var index = 0; index < decision.ManagedArrivals; index++)
            AddManagedPlayer(table, nowUtc);
        return true;
    }

    private static ManagedTablePopulationObservation PopulationObservation(ThreeCardPokerTableState table) => new(
        ManagedPlayerGames.ThreeCardPoker,
        table.TableId,
        table.RoundNumber,
        table.Players.Count(player => !ThreeCardPokerManagedSeat.IsManaged(player)),
        table.Players.Count(ThreeCardPokerManagedSeat.IsManaged),
        ThreeCardPokerManagedTablePolicy.MinimumStartOccupancy,
        Math.Max(ThreeCardPokerManagedTablePolicy.MaximumOccupiedSeats, table.Players.Count),
        table.Phase != ThreeCardPokerTablePhases.Closed);

    private void AddManagedPlayer(ThreeCardPokerTableState table, DateTime nowUtc)
    {
        if (table.Players.Count >= ThreeCardPokerManagedTablePolicy.MaximumOccupiedSeats) return;
        var profile = managedPlayers.Take(
            table.TableId,
            table.Players.Where(ThreeCardPokerManagedSeat.IsManaged).Select(player => player.ActorId).ToArray(),
            nowUtc);
        var player = new ThreeCardPokerTablePlayer
        {
            ActorId = profile.UserId,
            PublicSeatId = $"seat_{Guid.NewGuid():N}",
            DisplayName = profile.PlayerName,
            Seat = ClosestOpenSeatOnDealerLeft(table),
            SessionId = $"managed-session-{Guid.NewGuid():N}",
            SessionStartedAtUtc = nowUtc,
            JoinedRound = table.RoundNumber,
            NextWagerCents = 0,
            HostMetadata = ThreeCardPokerManagedSeat.Create(
                profile.SkillLevel,
                InitialManagedWager(profile.UserId),
                ManagedWagerChangeChance(profile.UserId)),
            Status = "waiting-to-wager"
        };
        ThreeCardPokerManagedSeat.SetDepartureRound(
            player,
            ManagedPlayerTableStayPolicy.DepartureRound(
                ManagedPlayerGames.ThreeCardPoker,
                table.TableId,
                profile.UserId,
                table.RoundNumber));
        table.Players.Add(player);
    }

    private static void ReleaseManagedPlayer(
        ThreeCardPokerTableState table,
        ThreeCardPokerTablePlayer player,
        ThreeCardPokerTableJournal journal)
    {
        if (ThreeCardPokerManagedSeat.IsManaged(player))
            journal.ManagedPlayerReleases.Add(new(player.ActorId, table.TableId));
    }

    internal static IReadOnlyList<int> RandomizedInitialSeats(int occupiedSeats, ulong seed)
    {
        if (occupiedSeats is < 1 or > ThreeCardPokerTableEngine.Capacity)
            throw new ArgumentOutOfRangeException(nameof(occupiedSeats));

        var offset = checked((int)(((seed % (ulong)ThreeCardPokerTableEngine.Capacity) * 2UL + 4UL)
            % (ulong)ThreeCardPokerTableEngine.Capacity));
        var direction = (seed & 2UL) == 0 ? 1 : ThreeCardPokerTableEngine.Capacity - 1;
        return Enumerable.Range(0, ThreeCardPokerTableEngine.Capacity)
            .Select(index => (offset + index * direction) % ThreeCardPokerTableEngine.Capacity)
            .Take(occupiedSeats)
            .ToArray();
    }

    private static int ClosestOpenSeatOnDealerLeft(ThreeCardPokerTableState table) =>
        Enumerable.Range(0, ThreeCardPokerTableEngine.Capacity)
            .First(seat => table.Players.All(player => player.Seat != seat));

    private static int NextOpenSeatAfterPlayers(ThreeCardPokerTableState table)
    {
        var start = table.Players.Count == 0
            ? 0
            : (table.Players.OrderBy(player => player.JoinedRound).ThenBy(player => player.Seat).Last().Seat + 1)
              % ThreeCardPokerTableEngine.Capacity;
        return Enumerable.Range(0, ThreeCardPokerTableEngine.Capacity)
            .Select(offset => (start + offset) % ThreeCardPokerTableEngine.Capacity)
            .First(seat => table.Players.All(player => player.Seat != seat));
    }

    private static void ScheduleGracefulManagedDeparture(ThreeCardPokerTableState table)
    {
        var departing = table.Players
            .Where(ThreeCardPokerManagedSeat.IsManaged)
            .OrderBy(player => player.SessionStartedAtUtc)
            .ThenBy(player => player.Seat)
            .FirstOrDefault();
        if (departing is null) return;

        var gracefulDepartureRound = checked(table.RoundNumber + RandomNumberGenerator.GetInt32(1, 4));
        var scheduledDepartureRound = ThreeCardPokerManagedSeat.DepartureRound(departing);
        if (scheduledDepartureRound is null || gracefulDepartureRound < scheduledDepartureRound)
            ThreeCardPokerManagedSeat.SetDepartureRound(departing, gracefulDepartureRound);
    }

    private static ThreeCardPokerTablePlayer Human(ThreeCardPokerTableTicket ticket, int seat, int joinedRound) => new()
    {
        ActorId = ticket.UserId,
        PublicSeatId = ticket.PublicSeatId,
        DisplayName = ticket.DisplayName,
        Seat = seat,
        SessionId = ticket.TicketId,
        SessionStartedAtUtc = ticket.JoinedAtUtc,
        JoinedRound = joinedRound,
        NextWagerCents = 0,
        Status = "awaiting-wager"
    };

    private void CloseTable(
        ThreeCardPokerTableLobbyState state,
        ThreeCardPokerTableState table,
        ThreeCardPokerTableJournal journal)
    {
        population.Close(ManagedPlayerGames.ThreeCardPoker, table.TableId);
        table.Phase = ThreeCardPokerTablePhases.Closed;
        table.ActiveSeat = null;
        table.PendingSeat = null;
        table.ActionDeadlineAtUtc = null;
        table.WagerDeadlineAtUtc = null;
        table.Transition = null;
        table.NextTransitionAtUtc = null;
        foreach (var player in table.Players.Where(ThreeCardPokerManagedSeat.IsManaged))
            ReleaseManagedPlayer(table, player, journal);
        foreach (var ticket in state.Tickets.Where(ticket => ticket.Status == "queued" && ticket.TargetTableId == table.TableId).ToArray())
            ReplaceTicket(state, ticket with { TargetTableId = null, EligibleAfterRound = 0 });
    }

    private static void MarkTicketMatched(ThreeCardPokerTableLobbyState state, ThreeCardPokerTableTicket ticket) =>
        ReplaceTicket(state, ticket with { Status = "matched", Version = checked(ticket.Version + 1) });

    private static void ReplaceTicket(ThreeCardPokerTableLobbyState state, ThreeCardPokerTableTicket replacement)
    {
        var index = state.Tickets.FindIndex(ticket => ticket.TicketId == replacement.TicketId);
        if (index < 0) throw new InvalidOperationException("A Three Card Poker queue ticket disappeared during a transition.");
        state.Tickets[index] = replacement;
    }

    private static ThreeCardPokerTableState OwnedTable(ThreeCardPokerTableLobbyState state, string userId, string tableId)
    {
        if (!state.Tables.TryGetValue(tableId, out var table) ||
            table.Players.All(player => player.ActorId != userId))
            throw new ThreeCardPokerTableNotFoundException("The Three Card Poker table was not found.");
        return table;
    }

    private static bool Replay(
        ThreeCardPokerTableLobbyState state,
        string userId,
        string idempotencyKey,
        string operation,
        string target,
        string detail)
    {
        if (!state.Guards.TryGetValue(GuardKey(userId, idempotencyKey), out var guard)) return false;
        if (guard.Operation != operation || guard.Target != target || guard.Detail != detail)
            throw new ThreeCardPokerTableConflictException(
                "This Idempotency-Key was already used for a different Three Card Poker table request.");
        return true;
    }

    private static ThreeCardPokerTableCoordinatorResult Result(
        ThreeCardPokerTableLobbyState state,
        IDictionary<string, long> balances,
        string userId,
        DateTime nowUtc,
        ThreeCardPokerTableJournal journal) => new(
            new ThreeCardPokerTableStoreResult(
                ThreeCardPokerTableProjection.Session(state, userId, nowUtc),
                Balance(balances, userId)),
            journal);

    private static void Debit(IDictionary<string, long> balances, string userId, long cents)
    {
        var available = Balance(balances, userId);
        if (available < cents) throw new ThreeCardPokerTableInsufficientCreditsException(available, cents);
        balances[userId] = checked(available - cents);
    }

    private static void Credit(IDictionary<string, long> balances, string userId, long cents) =>
        balances[userId] = checked(Balance(balances, userId) + cents);

    private static long Balance(IDictionary<string, long> balances, string userId) =>
        balances.TryGetValue(userId, out var value) ? value : 0;

    internal static long InitialManagedWager(string actorId)
    {
        var options = new[] { 50L, 100L, 150L, 200L, 250L, 300L, 400L, 500L, 750L, 1_000L, 1_500L };
        var hash = SHA256.HashData(System.Text.Encoding.UTF8.GetBytes($"three-card-poker-wager\n{actorId}"));
        return options[hash[0] % options.Length];
    }

    internal static int ManagedWagerChangeChance(string actorId)
    {
        var options = new[] { 600, 1_200, 2_200, 3_400 };
        var hash = SHA256.HashData(System.Text.Encoding.UTF8.GetBytes($"three-card-poker-tempo\n{actorId}"));
        return options[hash[0] % options.Length];
    }

    internal static void UpdateManagedWagerHabit(ThreeCardPokerTablePlayer player, long committed)
    {
        var net = checked(player.PayoutCents - committed);
        ThreeCardPokerManagedSeat.SetLastNet(player, net);
        if (net > 0)
        {
            ThreeCardPokerManagedSeat.SetConsecutiveWins(player, checked(ThreeCardPokerManagedSeat.ConsecutiveWins(player) + 1));
            ThreeCardPokerManagedSeat.SetConsecutiveLosses(player, 0);
        }
        else if (net < 0)
        {
            ThreeCardPokerManagedSeat.SetConsecutiveLosses(player, checked(ThreeCardPokerManagedSeat.ConsecutiveLosses(player) + 1));
            ThreeCardPokerManagedSeat.SetConsecutiveWins(player, 0);
        }
        else
        {
            ThreeCardPokerManagedSeat.SetConsecutiveWins(player, 0);
            ThreeCardPokerManagedSeat.SetConsecutiveLosses(player, 0);
        }

        var baseWager = ThreeCardPokerManagedSeat.BaseWager(player);
        var wager = baseWager > 0 ? baseWager : InitialManagedWager(player.ActorId);
        var increment = ThreeCardPokerMoney.WagerIncrementCents;
        var bigWin = net >= Math.Max(increment, checked(committed + committed / 2));
        if (bigWin)
        {
            wager = checked(wager + RandomNumberGenerator.GetInt32(1, 5) * increment);
        }
        else if (ThreeCardPokerManagedSeat.ConsecutiveLosses(player) >= 3)
        {
            wager = checked(wager - RandomNumberGenerator.GetInt32(1, 4) * increment);
        }
        else if (RandomNumberGenerator.GetInt32(0, 10_000) < ThreeCardPokerManagedSeat.ChangeChance(player))
        {
            var direction = ThreeCardPokerManagedSeat.ConsecutiveWins(player) > 0 ? 1 : ThreeCardPokerManagedSeat.ConsecutiveLosses(player) > 0 ? -1 : RandomNumberGenerator.GetInt32(0, 2) * 2 - 1;
            wager = checked(wager + direction * RandomNumberGenerator.GetInt32(1, 3) * increment);
        }
        ThreeCardPokerManagedSeat.SetBaseWager(player, Math.Clamp(wager, ThreeCardPokerMoney.MinimumWagerCents, ThreeCardPokerMoney.MaximumWagerCents));
    }

    private void AdvanceManagedWagerTurns(ThreeCardPokerTableState table, DateTime nowUtc)
    {
        if (table.Phase != ThreeCardPokerTablePhases.Betting ||
            table.Transition != "managed-wager")
            return;

        var player = table.PendingSeat is { } seat
            ? table.Players.SingleOrDefault(value => ThreeCardPokerManagedSeat.IsManaged(value) && value.Seat == seat)
            : null;
        if (player is null) return;
        var readyAt = ThreeCardPokerManagedSeat.WagerReadyAt(player);
        if (readyAt is null)
        {
            readyAt = nowUtc.Add(ManagedPause(MinimumManagedWagerPause, MaximumManagedWagerPause));
            ThreeCardPokerManagedSeat.SetWagerReadyAt(player, readyAt.Value);
            table.NextTransitionAtUtc = null;
            table.WagerDeadlineAtUtc = null;
            table.Version = checked(table.Version + 1);
            table.UpdatedAtUtc = nowUtc;
            return;
        }
        var effectiveReadyAt = readyAt.Value;
        if (nowUtc < effectiveReadyAt) return;
        if (player.NextWagerCents == 0)
        {
            var baseWager = ThreeCardPokerManagedSeat.BaseWager(player);
            player.NextWagerCents = baseWager > 0
                ? baseWager
                : InitialManagedWager(player.ActorId);
            player.Status = "ready";
        }
        ThreeCardPokerManagedSeat.ClearWagerReadyAt(player);
        table.PendingSeat = null;
        table.ActiveSeat = null;
        table.WagerDeadlineAtUtc = null;
        table.Transition = null;
        table.NextTransitionAtUtc = null;
        table.UpdatedAtUtc = effectiveReadyAt;
        StartIfReady(table, effectiveReadyAt);
    }

    private static bool ScheduleNextWagerTurn(ThreeCardPokerTableState table, DateTime nowUtc)
    {
        if (table.Phase != ThreeCardPokerTablePhases.Betting) return false;
        if (table.Transition is "managed-wager" or "human-wager") return true;
        if (table.Transition is not null) return false;
        var next = table.Players
            .Where(player => player.NextWagerCents == 0 && player.Status != "sitting-out")
            .OrderBy(player => player.Seat)
            .FirstOrDefault();
        if (next is null) return false;

        var managed = ThreeCardPokerManagedSeat.IsManaged(next);
        next.Status = managed ? "considering-wager" : "choosing-wager";
        table.ActiveSeat = next.Seat;
        table.PendingSeat = next.Seat;
        table.WagerDeadlineAtUtc = nowUtc.Add(ThreeCardPokerTableEngine.WagerDuration);
        table.Transition = managed ? "managed-wager" : "human-wager";
        table.NextTransitionAtUtc = null;
        if (managed)
        {
            ThreeCardPokerManagedSeat.SetWagerReadyAt(next, nowUtc.Add(ManagedPause(
                MinimumManagedWagerPause,
                MaximumManagedWagerPause)));
            table.WagerDeadlineAtUtc = null;
        }
        table.UpdatedAtUtc = nowUtc;
        return true;
    }

    private static string GuardKey(string userId, string idempotencyKey) =>
        ThreeCardPokerTableIds.Hash($"{userId}\n{idempotencyKey}");
}
