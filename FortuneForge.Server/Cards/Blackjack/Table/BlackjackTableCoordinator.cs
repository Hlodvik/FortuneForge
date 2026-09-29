using System.Globalization;
using System.Security.Cryptography;

namespace FortuneForge.Server.Cards.Blackjack.Table;

internal sealed record BlackjackTableCoordinatorResult(BlackjackTableStoreResult Store, BlackjackTableJournal Journal);

internal sealed class BlackjackTableCoordinator(
    Func<IReadOnlyList<string>>? deckFactory = null,
    Func<ulong>? seedFactory = null,
    BlackjackManagedPlayerSupply? managedPlayerSupply = null)
{
    private readonly Func<IReadOnlyList<string>> createDeck = deckFactory ?? BlackjackRules.CreateShuffledDeck;
    private readonly Func<ulong> createSeed = seedFactory ??
        (() => BitConverter.ToUInt64(RandomNumberGenerator.GetBytes(sizeof(ulong))));
    private readonly BlackjackManagedPlayerSupply managedPlayers = managedPlayerSupply ?? new(generateWhenEmpty: true);

    public BlackjackTableCoordinatorResult Get(
        BlackjackTableLobbyState state,
        IDictionary<string, long> balances,
        string userId,
        DateTime nowUtc)
    {
        var journal = new BlackjackTableJournal();
        Advance(state, balances, journal, nowUtc);
        return Result(state, balances, userId, nowUtc, journal);
    }

    public BlackjackTableCoordinatorResult Join(
        BlackjackTableLobbyState state,
        IDictionary<string, long> balances,
        string userId,
        string displayName,
        int expectedVersion,
        string idempotencyKey,
        DateTime nowUtc)
    {
        var journal = new BlackjackTableJournal();
        Advance(state, balances, journal, nowUtc);
        var ticketId = BlackjackTableIds.Hash($"{userId}\n{idempotencyKey}");
        var detail = expectedVersion.ToString(CultureInfo.InvariantCulture);
        if (Replay(state, userId, idempotencyKey, "join", ticketId, detail))
            return Result(state, balances, userId, nowUtc, journal);
        var current = BlackjackTableProjection.Session(state, userId, nowUtc);
        if (current.Kind != BlackjackTableSessionKinds.Idle || current.Version != expectedVersion)
            throw new BlackjackTableConflictException("The Blackjack table session changed. Reconnect before joining.");
        var target = state.Tables.Values
            .Where(table => table.Phase != BlackjackTablePhases.Closed &&
                table.Players.Count(player => !player.IsBot) < BlackjackTableEngine.MaximumOccupiedSeats &&
                (table.Players.Count < BlackjackTableEngine.Capacity || table.Players.Any(player => player.IsBot)))
            .OrderBy(table => table.CreatedAtUtc)
            .ThenBy(table => table.TableId, StringComparer.Ordinal)
            .FirstOrDefault();
        var ticket = new BlackjackTableTicket(
            ticketId,
            userId,
            $"seat_{Guid.NewGuid():N}",
            displayName.Trim(),
            target?.TableId,
            target?.RoundNumber ?? 0,
            "queued",
            1,
            nowUtc,
            nowUtc.Add(BlackjackTableEngine.HumanGrace));
        state.Tickets.Add(ticket);
        state.Sessions[userId] = new(BlackjackTableSessionKinds.Queue, ticketId, null);
        state.Guards[GuardKey(userId, idempotencyKey)] = new("join", ticketId, detail, nowUtc);
        if (target?.Phase == BlackjackTablePhases.Betting)
        {
            target.Transition = null;
            target.NextTransitionAtUtc = null;
            target.WagerDeadlineAtUtc = nowUtc.Add(BlackjackTableEngine.WagerDuration);
            AdmitQueuedHumansAtBoundary(state, target, journal);
            target.RoundsRemainingWithoutHuman = 0;
            target.UpdatedAtUtc = nowUtc;
            StartIfReady(target, nowUtc);
        }
        else if (target is not null && target.Players.Count < BlackjackTableEngine.Capacity)
        {
            var seat = NextOpenSeatAfterPlayers(target);
            var joining = Human(ticket, seat, target.RoundNumber);
            joining.Status = "joining-next-round";
            target.Players.Add(joining);
            MarkTicketMatched(state, ticket);
            state.Sessions[userId] = new(BlackjackTableSessionKinds.Table, null, target.TableId);
            ScheduleGracefulBotDeparture(target);
            target.RoundsRemainingWithoutHuman = 0;
            target.UpdatedAtUtc = nowUtc;
        }
        else if (target is not null)
        {
            ScheduleGracefulBotDeparture(target);
        }
        Advance(state, balances, journal, nowUtc);
        return Result(state, balances, userId, nowUtc, journal);
    }

    public BlackjackTableCoordinatorResult Cancel(
        BlackjackTableLobbyState state,
        IDictionary<string, long> balances,
        string userId,
        string ticketId,
        int expectedVersion,
        string idempotencyKey,
        DateTime nowUtc)
    {
        var journal = new BlackjackTableJournal();
        Advance(state, balances, journal, nowUtc);
        var detail = expectedVersion.ToString(CultureInfo.InvariantCulture);
        if (Replay(state, userId, idempotencyKey, "cancel", ticketId, detail))
            return Result(state, balances, userId, nowUtc, journal);
        var index = state.Tickets.FindIndex(ticket => ticket.TicketId == ticketId && ticket.UserId == userId);
        if (index < 0) throw new BlackjackTableNotFoundException("The Blackjack table queue ticket was not found.");
        var ticket = state.Tickets[index];
        if (ticket.Status != "queued" || ticket.Version != expectedVersion ||
            !state.Sessions.TryGetValue(userId, out var session) || session.TicketId != ticketId)
            throw new BlackjackTableConflictException("This Blackjack queue ticket changed or was already matched.");
        state.Tickets[index] = ticket with { Status = "cancelled", Version = checked(ticket.Version + 1) };
        state.Sessions[userId] = new(BlackjackTableSessionKinds.Idle, null, null);
        state.Guards[GuardKey(userId, idempotencyKey)] = new("cancel", ticketId, detail, nowUtc);
        return Result(state, balances, userId, nowUtc, journal);
    }

    public BlackjackTableCoordinatorResult Wager(
        BlackjackTableLobbyState state,
        IDictionary<string, long> balances,
        string userId,
        string tableId,
        long wagerCents,
        int expectedVersion,
        string idempotencyKey,
        DateTime nowUtc)
    {
        var journal = new BlackjackTableJournal();
        Advance(state, balances, journal, nowUtc);
        var detail = $"{wagerCents}:{expectedVersion}";
        if (Replay(state, userId, idempotencyKey, "wager", tableId, detail))
            return Result(state, balances, userId, nowUtc, journal);
        var table = OwnedTable(state, userId, tableId);
        if (table.Phase != BlackjackTablePhases.Betting || table.Version != expectedVersion)
            throw new BlackjackTableConflictException("The Blackjack table changed or is not accepting its next wager.");
        var player = table.Players.Single(value => value.ActorId == userId);
        var priorWager = player.NextWagerCents;
        var difference = checked(wagerCents - priorWager);
        if (difference > 0) Debit(balances, userId, difference);
        else if (difference < 0) Credit(balances, userId, checked(-difference));
        player.NextWagerCents = wagerCents;
        player.ConsecutiveMissedRounds = 0;
        player.Status = "ready";
        table.Version = checked(table.Version + 1);
        table.UpdatedAtUtc = nowUtc;
        state.Guards[GuardKey(userId, idempotencyKey)] = new("wager", tableId, detail, nowUtc);
        var roundReference = $"{tableId}-round-{table.RoundNumber + 1}";
        if (difference != 0)
        {
            journal.Ledger.Add(new(
                $"blackjack-table-wager-{BlackjackTableIds.Hash($"{userId}\n{idempotencyKey}")}",
                userId,
                checked(-difference),
                balances[userId],
                priorWager == 0 ? "blackjack-table-wager" : "blackjack-table-wager-adjustment",
                roundReference,
                nowUtc));
        }
        StartIfReady(table, nowUtc);
        return Result(state, balances, userId, nowUtc, journal);
    }

    public BlackjackTableCoordinatorResult Action(
        BlackjackTableLobbyState state,
        IDictionary<string, long> balances,
        string userId,
        string tableId,
        string action,
        int expectedVersion,
        string idempotencyKey,
        DateTime nowUtc)
    {
        var journal = new BlackjackTableJournal();
        Advance(state, balances, journal, nowUtc);
        action = action.Trim().ToLowerInvariant();
        var detail = $"{action}:{expectedVersion}";
        if (Replay(state, userId, idempotencyKey, "action", tableId, detail))
            return Result(state, balances, userId, nowUtc, journal);
        var table = OwnedTable(state, userId, tableId);
        if (table.Version != expectedVersion)
            throw new BlackjackTableConflictException("The Blackjack table changed. Reconnect before acting.");
        var player = table.Players.Single(value => value.ActorId == userId);
        if (!BlackjackTableEngine.LegalActions(table, player).Contains(action))
            throw new BlackjackTableIllegalActionException("That Blackjack action is not legal now.");
        var isPlayAction = table.Phase == BlackjackTablePhases.Active;
        var additionalWager = BlackjackTableEngine.AdditionalWagerFor(player, action);
        if (additionalWager > 0)
        {
            Debit(balances, userId, additionalWager);
            var wagerKind = action switch
            {
                BlackjackActions.Double => "blackjack-table-double",
                BlackjackActions.Split => "blackjack-table-split",
                BlackjackActions.Insurance => "blackjack-table-insurance",
                _ => throw new InvalidOperationException("The Blackjack action wager type is invalid.")
            };
            journal.Ledger.Add(new(
                $"{wagerKind}-{BlackjackTableIds.Hash($"{userId}\n{idempotencyKey}")}",
                userId,
                -additionalWager,
                balances[userId],
                wagerKind,
                $"{tableId}-round-{table.RoundNumber}",
                nowUtc));
        }
        BlackjackTableEngine.ApplyAction(table, userId, action, nowUtc);
        if (isPlayAction)
        {
            player.ConsecutiveMissedActionRounds = 0;
            player.LastMissedActionRound = 0;
        }
        state.Guards[GuardKey(userId, idempotencyKey)] = new("action", tableId, detail, nowUtc);
        return Result(state, balances, userId, nowUtc, journal);
    }

    public BlackjackTableCoordinatorResult Leave(
        BlackjackTableLobbyState state,
        IDictionary<string, long> balances,
        string userId,
        string tableId,
        int expectedVersion,
        string idempotencyKey,
        DateTime nowUtc)
    {
        var journal = new BlackjackTableJournal();
        Advance(state, balances, journal, nowUtc);
        var detail = expectedVersion.ToString(CultureInfo.InvariantCulture);
        if (Replay(state, userId, idempotencyKey, "leave", tableId, detail))
            return Result(state, balances, userId, nowUtc, journal);
        var table = OwnedTable(state, userId, tableId);
        if (table.Version != expectedVersion)
            throw new BlackjackTableConflictException("The Blackjack table changed before the seat could be left.");
        var player = table.Players.Single(value => value.ActorId == userId);
        if (table.Phase == BlackjackTablePhases.Betting)
        {
            if (player.NextWagerCents > 0)
            {
                Credit(balances, userId, player.NextWagerCents);
                journal.Ledger.Add(new(
                    $"blackjack-table-leave-{tableId}-{BlackjackTableIds.Hash(userId)}",
                    userId,
                    player.NextWagerCents,
                    balances[userId],
                    "blackjack-table-wager-release",
                    tableId,
                    nowUtc));
            }
            player.NextWagerCents = 0;
            table.Players.Remove(player);
            table.Version = checked(table.Version + 1);
            table.UpdatedAtUtc = nowUtc;
            if (table.Players.All(value => value.IsBot))
                BeginDisconnectedContinuation(table);
            EnsureMinimumOccupancy(table, nowUtc);
            StartIfReady(table, nowUtc);
        }
        else
        {
            BlackjackTableEngine.MarkLeaving(table, player, nowUtc);
        }
        state.Sessions[userId] = new(BlackjackTableSessionKinds.Idle, null, null);
        state.Guards[GuardKey(userId, idempotencyKey)] = new("leave", tableId, detail, nowUtc);
        return Result(state, balances, userId, nowUtc, journal);
    }

    public BlackjackTableJournal Sweep(
        BlackjackTableLobbyState state,
        IDictionary<string, long> balances,
        DateTime nowUtc)
    {
        var journal = new BlackjackTableJournal();
        Advance(state, balances, journal, nowUtc);
        return journal;
    }

    private void Advance(
        BlackjackTableLobbyState state,
        IDictionary<string, long> balances,
        BlackjackTableJournal journal,
        DateTime nowUtc)
    {
        foreach (var table in state.Tables.Values.OrderBy(value => value.CreatedAtUtc).ToArray())
        {
            if (table.Phase is BlackjackTablePhases.Active or BlackjackTablePhases.Insurance or BlackjackTablePhases.Dealer)
                BlackjackTableEngine.AdvanceAutomatedTurns(table, nowUtc);
            if (table.Phase == "settlement") CompleteSettlement(state, balances, journal, table, nowUtc);
            AdvanceBotWagerTurns(table, nowUtc);
            if (table.Phase == BlackjackTablePhases.Betting && table.Transition == "wager-lock" &&
                table.NextTransitionAtUtc is { } readyAt && nowUtc >= readyAt)
            {
                table.Transition = null;
                table.NextTransitionAtUtc = null;
                StartIfReady(table, nowUtc, adjustmentElapsed: true);
            }
            if (table.Phase == BlackjackTablePhases.Betting &&
                table.WagerDeadlineAtUtc is { } deadline && nowUtc >= deadline)
            {
                ApplyWagerDeadline(state, table, nowUtc);
            }
        }
        StartNewTables(state, balances, journal, nowUtc);
    }

    private void StartNewTables(
        BlackjackTableLobbyState state,
        IDictionary<string, long> balances,
        BlackjackTableJournal journal,
        DateTime nowUtc)
    {
        while (true)
        {
            var eligible = state.Tickets
                .Where(ticket => ticket.Status == "queued" && ticket.TargetTableId is null)
                .OrderBy(ticket => ticket.JoinedAtUtc)
                .ThenBy(ticket => ticket.TicketId, StringComparer.Ordinal)
                .ToArray();
            if (eligible.Length == 0 || nowUtc < eligible[0].GraceEndsAtUtc) return;
            var selected = eligible.Take(BlackjackTableEngine.MaximumOccupiedSeats).ToArray();
            var tableId = BlackjackTableIds.Hash(string.Join("\n", selected.Select(ticket => ticket.TicketId)));
            var initialSeed = createSeed();
            var minimumOccupancy = Math.Max(
                BlackjackTableEngine.MinimumStartOccupancy,
                Math.Min(BlackjackTableEngine.MaximumOccupiedSeats, selected.Length));
            var startingOccupancy = Math.Min(
                BlackjackTableEngine.MaximumOccupiedSeats,
                minimumOccupancy + (initialSeed % 2 == 0 ? 1 : 0));
            var startingSeats = RandomizedInitialSeats(startingOccupancy, initialSeed);
            var players = selected.Select((ticket, index) => Human(ticket, startingSeats[index], 0)).ToList();
            var table = new BlackjackTableState
            {
                TableId = tableId,
                Players = players,
                CreatedAtUtc = nowUtc,
                UpdatedAtUtc = nowUtc,
                Phase = BlackjackTablePhases.Betting,
                Version = 1,
                RoundNumber = 0,
                WagerDeadlineAtUtc = nowUtc.Add(BlackjackTableEngine.WagerDuration)
            };
            EnsureOccupancy(table, startingOccupancy, nowUtc);
            state.Tables[tableId] = table;
            foreach (var ticket in selected)
            {
                MarkTicketMatched(state, ticket);
                state.Sessions[ticket.UserId] = new(BlackjackTableSessionKinds.Table, null, tableId);
            }
            StartIfReady(table, nowUtc);
        }
    }

    private void ApplyWagerDeadline(
        BlackjackTableLobbyState state,
        BlackjackTableState table,
        DateTime nowUtc)
    {
        table.Version = checked(table.Version + 1);
        table.UpdatedAtUtc = nowUtc;
        foreach (var player in table.Players
                     .Where(player => !player.IsBot && player.NextWagerCents == 0)
                     .ToArray())
        {
            player.ConsecutiveMissedRounds = checked(player.ConsecutiveMissedRounds + 1);
            player.Status = "sitting-out";
            if (player.ConsecutiveMissedRounds < 2) continue;

            table.Players.Remove(player);
            state.Sessions[player.ActorId] = new(BlackjackTableSessionKinds.Idle, null, null);
        }

        if (table.Players.All(player => player.IsBot))
        {
            BeginDisconnectedContinuation(table);
            EnsureMinimumOccupancy(table, nowUtc);
            StartIfReady(table, nowUtc);
            return;
        }
        if (table.Players.Any(player => !player.IsBot && player.NextWagerCents > 0))
        {
            StartIfReady(table, nowUtc, deadlineReached: true);
            return;
        }

        table.WagerDeadlineAtUtc = nowUtc.Add(BlackjackTableEngine.WagerDuration);
    }

    private void CompleteSettlement(
        BlackjackTableLobbyState state,
        IDictionary<string, long> balances,
        BlackjackTableJournal journal,
        BlackjackTableState table,
        DateTime nowUtc)
    {
        if (table.RoundAccountingSettled) return;
        var roundId = $"{table.TableId}-round-{table.RoundNumber}";
        long humanWagers = 0;
        long humanPayouts = 0;
        var humanCount = 0;
        var participants = table.Players.Where(player => player.TotalWagerCents > 0).ToArray();
        foreach (var player in participants)
        {
            var committed = BlackjackTableEngine.TotalCommitted(player);
            player.SessionWagerCents = checked(player.SessionWagerCents + committed);
            player.SessionPayoutCents = checked(player.SessionPayoutCents + player.PayoutCents);
            player.SessionRoundsPlayed = checked(player.SessionRoundsPlayed + 1);
            if (player.IsBot) UpdateBotWagerHabit(player, committed);
            if (!player.IsBot)
            {
                humanCount++;
                humanWagers = checked(humanWagers + committed);
                humanPayouts = checked(humanPayouts + player.PayoutCents);
                if (player.PayoutCents > 0)
                {
                    Credit(balances, player.ActorId, player.PayoutCents);
                    journal.Ledger.Add(new(
                        $"blackjack-table-payout-{roundId}-{BlackjackTableIds.Hash(player.ActorId)}",
                        player.ActorId,
                        player.PayoutCents,
                        balances[player.ActorId],
                        "blackjack-table-payout",
                        roundId,
                        nowUtc));
                }
            }
            journal.Results.Add(new(
                BlackjackTableIds.Hash($"{roundId}\n{player.ActorId}"),
                player.ActorId,
                table.TableId,
                table.RoundNumber,
                committed,
                player.PayoutCents,
                player.IsBot,
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
        foreach (var player in table.Players.Where(player => !player.IsBot && player.LeavingAfterRound).ToArray())
        {
            table.Players.Remove(player);
            state.Sessions[player.ActorId] = new(BlackjackTableSessionKinds.Idle, null, null);
        }
        foreach (var player in table.Players.Where(player => player.IsBot &&
                     (player.BotDepartureAfterRound is { } departureRound && departureRound <= table.RoundNumber ||
                      nowUtc - player.SessionStartedAtUtc >= TimeSpan.FromHours(1))).ToArray())
        {
            table.Players.Remove(player);
            ReleaseManagedPlayer(table, player, journal);
        }
        table.Phase = BlackjackTablePhases.Betting;
        table.ActiveSeat = null;
        table.PendingSeat = null;
        table.ActionDeadlineAtUtc = null;
        table.Transition = null;
        table.NextTransitionAtUtc = null;
        table.WagerDeadlineAtUtc = nowUtc.Add(BlackjackTableEngine.WagerDuration);
        AdmitQueuedHumansAtBoundary(state, table, journal);
        if (table.Players.Any(player => !player.IsBot))
        {
            table.RoundsRemainingWithoutHuman = 0;
            ApplyPopulationChange(table, journal, nowUtc);
        }
        else
        {
            if (table.RoundsRemainingWithoutHuman == 0)
                table.RoundsRemainingWithoutHuman = BlackjackTableEngine.DisconnectedTableRounds;
            else
                table.RoundsRemainingWithoutHuman--;
            if (table.RoundsRemainingWithoutHuman == 0)
            {
                CloseTable(state, table, journal);
                return;
            }
        }
        EnsureMinimumOccupancy(table, nowUtc);
        BlackjackTableEngine.PrepareForBetting(table);
        foreach (var player in table.Players)
        {
            player.NextWagerCents = 0;
            player.Status = player.IsBot ? "waiting-to-wager" : "awaiting-wager";
        }
        table.Version = checked(table.Version + 1);
        table.UpdatedAtUtc = nowUtc;
        StartIfReady(table, nowUtc);
    }

    private void AdmitQueuedHumansAtBoundary(
        BlackjackTableLobbyState state,
        BlackjackTableState table,
        BlackjackTableJournal journal)
    {
        var waiting = state.Tickets
            .Where(ticket => ticket.Status == "queued" && ticket.TargetTableId == table.TableId &&
                ticket.EligibleAfterRound <= table.RoundNumber)
            .OrderBy(ticket => ticket.JoinedAtUtc)
            .ThenBy(ticket => ticket.TicketId, StringComparer.Ordinal)
            .ToArray();
        foreach (var ticket in waiting)
        {
            if (table.Players.Count < BlackjackTableEngine.Capacity)
            {
                var emptySeat = NextOpenSeatAfterPlayers(table);
                table.Players.Add(Human(ticket, emptySeat, table.RoundNumber));
                if (table.Players.Count == BlackjackTableEngine.Capacity)
                    ScheduleGracefulBotDeparture(table);
            }
            else
            {
                ScheduleGracefulBotDeparture(table);
                break;
            }
            MarkTicketMatched(state, ticket);
            state.Sessions[ticket.UserId] = new(BlackjackTableSessionKinds.Table, null, table.TableId);
        }
    }

    private void StartIfReady(
        BlackjackTableState table,
        DateTime nowUtc,
        bool deadlineReached = false,
        bool adjustmentElapsed = false)
    {
        if (table.Phase != BlackjackTablePhases.Betting) return;
        if (table.Players.Any(player => player.IsBot && player.NextWagerCents == 0))
        {
            ScheduleNextBotWager(table, nowUtc);
            return;
        }
        var humans = table.Players.Where(player => !player.IsBot).ToArray();
        if (!deadlineReached && humans.Any(player => player.NextWagerCents == 0)) return;
        if (humans.Length > 0 && humans.All(player => player.NextWagerCents == 0)) return;
        if (humans.Length == 0 && table.RoundsRemainingWithoutHuman <= 0) return;
        if (!deadlineReached && !adjustmentElapsed)
        {
            table.Transition = "wager-lock";
            table.NextTransitionAtUtc = nowUtc.Add(BlackjackTableEngine.WagerAdjustmentDuration);
            return;
        }
        BlackjackTableEngine.Deal(table, createDeck(), createSeed(), nowUtc);
    }

    private void EnsureMinimumOccupancy(BlackjackTableState table, DateTime nowUtc)
        => EnsureOccupancy(table, BlackjackTableEngine.MinimumStartOccupancy, nowUtc);

    private void EnsureOccupancy(BlackjackTableState table, int occupancy, DateTime nowUtc)
    {
        var botCount = Math.Max(0, occupancy - table.Players.Count);
        for (var index = 0; index < botCount && table.Players.Count < BlackjackTableEngine.MaximumOccupiedSeats; index++)
            AddManagedPlayer(table, nowUtc);
    }

    private void ApplyPopulationChange(
        BlackjackTableState table,
        BlackjackTableJournal journal,
        DateTime nowUtc)
    {
        var longestHumanSession = table.Players
            .Where(player => !player.IsBot)
            .Select(player => player.SessionRoundsPlayed)
            .DefaultIfEmpty(0)
            .Max();
        if (table.NextPopulationChangeRound is null)
        {
            if (longestHumanSession < BlackjackTableEngine.FirstPopulationChangeAfterRounds) return;
            table.NextPopulationChangeRound = table.RoundNumber;
            table.NextPopulationChangeAddsPlayer = true;
        }
        if (table.RoundNumber < table.NextPopulationChangeRound) return;

        if (table.NextPopulationChangeAddsPlayer)
        {
            if (table.Players.Count < BlackjackTableEngine.MaximumOccupiedSeats)
                AddManagedPlayer(table, nowUtc);
            table.NextPopulationChangeAddsPlayer = false;
        }
        else
        {
            var departing = table.Players
                .Where(player => player.IsBot)
                .OrderBy(player => player.SessionStartedAtUtc)
                .ThenBy(player => player.ActorId, StringComparer.Ordinal)
                .FirstOrDefault();
            if (departing is not null && table.Players.Count > BlackjackTableEngine.MinimumStartOccupancy)
            {
                table.Players.Remove(departing);
                ReleaseManagedPlayer(table, departing, journal);
            }
            table.NextPopulationChangeAddsPlayer = true;
        }
        table.NextPopulationChangeRound = checked(
            table.RoundNumber + BlackjackTableEngine.PopulationChangeIntervalRounds);
    }

    private void AddManagedPlayer(BlackjackTableState table, DateTime nowUtc)
    {
        if (table.Players.Count >= BlackjackTableEngine.MaximumOccupiedSeats) return;
        var profile = managedPlayers.Take(
            table.TableId,
            table.Players.Where(player => player.IsBot).Select(player => player.ActorId).ToArray(),
            nowUtc);
        table.Players.Add(new BlackjackTablePlayer
        {
            ActorId = profile.UserId,
            PublicSeatId = $"seat_{Guid.NewGuid():N}",
            DisplayName = profile.PlayerName,
            IsBot = true,
            BotSkillLevel = profile.SkillLevel,
            Seat = ClosestOpenSeatOnDealerLeft(table),
            SessionId = $"managed-session-{Guid.NewGuid():N}",
            SessionStartedAtUtc = nowUtc,
            JoinedRound = table.RoundNumber,
            NextWagerCents = 0,
            BotBaseWagerCents = InitialBotWager(profile.UserId),
            BotWagerChangeChanceBasisPoints = BotWagerChangeChance(profile.UserId),
            Status = "waiting-to-wager"
        });
    }

    private static void BeginDisconnectedContinuation(BlackjackTableState table)
    {
        if (table.RoundsRemainingWithoutHuman == 0)
            table.RoundsRemainingWithoutHuman = BlackjackTableEngine.DisconnectedTableRounds;
    }

    private static void ReleaseManagedPlayer(
        BlackjackTableState table,
        BlackjackTablePlayer player,
        BlackjackTableJournal journal)
    {
        if (player.IsBot)
            journal.ManagedPlayerReleases.Add(new(player.ActorId, table.TableId));
    }

    internal static IReadOnlyList<int> RandomizedInitialSeats(int occupiedSeats, ulong seed)
    {
        if (occupiedSeats is < 1 or > BlackjackTableEngine.Capacity)
            throw new ArgumentOutOfRangeException(nameof(occupiedSeats));

        var offset = checked((int)(((seed % (ulong)BlackjackTableEngine.Capacity) * 2UL + 4UL)
            % (ulong)BlackjackTableEngine.Capacity));
        var direction = (seed & 2UL) == 0 ? 1 : BlackjackTableEngine.Capacity - 1;
        return Enumerable.Range(0, BlackjackTableEngine.Capacity)
            .Select(index => (offset + index * direction) % BlackjackTableEngine.Capacity)
            .Take(occupiedSeats)
            .ToArray();
    }

    private static int ClosestOpenSeatOnDealerLeft(BlackjackTableState table) =>
        Enumerable.Range(0, BlackjackTableEngine.Capacity)
            .First(seat => table.Players.All(player => player.Seat != seat));

    private static int NextOpenSeatAfterPlayers(BlackjackTableState table)
    {
        var start = table.Players.Count == 0
            ? 0
            : (table.Players.OrderBy(player => player.JoinedRound).ThenBy(player => player.Seat).Last().Seat + 1)
              % BlackjackTableEngine.Capacity;
        return Enumerable.Range(0, BlackjackTableEngine.Capacity)
            .Select(offset => (start + offset) % BlackjackTableEngine.Capacity)
            .First(seat => table.Players.All(player => player.Seat != seat));
    }

    private static void ScheduleGracefulBotDeparture(BlackjackTableState table)
    {
        var departing = table.Players
            .Where(player => player.IsBot && player.BotDepartureAfterRound is null)
            .OrderBy(player => player.SessionStartedAtUtc)
            .ThenBy(player => player.Seat)
            .FirstOrDefault();
        if (departing is not null)
            departing.BotDepartureAfterRound = checked(table.RoundNumber + RandomNumberGenerator.GetInt32(1, 4));
    }

    private static BlackjackTablePlayer Human(BlackjackTableTicket ticket, int seat, int joinedRound) => new()
    {
        ActorId = ticket.UserId,
        PublicSeatId = ticket.PublicSeatId,
        DisplayName = ticket.DisplayName,
        IsBot = false,
        BotSkillLevel = null,
        Seat = seat,
        SessionId = ticket.TicketId,
        SessionStartedAtUtc = ticket.JoinedAtUtc,
        JoinedRound = joinedRound,
        NextWagerCents = 0,
        Status = "awaiting-wager"
    };

    private static void CloseTable(
        BlackjackTableLobbyState state,
        BlackjackTableState table,
        BlackjackTableJournal journal)
    {
        table.Phase = BlackjackTablePhases.Closed;
        table.ActiveSeat = null;
        table.PendingSeat = null;
        table.ActionDeadlineAtUtc = null;
        table.WagerDeadlineAtUtc = null;
        table.Transition = null;
        table.NextTransitionAtUtc = null;
        foreach (var player in table.Players.Where(player => player.IsBot))
            ReleaseManagedPlayer(table, player, journal);
        foreach (var ticket in state.Tickets.Where(ticket => ticket.Status == "queued" && ticket.TargetTableId == table.TableId).ToArray())
            ReplaceTicket(state, ticket with { TargetTableId = null, EligibleAfterRound = 0 });
    }

    private static void MarkTicketMatched(BlackjackTableLobbyState state, BlackjackTableTicket ticket) =>
        ReplaceTicket(state, ticket with { Status = "matched", Version = checked(ticket.Version + 1) });

    private static void ReplaceTicket(BlackjackTableLobbyState state, BlackjackTableTicket replacement)
    {
        var index = state.Tickets.FindIndex(ticket => ticket.TicketId == replacement.TicketId);
        if (index < 0) throw new InvalidOperationException("A Blackjack queue ticket disappeared during a transition.");
        state.Tickets[index] = replacement;
    }

    private static BlackjackTableState OwnedTable(BlackjackTableLobbyState state, string userId, string tableId)
    {
        if (!state.Tables.TryGetValue(tableId, out var table) ||
            table.Players.All(player => player.ActorId != userId))
            throw new BlackjackTableNotFoundException("The Blackjack table was not found.");
        return table;
    }

    private static bool Replay(
        BlackjackTableLobbyState state,
        string userId,
        string idempotencyKey,
        string operation,
        string target,
        string detail)
    {
        if (!state.Guards.TryGetValue(GuardKey(userId, idempotencyKey), out var guard)) return false;
        if (guard.Operation != operation || guard.Target != target || guard.Detail != detail)
            throw new BlackjackTableConflictException(
                "This Idempotency-Key was already used for a different Blackjack table request.");
        return true;
    }

    private static BlackjackTableCoordinatorResult Result(
        BlackjackTableLobbyState state,
        IDictionary<string, long> balances,
        string userId,
        DateTime nowUtc,
        BlackjackTableJournal journal) => new(
            new BlackjackTableStoreResult(
                BlackjackTableProjection.Session(state, userId, nowUtc),
                Balance(balances, userId)),
            journal);

    private static void Debit(IDictionary<string, long> balances, string userId, long cents)
    {
        var available = Balance(balances, userId);
        if (available < cents) throw new BlackjackTableInsufficientCreditsException(available, cents);
        balances[userId] = checked(available - cents);
    }

    private static void Credit(IDictionary<string, long> balances, string userId, long cents) =>
        balances[userId] = checked(Balance(balances, userId) + cents);

    private static long Balance(IDictionary<string, long> balances, string userId) =>
        balances.TryGetValue(userId, out var value) ? value : 0;

    internal static long InitialBotWager(string actorId)
    {
        var options = new[] { 50L, 100L, 150L, 200L, 250L, 300L, 400L, 500L, 750L, 1_000L, 1_500L };
        var hash = SHA256.HashData(System.Text.Encoding.UTF8.GetBytes($"blackjack-wager\n{actorId}"));
        return options[hash[0] % options.Length];
    }

    internal static int BotWagerChangeChance(string actorId)
    {
        var options = new[] { 600, 1_200, 2_200, 3_400 };
        var hash = SHA256.HashData(System.Text.Encoding.UTF8.GetBytes($"blackjack-tempo\n{actorId}"));
        return options[hash[0] % options.Length];
    }

    internal static void UpdateBotWagerHabit(BlackjackTablePlayer player, long committed)
    {
        var net = checked(player.PayoutCents - committed);
        player.BotLastNetCents = net;
        if (net > 0)
        {
            player.BotConsecutiveWins = checked(player.BotConsecutiveWins + 1);
            player.BotConsecutiveLosses = 0;
        }
        else if (net < 0)
        {
            player.BotConsecutiveLosses = checked(player.BotConsecutiveLosses + 1);
            player.BotConsecutiveWins = 0;
        }
        else
        {
            player.BotConsecutiveWins = 0;
            player.BotConsecutiveLosses = 0;
        }

        var wager = player.BotBaseWagerCents > 0 ? player.BotBaseWagerCents : InitialBotWager(player.ActorId);
        var increment = BlackjackMoney.WagerIncrementCents;
        var bigWin = net >= Math.Max(increment, checked(committed + committed / 2));
        if (bigWin)
        {
            wager = checked(wager + RandomNumberGenerator.GetInt32(1, 5) * increment);
        }
        else if (player.BotConsecutiveLosses >= 3)
        {
            wager = checked(wager - RandomNumberGenerator.GetInt32(1, 4) * increment);
        }
        else if (RandomNumberGenerator.GetInt32(0, 10_000) < player.BotWagerChangeChanceBasisPoints)
        {
            var direction = player.BotConsecutiveWins > 0 ? 1 : player.BotConsecutiveLosses > 0 ? -1 : RandomNumberGenerator.GetInt32(0, 2) * 2 - 1;
            wager = checked(wager + direction * RandomNumberGenerator.GetInt32(1, 3) * increment);
        }
        player.BotBaseWagerCents = Math.Clamp(wager, BlackjackMoney.MinimumWagerCents, BlackjackMoney.MaximumWagerCents);
    }

    private void AdvanceBotWagerTurns(BlackjackTableState table, DateTime nowUtc)
    {
        if (table.Phase != BlackjackTablePhases.Betting ||
            table.Transition != "bot-wager" ||
            table.NextTransitionAtUtc is not { } readyAt || nowUtc < readyAt)
            return;

        var player = table.PendingSeat is { } seat
            ? table.Players.SingleOrDefault(value => value.IsBot && value.Seat == seat)
            : null;
        if (player is not null && player.NextWagerCents == 0)
        {
            player.NextWagerCents = player.BotBaseWagerCents > 0
                ? player.BotBaseWagerCents
                : InitialBotWager(player.ActorId);
            player.Status = "ready";
        }
        table.PendingSeat = null;
        table.Transition = null;
        table.NextTransitionAtUtc = null;
        table.UpdatedAtUtc = readyAt;
        if (!ScheduleNextBotWager(table, readyAt))
            StartIfReady(table, readyAt);
    }

    private static bool ScheduleNextBotWager(BlackjackTableState table, DateTime nowUtc)
    {
        if (table.Phase != BlackjackTablePhases.Betting) return false;
        if (table.Transition == "bot-wager") return true;
        if (table.Transition is not null) return false;
        var next = table.Players
            .Where(player => player.IsBot && player.NextWagerCents == 0)
            .OrderBy(player => player.Seat)
            .FirstOrDefault();
        if (next is null) return false;

        next.Status = "considering-wager";
        table.PendingSeat = next.Seat;
        table.Transition = "bot-wager";
        table.NextTransitionAtUtc = nowUtc.AddMilliseconds(RandomNumberGenerator.GetInt32(700, 1_801));
        table.UpdatedAtUtc = nowUtc;
        return true;
    }

    private static string GuardKey(string userId, string idempotencyKey) =>
        BlackjackTableIds.Hash($"{userId}\n{idempotencyKey}");
}
