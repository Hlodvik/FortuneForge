using FortuneForge.Games.SicBo;

namespace FortuneForge.Games.Tests.SicBo;

public sealed class SicBoPaytableTests
{
    [Theory]
    [InlineData(SicBoBetKind.Small, 4, 1, 1)]
    [InlineData(SicBoBetKind.Big, 6, 6, 5)]
    [InlineData(SicBoBetKind.Odd, 1, 2, 2)]
    [InlineData(SicBoBetKind.Even, 1, 2, 3)]
    [InlineData(SicBoBetKind.AnyTriple, 2, 2, 2)]
    public void EveryNoSelectionBetKindHasAWinningRoll(SicBoBetKind kind, int first, int second, int third) =>
        AssertWinning(NoSelectionBet(kind), new SicBoRoll(first, second, third), kind == SicBoBetKind.AnyTriple ? 32m : 1m);

    [Theory]
    [InlineData(SicBoBetKind.Small, 6, 6, 6)]
    [InlineData(SicBoBetKind.Big, 1, 1, 1)]
    [InlineData(SicBoBetKind.Odd, 1, 1, 1)]
    [InlineData(SicBoBetKind.Even, 2, 2, 2)]
    [InlineData(SicBoBetKind.AnyTriple, 1, 2, 3)]
    public void EveryNoSelectionBetKindHasALosingRoll(SicBoBetKind kind, int first, int second, int third) =>
        AssertLosing(NoSelectionBet(kind), new SicBoRoll(first, second, third));

    [Theory]
    [InlineData(SicBoBetKind.Small, 2, 2, 2)]
    [InlineData(SicBoBetKind.Big, 5, 5, 5)]
    [InlineData(SicBoBetKind.Odd, 1, 1, 1)]
    [InlineData(SicBoBetKind.Even, 2, 2, 2)]
    public void SmallBigOddAndEvenLoseOnEveryTriple(SicBoBetKind kind, int first, int second, int third) =>
        AssertLosing(NoSelectionBet(kind), new SicBoRoll(first, second, third));

    [Theory]
    [InlineData(0, 0)]
    [InlineData(1, 1)]
    [InlineData(2, 2)]
    [InlineData(3, 12)]
    public void SingleNumberUsesTheOccurrencePaytable(int occurrences, decimal expectedOdds)
    {
        var roll = occurrences switch
        {
            0 => new SicBoRoll(1, 2, 3),
            1 => new SicBoRoll(6, 1, 2),
            2 => new SicBoRoll(6, 6, 1),
            3 => new SicBoRoll(6, 6, 6),
            _ => throw new ArgumentOutOfRangeException(nameof(occurrences)),
        };
        var bet = SicBoBet.SingleNumber(2m, 6);

        if (occurrences == 0) AssertLosing(bet, roll);
        else AssertWinning(bet, roll, expectedOdds);
    }

    [Theory]
    [InlineData(4, 64)]
    [InlineData(17, 64)]
    [InlineData(5, 32)]
    [InlineData(16, 32)]
    [InlineData(6, 19)]
    [InlineData(15, 19)]
    [InlineData(7, 12)]
    [InlineData(14, 12)]
    [InlineData(8, 8.5)]
    [InlineData(13, 8.5)]
    [InlineData(9, 7)]
    [InlineData(12, 7)]
    [InlineData(10, 6.5)]
    [InlineData(11, 6.5)]
    public void TotalBetUsesEverySelectedTotalMultiplier(int total, decimal expectedOdds) =>
        AssertWinning(SicBoBet.ForTotal(1m, total), RollForTotal(total), expectedOdds);

    [Fact]
    public void TotalBetLosesWhenTheTotalDoesNotMatch() =>
        AssertLosing(SicBoBet.ForTotal(1m, 10), new SicBoRoll(1, 2, 3));

    [Fact]
    public void TwoNumberCombinationRequiresBothFaces()
    {
        var bet = SicBoBet.TwoNumberCombination(2m, 2, 5);

        AssertWinning(bet, new SicBoRoll(2, 5, 5), 6m);
        AssertLosing(bet, new SicBoRoll(2, 2, 4));
    }

    [Fact]
    public void SpecificDoubleWinsOnATripleAndLosesBelowTwoOccurrences()
    {
        var bet = SicBoBet.SpecificDouble(2m, 4);

        AssertWinning(bet, new SicBoRoll(4, 4, 4), 11.5m);
        AssertLosing(bet, new SicBoRoll(4, 1, 2));
    }

    [Fact]
    public void SpecificTripleRequiresTheSelectedFace()
    {
        var bet = SicBoBet.SpecificTriple(1m, 3);

        AssertWinning(bet, new SicBoRoll(3, 3, 3), 195m);
        AssertLosing(bet, new SicBoRoll(2, 2, 2));
    }

    [Fact]
    public void SettlementUsesExactDecimalOddsAndReturnArithmetic()
    {
        var settlement = SicBoPaytable.Settle(SicBoBet.ForTotal(2.5m, 8), new SicBoRoll(1, 1, 6));

        Assert.True(settlement.Won);
        Assert.Equal(8.5m, settlement.ProfitOdds);
        Assert.Equal(21.25m, settlement.Profit);
        Assert.Equal(23.75m, settlement.TotalReturn);
    }

    [Fact]
    public void SettlementRejectsNullInputs()
    {
        var bet = SicBoBet.Small(1m);
        var roll = new SicBoRoll(1, 2, 3);

        Assert.Throws<ArgumentNullException>(() => SicBoPaytable.Settle(null!, roll));
        Assert.Throws<ArgumentNullException>(() => SicBoPaytable.Settle(bet, null!));
    }

    private static SicBoBet NoSelectionBet(SicBoBetKind kind) => kind switch
    {
        SicBoBetKind.Small => SicBoBet.Small(1m),
        SicBoBetKind.Big => SicBoBet.Big(1m),
        SicBoBetKind.Odd => SicBoBet.Odd(1m),
        SicBoBetKind.Even => SicBoBet.Even(1m),
        SicBoBetKind.AnyTriple => SicBoBet.AnyTriple(1m),
        _ => throw new ArgumentOutOfRangeException(nameof(kind)),
    };

    private static SicBoRoll RollForTotal(int total) => total switch
    {
        4 => new SicBoRoll(1, 1, 2), 5 => new SicBoRoll(1, 1, 3), 6 => new SicBoRoll(1, 1, 4),
        7 => new SicBoRoll(1, 1, 5), 8 => new SicBoRoll(1, 1, 6), 9 => new SicBoRoll(1, 2, 6),
        10 => new SicBoRoll(1, 3, 6), 11 => new SicBoRoll(1, 4, 6), 12 => new SicBoRoll(1, 5, 6),
        13 => new SicBoRoll(2, 5, 6), 14 => new SicBoRoll(2, 6, 6), 15 => new SicBoRoll(3, 6, 6),
        16 => new SicBoRoll(4, 6, 6), 17 => new SicBoRoll(5, 6, 6),
        _ => throw new ArgumentOutOfRangeException(nameof(total)),
    };

    private static void AssertWinning(SicBoBet bet, SicBoRoll roll, decimal expectedOdds)
    {
        var settlement = SicBoPaytable.Settle(bet, roll);
        Assert.True(settlement.Won);
        Assert.Equal(expectedOdds, settlement.ProfitOdds);
        Assert.Equal(bet.Stake * expectedOdds, settlement.Profit);
        Assert.Equal(bet.Stake * (expectedOdds + 1m), settlement.TotalReturn);
    }

    private static void AssertLosing(SicBoBet bet, SicBoRoll roll)
    {
        var settlement = SicBoPaytable.Settle(bet, roll);
        Assert.False(settlement.Won);
        Assert.Equal(0m, settlement.ProfitOdds);
        Assert.Equal(-bet.Stake, settlement.Profit);
        Assert.Equal(0m, settlement.TotalReturn);
    }
}
