namespace FortuneForge.Server.Numbers.Keno;

internal static class KenoOutcomeAudit
{
    internal const string Version = "keno-20-of-80-v3-outcome-v1";

    // Frozen copy of the v3 gross-return schedule. Keep this independent of
    // the production paytable so a changed rule cannot validate itself.
    private static readonly decimal[][] GrossReturns =
    [
        [0, 2.5m],
        [0, 0, 11],
        [0, 0, 2, 27],
        [0, 0, 1, 5, 72],
        [0, 0, 0, 2, 15, 465],
        [0, 0, 0, 1, 5, 55, 1_600],
        [0, 0, 0, 1, 2, 15, 150, 5_500],
        [0, 0, 0, 0, 2, 10, 60, 600, 15_000],
        [0, 0, 0, 0, 1, 4, 25, 215, 3_000, 50_000],
        [5, 0, 0, 0, 0, 2, 10, 55, 500, 4_500, 200_000]
    ];

    internal static bool Verify(KenoStoreRound round)
    {
        if (round.PaytableId != "fortuneforge-keno-v3" || round.WagerCents < KenoMoney.MinimumWagerCents ||
            round.WagerCents > KenoMoney.MaximumWagerCents || round.WagerCents % KenoMoney.WagerIncrementCents != 0)
            return false;
        var ticket = round.Ticket.Numbers;
        var draw = round.Draw.Numbers;
        if (ticket.Length is < 1 or > 10 || draw.Length != 20 ||
            ticket.Any(number => number is < 1 or > 80) || draw.Any(number => number is < 1 or > 80) ||
            ticket.Distinct().Count() != ticket.Length || draw.Distinct().Count() != draw.Length)
            return false;
        var hits = ticket.Intersect(draw).Count();
        if (hits != round.HitCount) return false;
        var total = checked(round.WagerCents * GrossReturns[ticket.Length - 1][hits]);
        return total == decimal.Truncate(total) && round.PayoutCents == (long)total;
    }
}
