namespace FortuneForge.Games.ThreeCardPoker;

public static class ThreeCardPokerTableEngine
{
    public const int Capacity = 5;
    public static readonly TimeSpan HumanGrace = TimeSpan.FromSeconds(5);
    public static readonly TimeSpan ActionDuration = TimeSpan.FromMinutes(1);
    public static readonly TimeSpan WagerDuration = TimeSpan.FromSeconds(20);
    public static readonly TimeSpan NextRoundCountdownDuration = TimeSpan.FromSeconds(5);
    public static readonly TimeSpan SettlementDisplayDuration = TimeSpan.FromSeconds(4);
    public static readonly TimeSpan WagerAdjustmentDuration = TimeSpan.FromMilliseconds(800);
    public static readonly TimeSpan ActionSettleDuration = TimeSpan.FromMilliseconds(650);
    public static readonly TimeSpan InitialCardDuration = TimeSpan.FromMilliseconds(160);
    public static readonly TimeSpan DealerCardDuration = TimeSpan.FromMilliseconds(700);

    public static void PrepareForBetting(ThreeCardPokerTableState table)
    {
        table.Deck = [];
        table.NextCardIndex = 0;
        table.DealerCards = [];
        table.DealerVisibleCardCount = 0;
        foreach (var player in table.Players)
        {
            player.Cards = [];
            player.WagerCents = player.TotalWagerCents = player.PayoutCents = 0;
            player.PairPlusCents = player.PlayWagerCents = 0;
            player.AnteBonusPayoutCents = player.PairPlusPayoutCents = 0;
            player.Outcome = player.LastAction = null;
        }
    }

    public static void Deal(ThreeCardPokerTableState table, IReadOnlyList<string> deck, ulong roundSeed, DateTime nowUtc)
    {
        BeginRound(table, deck, roundSeed, nowUtc);
        for (var pass = 0; pass < 3; pass++)
        {
            foreach (var player in Participants(table)) player.Cards.Add(Draw(table));
            table.DealerCards.Add(Draw(table));
        }
        FinishDeal(table, nowUtc);
    }

    public static void BeginDeal(ThreeCardPokerTableState table, IReadOnlyList<string> deck, ulong roundSeed, DateTime nowUtc)
    {
        BeginRound(table, deck, roundSeed, nowUtc);
        table.Phase = ThreeCardPokerTablePhases.Dealing;
        table.Transition = "initial-deal";
        table.NextTransitionAtUtc = nowUtc.Add(InitialCardDuration);
        Changed(table, nowUtc);
    }

    private static void BeginRound(ThreeCardPokerTableState table, IReadOnlyList<string> deck, ulong roundSeed, DateTime nowUtc)
    {
        if (table.Phase != ThreeCardPokerTablePhases.Betting)
            throw new ThreeCardPokerTableConflictException("This Three Card Poker table is not accepting a new deal.");
        if (table.Players.Count is < 1 or > Capacity || table.Players.Select(player => player.Seat).Distinct().Count() != table.Players.Count)
            throw new InvalidOperationException("A Three Card Poker table requires one through five distinct seats.");
        if (!table.Players.Any(player => player.NextWagerCents > 0))
            throw new ThreeCardPokerTableConflictException("At least one player must wager before dealing.");
        if (deck.Count != 52 || deck.Distinct(StringComparer.Ordinal).Count() != 52)
            throw new ArgumentException("Three Card Poker uses one complete 52-card deck.", nameof(deck));
        foreach (var card in deck) _ = ThreeCardPokerRules.ParseCard(card);
        PrepareForBetting(table);
        table.RoundNumber = checked(table.RoundNumber + 1);
        table.RoundSeed = roundSeed;
        table.Deck = deck.ToArray();
        table.RoundAccountingSettled = false;
        table.Phase = ThreeCardPokerTablePhases.Active;
        table.WagerDeadlineAtUtc = null;
        ClearTurn(table);
        foreach (var player in table.Players)
        {
            player.WagerCents = player.NextWagerCents;
            player.PairPlusCents = player.NextPairPlusCents;
            player.TotalWagerCents = checked(player.WagerCents + player.PairPlusCents);
            player.NextWagerCents = player.NextPairPlusCents = 0;
            player.Status = player.WagerCents > 0 ? "playing" : "sitting-out";
        }
        Changed(table, nowUtc);
    }

    private static void FinishDeal(ThreeCardPokerTableState table, DateTime nowUtc)
    {
        table.Phase = ThreeCardPokerTablePhases.Active;
        ClearTurn(table);
        ActivateNext(table, nowUtc);
    }

    public static IReadOnlyList<string> LegalActions(ThreeCardPokerTableState table, ThreeCardPokerTablePlayer player) =>
        table.Phase == ThreeCardPokerTablePhases.Active && table.Transition is null &&
        table.ActiveSeat == player.Seat && player.Status == "playing" && !player.LeavingAfterRound
            ? [ThreeCardPokerActions.Play, ThreeCardPokerActions.Fold] : [];

    public static long AdditionalWagerFor(ThreeCardPokerTablePlayer player, string action) =>
        action == ThreeCardPokerActions.Play ? player.WagerCents : 0;
    public static long TotalCommitted(ThreeCardPokerTablePlayer player) => player.TotalWagerCents;

    public static void ApplyAction(ThreeCardPokerTableState table, string actorId, string action, DateTime nowUtc)
    {
        var player = table.Players.SingleOrDefault(value => value.ActorId == actorId)
            ?? throw new ThreeCardPokerTableNotFoundException("This Three Card Poker seat was not found.");
        action = action.Trim().ToLowerInvariant();
        if (!LegalActions(table, player).Contains(action))
            throw new ThreeCardPokerTableIllegalActionException("Choose play or fold during your turn.");
        ApplyActionCore(table, player, action, nowUtc);
    }

    private static void ApplyActionCore(ThreeCardPokerTableState table, ThreeCardPokerTablePlayer player, string action, DateTime nowUtc)
    {
        player.LastAction = action;
        if (action == ThreeCardPokerActions.Play)
        {
            player.PlayWagerCents = player.WagerCents;
            player.TotalWagerCents = checked(player.TotalWagerCents + player.PlayWagerCents);
            player.Status = "played";
        }
        else
        {
            player.Status = "folded";
            player.Outcome = "fold";
        }
        ClearTurn(table);
        table.Transition = "action-settle";
        table.NextTransitionAtUtc = nowUtc.Add(ActionSettleDuration);
        Changed(table, nowUtc);
    }

    public static void MarkLeaving(ThreeCardPokerTableState table, ThreeCardPokerTablePlayer player, DateTime nowUtc)
    {
        player.LeavingAfterRound = true;
        if (table.Phase == ThreeCardPokerTablePhases.Active && table.ActiveSeat == player.Seat && table.Transition is null)
            ApplyActionCore(table, player, ThreeCardPokerActions.Fold, nowUtc);
        player.LastAction = "left-table";
        Changed(table, nowUtc);
    }

    public static void AdvanceAutomatedTurns(ThreeCardPokerTableState table, DateTime nowUtc)
    {
        if (table.NextTransitionAtUtc is not { } deadline || nowUtc < deadline) return;
        if (table.Phase == ThreeCardPokerTablePhases.Dealing)
        {
            // Preserve card-by-card table animation while advancing missed timer ticks.
            while (table.Phase == ThreeCardPokerTablePhases.Dealing && table.NextTransitionAtUtc <= nowUtc)
            {
                var cardAt = table.NextTransitionAtUtc!.Value;
                var participants = Participants(table);
                var slot = table.NextCardIndex % (participants.Count + 1);
                if (slot == participants.Count) table.DealerCards.Add(Draw(table));
                else participants[slot].Cards.Add(Draw(table));
                if (table.NextCardIndex == 3 * (participants.Count + 1)) FinishDeal(table, cardAt);
                else table.NextTransitionAtUtc = cardAt.Add(InitialCardDuration);
                Changed(table, cardAt);
            }
        }
        else if (table.Phase == ThreeCardPokerTablePhases.Active && table.Transition == "action-settle")
        {
            ClearTurn(table);
            ActivateNext(table, nowUtc);
        }
        else if (table.Phase == ThreeCardPokerTablePhases.Dealer)
        {
            table.DealerVisibleCardCount = table.DealerCards.Count;
            Settle(table, nowUtc);
        }
    }

    private static void ActivateNext(ThreeCardPokerTableState table, DateTime nowUtc)
    {
        var next = Participants(table).FirstOrDefault(player => player.Status == "playing");
        if (next is null)
        {
            table.Phase = ThreeCardPokerTablePhases.Dealer;
            table.Transition = "dealer-reveal";
            table.NextTransitionAtUtc = nowUtc.Add(DealerCardDuration);
            ClearAction(table);
        }
        else
        {
            table.ActiveSeat = next.Seat;
            table.ActionDeadlineAtUtc = nowUtc.Add(ActionDuration);
            if (next.LeavingAfterRound) ApplyActionCore(table, next, ThreeCardPokerActions.Fold, nowUtc);
        }
        Changed(table, nowUtc);
    }

    public static void Settle(ThreeCardPokerTableState table, DateTime nowUtc)
    {
        var dealer = ThreeCardPokerRules.Evaluate(table.DealerCards);
        foreach (var player in Participants(table))
        {
            var hand = ThreeCardPokerRules.Evaluate(player.Cards);
            var pairOdds = ThreeCardPokerRules.PairPlusOdds(hand.Category);
            // Pair Plus is independent of Ante/Play and pays even after folding.
            player.PairPlusPayoutCents = pairOdds == 0 ? 0 : checked(player.PairPlusCents * (pairOdds + 1));
            long mainPayout = 0;
            if (player.PlayWagerCents > 0)
            {
                player.AnteBonusPayoutCents = checked(player.WagerCents * ThreeCardPokerRules.AnteBonusOdds(hand.Category));
                if (!ThreeCardPokerRules.DealerQualifies(dealer))
                {
                    player.Outcome = "dealer-not-qualified";
                    mainPayout = checked(player.WagerCents * 2 + player.PlayWagerCents);
                }
                else
                {
                    var comparison = hand.CompareTo(dealer);
                    player.Outcome = comparison > 0 ? "player-win" : comparison == 0 ? "push" : "dealer-win";
                    mainPayout = comparison > 0 ? checked((player.WagerCents + player.PlayWagerCents) * 2)
                        : comparison == 0 ? checked(player.WagerCents + player.PlayWagerCents) : 0;
                }
            }
            else player.Outcome = "fold";
            player.PayoutCents = checked(mainPayout + player.AnteBonusPayoutCents + player.PairPlusPayoutCents);
            player.Status = "completed";
        }
        table.DealerVisibleCardCount = table.DealerCards.Count;
        ClearTurn(table);
        table.Phase = ThreeCardPokerTablePhases.Settlement;
        Changed(table, nowUtc);
    }

    private static IReadOnlyList<ThreeCardPokerTablePlayer> Participants(ThreeCardPokerTableState table) =>
        table.Players.Where(player => player.WagerCents > 0).OrderBy(player => player.Seat).ToArray();
    private static string Draw(ThreeCardPokerTableState table) => table.Deck[table.NextCardIndex++];
    private static void ClearAction(ThreeCardPokerTableState table)
    {
        table.ActiveSeat = table.PendingSeat = null;
        table.ActionDeadlineAtUtc = null;
    }
    private static void ClearTurn(ThreeCardPokerTableState table)
    {
        ClearAction(table);
        table.Transition = null;
        table.NextTransitionAtUtc = null;
    }
    private static void Changed(ThreeCardPokerTableState table, DateTime nowUtc)
    {
        table.Version = checked(table.Version + 1);
        table.UpdatedAtUtc = nowUtc;
    }
}
