namespace FortuneForge.Games.CasinoWar;

public sealed record CasinoWarTieBetSettlement(
    decimal Stake,
    bool Won,
    CasinoWarSettlementDisposition Disposition,
    decimal TotalReturn,
    CasinoWarOpeningOutcome OpeningOutcome)
{
    public decimal Profit => TotalReturn - Stake;
}

public static class CasinoWarTieBetPaytable
{
    public static CasinoWarTieBetSettlement Settle(CasinoWarOpeningResult opening, decimal stake)
    {
        ArgumentNullException.ThrowIfNull(opening);
        if (stake <= 0m)
            throw new ArgumentOutOfRangeException(nameof(stake), "A Casino War Tie side-bet stake must be positive.");

        ValidateOpeningOutcome(opening.Outcome);
        var won = opening.Outcome == CasinoWarOpeningOutcome.TieDecisionRequired;
        return new CasinoWarTieBetSettlement(
            stake,
            won,
            won ? CasinoWarSettlementDisposition.Win : CasinoWarSettlementDisposition.Loss,
            won ? stake * 11m : 0m,
            opening.Outcome);
    }

    private static void ValidateOpeningOutcome(CasinoWarOpeningOutcome outcome)
    {
        if (outcome is not CasinoWarOpeningOutcome.PlayerWin
            and not CasinoWarOpeningOutcome.DealerWin
            and not CasinoWarOpeningOutcome.TieDecisionRequired)
        {
            throw new ArgumentOutOfRangeException(nameof(outcome));
        }
    }
}
