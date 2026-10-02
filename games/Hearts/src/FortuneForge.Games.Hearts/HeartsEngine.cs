using FortuneForge.Games.Cards;
using FortuneForge.Games.TrickTaking;

namespace FortuneForge.Games.Hearts;

public static class HeartsEngine
{
    public static HeartsState Start(uint seed, HeartsPassDirection passDirection = HeartsPassDirection.Hold)
    {
        var deal = TrickTakingDealer.Deal(seed);
        var firstPlayer = deal.Hands.Single(hand => hand.Cards.Contains(HeartsRules.OpeningCard)).Seat;
        var phase = passDirection == HeartsPassDirection.Hold ? HeartsPhase.Playing : HeartsPhase.Passing;
        return new HeartsState(
            seed,
            firstPlayer,
            phase,
            deal.Hands,
            new TrickState(firstPlayer, []),
            [],
            Enum.GetValues<PlayerSeat>().Select(seat => new HeartsScore(seat, 0)).ToArray(),
            false,
            passDirection,
            []);
    }

    public static IReadOnlyList<PlayingCard> LegalCards(HeartsState state, PlayerSeat seat) =>
        HeartsRules.LegalCards(state, seat);

    public static HeartsTransition Apply(HeartsState state, HeartsCommand command)
    {
        ArgumentNullException.ThrowIfNull(state);
        ArgumentNullException.ThrowIfNull(command);
        return command switch
        {
            PassHeartsCards pass => Pass(state, pass),
            PlayHeartsCard play => Play(state, play),
            _ => throw new HeartsRuleException("The Hearts command is not supported."),
        };
    }

    private static HeartsTransition Pass(HeartsState state, PassHeartsCards command)
    {
        HeartsRules.ValidatePass(state, command.Seat, command.Cards);
        var passes = state.SubmittedPasses.Append(new HeartsPass(command.Seat, command.Cards.ToArray())).ToArray();
        if (passes.Length < TrickTakingRules.PlayerCount)
        {
            var waiting = state with { Passes = passes };
            return new HeartsTransition(waiting, HeartsEventType.CardsPassed, $"{command.Seat} submitted three cards.");
        }

        var hands = ApplyPasses(state.Hands, passes, state.PassDirection);
        var firstPlayer = hands.Single(hand => hand.Cards.Contains(HeartsRules.OpeningCard)).Seat;
        var next = state with
        {
            Hands = hands,
            Turn = firstPlayer,
            Phase = HeartsPhase.Playing,
            CurrentTrick = new TrickState(firstPlayer, []),
            Passes = passes,
        };
        return new HeartsTransition(next, HeartsEventType.CardsPassed, $"Cards passed {state.PassDirection.ToString().ToLowerInvariant()}. {firstPlayer} leads with the two of clubs.");
    }

    private static HeartsTransition Play(HeartsState state, PlayHeartsCard command)
    {
        if (state.Phase != HeartsPhase.Playing)
            throw new HeartsRuleException($"Hearts is in the {state.Phase} phase.");
        if (state.Turn != command.Seat)
            throw new HeartsRuleException($"It is {state.Turn}'s turn.");

        var hand = state.HandFor(command.Seat);
        HeartsRules.ValidatePlay(state, command.Seat, command.Card);
        var trick = state.CurrentTrick with
        {
            Plays = state.CurrentTrick.Plays.Append(new TrickPlay(command.Seat, command.Card)).ToArray(),
        };
        var hands = ReplaceHand(state.Hands, command.Seat, hand.Where(card => card != command.Card).ToArray());
        var played = state with
        {
            Hands = hands,
            CurrentTrick = trick,
            Turn = command.Seat.Next(),
            HeartsBroken = state.HeartsBroken || command.Card.Suit == CardSuit.Hearts,
        };

        if (!trick.IsComplete)
            return new HeartsTransition(played, HeartsEventType.CardPlayed, $"{command.Seat} played {command.Card.Code}.");

        return CompleteTrick(played, trick);
    }

    private static HeartsTransition CompleteTrick(HeartsState state, TrickState trick)
    {
        var winner = TrickTakingRules.WinningSeat(trick);
        var trickPoints = trick.Plays.Sum(play => HeartsRules.PointValue(play.Card));
        var scores = state.Scores
            .Select(score => score.Seat == winner ? score with { Points = score.Points + trickPoints } : score)
            .ToArray();
        var completed = new CompletedTrick(
            state.CompletedTricks.Count + 1,
            trick.Leader,
            winner,
            trick.Plays);
        var history = state.CompletedTricks.Append(completed).ToArray();
        var roundComplete = history.Length == TrickTakingRules.CardsPerPlayer;
        if (roundComplete)
            scores = HeartsRules.ApplyShootingTheMoon(scores).ToArray();

        var next = state with
        {
            Turn = winner,
            CurrentTrick = new TrickState(winner, []),
            CompletedTricks = history,
            Scores = scores,
            Phase = roundComplete ? HeartsPhase.Complete : HeartsPhase.Playing,
        };
        return new HeartsTransition(
            next,
            roundComplete ? HeartsEventType.RoundCompleted : HeartsEventType.TrickCompleted,
            roundComplete ? $"{winner} took the final trick." : $"{winner} took {trickPoints} points.");
    }

    private static IReadOnlyList<PlayerHand> ApplyPasses(
        IReadOnlyList<PlayerHand> hands,
        IReadOnlyList<HeartsPass> passes,
        HeartsPassDirection direction)
    {
        var result = hands.ToDictionary(hand => hand.Seat, hand => hand.Cards.ToList());
        foreach (var pass in passes)
            foreach (var card in pass.Cards)
                result[pass.Seat].Remove(card);
        foreach (var pass in passes)
        {
            var recipient = HeartsRules.PassTarget(pass.Seat, direction);
            result[recipient].AddRange(pass.Cards);
        }

        return Enum.GetValues<PlayerSeat>()
            .Select(seat => new PlayerHand(seat, result[seat].ToArray()))
            .ToArray();
    }

    private static IReadOnlyList<PlayerHand> ReplaceHand(
        IReadOnlyList<PlayerHand> hands,
        PlayerSeat seat,
        IReadOnlyList<PlayingCard> cards) => hands
        .Select(hand => hand.Seat == seat ? hand with { Cards = cards } : hand)
        .ToArray();
}
