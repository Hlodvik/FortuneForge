namespace FortuneForge.Games.Baccarat;

public enum BaccaratBetDisposition
{
    Win,
    Loss,
    Push,
    NotPlaced,
}

public sealed record BaccaratPairSettlement(decimal Stake, BaccaratBetDisposition Disposition, decimal Profit, decimal TotalReturn)
{
    public static BaccaratPairSettlement NotPlaced { get; } = new(0m, BaccaratBetDisposition.NotPlaced, 0m, 0m);
}

public sealed record BaccaratBetSettlement
{
    public BaccaratBetSettlement(
        BaccaratBetSide BetSide,
        decimal Stake,
        BaccaratRoundOutcome RoundOutcome,
        BaccaratBetDisposition Disposition,
        decimal Profit,
        decimal TotalReturn)
    {
        if (Stake <= 0)
            throw new ArgumentOutOfRangeException(nameof(Stake), "A Baccarat stake must be positive.");
        if (TotalReturn < 0)
            throw new ArgumentOutOfRangeException(nameof(TotalReturn), "A Baccarat return cannot be negative.");
        if (Profit != TotalReturn - Stake)
            throw new ArgumentException("Profit must equal total return minus stake.");

        this.BetSide = BetSide;
        this.Stake = Stake;
        this.RoundOutcome = RoundOutcome;
        this.Disposition = Disposition;
        this.Profit = Profit;
        this.TotalReturn = TotalReturn;
    }

    public BaccaratBetSide BetSide { get; }
    public decimal Stake { get; }
    public BaccaratRoundOutcome RoundOutcome { get; }
    public BaccaratBetDisposition Disposition { get; }
    public decimal Profit { get; }
    public decimal TotalReturn { get; }
}

public static class PuntoBancoPaytable
{
    public static BaccaratBetSettlement Settle(
        BaccaratBetSide betSide,
        decimal stake,
        PuntoBancoRoundResult completedRound)
    {
        ArgumentNullException.ThrowIfNull(completedRound);
        return Settle(betSide, stake, completedRound.Outcome);
    }

    public static BaccaratBetSettlement Settle(
        BaccaratBetSide betSide,
        decimal stake,
        BaccaratRoundOutcome roundOutcome)
    {
        if (stake <= 0)
            throw new ArgumentOutOfRangeException(nameof(stake), "A Baccarat stake must be positive.");
        ValidateBetSide(betSide);
        ValidateRoundOutcome(roundOutcome);

        var disposition = roundOutcome == BaccaratRoundOutcome.Tie && betSide is BaccaratBetSide.Player or BaccaratBetSide.Banker
            ? BaccaratBetDisposition.Push
            : BetMatchesOutcome(betSide, roundOutcome)
                ? BaccaratBetDisposition.Win
                : BaccaratBetDisposition.Loss;
        var totalReturn = disposition switch
        {
            BaccaratBetDisposition.Push => stake,
            BaccaratBetDisposition.Win when betSide == BaccaratBetSide.Player => stake * 2m,
            BaccaratBetDisposition.Win when betSide == BaccaratBetSide.Banker =>
                decimal.Round(stake * 1.95m, 2, MidpointRounding.AwayFromZero),
            BaccaratBetDisposition.Win => stake * 9m,
            _ => 0m,
        };

        return new BaccaratBetSettlement(
            betSide,
            stake,
            roundOutcome,
            disposition,
            totalReturn - stake,
            totalReturn);
    }

    /// <summary>A pair wins when the first two dealt cards on that side share a rank, regardless of the third-card draw.</summary>
    public static BaccaratPairSettlement SettlePair(decimal stake, BaccaratHand hand)
    {
        ArgumentNullException.ThrowIfNull(hand);
        if (stake < 0m)
            throw new ArgumentOutOfRangeException(nameof(stake), "A Baccarat pair stake cannot be negative.");
        if (stake == 0m) return BaccaratPairSettlement.NotPlaced;

        var won = hand.Cards[0].Rank == hand.Cards[1].Rank;
        var totalReturn = won ? stake * 12m : 0m;
        return new BaccaratPairSettlement(
            stake,
            won ? BaccaratBetDisposition.Win : BaccaratBetDisposition.Loss,
            totalReturn - stake,
            totalReturn);
    }

    private static bool BetMatchesOutcome(BaccaratBetSide betSide, BaccaratRoundOutcome roundOutcome) =>
        (betSide, roundOutcome) is
            (BaccaratBetSide.Player, BaccaratRoundOutcome.Player) or
            (BaccaratBetSide.Banker, BaccaratRoundOutcome.Banker) or
            (BaccaratBetSide.Tie, BaccaratRoundOutcome.Tie);

    private static void ValidateBetSide(BaccaratBetSide betSide)
    {
        if (!Enum.IsDefined(betSide))
            throw new ArgumentOutOfRangeException(nameof(betSide));
    }

    private static void ValidateRoundOutcome(BaccaratRoundOutcome roundOutcome)
    {
        if (!Enum.IsDefined(roundOutcome))
            throw new ArgumentOutOfRangeException(nameof(roundOutcome));
    }
}
