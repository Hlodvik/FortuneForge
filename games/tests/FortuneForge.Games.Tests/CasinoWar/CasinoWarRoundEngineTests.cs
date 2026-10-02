using FortuneForge.Games.Cards;
using FortuneForge.Games.CasinoWar;

namespace FortuneForge.Games.Tests.CasinoWar;

public sealed class CasinoWarRoundEngineTests
{
    [Theory]
    [InlineData(CardRank.Ace, CardRank.King, CasinoWarSettlementDisposition.Win, CasinoWarRoundOutcome.PlayerOpeningWin, 20d)]
    [InlineData(CardRank.Two, CardRank.Three, CasinoWarSettlementDisposition.Loss, CasinoWarRoundOutcome.DealerOpeningWin, 0d)]
    public void StartCompletesImmediateOpeningOutcomes(
        CardRank playerRank,
        CardRank dealerRank,
        CasinoWarSettlementDisposition expectedDisposition,
        CasinoWarRoundOutcome expectedOutcome,
        double expectedReturn)
    {
        var round = CasinoWarRoundEngine.Start(Shoe(playerRank, dealerRank), 10m);

        Assert.Equal(CasinoWarRoundPhase.Completed, round.Phase);
        Assert.Null(round.TieDecision);
        Assert.Null(round.PlayerWarCard);
        Assert.Null(round.DealerWarCard);
        Assert.NotNull(round.Settlement);
        Assert.Equal(expectedDisposition, round.Settlement.Disposition);
        Assert.Equal(expectedOutcome, round.Settlement.Outcome);
        Assert.Equal(10m, round.Settlement.TotalWagered);
        Assert.Equal((decimal)expectedReturn, round.Settlement.TotalReturn);
        Assert.Equal((decimal)expectedReturn - 10m, round.Settlement.Profit);
        Assert.Equal(2, round.CardsConsumed);
    }

    [Fact]
    public void SurrenderCompletesAnOpeningTieForHalfThePrimaryStake()
    {
        var round = CasinoWarRoundEngine.Start(Shoe(CardRank.Queen, CardRank.Queen), 10m);
        var completed = CasinoWarRoundEngine.Decide(round, CasinoWarTieDecision.Surrender);

        Assert.Equal(CasinoWarRoundPhase.AwaitingTieDecision, round.Phase);
        Assert.Null(round.Settlement);
        Assert.Equal(CasinoWarRoundPhase.Completed, completed.Phase);
        Assert.Equal(CasinoWarTieDecision.Surrender, completed.TieDecision);
        Assert.Null(completed.PlayerWarCard);
        Assert.Null(completed.DealerWarCard);
        Assert.Equal(CasinoWarSettlementDisposition.Surrender, completed.Settlement!.Disposition);
        Assert.Equal(CasinoWarRoundOutcome.PlayerSurrendered, completed.Settlement.Outcome);
        Assert.Equal(10m, completed.Settlement.TotalWagered);
        Assert.Equal(5m, completed.Settlement.TotalReturn);
        Assert.Equal(-5m, completed.Settlement.Profit);
        Assert.Equal(2, completed.CardsConsumed);
    }

    [Fact]
    public void GoToWarCompletesAPlayerWinAndConsumesBurnsAndWarCardsInOrder()
    {
        PlayingCard[] shoe =
        [
            Card(CardRank.Queen, CardSuit.Clubs),
            Card(CardRank.Queen, CardSuit.Diamonds),
            Card(CardRank.Two, CardSuit.Hearts),
            Card(CardRank.Three, CardSuit.Spades),
            Card(CardRank.Four, CardSuit.Clubs),
            Card(CardRank.Ace, CardSuit.Diamonds),
            Card(CardRank.Five, CardSuit.Hearts),
            Card(CardRank.Six, CardSuit.Spades),
            Card(CardRank.Seven, CardSuit.Clubs),
            Card(CardRank.King, CardSuit.Diamonds),
        ];

        var completed = CasinoWarRoundEngine.Decide(
            CasinoWarRoundEngine.Start(shoe, 10m),
            CasinoWarTieDecision.GoToWar);

        Assert.Equal(shoe[5], completed.PlayerWarCard);
        Assert.Equal(shoe[9], completed.DealerWarCard);
        Assert.Equal(shoe, completed.ConsumedCards.ToArray());
        Assert.Equal(10, completed.CardsConsumed);
        Assert.Equal(CasinoWarSettlementDisposition.Win, completed.Settlement!.Disposition);
        Assert.Equal(CasinoWarRoundOutcome.PlayerWarWin, completed.Settlement.Outcome);
        Assert.Equal(20m, completed.Settlement.TotalWagered);
        Assert.Equal(30m, completed.Settlement.TotalReturn);
        Assert.Equal(10m, completed.Settlement.Profit);
    }

    [Fact]
    public void GoToWarCompletesADealerWin()
    {
        var completed = CasinoWarRoundEngine.Decide(
            CasinoWarRoundEngine.Start(WarShoe(CardRank.Two, CardRank.Ace), 10m),
            CasinoWarTieDecision.GoToWar);

        Assert.Equal(CasinoWarSettlementDisposition.Loss, completed.Settlement!.Disposition);
        Assert.Equal(CasinoWarRoundOutcome.DealerWarWin, completed.Settlement.Outcome);
        Assert.Equal(20m, completed.Settlement.TotalWagered);
        Assert.Equal(0m, completed.Settlement.TotalReturn);
        Assert.Equal(-20m, completed.Settlement.Profit);
    }

    [Fact]
    public void GoToWarAwardsASecondTieToThePlayer()
    {
        var completed = CasinoWarRoundEngine.Decide(
            CasinoWarRoundEngine.Start(WarShoe(CardRank.Ace, CardRank.Ace), 10m),
            CasinoWarTieDecision.GoToWar);

        Assert.Equal(CasinoWarSettlementDisposition.Win, completed.Settlement!.Disposition);
        Assert.Equal(CasinoWarRoundOutcome.PlayerWarTieWin, completed.Settlement.Outcome);
        Assert.Equal(20m, completed.Settlement.TotalWagered);
        Assert.Equal(40m, completed.Settlement.TotalReturn);
        Assert.Equal(20m, completed.Settlement.Profit);
    }

    [Fact]
    public void GoToWarRejectsAnInsufficientWarShoeWithoutMutatingTheRound()
    {
        var round = CasinoWarRoundEngine.Start(Shoe(CardRank.Queen, CardRank.Queen, CardRank.Two, CardRank.Three, CardRank.Four, CardRank.Five, CardRank.Six, CardRank.Seven, CardRank.Eight), 10m);

        Assert.Throws<ArgumentException>(() => CasinoWarRoundEngine.Decide(round, CasinoWarTieDecision.GoToWar));
        Assert.Equal(CasinoWarRoundPhase.AwaitingTieDecision, round.Phase);
        Assert.Equal(2, round.CardsConsumed);
    }

    [Fact]
    public void DecideRejectsNonTieRepeatAndMalformedDecisions()
    {
        var immediateRound = CasinoWarRoundEngine.Start(Shoe(CardRank.Ace, CardRank.King), 10m);
        var completedTieRound = CasinoWarRoundEngine.Decide(
            CasinoWarRoundEngine.Start(Shoe(CardRank.Queen, CardRank.Queen), 10m),
            CasinoWarTieDecision.Surrender);

        Assert.Throws<InvalidOperationException>(() => CasinoWarRoundEngine.Decide(immediateRound, CasinoWarTieDecision.Surrender));
        Assert.Throws<InvalidOperationException>(() => CasinoWarRoundEngine.Decide(completedTieRound, CasinoWarTieDecision.GoToWar));
        Assert.Throws<ArgumentOutOfRangeException>(() => CasinoWarRoundEngine.Decide(
            CasinoWarRoundEngine.Start(Shoe(CardRank.Queen, CardRank.Queen), 10m),
            (CasinoWarTieDecision)99));
    }

    [Fact]
    public void RoundUsesExactDecimalStakeArithmetic()
    {
        var completed = CasinoWarRoundEngine.Decide(
            CasinoWarRoundEngine.Start(WarShoe(CardRank.Ace, CardRank.King), 1.25m),
            CasinoWarTieDecision.GoToWar);

        Assert.Equal(2.5m, completed.Settlement!.TotalWagered);
        Assert.Equal(3.75m, completed.Settlement.TotalReturn);
        Assert.Equal(1.25m, completed.Settlement.Profit);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-1)]
    public void StartRejectsANonPositiveStake(decimal stake) =>
        Assert.Throws<ArgumentOutOfRangeException>(() => CasinoWarRoundEngine.Start(Shoe(CardRank.Ace, CardRank.King), stake));

    [Fact]
    public void StartRejectsNullAndInsufficientShoes()
    {
        Assert.Throws<ArgumentNullException>(() => CasinoWarRoundEngine.Start(null!, 10m));
        Assert.Throws<ArgumentException>(() => CasinoWarRoundEngine.Start(Shoe(CardRank.Ace), 10m));
    }

    private static PlayingCard[] WarShoe(CardRank playerWarRank, CardRank dealerWarRank) =>
    [
        Card(CardRank.Queen, CardSuit.Clubs),
        Card(CardRank.Queen, CardSuit.Diamonds),
        Card(CardRank.Two, CardSuit.Hearts),
        Card(CardRank.Three, CardSuit.Spades),
        Card(CardRank.Four, CardSuit.Clubs),
        Card(playerWarRank, CardSuit.Diamonds),
        Card(CardRank.Five, CardSuit.Hearts),
        Card(CardRank.Six, CardSuit.Spades),
        Card(CardRank.Seven, CardSuit.Clubs),
        Card(dealerWarRank, CardSuit.Diamonds),
    ];

    private static PlayingCard[] Shoe(params CardRank[] ranks) => ranks
        .Select((rank, index) => Card(rank, (CardSuit)(index % 4)))
        .ToArray();

    private static PlayingCard Card(CardRank rank, CardSuit suit) => new(rank, suit);
}
