using System.Numerics;

namespace FortuneForge.Games.Mines.Tests;

public sealed class MinesRulesTests
{
    [Fact]
    public void HistoricalNinetyPercentCurveStillMatchesIndependentSequentialSurvivalAtAllThreeHundredPoints()
    {
        var points = 0;
        for (var mines = 1; mines <= 24; mines++)
        {
            BigInteger survivingSequences = 1;
            BigInteger possibleSequences = 1;
            decimal previousMultiplier = 0m;
            for (var picks = 1; picks <= 25 - mines; picks++)
            {
                survivingSequences *= 26 - mines - picks;
                possibleSequences *= 26 - picks;
                var (numerator, denominator) = MinesRules.SurvivalProbability(mines, picks);
                Assert.Equal(survivingSequences * denominator, possibleSequences * numerator);
                var multiplier = MinesRules.Multiplier(mines, picks, 1000, 1);
                Assert.True(multiplier > previousMultiplier);
                Assert.InRange(multiplier * (decimal)numerator / (decimal)denominator, 0.89999999999999999999999999m, 0.90000000000000000000000001m);
                previousMultiplier = multiplier;
                foreach (var wager in new long[] { 10, 11, 99, 100, 101, 1234, 9999, 10000 })
                {
                    // Independent falling-factorial calculation, rather than the engine's combinations.
                    var exactNumerator = wager * 90 * possibleSequences;
                    var exactDenominator = 100 * survivingSequences;
                    var expected = exactNumerator / exactDenominator;
                    var actual = MinesRules.ReturnCents(wager, mines, picks, 1000, 1);
                    Assert.Equal((long)expected, actual);
                    Assert.True(actual * exactDenominator <= exactNumerator);
                    Assert.True((actual + BigInteger.One) * exactDenominator > exactNumerator);
                    Assert.InRange(actual, 0, MinesRules.HistoricalCurrentMaximumReturnCents);
                }
                points++;
            }
        }
        Assert.Equal(300, points);
    }

    [Fact]
    public void MaximumWinIsVerifiedAcrossEveryMineCountAndPosition()
    {
        var maximum = Enumerable.Range(1, 24)
            .SelectMany(mines => Enumerable.Range(1, 25 - mines)
                .Select(picks => new { mines, picks, payout = MinesRules.ReturnCents(500, mines, picks) }))
            .ToArray();
        Assert.Equal(25_000, maximum.Max(point => point.payout));
        Assert.Equal(MinesRules.MaximumReturnCents, maximum.Max(point => point.payout));
        Assert.Equal(Enumerable.Range(2, 22).Select(mines => (mines, 25 - mines)),
            maximum.Where(point => point.payout == MinesRules.MaximumReturnCents).Select(point => (point.mines, point.picks)));
        Assert.Equal(50m, MinesRules.Multiplier(12, 13));
        Assert.Equal(50m, MinesRules.Multiplier(13, 12));
        Assert.Equal(MinesRules.HistoricalCurrentMaximumReturnCents, MinesRules.ReturnCents(10000, 12, 13, 1000, 1));
    }

    [Fact]
    public void SettlementDoesNotUseRoundedDisplayMultiplier()
    {
        Assert.Equal(116, MinesRules.ReturnCents(100, 3, 2));
        Assert.Equal(117m, 100 * decimal.Round(MinesRules.Multiplier(3, 2), 2, MidpointRounding.AwayFromZero));
        Assert.Equal(9, MinesRules.ReturnCents(10, 1, 1, 1000, 1));
        Assert.Equal(247, MinesRules.ReturnCents(11, 24, 1, 1000, 1)); // Historical 247.5 cents floors once.
    }

    [Theory]
    [InlineData(1, 0.9375, 93)]
    [InlineData(2, 0.9782608695652173913043478261, 97)]
    public void EarlySafeRevealCanReturnLessThanTheStakeWithoutAHiddenMinimum(int mines, double displayedMultiplier, long cents)
    {
        Assert.InRange(MinesRules.Multiplier(mines, 1), 0m, 1m);
        Assert.Equal(displayedMultiplier, (double)MinesRules.Multiplier(mines, 1), 12);
        Assert.Equal(cents, MinesRules.ReturnCents(100, mines, 1));
    }

    [Fact]
    public void LegacyScheduleStillMatchesIndependentNinetyNinePercentOracleAtAllThreeHundredPoints()
    {
        var points = 0;
        for (var mines = 1; mines <= 24; mines++)
        {
            BigInteger survivingSequences = 1;
            BigInteger possibleSequences = 1;
            for (var picks = 1; picks <= 25 - mines; picks++)
            {
                survivingSequences *= 26 - mines - picks;
                possibleSequences *= 26 - picks;
                var multiplier = MinesRules.Multiplier(mines, picks, MinesRules.LegacyHouseEdgeBasisPoints, 1);
                Assert.InRange(multiplier * (decimal)survivingSequences / (decimal)possibleSequences,
                    0.98999999999999999999999999m, 0.99000000000000000000000001m);
                foreach (var wager in new long[] { 10, 11, 99, 100, 101, 1234, 9999, 10000 })
                {
                    var expected = wager * 99 * possibleSequences / (100 * survivingSequences);
                    Assert.Equal((long)expected, MinesRules.ReturnCents(wager, mines, picks, MinesRules.LegacyHouseEdgeBasisPoints, 1));
                }
                points++;
            }
        }
        Assert.Equal(300, points);
        Assert.Equal(MinesRules.LegacyMaximumReturnCents,
            MinesRules.ReturnCents(10000, 12, 13, MinesRules.LegacyHouseEdgeBasisPoints, 1));
    }

    [Theory]
    [InlineData(int.MinValue)]
    [InlineData(-1)]
    [InlineData(0)]
    [InlineData(101)]
    [InlineData(999)]
    [InlineData(1001)]
    [InlineData(10000)]
    [InlineData(int.MaxValue)]
    public void UnrecognizedPayoutSchedulesAreRejectedEvenBeforeTheFirstPick(int edge)
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => MinesRules.Multiplier(3, 0, edge));
        Assert.Throws<ArgumentOutOfRangeException>(() => MinesRules.ReturnCents(100, 3, 0, edge));
        Assert.Throws<ArgumentOutOfRangeException>(() => MinesRules.ValidateHouseEdge(edge));
    }

    [Fact]
    public void CurrencyBoundariesAndInitialStateAreExplicit()
    {
        Assert.Equal(100, MinesRules.MinimumWagerCents);
        Assert.Equal(500, MinesRules.MaximumWagerCents);
        MinesRules.ValidateWager(100);
        MinesRules.ValidateWager(500);
        foreach (var wager in new long[] { long.MinValue, -1, 0, 9, 10, 99, 501, 10000, 10001, long.MaxValue })
            Assert.Throws<ArgumentOutOfRangeException>(() => MinesRules.ValidateWager(wager));
        foreach (var wager in new long[] { 10, 99, 100, 500, 501, 10000 })
            MinesRules.ValidateAcceptedWager(wager);
        foreach (var wager in new long[] { long.MinValue, -1, 0, 9, 10001, long.MaxValue })
            Assert.Throws<ArgumentOutOfRangeException>(() => MinesRules.ValidateAcceptedWager(wager));
        Assert.Equal(0, MinesRules.ReturnCents(100, 3, 0));
        Assert.Equal(1m, MinesRules.Multiplier(3, 0));
        Assert.Equal((BigInteger.One, BigInteger.One), MinesRules.SurvivalProbability(3, 0));
        foreach (var wager in new long[] { long.MinValue, -1, 0, 9, 10001, long.MaxValue })
            Assert.Throws<ArgumentOutOfRangeException>(() => MinesRules.ReturnCents(wager, 3, 1, 1000, 1));
        Assert.Throws<ArgumentOutOfRangeException>(() => MinesRules.ReturnCents(99, 3, 1));
        Assert.Throws<ArgumentOutOfRangeException>(() => MinesRules.ReturnCents(501, 3, 1));
        foreach (var mines in new[] { int.MinValue, 0, 25, int.MaxValue })
            Assert.Throws<ArgumentOutOfRangeException>(() => MinesRules.Multiplier(mines, 1));
        Assert.Throws<ArgumentOutOfRangeException>(() => MinesRules.Multiplier(3, -1));
        Assert.Throws<ArgumentOutOfRangeException>(() => MinesRules.Multiplier(3, 23));
        Assert.Throws<ArgumentOutOfRangeException>(() => MinesRules.Combinations(4, 5));
        Assert.Throws<ArgumentOutOfRangeException>(() => MinesRules.Combinations(-1, 0));
        Assert.Equal(5_200_300, MinesRules.Combinations(25, 12));
    }
}
