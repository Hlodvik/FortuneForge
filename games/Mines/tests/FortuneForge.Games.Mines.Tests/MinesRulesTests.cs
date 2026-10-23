using System.Numerics;

namespace FortuneForge.Games.Mines.Tests;

public sealed class MinesRulesTests
{
    [Fact]
    public void EntireCurveMatchesIndependentSequentialSurvivalAndOnePercentEdge()
    {
        var points = 0;
        for (var mines = 1; mines <= 24; mines++)
        {
            BigInteger survivingSequences = 1;
            BigInteger possibleSequences = 1;
            decimal previousMultiplier = 1m;
            for (var picks = 1; picks <= 25 - mines; picks++)
            {
                survivingSequences *= 26 - mines - picks;
                possibleSequences *= 26 - picks;
                var (numerator, denominator) = MinesRules.SurvivalProbability(mines, picks);
                Assert.Equal(survivingSequences * denominator, possibleSequences * numerator);
                var multiplier = MinesRules.Multiplier(mines, picks);
                Assert.True(multiplier > previousMultiplier);
                Assert.InRange(multiplier * (decimal)numerator / (decimal)denominator, 0.98999999999999999999999999m, 0.99000000000000000000000001m);
                previousMultiplier = multiplier;
                foreach (var wager in new long[] { 10, 11, 99, 100, 101, 1234, 9999, 10000 })
                {
                    // Independent falling-factorial calculation, rather than the engine's combinations.
                    var exactNumerator = wager * 99 * possibleSequences;
                    var exactDenominator = 100 * survivingSequences;
                    var expected = exactNumerator / exactDenominator;
                    var actual = MinesRules.ReturnCents(wager, mines, picks);
                    Assert.Equal((long)expected, actual);
                    Assert.True(actual * exactDenominator <= exactNumerator);
                    Assert.True((actual + BigInteger.One) * exactDenominator > exactNumerator);
                    Assert.InRange(actual, 0, MinesRules.MaximumReturnCents);
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
                .Select(picks => new { mines, picks, payout = MinesRules.ReturnCents(10000, mines, picks) }))
            .ToArray();
        Assert.Equal(51_482_970_000, maximum.Max(point => point.payout));
        Assert.Equal(MinesRules.MaximumReturnCents, maximum.Max(point => point.payout));
        Assert.Equal(new[] { (12, 13), (13, 12) }, maximum.Where(point => point.payout == MinesRules.MaximumReturnCents)
            .Select(point => (point.mines, point.picks)));
        Assert.Equal(5_148_297m, MinesRules.Multiplier(12, 13));
        Assert.Equal(5_148_297m, MinesRules.Multiplier(13, 12));
    }

    [Fact]
    public void SettlementDoesNotUseRoundedDisplayMultiplier()
    {
        Assert.Equal(1.125m, MinesRules.Multiplier(3, 1));
        Assert.Equal(112, MinesRules.ReturnCents(100, 3, 1));
        Assert.Equal(113m, 100 * decimal.Round(MinesRules.Multiplier(3, 1), 2, MidpointRounding.AwayFromZero));
        Assert.Equal(10, MinesRules.ReturnCents(10, 1, 1)); // Small returns can round away all nominal profit.
        Assert.Equal(247, MinesRules.ReturnCents(10, 24, 1)); // 247.5 cents floors once.
    }

    [Fact]
    public void CurrencyBoundariesAndInitialStateAreExplicit()
    {
        Assert.Equal(0, MinesRules.ReturnCents(10, 3, 0));
        Assert.Equal(1m, MinesRules.Multiplier(3, 0));
        Assert.Equal((BigInteger.One, BigInteger.One), MinesRules.SurvivalProbability(3, 0));
        foreach (var wager in new long[] { long.MinValue, -1, 0, 9, 10001, long.MaxValue })
            Assert.Throws<ArgumentOutOfRangeException>(() => MinesRules.ReturnCents(wager, 3, 1));
        foreach (var mines in new[] { int.MinValue, 0, 25, int.MaxValue })
            Assert.Throws<ArgumentOutOfRangeException>(() => MinesRules.Multiplier(mines, 1));
        Assert.Throws<ArgumentOutOfRangeException>(() => MinesRules.Multiplier(3, -1));
        Assert.Throws<ArgumentOutOfRangeException>(() => MinesRules.Multiplier(3, 23));
        Assert.Throws<ArgumentOutOfRangeException>(() => MinesRules.Combinations(4, 5));
        Assert.Throws<ArgumentOutOfRangeException>(() => MinesRules.Combinations(-1, 0));
        Assert.Equal(5_200_300, MinesRules.Combinations(25, 12));
    }
}
