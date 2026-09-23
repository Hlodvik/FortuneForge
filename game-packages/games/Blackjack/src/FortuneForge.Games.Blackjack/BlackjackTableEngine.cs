using FortuneForge.Games.Cards;

namespace FortuneForge.Games.Blackjack;

public static class BlackjackTableEngine
{
    public const int MinimumStartOccupancy = 3;
    public const int Capacity = 5;
    public static readonly TimeSpan HumanGrace = TimeSpan.FromSeconds(5);
    public static readonly TimeSpan ActionDuration = TimeSpan.FromMinutes(1);
    public static readonly TimeSpan WagerDuration = TimeSpan.FromMinutes(1);
    public static readonly TimeSpan WagerAdjustmentDuration = TimeSpan.FromMilliseconds(800);
    public static readonly TimeSpan ActionSettleDuration = TimeSpan.FromMilliseconds(650);
    public static readonly TimeSpan DealerCardDuration = TimeSpan.FromMilliseconds(700);
    public static readonly TimeSpan MinimumTurnPause = TimeSpan.FromMilliseconds(850);
    public static readonly TimeSpan MaximumTurnPause = TimeSpan.FromMilliseconds(1_450);

    public static void Deal(BlackjackTableState table, IReadOnlyList<string> deck, ulong roundSeed, DateTime nowUtc)
    {
        if (table.Phase == BlackjackTablePhases.Closed)
            throw new BlackjackTableConflictException("This Blackjack table is closed.");
        if (table.Players.Count is < 1 or > Capacity)
            throw new InvalidOperationException("A Blackjack table must contain one through five occupied seats.");
        if (!table.Players.Any(player => !player.IsBot && player.NextWagerCents > 0))
            throw new BlackjackTableConflictException("At least one seated person must choose a wager before the round starts.");
        ValidateDeck(deck);

        table.RoundNumber = checked(table.RoundNumber + 1);
        table.RoundSeed = roundSeed;
        table.Deck = deck.ToArray();
        table.NextCardIndex = 0;
        table.DealerCards = [];
        table.DealerVisibleCardCount = 0;
        table.RoundAccountingSettled = false;
        table.Phase = BlackjackTablePhases.Active;
        table.WagerDeadlineAtUtc = null;
        ClearTurn(table);
        table.UpdatedAtUtc = nowUtc;

        var participants = table.Players.Where(player => player.NextWagerCents > 0).OrderBy(player => player.Seat).ToArray();
        foreach (var player in table.Players.OrderBy(player => player.Seat))
        {
            player.Cards = [];
            player.WagerCents = player.NextWagerCents;
            player.TotalWagerCents = player.NextWagerCents;
            player.NextWagerCents = 0;
            player.PayoutCents = 0;
            player.Status = player.WagerCents > 0 ? "playing" : "sitting-out";
            player.Outcome = null;
            player.LastAction = null;
            player.LeavingAfterRound = false;
            player.SecondaryHand = null;
            player.ActiveHandIndex = 0;
            player.InsuranceWagerCents = 0;
            player.InsurancePayoutCents = 0;
            player.InsuranceAccepted = null;
        }

        for (var pass = 0; pass < 2; pass++)
        {
            foreach (var player in participants) player.Cards.Add(Draw(table));
            table.DealerCards.Add(Draw(table));
        }
        table.DealerVisibleCardCount = 1;
        table.Version = checked(table.Version + 1);

        if (BlackjackRules.ParseCard(table.DealerCards[0]).Rank == "A")
        {
            table.Phase = BlackjackTablePhases.Insurance;
            ActivateSeat(table, participants[0], nowUtc);
            return;
        }
        ResolveDealerPeekAndStartPlayers(table, participants, nowUtc);
    }

    public static IReadOnlyList<string> LegalActions(BlackjackTableState table, BlackjackTablePlayer player)
    {
        if (player.LeavingAfterRound || table.Transition is not null || table.ActiveSeat != player.Seat) return [];
        if (table.Phase == BlackjackTablePhases.Insurance)
            return player.InsuranceAccepted is null && player.WagerCents > 0
                ? [BlackjackActions.Insurance, BlackjackActions.DeclineInsurance]
                : [];
        if (table.Phase != BlackjackTablePhases.Active || !ActiveHandIsPlaying(player)) return [];

        var cards = ActiveCards(player);
        var actions = new List<string> { BlackjackActions.Hit, BlackjackActions.Stand };
        if (cards.Count == 2) actions.Add(BlackjackActions.Double);
        if (player.SecondaryHand is null && player.ActiveHandIndex == 0 && cards.Count == 2 &&
            SplitValue(cards[0]) == SplitValue(cards[1]))
            actions.Add(BlackjackActions.Split);
        if (player.SecondaryHand is null && player.ActiveHandIndex == 0 && cards.Count == 2 &&
            player.TotalWagerCents == player.WagerCents)
            actions.Add(BlackjackActions.Surrender);
        return actions;
    }

    public static long AdditionalWagerFor(BlackjackTablePlayer player, string action) => action switch
    {
        BlackjackActions.Double => ActiveWager(player),
        BlackjackActions.Split => player.WagerCents,
        BlackjackActions.Insurance => player.WagerCents / 2,
        _ => 0
    };

    public static long TotalCommitted(BlackjackTablePlayer player) => checked(
        player.TotalWagerCents + (player.SecondaryHand?.TotalWagerCents ?? 0) + player.InsuranceWagerCents);

    public static long PrimaryPayoutFor(BlackjackTablePlayer player) =>
        PayoutForHand(player.Outcome, player.TotalWagerCents, player.SecondaryHand is null);

    public static long SecondaryPayoutFor(BlackjackTablePlayer player) => player.SecondaryHand is { } hand
        ? PayoutForHand(hand.Outcome, hand.TotalWagerCents, false)
        : 0;

    public static long PayoutFor(BlackjackTablePlayer player) => checked(
        PrimaryPayoutFor(player) + SecondaryPayoutFor(player) + player.InsurancePayoutCents);

    public static void ApplyAction(BlackjackTableState table, string actorId, string action, DateTime nowUtc)
    {
        if (table.Phase is not (BlackjackTablePhases.Active or BlackjackTablePhases.Insurance))
            throw new BlackjackTableConflictException("This Blackjack round is not accepting actions.");
        var player = table.Players.SingleOrDefault(value => value.ActorId == actorId)
            ?? throw new BlackjackTableNotFoundException("This seat was not found at the Blackjack table.");
        action = action.Trim().ToLowerInvariant();
        if (!LegalActions(table, player).Contains(action))
            throw new BlackjackTableIllegalActionException("That Blackjack action is not legal now.");
        if (table.Phase == BlackjackTablePhases.Insurance) ApplyInsuranceCore(table, player, action, nowUtc);
        else ApplyActionCore(table, player, action, nowUtc);
    }

    public static void MarkLeaving(BlackjackTableState table, BlackjackTablePlayer player, DateTime nowUtc)
    {
        player.LeavingAfterRound = true;
        player.LastAction = "left-table";
        if (table.ActiveSeat == player.Seat && table.Transition is null)
        {
            if (table.Phase == BlackjackTablePhases.Insurance && player.InsuranceAccepted is null)
            {
                ApplyInsuranceCore(table, player, BlackjackActions.DeclineInsurance, nowUtc);
                player.LastAction = "left-table";
                return;
            }
            if (table.Phase == BlackjackTablePhases.Active && ActiveHandIsPlaying(player))
            {
                ApplyActionCore(table, player, BlackjackActions.Stand, nowUtc);
                player.LastAction = "left-table";
                return;
            }
        }
        table.Version = checked(table.Version + 1);
        table.UpdatedAtUtc = nowUtc;
    }

    public static void AdvanceAutomatedTurns(BlackjackTableState table, DateTime nowUtc)
    {
        if (table.Phase == BlackjackTablePhases.Dealer)
        {
            AdvanceDealer(table, nowUtc);
            return;
        }
        if (table.Phase is not (BlackjackTablePhases.Active or BlackjackTablePhases.Insurance)) return;

        if (table.Transition == "action-settle")
        {
            if (table.NextTransitionAtUtc is { } settleAt && nowUtc < settleAt) return;
            var pending = table.PendingSeat is { } seat ? table.Players.SingleOrDefault(player => player.Seat == seat) : null;
            table.PendingSeat = null;
            table.Transition = null;
            table.NextTransitionAtUtc = null;
            if (pending is not null)
            {
                ActivateSeat(table, pending, nowUtc);
                return;
            }
            if (table.Phase == BlackjackTablePhases.Insurance)
                ResolveDealerPeekAndStartPlayers(table, Participants(table), nowUtc);
            else BeginDealer(table, nowUtc);
            return;
        }

        var current = table.ActiveSeat is { } active ? table.Players.SingleOrDefault(player => player.Seat == active) : null;
        if (current is null)
        {
            if (table.Phase == BlackjackTablePhases.Insurance)
                ResolveDealerPeekAndStartPlayers(table, Participants(table), nowUtc);
            else BeginDealer(table, nowUtc);
            return;
        }
        if (table.ActionDeadlineAtUtc is { } deadline && nowUtc < deadline) return;

        if (table.Phase == BlackjackTablePhases.Insurance)
        {
            ApplyInsuranceCore(table, current, BlackjackActions.DeclineInsurance, nowUtc);
            return;
        }
        if (current.IsBot)
        {
            var action = new BlackjackBotAgent().Choose(
                new BlackjackBotObservation(ActiveCards(current), table.DealerCards[0], CoreLegalActions(current)),
                current.BotSkillLevel!.Value,
                table.RoundSeed,
                table.Version,
                new CardBotGameOptions());
            ApplyActionCore(table, current, action, nowUtc);
            return;
        }
        if (current.LastMissedActionRound != table.RoundNumber)
        {
            current.ConsecutiveMissedActionRounds = current.LastMissedActionRound == table.RoundNumber - 1
                ? checked(current.ConsecutiveMissedActionRounds + 1)
                : 1;
            current.LastMissedActionRound = table.RoundNumber;
        }
        if (current.ConsecutiveMissedActionRounds >= 2) current.LeavingAfterRound = true;
        ApplyActionCore(table, current, BlackjackActions.Stand, nowUtc);
    }

    private static void ApplyInsuranceCore(BlackjackTableState table, BlackjackTablePlayer player, string action, DateTime nowUtc)
    {
        player.InsuranceAccepted = action == BlackjackActions.Insurance;
        player.InsuranceWagerCents = player.InsuranceAccepted.Value ? player.WagerCents / 2 : 0;
        player.LastAction = action;
        var next = Participants(table).Where(value => value.InsuranceAccepted is null && value.Seat > player.Seat)
            .OrderBy(value => value.Seat).FirstOrDefault();
        ScheduleNext(table, next, nowUtc);
    }

    private static void ApplyActionCore(BlackjackTableState table, BlackjackTablePlayer player, string action, DateTime nowUtc)
    {
        switch (action)
        {
            case BlackjackActions.Hit:
                ActiveCards(player).Add(Draw(table));
                SetActiveLastAction(player, BlackjackActions.Hit);
                var hit = BlackjackRules.Score(ActiveCards(player));
                if (hit.Bust)
                {
                    SetActiveStatus(player, "bust");
                    SetActiveOutcome(player, BlackjackOutcomes.PlayerBust);
                }
                else if (hit.Score == 21) SetActiveStatus(player, "stood");
                break;
            case BlackjackActions.Stand:
                SetActiveStatus(player, "stood");
                SetActiveLastAction(player, BlackjackActions.Stand);
                break;
            case BlackjackActions.Double:
                AddActiveWager(player, ActiveWager(player));
                ActiveCards(player).Add(Draw(table));
                SetActiveLastAction(player, BlackjackActions.Double);
                if (BlackjackRules.Score(ActiveCards(player)).Bust)
                {
                    SetActiveStatus(player, "bust");
                    SetActiveOutcome(player, BlackjackOutcomes.PlayerBust);
                }
                else SetActiveStatus(player, "stood");
                break;
            case BlackjackActions.Split:
                Split(player, table);
                break;
            case BlackjackActions.Surrender:
                SetActiveStatus(player, "completed");
                SetActiveOutcome(player, BlackjackOutcomes.PlayerSurrender);
                SetActiveLastAction(player, BlackjackActions.Surrender);
                break;
            default:
                throw new BlackjackTableIllegalActionException("Choose hit, stand, double, split, or surrender.");
        }
        ScheduleNext(table, NextPlayingHand(table, player), nowUtc);
    }

    private static void Split(BlackjackTablePlayer player, BlackjackTableState table)
    {
        var first = player.Cards[0];
        var second = player.Cards[1];
        player.Cards = [first, Draw(table)];
        player.TotalWagerCents = player.WagerCents;
        player.Status = "playing";
        player.Outcome = null;
        player.LastAction = BlackjackActions.Split;
        player.SecondaryHand = new BlackjackTableSecondaryHand
        {
            Cards = [second, Draw(table)],
            WagerCents = player.WagerCents,
            TotalWagerCents = player.WagerCents,
            Status = "playing",
            LastAction = BlackjackActions.Split
        };
        player.ActiveHandIndex = 0;
        if (BlackjackRules.ParseCard(first).Rank == "A")
        {
            player.Status = "stood";
            player.SecondaryHand.Status = "stood";
            return;
        }
        if (BlackjackRules.Score(player.Cards).Score == 21) player.Status = "stood";
        if (BlackjackRules.Score(player.SecondaryHand.Cards).Score == 21) player.SecondaryHand.Status = "stood";
    }

    private static BlackjackTablePlayer? NextPlayingHand(BlackjackTableState table, BlackjackTablePlayer current)
    {
        if (ActiveHandIsPlaying(current)) return current;
        if (current.ActiveHandIndex == 0 && current.SecondaryHand?.Status == "playing")
        {
            current.ActiveHandIndex = 1;
            return current;
        }
        return table.Players.Where(value => HasPlayingHand(value) && value.Seat > current.Seat)
            .OrderBy(value => value.Seat).FirstOrDefault();
    }

    private static void ScheduleNext(BlackjackTableState table, BlackjackTablePlayer? next, DateTime nowUtc)
    {
        table.ActiveSeat = null;
        table.ActionDeadlineAtUtc = null;
        table.PendingSeat = next?.Seat;
        table.Transition = "action-settle";
        table.NextTransitionAtUtc = nowUtc.Add(ActionSettleDuration);
        table.Version = checked(table.Version + 1);
        table.UpdatedAtUtc = nowUtc;
    }

    private static void ActivateSeat(BlackjackTableState table, BlackjackTablePlayer player, DateTime nowUtc)
    {
        table.ActiveSeat = player.Seat;
        table.PendingSeat = null;
        if (player.IsBot || player.LeavingAfterRound)
        {
            var delay = TurnPause(table, player.Seat);
            table.Transition = "turn-pause";
            table.NextTransitionAtUtc = nowUtc.Add(delay);
            table.ActionDeadlineAtUtc = table.NextTransitionAtUtc;
        }
        else
        {
            table.Transition = null;
            table.NextTransitionAtUtc = null;
            table.ActionDeadlineAtUtc = nowUtc.Add(ActionDuration);
        }
        table.Version = checked(table.Version + 1);
        table.UpdatedAtUtc = nowUtc;
    }

    private static void ResolveDealerPeekAndStartPlayers(BlackjackTableState table, IReadOnlyList<BlackjackTablePlayer> participants, DateTime nowUtc)
    {
        var dealer = BlackjackRules.Score(table.DealerCards);
        foreach (var player in participants)
        {
            var hand = BlackjackRules.Score(player.Cards);
            player.InsurancePayoutCents = dealer.Blackjack && player.InsuranceWagerCents > 0
                ? checked(player.InsuranceWagerCents * 3) : 0;
            if (dealer.Blackjack)
            {
                player.Status = "completed";
                player.Outcome = hand.Blackjack ? BlackjackOutcomes.Push : BlackjackOutcomes.DealerBlackjack;
            }
            else if (hand.Blackjack)
            {
                player.Status = "completed";
                player.Outcome = BlackjackOutcomes.PlayerBlackjack;
            }
        }
        table.Phase = BlackjackTablePhases.Active;
        ClearTurn(table);
        var first = participants.FirstOrDefault(HasPlayingHand);
        if (first is null) BeginDealer(table, nowUtc);
        else ActivateSeat(table, first, nowUtc);
    }

    private static void BeginDealer(BlackjackTableState table, DateTime nowUtc)
    {
        table.Phase = BlackjackTablePhases.Dealer;
        table.ActiveSeat = null;
        table.PendingSeat = null;
        table.ActionDeadlineAtUtc = null;
        table.DealerVisibleCardCount = Math.Min(1, table.DealerCards.Count);
        table.Transition = "dealer-reveal";
        table.NextTransitionAtUtc = nowUtc.Add(DealerCardDuration);
        table.Version = checked(table.Version + 1);
        table.UpdatedAtUtc = nowUtc;
    }

    private static void AdvanceDealer(BlackjackTableState table, DateTime nowUtc)
    {
        if (table.NextTransitionAtUtc is { } deadline && nowUtc < deadline) return;
        switch (table.Transition)
        {
            case "dealer-reveal":
                table.DealerVisibleCardCount = Math.Min(2, table.DealerCards.Count);
                ScheduleDealerContinuation(table, nowUtc);
                break;
            case "dealer-draw":
                table.DealerCards.Add(Draw(table));
                table.DealerVisibleCardCount = table.DealerCards.Count;
                ScheduleDealerContinuation(table, nowUtc);
                break;
            case "dealer-settle": FinishDealer(table, nowUtc); break;
            default: throw new InvalidOperationException("The Blackjack dealer transition is invalid.");
        }
    }

    private static void ScheduleDealerContinuation(BlackjackTableState table, DateTime nowUtc)
    {
        var unresolved = table.Players.Any(player => player.TotalWagerCents > 0 &&
            (player.Outcome is null || player.SecondaryHand is { Outcome: null }));
        var draw = unresolved && BlackjackRules.Score(table.DealerCards).Score < 17;
        table.Transition = draw ? "dealer-draw" : "dealer-settle";
        table.NextTransitionAtUtc = nowUtc.Add(draw ? DealerCardDuration : ActionSettleDuration);
        table.Version = checked(table.Version + 1);
        table.UpdatedAtUtc = nowUtc;
    }

    private static void FinishDealer(BlackjackTableState table, DateTime nowUtc)
    {
        var dealer = BlackjackRules.Score(table.DealerCards);
        foreach (var player in table.Players.Where(player => player.TotalWagerCents > 0))
        {
            if (player.Outcome is null) player.Outcome = ResolveOutcome(player.Cards, dealer);
            if (player.SecondaryHand is { Outcome: null } secondary) secondary.Outcome = ResolveOutcome(secondary.Cards, dealer);
            if (player.Outcome != BlackjackOutcomes.PlayerBust) player.Status = "completed";
            if (player.SecondaryHand is { } hand && hand.Outcome != BlackjackOutcomes.PlayerBust) hand.Status = "completed";
            player.PayoutCents = PayoutFor(player);
        }
        table.DealerVisibleCardCount = table.DealerCards.Count;
        ClearTurn(table);
        table.Phase = "settlement";
        table.Version = checked(table.Version + 1);
        table.UpdatedAtUtc = nowUtc;
    }

    private static string ResolveOutcome(IReadOnlyList<string> cards, BlackjackHandValue dealer)
    {
        var hand = BlackjackRules.Score(cards);
        return hand.Bust ? BlackjackOutcomes.PlayerBust
            : dealer.Blackjack ? BlackjackOutcomes.DealerBlackjack
            : dealer.Bust || hand.Score > dealer.Score ? BlackjackOutcomes.PlayerWin
            : hand.Score == dealer.Score ? BlackjackOutcomes.Push : BlackjackOutcomes.DealerWin;
    }

    private static long PayoutForHand(string? outcome, long totalWagerCents, bool naturalAllowed) => outcome switch
    {
        BlackjackOutcomes.PlayerBlackjack when naturalAllowed => checked(totalWagerCents * 5 / 2),
        BlackjackOutcomes.PlayerBlackjack => checked(totalWagerCents * 2),
        BlackjackOutcomes.PlayerWin => checked(totalWagerCents * 2),
        BlackjackOutcomes.Push => totalWagerCents,
        BlackjackOutcomes.PlayerSurrender => totalWagerCents / 2,
        _ => 0
    };

    private static IReadOnlyList<string> CoreLegalActions(BlackjackTablePlayer player) => ActiveCards(player).Count == 2
        ? [BlackjackActions.Hit, BlackjackActions.Stand, BlackjackActions.Double]
        : [BlackjackActions.Hit, BlackjackActions.Stand];
    private static IReadOnlyList<BlackjackTablePlayer> Participants(BlackjackTableState table) =>
        table.Players.Where(player => player.WagerCents > 0).OrderBy(player => player.Seat).ToArray();
    private static bool HasPlayingHand(BlackjackTablePlayer player) =>
        player.Status == "playing" || player.SecondaryHand?.Status == "playing";
    private static bool ActiveHandIsPlaying(BlackjackTablePlayer player) => player.ActiveHandIndex == 0
        ? player.Status == "playing" : player.SecondaryHand?.Status == "playing";
    private static List<string> ActiveCards(BlackjackTablePlayer player) => player.ActiveHandIndex == 0
        ? player.Cards : player.SecondaryHand?.Cards ?? throw new InvalidOperationException("The active split hand is missing.");
    private static long ActiveWager(BlackjackTablePlayer player) => player.ActiveHandIndex == 0
        ? player.WagerCents : player.SecondaryHand?.WagerCents ?? throw new InvalidOperationException("The active split hand is missing.");
    private static int SplitValue(string card) => BlackjackRules.ParseCard(card).Rank switch
    {
        "A" => 11,
        "J" or "Q" or "K" => 10,
        var rank => int.Parse(rank, System.Globalization.CultureInfo.InvariantCulture)
    };
    private static void AddActiveWager(BlackjackTablePlayer player, long amount)
    {
        if (player.ActiveHandIndex == 0) player.TotalWagerCents = checked(player.TotalWagerCents + amount);
        else player.SecondaryHand!.TotalWagerCents = checked(player.SecondaryHand.TotalWagerCents + amount);
    }
    private static void SetActiveStatus(BlackjackTablePlayer player, string value)
    {
        if (player.ActiveHandIndex == 0) player.Status = value; else player.SecondaryHand!.Status = value;
    }
    private static void SetActiveOutcome(BlackjackTablePlayer player, string value)
    {
        if (player.ActiveHandIndex == 0) player.Outcome = value; else player.SecondaryHand!.Outcome = value;
    }
    private static void SetActiveLastAction(BlackjackTablePlayer player, string value)
    {
        if (player.ActiveHandIndex == 0) player.LastAction = value; else player.SecondaryHand!.LastAction = value;
    }
    private static void ClearTurn(BlackjackTableState table)
    {
        table.ActiveSeat = null;
        table.PendingSeat = null;
        table.Transition = null;
        table.NextTransitionAtUtc = null;
        table.ActionDeadlineAtUtc = null;
    }
    private static TimeSpan TurnPause(BlackjackTableState table, int seat)
    {
        var range = checked((int)(MaximumTurnPause - MinimumTurnPause).TotalMilliseconds + 1);
        var mixed = table.RoundSeed ^ ((ulong)(uint)table.Version << 32) ^ (uint)seat;
        return MinimumTurnPause.Add(TimeSpan.FromMilliseconds((long)(mixed % (ulong)range)));
    }
    private static string Draw(BlackjackTableState table)
    {
        if (table.NextCardIndex >= table.Deck.Count) throw new InvalidOperationException("The Blackjack table deck ran out of cards.");
        return table.Deck[table.NextCardIndex++];
    }
    private static void ValidateDeck(IReadOnlyList<string> deck)
    {
        if (deck.Count != 52 || deck.Distinct(StringComparer.Ordinal).Count() != 52)
            throw new ArgumentException("A Blackjack table deck must contain 52 unique cards.", nameof(deck));
        foreach (var card in deck) _ = BlackjackRules.ParseCard(card);
    }
}
