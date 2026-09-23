namespace FortuneForge.Games.SicBo;

public sealed record SicBoBetSettlement
{
    public SicBoBetSettlement(
        SicBoBet bet,
        SicBoRoll roll,
        bool won,
        decimal profitOdds,
        decimal profit,
        decimal totalReturn)
    {
        ArgumentNullException.ThrowIfNull(bet);
        ArgumentNullException.ThrowIfNull(roll);
        if (profitOdds < 0m)
            throw new ArgumentOutOfRangeException(nameof(profitOdds), "Sic Bo profit odds cannot be negative.");
        if (won && profitOdds <= 0m)
            throw new ArgumentOutOfRangeException(nameof(profitOdds), "A winning Sic Bo settlement requires positive profit odds.");
        if (!won && profitOdds != 0m)
            throw new ArgumentException("A losing Sic Bo settlement must have zero profit odds.", nameof(profitOdds));

        var expectedProfit = won ? bet.Stake * profitOdds : -bet.Stake;
        var expectedReturn = won ? bet.Stake * (profitOdds + 1m) : 0m;
        if (profit != expectedProfit)
            throw new ArgumentException("Sic Bo settlement profit does not match the stake and profit odds.", nameof(profit));
        if (totalReturn != expectedReturn)
            throw new ArgumentException("Sic Bo settlement return does not match the stake and profit odds.", nameof(totalReturn));

        Bet = bet;
        Roll = roll;
        Won = won;
        ProfitOdds = profitOdds;
        Profit = profit;
        TotalReturn = totalReturn;
    }

    public SicBoBet Bet { get; }

    public SicBoRoll Roll { get; }

    public bool Won { get; }

    public decimal ProfitOdds { get; }

    public decimal Profit { get; }

    public decimal TotalReturn { get; }
}

public static class SicBoPaytable
{
    public static SicBoBetSettlement Settle(SicBoBet bet, SicBoRoll roll)
    {
        ArgumentNullException.ThrowIfNull(bet);
        ArgumentNullException.ThrowIfNull(roll);

        var profitOdds = ProfitOddsFor(bet, roll);
        var won = profitOdds is not null;
        var odds = profitOdds ?? 0m;
        return new SicBoBetSettlement(
            bet,
            roll,
            won,
            odds,
            won ? bet.Stake * odds : -bet.Stake,
            won ? bet.Stake * (odds + 1m) : 0m);
    }

    private static decimal? ProfitOddsFor(SicBoBet bet, SicBoRoll roll) => bet.Kind switch
    {
        SicBoBetKind.Small => !roll.IsTriple && roll.Total is >= 4 and <= 10 ? 1m : null,
        SicBoBetKind.Big => !roll.IsTriple && roll.Total is >= 11 and <= 17 ? 1m : null,
        SicBoBetKind.Odd => !roll.IsTriple && roll.Total % 2 == 1 ? 1m : null,
        SicBoBetKind.Even => !roll.IsTriple && roll.Total % 2 == 0 ? 1m : null,
        SicBoBetKind.SingleNumber => SingleNumberOdds(roll.OccurrencesOf(bet.Face!.Value)),
        SicBoBetKind.Total => TotalOdds(bet.Total!.Value, roll.Total),
        SicBoBetKind.TwoNumberCombination => CombinationOdds(bet.Combination!.Value, roll),
        SicBoBetKind.SpecificDouble => roll.OccurrencesOf(bet.Face!.Value) >= 2 ? 11.5m : null,
        SicBoBetKind.AnyTriple => roll.IsTriple ? 32m : null,
        SicBoBetKind.SpecificTriple => roll.IsTriple && roll.Values[0] == bet.Face!.Value ? 195m : null,
        _ => throw new ArgumentOutOfRangeException(nameof(bet), "The Sic Bo bet kind is invalid."),
    };

    private static decimal? SingleNumberOdds(int occurrences) => occurrences switch
    {
        1 => 1m,
        2 => 2m,
        3 => 12m,
        0 => null,
        _ => throw new ArgumentOutOfRangeException(nameof(occurrences)),
    };

    private static decimal? TotalOdds(int selectedTotal, int rolledTotal)
    {
        if (selectedTotal != rolledTotal) return null;
        return selectedTotal switch
        {
            4 or 17 => 64m,
            5 or 16 => 32m,
            6 or 15 => 19m,
            7 or 14 => 12m,
            8 or 13 => 8.5m,
            9 or 12 => 7m,
            10 or 11 => 6.5m,
            _ => throw new ArgumentOutOfRangeException(nameof(selectedTotal)),
        };
    }

    private static decimal? CombinationOdds(SicBoTwoNumberCombination combination, SicBoRoll roll) =>
        roll.OccurrencesOf(combination.FirstFace) > 0 && roll.OccurrencesOf(combination.SecondFace) > 0 ? 6m : null;
}
