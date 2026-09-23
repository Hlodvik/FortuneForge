using FortuneForge.Games.Cards;
using FortuneForge.Games.TrickTaking;

namespace FortuneForge.Games.Tests;

public sealed class TrickTakingRulesTests
{
    [Fact]
    public void DealIsCompleteAndDeterministic()
    {
        var first = TrickTakingDealer.Deal(42);
        var replay = TrickTakingDealer.Deal(42);

        Assert.Equal(52, first.Hands.Sum(hand => hand.Cards.Count));
        Assert.Equal(52, first.Hands.SelectMany(hand => hand.Cards).Distinct().Count());
        Assert.Equal(
            first.Hands.SelectMany(hand => hand.Cards).Select(card => card.Code),
            replay.Hands.SelectMany(hand => hand.Cards).Select(card => card.Code));
    }

    [Fact]
    public void PlayerMustFollowTheLedSuit()
    {
        var trick = new TrickState(
            PlayerSeat.North,
            [new TrickPlay(PlayerSeat.North, Card(CardRank.Ten, CardSuit.Clubs))]);
        PlayingCard[] hand =
        [
            Card(CardRank.Two, CardSuit.Clubs),
            Card(CardRank.Ace, CardSuit.Spades),
        ];

        var exception = Assert.Throws<TrickTakingRuleException>(() =>
            TrickTakingRules.ValidatePlay(hand, trick, PlayerSeat.East, hand[1]));

        Assert.Contains("follow", exception.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void TrumpWinsOverTheLedSuit()
    {
        var trick = new TrickState(
            PlayerSeat.North,
            [
                new(PlayerSeat.North, Card(CardRank.Ace, CardSuit.Clubs)),
                new(PlayerSeat.East, Card(CardRank.Two, CardSuit.Spades)),
                new(PlayerSeat.South, Card(CardRank.King, CardSuit.Clubs)),
                new(PlayerSeat.West, Card(CardRank.Queen, CardSuit.Spades)),
            ]);

        Assert.Equal(PlayerSeat.West, TrickTakingRules.WinningSeat(trick, CardSuit.Spades));
        Assert.Equal(PlayerSeat.North, TrickTakingRules.WinningSeat(trick));
    }

    [Fact]
    public void DealRejectsCardsOutsideTheStandardDeck()
    {
        var cards = StandardDeck.Create().ToArray();
        cards[0] = new PlayingCard((CardRank)99, CardSuit.Clubs);
        var hands = Enum.GetValues<PlayerSeat>()
            .Select((seat, index) => new PlayerHand(seat, cards.Skip(index * 13).Take(13).ToArray()))
            .ToArray();

        Assert.Throws<TrickTakingRuleException>(() => TrickTakingRules.ValidateDeal(hands));
    }

    private static PlayingCard Card(CardRank rank, CardSuit suit) => new(rank, suit);
}
