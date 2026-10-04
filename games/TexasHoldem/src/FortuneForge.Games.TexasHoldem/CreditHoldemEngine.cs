namespace FortuneForge.Games.TexasHoldem;

internal static class CreditHoldemEngine
{
    public static readonly TimeSpan HumanGrace = TimeSpan.FromSeconds(5);
    public static readonly TimeSpan ActionDuration = TimeSpan.FromSeconds(30);
    public static readonly TimeSpan MatchDuration = TimeSpan.FromMinutes(5);

    public static CreditHoldemMatch Deal(
        string matchId,
        IReadOnlyList<CreditHoldemSeatAssignment> seats,
        string partitionKey,
        ulong seed,
        DateTime nowUtc,
        string tableRuleId = CreditHoldemTableRules.StandardId)
    {
        if (seats.Count is < CreditHoldemMoney.MinimumStartPlayers or > CreditHoldemMoney.MaximumSeats)
            throw new ArgumentOutOfRangeException(nameof(seats));
        if (!seats.Any(seat => seat.IsAccountBacked))
            throw new InvalidOperationException("A credit table requires an account-backed player.");
        if (seats.Select(seat => seat.Seat).Distinct().Count() != seats.Count)
            throw new ArgumentException("Hold'em seat assignments must be unique.", nameof(seats));

        var rule = CreditHoldemTableRules.Resolve(tableRuleId);
        return CreateHand(matchId, partitionKey, seats, seed, 0, 1, 1, nowUtc, rule.Id);
    }

    public static CreditHoldemMatch? StartNextHand(
        CreditHoldemMatch prior,
        IReadOnlyDictionary<string, long> balances,
        ulong seed,
        int minimumHumans,
        DateTime nowUtc)
    {
        if (prior.Status != "completed" || !prior.AccountingSettled)
            throw new InvalidOperationException("The previous hand must be settled before the next deal.");

        var rule = CreditHoldemTableRules.Resolve(prior.TableRuleId);
        var descriptors = prior.Players
            .Where(player => !prior.LeavingActorIds.Contains(player.ActorId))
            .OrderBy(player => player.Seat)
            .Select(player => player.IsAccountBacked
                ? ReturningAccountSeat(player, balances, rule)
                : new CreditHoldemSeatAssignment(
                    player.ActorId,
                    player.PublicSeatId,
                    player.DisplayName,
                    false,
                    player.Seat,
                    player.StartingStack,
                    player.HostMetadata))
            .Where(descriptor => !descriptor.IsAccountBacked || descriptor.Stack >= rule.BigBlindCents)
            .ToList();

        foreach (var ticket in prior.PendingTakeovers.OrderBy(ticket => ticket.JoinedAtUtc).ThenBy(ticket => ticket.TicketId, StringComparer.Ordinal))
        {
            if (!balances.TryGetValue(ticket.UserId, out var balance) || balance < rule.BigBlindCents) continue;
            var accountSeat = AccountSeat(ticket, descriptors.Count, balances, rule);
            var openSeat = Enumerable.Range(0, CreditHoldemMoney.MaximumSeats)
                .FirstOrDefault(seat => descriptors.All(value => value.Seat != seat));
            if (descriptors.Count < CreditHoldemMoney.MaximumSeats)
                descriptors.Add(accountSeat with { Seat = openSeat });
            else
            {
                var replace = descriptors.FindLastIndex(value => !value.IsAccountBacked);
                if (replace >= 0) descriptors[replace] = accountSeat with { Seat = descriptors[replace].Seat };
            }
        }

        var accountSeats = descriptors.Where(value => value.IsAccountBacked).ToArray();
        if (accountSeats.Length < minimumHumans || descriptors.Count < CreditHoldemMoney.MinimumStartPlayers) return null;
        descriptors = descriptors.OrderBy(value => value.Seat).ToList();
        var dealerSeat = NextOccupiedSeat(descriptors, prior.DealerSeat);
        return CreateHand(
            prior.MatchId,
            prior.PartitionKey,
            descriptors,
            seed,
            dealerSeat,
            checked(prior.Version + 1),
            checked(prior.HandNumber + 1),
            nowUtc,
            rule.Id);
    }

    public static IReadOnlyList<string> LegalActions(CreditHoldemMatch match, CreditHoldemPlayer player)
    {
        if (match.Status != "active" || player.Status != "active" || match.ActiveSeat != player.Seat) return [];
        var call = Math.Max(0, match.CurrentBet - player.CommittedRound);
        var actions = new List<string>();
        if (call > 0)
        {
            actions.Add(CreditHoldemActions.Fold);
            actions.Add(CreditHoldemActions.Call);
        }
        else actions.Add(CreditHoldemActions.Check);
        var reopenIncrement = player.ReopenRaiseIncrement > 0 ? player.ReopenRaiseIncrement : match.MinimumRaise;
        var reopened = player.CanRaise || player.HasActed && match.CurrentBet - player.BetWhenLastActed >= reopenIncrement;
        if (reopened && player.Stack > call && player.CommittedRound + player.Stack > match.CurrentBet)
            actions.Add(CreditHoldemActions.Raise);
        return actions;
    }

    public static int ApplyAction(
        CreditHoldemMatch match,
        string actorId,
        string action,
        int? raiseTo,
        DateTime nowUtc)
    {
        if (match.Status != "active") throw new CreditHoldemConflictException("This Hold'em hand has finished.");
        if (nowUtc >= match.MatchDeadlineAtUtc) throw new CreditHoldemConflictException("The hand deadline has passed.");
        var player = match.Players.SingleOrDefault(value => value.ActorId == actorId)
            ?? throw new CreditHoldemNotFoundException("This player does not have a seat at the table.");
        if (player.Seat != match.ActiveSeat) throw new CreditHoldemConflictException("It is not this seat's turn.");
        action = action.Trim().ToLowerInvariant();
        if (!LegalActions(match, player).Contains(action))
            throw new CreditHoldemIllegalActionException("That Hold'em action is not legal now.");

        var before = player.CommittedHand;
        var call = Math.Max(0, match.CurrentBet - player.CommittedRound);
        switch (action)
        {
            case CreditHoldemActions.Fold:
                player.Status = "folded";
                player.HasActed = true;
                player.CanRaise = false;
                break;
            case CreditHoldemActions.Check:
                player.HasActed = true;
                player.CanRaise = false;
                break;
            case CreditHoldemActions.Call:
                Commit(player, Math.Min(call, player.Stack));
                player.HasActed = true;
                player.CanRaise = false;
                break;
            case CreditHoldemActions.Raise:
                var maximum = checked(player.CommittedRound + player.Stack);
                var minimum = checked(match.CurrentBet + match.MinimumRaise);
                if (raiseTo is null || raiseTo <= match.CurrentBet || raiseTo > maximum ||
                    raiseTo < minimum && raiseTo != maximum)
                    throw new CreditHoldemIllegalActionException(
                        $"RaiseTo must be at least {minimum}, except for a short all-in at {maximum}.");
                var priorBet = match.CurrentBet;
                Commit(player, raiseTo.Value - player.CommittedRound);
                match.CurrentBet = raiseTo.Value;
                if (raiseTo.Value >= minimum)
                {
                    match.MinimumRaise = raiseTo.Value - priorBet;
                    foreach (var other in match.Players.Where(value => value.Status == "active" && value != player))
                    {
                        other.HasActed = false;
                        other.CanRaise = true;
                    }
                }
                player.HasActed = true;
                player.CanRaise = false;
                break;
        }
        player.LastAction = action;
        if (player.IsAccountBacked)
        {
            player.ConsecutiveMissedActionHands = 0;
            player.LastMissedActionHand = 0;
        }
        match.ActionLog.Add(new CreditHoldemActionLogEntry(
            match.ActionLog.Count + 1,
            match.HandNumber,
            match.Street,
            player.PublicSeatId,
            player.DisplayName,
            action,
            player.CommittedHand - before));
        player.BetWhenLastActed = match.CurrentBet;
        player.ReopenRaiseIncrement = match.MinimumRaise;
        match.Version = checked(match.Version + 1);
        match.UpdatedAtUtc = nowUtc;
        Progress(match, nowUtc);
        return player.CommittedHand - before;
    }

    public static bool AdvanceExpiredTurn(CreditHoldemMatch match, DateTime nowUtc)
    {
        if (match.Status != "active") return false;
        if (nowUtc >= match.MatchDeadlineAtUtc)
        {
            ForceComplete(match, nowUtc);
            return true;
        }
        var current = match.Players.Single(player => player.Seat == match.ActiveSeat);
        if (match.ActionDeadlineAtUtc is { } deadline && nowUtc < deadline) return false;
        var automatic = match.CurrentBet == current.CommittedRound
            ? CreditHoldemActions.Check
            : CreditHoldemActions.Fold;
        _ = ApplyAction(match, current.ActorId, automatic, null, nowUtc);
        return true;
    }

    public static void Leave(CreditHoldemMatch match, string actorId, DateTime nowUtc)
    {
        var player = match.Players.SingleOrDefault(value => value.ActorId == actorId && value.IsAccountBacked)
            ?? throw new CreditHoldemNotFoundException("This player does not have a seat at the table.");
        match.LeavingActorIds.Add(actorId);
        if (match.Status == "active" && player.Status is "active" or "all-in")
        {
            var wasActive = player.Seat == match.ActiveSeat;
            player.Status = "folded";
            player.LastAction = "left";
            match.ActionLog.Add(new CreditHoldemActionLogEntry(
                match.ActionLog.Count + 1,
                match.HandNumber,
                match.Street,
                player.PublicSeatId,
                player.DisplayName,
                "left",
                0));
            player.HasActed = true;
            player.CanRaise = false;
            match.Version = checked(match.Version + 1);
            match.UpdatedAtUtc = nowUtc;
            if (AllAccountPlayersFolded(match)) ForceComplete(match, nowUtc, incrementVersion: false);
            else if (match.Players.Count(value => value.Status != "folded") == 1) AwardUncontested(match, nowUtc);
            else if (wasActive) Progress(match, nowUtc);
        }
    }

    public static void ForceComplete(CreditHoldemMatch match, DateTime nowUtc, bool incrementVersion = true)
    {
        if (match.Status != "active") return;
        while (match.Community.Count < 5) DealNextStreet(match);
        foreach (var player in match.Players.Where(player => player.Status != "folded"))
            player.RevealAtShowdown = true;
        SettleShowdown(match);
        if (incrementVersion) match.Version = checked(match.Version + 1);
        Complete(match, nowUtc);
    }

    public static CreditHoldemFinancialSettlement ApplyFinancialSettlement(CreditHoldemMatch match)
    {
        if (match.Status != "completed")
            throw new InvalidOperationException("A running Hold'em hand cannot be financially settled.");
        if (match.AccountingSettled)
            return new(match.HumanCommittedCents, match.HumanPayoutsCents, match.HumanPayoutCents, match.HouseNetCents);

        var settlement = CalculateHumanSettlement(match);
        match.HumanCommittedCents = settlement.HumanCommittedCents;
        match.HumanPayoutCents = settlement.HumanPayoutCents;
        match.HouseNetCents = settlement.HouseNetCents;
        match.HumanPayoutsCents = settlement.HumanPayoutsCents.ToDictionary(StringComparer.Ordinal);
        foreach (var player in match.Players.Where(value => value.IsAccountBacked))
        {
            var payout = settlement.HumanPayoutsCents.GetValueOrDefault(player.ActorId);
            player.AccountPayoutCents = payout;
        }
        match.AccountingSettled = true;
        return settlement;
    }

    public static CreditHoldemFinancialSettlement CalculateHumanSettlement(CreditHoldemMatch match)
    {
        var payouts = match.Players.Where(value => value.IsAccountBacked)
            .ToDictionary(value => value.ActorId, _ => 0L, StringComparer.Ordinal);
        var levels = match.Players.Select(value => value.CommittedHand).Where(value => value > 0).Distinct().Order().ToArray();
        var previous = 0;
        long committed = 0;
        foreach (var level in levels)
        {
            var contributors = match.Players.Where(value => value.CommittedHand >= level).ToArray();
            var segment = level - previous;
            previous = level;
            var accountPot = checked((long)segment * contributors.Count(value => value.IsAccountBacked));
            committed = checked(committed + accountPot);
            if (accountPot == 0) continue;
            var eligible = contributors.Where(value => value.Status != "folded").ToArray();
            if (eligible.Length == 0) continue;
            var winners = Winners(match, eligible);
            var share = accountPot / winners.Length;
            var remainder = accountPot % winners.Length;
            for (var index = 0; index < winners.Length; index++)
            {
                if (!winners[index].IsAccountBacked) continue;
                payouts[winners[index].ActorId] = checked(
                    payouts[winners[index].ActorId] + share + (index < remainder ? 1 : 0));
            }
        }
        var paid = payouts.Values.Sum();
        return new(committed, payouts, paid, checked(committed - paid));
    }

    private static CreditHoldemMatch CreateHand(
        string matchId,
        string partitionKey,
        IReadOnlyList<CreditHoldemSeatAssignment> descriptors,
        ulong seed,
        int dealerSeat,
        int version,
        int handNumber,
        DateTime nowUtc,
        string tableRuleId)
    {
        var rule = CreditHoldemTableRules.Resolve(tableRuleId);
        var deck = TexasHoldemRules.CreateDeck(seed);
        var players = descriptors.Select(descriptor =>
        {
            return new CreditHoldemPlayer
            {
                ActorId = descriptor.ActorId,
                PublicSeatId = descriptor.PublicSeatId,
                DisplayName = descriptor.DisplayName,
                IsAccountBacked = descriptor.IsAccountBacked,
                HostMetadata = descriptor.HostMetadata is null
                    ? new(StringComparer.Ordinal)
                    : new(descriptor.HostMetadata, StringComparer.Ordinal),
                Seat = descriptor.Seat,
                StartingStack = Math.Min(rule.MaximumStackCents, descriptor.Stack),
                Stack = Math.Min(rule.MaximumStackCents, descriptor.Stack),
                HoleCards = [],
                ConsecutiveMissedActionHands = descriptor.ConsecutiveMissedActionHands,
                LastMissedActionHand = descriptor.LastMissedActionHand
            };
        }).OrderBy(value => value.Seat).ToList();

        dealerSeat = players.FindIndex(value => value.Seat == dealerSeat) >= 0 ? dealerSeat : players[0].Seat;
        var nextCard = 0;
        var dealerIndex = players.FindIndex(value => value.Seat == dealerSeat);
        var firstToDeal = players.Count == 2 ? dealerIndex : (dealerIndex + 1) % players.Count;
        for (var round = 0; round < 2; round++)
            for (var offset = 0; offset < players.Count; offset++)
                players[(firstToDeal + offset) % players.Count].HoleCards.Add(deck[nextCard++]);

        var smallBlindIndex = players.Count == 2 ? dealerIndex : (dealerIndex + 1) % players.Count;
        var bigBlindIndex = (smallBlindIndex + 1) % players.Count;
        Commit(players[smallBlindIndex], Math.Min(rule.SmallBlindCents, players[smallBlindIndex].Stack));
        players[smallBlindIndex].LastAction = "small-blind";
        Commit(players[bigBlindIndex], Math.Min(rule.BigBlindCents, players[bigBlindIndex].Stack));
        players[bigBlindIndex].LastAction = "big-blind";
        var activeIndex = players.Count == 2 ? dealerIndex : (bigBlindIndex + 1) % players.Count;
        var match = new CreditHoldemMatch
        {
            MatchId = matchId,
            PartitionKey = partitionKey,
            TableRuleId = rule.Id,
            PendingTakeovers = [],
            LeavingActorIds = [],
            HumanPayoutsCents = [],
            DealSeed = seed,
            Deck = deck,
            Players = players,
            Community = [],
            NextCardIndex = nextCard,
            StartedAtUtc = nowUtc,
            UpdatedAtUtc = nowUtc,
            MatchDeadlineAtUtc = nowUtc.Add(MatchDuration),
            DealerSeat = dealerSeat,
            ActiveSeat = players[activeIndex].Seat,
            CurrentBet = rule.BigBlindCents,
            MinimumRaise = rule.BigBlindCents,
            Version = version,
            HandNumber = handNumber
        };
        match.ActionLog.Add(new CreditHoldemActionLogEntry(1, handNumber, "preflop", players[smallBlindIndex].PublicSeatId, players[smallBlindIndex].DisplayName, "small-blind", players[smallBlindIndex].CommittedHand));
        match.ActionLog.Add(new CreditHoldemActionLogEntry(2, handNumber, "preflop", players[bigBlindIndex].PublicSeatId, players[bigBlindIndex].DisplayName, "big-blind", players[bigBlindIndex].CommittedHand));
        SetActionDeadline(match, nowUtc);
        return match;
    }

    private static void Progress(CreditHoldemMatch match, DateTime nowUtc)
    {
        if (AllAccountPlayersFolded(match))
        {
            ForceComplete(match, nowUtc, incrementVersion: false);
            return;
        }
        var contenders = match.Players.Where(value => value.Status != "folded").ToArray();
        if (contenders.Length == 1)
        {
            AwardUncontested(match, nowUtc);
            return;
        }
        if (BettingRoundComplete(match))
        {
            if (match.Street == "river" || contenders.Count(value => value.Status == "active") <= 1)
            {
                while (match.Community.Count < 5) DealNextStreet(match);
                foreach (var player in contenders) player.RevealAtShowdown = true;
                SettleShowdown(match);
                Complete(match, nowUtc);
                return;
            }
            DealNextStreet(match);
            foreach (var player in match.Players)
            {
                player.CommittedRound = 0;
                player.HasActed = player.Status != "active";
                player.CanRaise = player.Status == "active";
                player.BetWhenLastActed = 0;
                player.ReopenRaiseIncrement = 0;
                player.LastAction = null;
            }
            match.CurrentBet = 0;
            match.MinimumRaise = CreditHoldemTableRules.Resolve(match.TableRuleId).BigBlindCents;
            match.ActiveSeat = NextActive(match, match.DealerSeat);
        }
        else match.ActiveSeat = NextActive(match, match.ActiveSeat);
        SetActionDeadline(match, nowUtc);
    }

    private static void AwardUncontested(CreditHoldemMatch match, DateTime nowUtc)
    {
        var winner = match.Players.Single(value => value.Status != "folded");
        var pot = Pot(match);
        winner.Stack = checked(winner.Stack + pot);
        winner.WonHandChips = checked(winner.WonHandChips + pot);
        Complete(match, nowUtc);
    }

    private static void SettleShowdown(CreditHoldemMatch match)
    {
        var levels = match.Players.Select(value => value.CommittedHand).Where(value => value > 0).Distinct().Order().ToArray();
        var previous = 0;
        foreach (var level in levels)
        {
            var contributors = match.Players.Where(value => value.CommittedHand >= level).ToArray();
            var pot = checked((level - previous) * contributors.Length);
            previous = level;
            var eligible = contributors.Where(value => value.Status != "folded").ToArray();
            if (eligible.Length == 0) continue;
            var winners = Winners(match, eligible);
            var share = pot / winners.Length;
            var remainder = pot % winners.Length;
            for (var index = 0; index < winners.Length; index++)
            {
                var award = share + (index < remainder ? 1 : 0);
                winners[index].Stack = checked(winners[index].Stack + award);
                winners[index].WonHandChips = checked(winners[index].WonHandChips + award);
            }
        }
    }

    private static CreditHoldemPlayer[] Winners(CreditHoldemMatch match, IReadOnlyCollection<CreditHoldemPlayer> eligible)
    {
        if (eligible.Count == 1) return [eligible.Single()];
        var best = eligible.Max(value => HandScore(match, value));
        return eligible.Where(value => HandScore(match, value) == best)
            .OrderBy(value => (value.Seat - match.DealerSeat - 1 + CreditHoldemMoney.MaximumSeats) % CreditHoldemMoney.MaximumSeats)
            .ToArray();
    }

    private static void Complete(CreditHoldemMatch match, DateTime nowUtc)
    {
        match.Status = "completed";
        match.Street = match.Community.Count == 5 ? "showdown" : "settled";
        match.ActiveSeat = -1;
        match.ActionDeadlineAtUtc = null;
        match.CompletedAtUtc = nowUtc;
        match.UpdatedAtUtc = nowUtc;
    }

    private static bool BettingRoundComplete(CreditHoldemMatch match) => match.Players
        .Where(value => value.Status == "active")
        .All(value => value.HasActed && value.CommittedRound == match.CurrentBet);

    private static bool AllAccountPlayersFolded(CreditHoldemMatch match) => match.Players
        .Where(value => value.IsAccountBacked)
        .All(value => value.Status == "folded");

    private static int NextActive(CreditHoldemMatch match, int afterSeat)
    {
        var ordered = match.Players.OrderBy(value => value.Seat).ToArray();
        var start = Array.FindIndex(ordered, value => value.Seat == afterSeat);
        for (var offset = 1; offset <= ordered.Length; offset++)
        {
            var player = ordered[(start + offset + ordered.Length) % ordered.Length];
            if (player.Status == "active") return player.Seat;
        }
        throw new InvalidOperationException("No active Hold'em player remains.");
    }

    private static void DealNextStreet(CreditHoldemMatch match)
    {
        match.NextCardIndex++;
        var count = match.Community.Count == 0 ? 3 : 1;
        for (var index = 0; index < count; index++) match.Community.Add(match.Deck[match.NextCardIndex++]);
        match.Street = match.Community.Count switch { 3 => "flop", 4 => "turn", 5 => "river", _ => match.Street };
    }

    private static ulong HandScore(CreditHoldemMatch match, CreditHoldemPlayer player) =>
        match.Community.Count == 5 && player.Status != "folded"
            ? TexasHoldemRules.Evaluate(player.HoleCards.Concat(match.Community).ToArray()).Score
            : 0;

    private static int Commit(CreditHoldemPlayer player, int amount)
    {
        if (amount < 0 || amount > player.Stack) throw new ArgumentOutOfRangeException(nameof(amount));
        player.Stack -= amount;
        player.CommittedRound += amount;
        player.CommittedHand += amount;
        if (player.Stack == 0) player.Status = "all-in";
        return amount;
    }

    private static int Pot(CreditHoldemMatch match) => match.Players.Sum(value => value.CommittedHand);

    private static void SetActionDeadline(CreditHoldemMatch match, DateTime nowUtc)
    {
        match.ActionDeadlineAtUtc = nowUtc.Add(ActionDuration);
    }

    private static CreditHoldemSeatAssignment AccountSeat(
        CreditHoldemTicket ticket,
        int seat,
        IReadOnlyDictionary<string, long> balances,
        CreditHoldemTableRule rule)
    {
        var stack = CreditHoldemMoney.StackFromBalance(
            balances.GetValueOrDefault(ticket.UserId), rule.MaximumStackCents);
        return new(ticket.UserId, ticket.PublicSeatId, ticket.DisplayName, true, seat, stack);
    }

    private static CreditHoldemSeatAssignment ReturningAccountSeat(
        CreditHoldemPlayer player,
        IReadOnlyDictionary<string, long> balances,
        CreditHoldemTableRule rule)
    {
        return new(
            player.ActorId,
            player.PublicSeatId,
            player.DisplayName,
            true,
            player.Seat,
            CreditHoldemMoney.StackFromBalance(
                balances.GetValueOrDefault(player.ActorId), rule.MaximumStackCents),
            player.HostMetadata,
            player.ConsecutiveMissedActionHands,
            player.LastMissedActionHand);
    }

    private static int NextOccupiedSeat(IReadOnlyList<CreditHoldemSeatAssignment> descriptors, int afterSeat)
    {
        for (var offset = 1; offset <= CreditHoldemMoney.MaximumSeats; offset++)
        {
            var seat = (afterSeat + offset) % CreditHoldemMoney.MaximumSeats;
            if (descriptors.Any(value => value.Seat == seat)) return seat;
        }
        return descriptors[0].Seat;
    }
}
