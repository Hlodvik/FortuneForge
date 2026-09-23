using FortuneForge.Games.Cards;
using FortuneForge.Games.TrickTaking;

namespace FortuneForge.Games.Hearts;

public static class HeartsRules
{
    public static readonly PlayingCard OpeningCard = new(CardRank.Two, CardSuit.Clubs);

    public static IReadOnlyList<PlayingCard> LegalCards(HeartsState state, PlayerSeat seat)
    {
        ArgumentNullException.ThrowIfNull(state);
        ValidateSeat(seat);
        if (state.Phase != HeartsPhase.Playing)
            throw new HeartsRuleException($"Hearts is in the {state.Phase} phase.");

        var hand = state.HandFor(seat);
        var legal = TrickTakingRules.LegalCards(hand, state.CurrentTrick);
        if (state.CompletedTricks.Count == 0 && state.CurrentTrick.Plays.Count == 0)
            return hand.Contains(OpeningCard) ? [OpeningCard] : legal;

        if (state.CurrentTrick.Plays.Count == 0 &&
            !state.HeartsBroken &&
            legal.Any(card => card.Suit != CardSuit.Hearts))
        {
            legal = legal.Where(card => card.Suit != CardSuit.Hearts).ToArray();
        }

        if (state.CompletedTricks.Count == 0)
        {
            var safe = legal.Where(card => PointValue(card) == 0).ToArray();
            if (safe.Length > 0)
                legal = safe;
        }

        return legal;
    }

    public static void ValidatePass(HeartsState state, PlayerSeat seat, IReadOnlyList<PlayingCard> cards)
    {
        ArgumentNullException.ThrowIfNull(state);
        ArgumentNullException.ThrowIfNull(cards);
        ValidateSeat(seat);
        if (state.Phase != HeartsPhase.Passing)
            throw new HeartsRuleException("Cards can only be passed before the round begins.");
        if (state.PassDirection == HeartsPassDirection.Hold)
            throw new HeartsRuleException("This Hearts round does not pass cards.");
        if (state.SubmittedPasses.Any(pass => pass.Seat == seat))
            throw new HeartsRuleException("That player has already submitted a pass.");
        if (cards.Count != 3 || cards.Distinct().Count() != 3)
            throw new HeartsRuleException("Hearts passes must contain exactly three different cards.");
        var hand = state.HandFor(seat);
        if (cards.Any(card => !hand.Contains(card)))
            throw new HeartsRuleException("A Hearts pass can only contain cards in the player's hand.");
    }

    public static void ValidatePlay(HeartsState state, PlayerSeat seat, PlayingCard card)
    {
        ArgumentNullException.ThrowIfNull(state);
        ValidateSeat(seat);
        var hand = state.HandFor(seat);
        if (state.CompletedTricks.Count == 0 &&
            state.CurrentTrick.Plays.Count == 0 &&
            card != OpeningCard)
        {
            throw new HeartsRuleException("The first trick must be led with the two of clubs.");
        }
        try
        {
            TrickTakingRules.ValidatePlay(hand, state.CurrentTrick, seat, card);
        }
        catch (TrickTakingRuleException exception)
        {
            throw new HeartsRuleException(exception.Message);
        }

        if (!LegalCards(state, seat).Contains(card))
        {
            if (state.CompletedTricks.Count == 0 && PointValue(card) > 0)
                throw new HeartsRuleException("A point card cannot be played on the first trick while a safe card is available.");
            if (state.CurrentTrick.Plays.Count == 0 && card.Suit == CardSuit.Hearts)
                throw new HeartsRuleException("Hearts cannot lead until broken while another suit is available.");
            throw new HeartsRuleException("That card is not legal in the current Hearts trick.");
        }
    }

    public static int PointValue(PlayingCard card) => card switch
    {
        { Suit: CardSuit.Hearts } => 1,
        { Suit: CardSuit.Spades, Rank: CardRank.Queen } => 13,
        _ => 0,
    };

    public static PlayerSeat PassTarget(PlayerSeat seat, HeartsPassDirection direction) => direction switch
    {
        HeartsPassDirection.Left => seat.Next(),
        HeartsPassDirection.Right => seat.Advance(3),
        HeartsPassDirection.Across => seat.Partner(),
        HeartsPassDirection.Hold => seat,
        _ => throw new ArgumentOutOfRangeException(nameof(direction)),
    };

    public static HeartsPassDirection NextPassDirection(HeartsPassDirection direction) => direction switch
    {
        HeartsPassDirection.Left => HeartsPassDirection.Right,
        HeartsPassDirection.Right => HeartsPassDirection.Across,
        HeartsPassDirection.Across => HeartsPassDirection.Hold,
        HeartsPassDirection.Hold => HeartsPassDirection.Left,
        _ => throw new ArgumentOutOfRangeException(nameof(direction)),
    };

    public static IReadOnlyList<HeartsScore> ApplyShootingTheMoon(IReadOnlyList<HeartsScore> scores)
    {
        ArgumentNullException.ThrowIfNull(scores);
        var shooter = scores.SingleOrDefault(score => score.Points == 26);
        return shooter is null
            ? scores.ToArray()
            : scores.Select(score => score with { Points = score.Seat == shooter.Seat ? 0 : 26 }).ToArray();
    }

    private static void ValidateSeat(PlayerSeat seat)
    {
        if (!Enum.IsDefined(seat))
            throw new HeartsRuleException("A Hearts seat is invalid.");
    }
}
