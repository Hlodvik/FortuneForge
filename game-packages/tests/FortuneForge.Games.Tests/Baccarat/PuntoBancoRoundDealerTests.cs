using FortuneForge.Games.Baccarat;
using FortuneForge.Games.Cards;

namespace FortuneForge.Games.Tests.Baccarat;

public sealed class PuntoBancoRoundDealerTests
{
    [Fact]
    public void DealUsesPlayerBankerPlayerBankerOrderForTheInitialHands()
    {
        PlayingCard[] shoe =
        [
            Card(CardRank.Ace, CardSuit.Clubs),
            Card(CardRank.Two, CardSuit.Diamonds),
            Card(CardRank.Three, CardSuit.Hearts),
            Card(CardRank.Four, CardSuit.Spades),
            Card(CardRank.Ten, CardSuit.Clubs),
        ];

        var result = PuntoBancoRoundDealer.Deal(shoe);

        Assert.Equal([shoe[0], shoe[2]], result.PlayerHand.Cards.Take(2).ToArray());
        Assert.Equal([shoe[1], shoe[3]], result.BankerHand.Cards.ToArray());
        Assert.Equal(shoe, result.ConsumedCards.ToArray());
    }

    [Fact]
    public void DealStopsOnAPlayerNatural()
    {
        var result = PuntoBancoRoundDealer.Deal(Shoe(CardRank.Nine, CardRank.Two, CardRank.Ten, CardRank.Three, CardRank.Ace));

        Assert.True(result.EndedOnNatural);
        Assert.Equal(BaccaratRoundOutcome.Player, result.Outcome);
        Assert.Equal(4, result.CardsConsumed);
    }

    [Fact]
    public void DealStopsOnABankerNatural()
    {
        var result = PuntoBancoRoundDealer.Deal(Shoe(CardRank.Two, CardRank.Nine, CardRank.Three, CardRank.Ten, CardRank.Ace));

        Assert.True(result.EndedOnNatural);
        Assert.Equal(BaccaratRoundOutcome.Banker, result.Outcome);
        Assert.Equal(4, result.CardsConsumed);
    }

    [Fact]
    public void DealLetsTheBankerDrawWhenThePlayerStands()
    {
        var shoe = Shoe(CardRank.Ace, CardRank.Ten, CardRank.Five, CardRank.Ten, CardRank.Two);

        var result = PuntoBancoRoundDealer.Deal(shoe);

        Assert.Equal(2, result.PlayerHand.Cards.Length);
        Assert.Equal(3, result.BankerHand.Cards.Length);
        Assert.Equal(shoe[4], result.BankerHand.Cards[2]);
    }

    [Fact]
    public void DealLetsThePlayerDrawWhenTheBankerStands()
    {
        var shoe = Shoe(CardRank.Two, CardRank.Three, CardRank.Three, CardRank.Four, CardRank.Ten);

        var result = PuntoBancoRoundDealer.Deal(shoe);

        Assert.Equal(3, result.PlayerHand.Cards.Length);
        Assert.Equal(2, result.BankerHand.Cards.Length);
        Assert.Equal(shoe[4], result.PlayerHand.Cards[2]);
    }

    [Fact]
    public void DealLetsBothSidesDrawWhenTheTableauRequiresIt()
    {
        var shoe = Shoe(CardRank.Two, CardRank.Ten, CardRank.Three, CardRank.Four, CardRank.Two, CardRank.Six);

        var result = PuntoBancoRoundDealer.Deal(shoe);

        Assert.Equal(3, result.PlayerHand.Cards.Length);
        Assert.Equal(3, result.BankerHand.Cards.Length);
        Assert.Equal(6, result.CardsConsumed);
        Assert.Equal(shoe, result.ConsumedCards.ToArray());
    }

    [Fact]
    public void DealCanEndInATie()
    {
        var result = PuntoBancoRoundDealer.Deal(Shoe(CardRank.Four, CardRank.Four, CardRank.Four, CardRank.Four));

        Assert.Equal(BaccaratRoundOutcome.Tie, result.Outcome);
    }

    [Fact]
    public void DealRejectsAnInsufficientInitialShoe()
    {
        Assert.Throws<ArgumentException>(() =>
            PuntoBancoRoundDealer.Deal(Shoe(CardRank.Ace, CardRank.Two, CardRank.Three)));
    }

    [Fact]
    public void DealRejectsAnInsufficientShoeWhenThePlayerMustDraw()
    {
        Assert.Throws<ArgumentException>(() =>
            PuntoBancoRoundDealer.Deal(Shoe(CardRank.Two, CardRank.Three, CardRank.Three, CardRank.Four)));
    }

    [Fact]
    public void DealRejectsAnInsufficientShoeWhenTheBankerMustDraw()
    {
        Assert.Throws<ArgumentException>(() =>
            PuntoBancoRoundDealer.Deal(Shoe(CardRank.Ace, CardRank.Ten, CardRank.Five, CardRank.Ten)));
    }

    private static PlayingCard Card(CardRank rank, CardSuit suit) => new(rank, suit);

    private static PlayingCard[] Shoe(params CardRank[] ranks) => ranks
        .Select((rank, index) => new PlayingCard(rank, (CardSuit)(index % 4)))
        .ToArray();
}
