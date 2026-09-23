using FortuneForge.Games.Cards;

namespace FortuneForge.Games.TrickTaking;

public static class TrickTakingRules
{
    public const int PlayerCount = 4;
    public const int CardsPerPlayer = 13;

    public static IReadOnlyList<PlayingCard> LegalCards(
        IReadOnlyList<PlayingCard> hand,
        TrickState trick)
    {
        ArgumentNullException.ThrowIfNull(hand);
        ArgumentNullException.ThrowIfNull(trick);
        ValidateTrickShape(trick);

        if (trick.LeadSuit is not { } leadSuit)
            return hand.ToArray();

        var following = hand.Where(card => card.Suit == leadSuit).ToArray();
        return following.Length == 0 ? hand.ToArray() : following;
    }

    public static void ValidatePlay(
        IReadOnlyList<PlayingCard> hand,
        TrickState trick,
        PlayerSeat seat,
        PlayingCard card)
    {
        ArgumentNullException.ThrowIfNull(hand);
        ArgumentNullException.ThrowIfNull(trick);
        ValidateTrickShape(trick);

        if (trick.IsComplete)
            throw new TrickTakingRuleException("The trick is already complete.");
        if (trick.NextPlayer != seat)
            throw new TrickTakingRuleException($"It is {trick.NextPlayer}'s turn.");
        if (!hand.Contains(card))
            throw new TrickTakingRuleException("The selected card is not in the player's hand.");
        if (!LegalCards(hand, trick).Contains(card))
            throw new TrickTakingRuleException("A player must follow the led suit when able.");
    }

    public static PlayerSeat WinningSeat(TrickState trick, CardSuit? trumpSuit = null)
    {
        ArgumentNullException.ThrowIfNull(trick);
        ValidateTrickShape(trick);
        if (!trick.IsComplete)
            throw new TrickTakingRuleException("Four cards are required to resolve a trick.");

        var leadSuit = trick.LeadSuit!.Value;
        return trick.Plays
            .Where(play => IsContender(play.Card, trick.Plays, leadSuit, trumpSuit))
            .MaxBy(play => RankValue(play.Card.Rank))!
            .Seat;
    }

    public static void ValidateDeal(IReadOnlyList<PlayerHand> hands)
    {
        ArgumentNullException.ThrowIfNull(hands);
        if (hands.Count != PlayerCount || hands.Select(hand => hand.Seat).Distinct().Count() != PlayerCount)
            throw new TrickTakingRuleException("A deal requires one hand for each of four seats.");
        if (hands.Any(hand => hand.Cards.Count != CardsPerPlayer))
            throw new TrickTakingRuleException("Each player must receive thirteen cards.");

        var cards = hands.SelectMany(hand => hand.Cards).ToArray();
        var standardDeck = StandardDeck.Create().ToHashSet();
        if (cards.Length != standardDeck.Count || !standardDeck.SetEquals(cards))
            throw new TrickTakingRuleException("A deal must contain every standard card exactly once.");
    }

    private static bool IsContender(
        PlayingCard card,
        IReadOnlyList<TrickPlay> plays,
        CardSuit leadSuit,
        CardSuit? trumpSuit)
    {
        var hasTrump = trumpSuit is { } trump && plays.Any(play => play.Card.Suit == trump);
        return hasTrump ? card.Suit == trumpSuit : card.Suit == leadSuit;
    }

    private static int RankValue(CardRank rank) => rank == CardRank.Ace ? 14 : (int)rank;

    private static void ValidateTrickShape(TrickState trick)
    {
        if (trick.Plays.Count > PlayerCount)
            throw new TrickTakingRuleException("A trick cannot contain more than four plays.");

        for (var index = 0; index < trick.Plays.Count; index++)
        {
            if (trick.Plays[index].Seat != trick.Leader.Advance(index))
                throw new TrickTakingRuleException("Trick plays must follow clockwise seat order.");
        }
    }
}
