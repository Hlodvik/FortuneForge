namespace FortuneForge.Server.Dice.SicBo;

internal static class SicBoOutcomeAudit
{
    internal const string Version = "sic-bo-three-dice-outcome-v1";

    internal static bool Verify(SicBoStoreRound round, long stakeCents, long payoutCents)
    {
        var dice = round.Roll.Values;
        if (dice.Length != 3 || dice.Any(value => value is < 1 or > 6) ||
            round.Bets.Count is < 1 or > SicBoMoney.MaximumBetsPerRound) return false;
        var total = dice.Sum();
        var triple = dice[0] == dice[1] && dice[1] == dice[2];
        long checkedStake = 0, checkedReturn = 0;
        foreach (var bet in round.Bets)
        {
            if (bet.StakeCents < SicBoMoney.MinimumStakeCents || bet.StakeCents > SicBoMoney.LegacyMaximumStakeCents ||
                bet.StakeCents % SicBoMoney.StakeIncrementCents != 0) return false;
            checkedStake = checked(checkedStake + bet.StakeCents);
            var odds = ProfitOdds(bet, dice, total, triple);
            if (odds == decimal.MinValue) return false;
            if (odds is { } profitOdds)
            {
                var gross = checked(bet.StakeCents * (profitOdds + 1m));
                if (gross != decimal.Truncate(gross)) return false;
                checkedReturn = checked(checkedReturn + (long)gross);
            }
        }
        return checkedStake == stakeCents && checkedReturn == payoutCents;
    }

    private static decimal? ProfitOdds(SicBoStoredBet bet, IReadOnlyList<int> dice, int total, bool triple)
    {
        int Count(int face) => dice.Count(value => value == face);
        if (bet.Face is { } face && face is < 1 or > 6) return decimal.MinValue;
        return bet.Kind switch
        {
            "small" when bet.Face is null && bet.Total is null && bet.FirstFace is null && bet.SecondFace is null => !triple && total is >= 4 and <= 10 ? 1m : null,
            "big" when bet.Face is null && bet.Total is null && bet.FirstFace is null && bet.SecondFace is null => !triple && total is >= 11 and <= 17 ? 1m : null,
            "odd" when bet.Face is null && bet.Total is null && bet.FirstFace is null && bet.SecondFace is null => !triple && total % 2 == 1 ? 1m : null,
            "even" when bet.Face is null && bet.Total is null && bet.FirstFace is null && bet.SecondFace is null => !triple && total % 2 == 0 ? 1m : null,
            "any-triple" when bet.Face is null && bet.Total is null && bet.FirstFace is null && bet.SecondFace is null => triple ? 32m : null,
            "single-number" when bet.Face is { } single && bet.Total is null && bet.FirstFace is null && bet.SecondFace is null => Count(single) switch { 0 => null, 1 => 1m, 2 => 2m, 3 => 12m, _ => decimal.MinValue },
            "specific-double" when bet.Face is { } doubled && bet.Total is null && bet.FirstFace is null && bet.SecondFace is null => Count(doubled) >= 2 ? 11.5m : null,
            "specific-triple" when bet.Face is { } selected && bet.Total is null && bet.FirstFace is null && bet.SecondFace is null => triple && dice[0] == selected ? 195m : null,
            "total" when bet.Face is null && bet.Total is >= 4 and <= 17 && bet.FirstFace is null && bet.SecondFace is null => bet.Total == total ? total switch
            {
                4 or 17 => 64m, 5 or 16 => 32m, 6 or 15 => 19m, 7 or 14 => 12m,
                8 or 13 => 8.5m, 9 or 12 => 7m, 10 or 11 => 6.5m,
                _ => decimal.MinValue
            } : null,
            "two-number-combination" when bet.Face is null && bet.Total is null &&
                bet.FirstFace is >= 1 and <= 6 && bet.SecondFace is >= 1 and <= 6 &&
                bet.FirstFace < bet.SecondFace => Count(bet.FirstFace.Value) > 0 && Count(bet.SecondFace.Value) > 0 ? 6m : null,
            _ => decimal.MinValue
        };
    }
}
