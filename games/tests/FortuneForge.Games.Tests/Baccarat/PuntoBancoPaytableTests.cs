using System.Collections.Immutable;
using FortuneForge.Games.Baccarat;
using FortuneForge.Games.Cards;

namespace FortuneForge.Games.Tests.Baccarat;

public sealed class PuntoBancoPaytableTests
{
    [Theory]
    [InlineData(BaccaratBetSide.Player, BaccaratRoundOutcome.Player, BaccaratBetDisposition.Win, 2)]
    [InlineData(BaccaratBetSide.Player, BaccaratRoundOutcome.Banker, BaccaratBetDisposition.Loss, 0)]
    [InlineData(BaccaratBetSide.Player, BaccaratRoundOutcome.Tie, BaccaratBetDisposition.Push, 1)]
    [InlineData(BaccaratBetSide.Banker, BaccaratRoundOutcome.Player, BaccaratBetDisposition.Loss, 0)]
    [InlineData(BaccaratBetSide.Banker, BaccaratRoundOutcome.Banker, BaccaratBetDisposition.Win, 1.95)]
    [InlineData(BaccaratBetSide.Banker, BaccaratRoundOutcome.Tie, BaccaratBetDisposition.Push, 1)]
    [InlineData(BaccaratBetSide.Tie, BaccaratRoundOutcome.Player, BaccaratBetDisposition.Loss, 0)]
    [InlineData(BaccaratBetSide.Tie, BaccaratRoundOutcome.Banker, BaccaratBetDisposition.Loss, 0)]
    [InlineData(BaccaratBetSide.Tie, BaccaratRoundOutcome.Tie, BaccaratBetDisposition.Win, 9)]
    public void SettleCoversEveryBetSideAndRoundOutcome(
        BaccaratBetSide betSide,
        BaccaratRoundOutcome outcome,
        BaccaratBetDisposition expectedDisposition,
        decimal returnMultiplier)
    {
        const decimal stake = 10m;

        var settlement = PuntoBancoPaytable.Settle(betSide, stake, CompletedRound(outcome));

        Assert.Equal(expectedDisposition, settlement.Disposition);
        Assert.Equal(stake * returnMultiplier, settlement.TotalReturn);
        Assert.Equal(settlement.TotalReturn - stake, settlement.Profit);
        Assert.Equal(betSide, settlement.BetSide);
        Assert.Equal(outcome, settlement.RoundOutcome);
    }

    [Fact]
    public void BankerWinAppliesFivePercentCommissionToDecimalStakes()
    {
        var settlement = PuntoBancoPaytable.Settle(
            BaccaratBetSide.Banker,
            12.34m,
            BaccaratRoundOutcome.Banker);

        Assert.Equal(24.063m, settlement.TotalReturn);
        Assert.Equal(11.723m, settlement.Profit);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-0.01)]
    public void SettleRejectsNonPositiveStakes(decimal stake)
    {
        Assert.Throws<ArgumentOutOfRangeException>(() =>
            PuntoBancoPaytable.Settle(BaccaratBetSide.Player, stake, BaccaratRoundOutcome.Player));
    }

    private static PuntoBancoRoundResult CompletedRound(BaccaratRoundOutcome outcome)
    {
        var hand = new BaccaratHand(
        [
            new PlayingCard(CardRank.Four, CardSuit.Clubs),
            new PlayingCard(CardRank.Three, CardSuit.Diamonds),
        ]);
        return new PuntoBancoRoundResult(hand, hand, outcome, false, ImmutableArray<PlayingCard>.Empty);
    }
}
