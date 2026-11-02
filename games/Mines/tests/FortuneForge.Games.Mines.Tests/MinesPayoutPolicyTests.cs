using System.Collections.Immutable;
using System.Numerics;
using System.Text.Json;

namespace FortuneForge.Games.Mines.Tests;

public sealed class MinesPayoutPolicyTests
{
    private static readonly long[] Wagers = [100, 101, 199, 234, 499, 500];

    [Fact]
    public void EveryCurrentSafeReturnMatchesAnIndependentFullBoardOracleAndTheFiftyTimesCeiling()
    {
        var points = 0;
        for (var mines = 1; mines <= 24; mines++)
        {
            var previous = 0m;
            for (var picks = 1; picks <= 25 - mines; picks++)
            {
                var expected = MinesPayoutOracle.Safe(mines, picks);
                var actual = MinesRules.Multiplier(mines, picks);
                Assert.Equal(expected.AsDecimal(), actual);
                Assert.InRange(actual, 0m, 50m);
                Assert.True(actual > previous);
                previous = actual;
                foreach (var wager in Wagers)
                {
                    Assert.Equal(expected.FloorTimes(wager), MinesRules.ReturnCents(wager, mines, picks));
                    Assert.InRange(MinesRules.ReturnCents(wager, mines, picks), 0, 25_000);
                }
                points++;
            }
            Assert.Equal(mines is 1 or 24 ? 22.5m : 50m, MinesRules.Multiplier(mines, 25 - mines));
        }
        Assert.Equal(300, points);
        Assert.Equal(25_000, MinesRules.MaximumReturnCents);
        Assert.Equal(50, MinesRules.MaximumMultiplier);
    }

    [Fact]
    public void EveryNextPickHasExactlyTheCurrentConditionalExpectedReturnBeforeWholeCentRounding()
    {
        var points = 0;
        for (var mines = 1; mines <= 24; mines++)
        {
            var safeTotal = 25 - mines;
            for (var priorSafe = 0; priorSafe < safeTotal; priorSafe++)
            {
                var current = MinesPayoutOracle.Safe(mines, priorSafe);
                var next = MinesPayoutOracle.Safe(mines, priorSafe + 1);
                var mine = MinesPayoutOracle.Mine(mines, priorSafe);
                var safeChance = Ratio.Of(safeTotal - priorSafe, 25 - priorSafe);
                Assert.Equal(current, safeChance * next + (Ratio.One - safeChance) * mine);
                Assert.True(mine >= Ratio.Zero && mine <= Ratio.Of(50));
                Assert.Equal(mine.AsDecimal(), MinesRules.MineMultiplier(mines, priorSafe));
                foreach (var wager in Wagers)
                    Assert.Equal(mine.FloorTimes(wager), MinesRules.MineReturnCents(wager, mines, priorSafe));
                points++;
            }
            Assert.Equal(0, MinesRules.MineReturnCents(500, mines, 0));
        }
        Assert.Equal(300, points);
        // Displaying the initial stake as1x must not introduce a risk-free0-pick cash-out or extra EV.
        Assert.Equal(1m, MinesRules.Multiplier(2, 0));
        Assert.Equal(0, MinesRules.ReturnCents(500, 2, 0));
    }

    [Fact]
    public void AllThreeHundredCashOutStrategiesIncludingFullClearHaveNinetyPercentNominalRtp()
    {
        var strategies = 0;
        for (var mines = 1; mines <= 24; mines++)
        {
            var mineReturnSoFar = Ratio.Zero;
            var roundedMineReturns = Wagers.ToDictionary(wager => wager, _ => Ratio.Zero);
            for (var target = 1; target <= 25 - mines; target++)
            {
                var survival = MinesPayoutOracle.Survival(mines, target);
                var mineProbability = MinesPayoutOracle.Survival(mines, target - 1) - survival;
                mineReturnSoFar += mineProbability * MinesPayoutOracle.Mine(mines, target - 1);
                Assert.Equal(Ratio.Of(9, 10), mineReturnSoFar + survival * MinesPayoutOracle.Safe(mines, target));
                foreach (var wager in Wagers)
                {
                    roundedMineReturns[wager] += mineProbability * Ratio.Of(MinesRules.MineReturnCents(wager, mines, target - 1));
                    var actualCentsEv = roundedMineReturns[wager] + survival * Ratio.Of(MinesRules.ReturnCents(wager, mines, target));
                    var nominalCentsEv = Ratio.Of(9 * wager, 10);
                    // Every terminal outcome floors once: total expected rounding loss is less than one cent.
                    Assert.True(actualCentsEv <= nominalCentsEv);
                    Assert.True(actualCentsEv > nominalCentsEv - Ratio.One);
                }
                strategies++;
            }
        }
        Assert.Equal(300, strategies);
    }

    [Fact]
    public void LateMineQuoteIsPaidExactlyOnceWithoutForcingAnEarlyCashOut()
    {
        var engine = new MinesEngine(new FixedBoard());
        var round = engine.Start(500, 2);
        for (var tile = 2; tile <= 22; tile++)
        {
            round = engine.Reveal(round, tile);
            Assert.Equal(MinesRoundStatus.Active, round.Status);
        }
        var before = engine.Project(round);
        Assert.Equal(2, before.PayoutPolicyVersion);
        Assert.Equal(45m, before.CurrentMultiplier);
        Assert.Equal(47.5m, before.NextMultiplier);
        Assert.Equal(22_500, before.CashOutCents);
        Assert.Equal(21_250, before.NextMinePayoutCents);
        Assert.All(before.Tiles.Where(tile => !tile.IsRevealed), tile => Assert.Equal(MinesTileKind.Hidden, tile.Kind));

        var mine = engine.Reveal(round, 0);
        Assert.Equal(MinesRoundStatus.Consolation, mine.Status);
        Assert.Equal(21_250, mine.PayoutCents);
        var view = engine.Project(mine);
        Assert.Equal(42.5m, view.CurrentMultiplier);
        Assert.Equal(before.NextMinePayoutCents, view.PayoutCents);
        Assert.Equal(0, view.NextMinePayoutCents);
        Assert.Null(view.NextMultiplier);
        Assert.False(view.CanCashOut);
        Assert.Equal(2, view.Tiles.Count(tile => tile.Kind == MinesTileKind.Mine));
        Assert.Throws<InvalidOperationException>(() => engine.Reveal(mine, 23));
        Assert.Throws<InvalidOperationException>(() => engine.CashOut(mine));
        var restored = JsonSerializer.Deserialize<MinesRoundState>(JsonSerializer.Serialize(mine))!;
        Assert.Equal(JsonSerializer.Serialize(mine), JsonSerializer.Serialize(restored));
        Assert.Equal(21_250, engine.Project(restored).PayoutCents);

        // Choosing to continue remains possible after the knee and after the nextsafe step.
        var penultimate = engine.Reveal(round, 23);
        Assert.Equal(MinesRoundStatus.Active, penultimate.Status);
        Assert.Equal(47.5m, engine.Project(penultimate).CurrentMultiplier);
        Assert.Equal(23_125, engine.Project(penultimate).NextMinePayoutCents);
        Assert.Equal(23_750, engine.CashOut(penultimate).PayoutCents);
        var clear = engine.Reveal(penultimate, 24);
        Assert.Equal(MinesRoundStatus.Cleared, clear.Status);
        Assert.Equal(25_000, clear.PayoutCents);
        Assert.Equal(50m, engine.Project(clear).CurrentMultiplier);
        Assert.Equal(0, engine.Project(clear).NextMinePayoutCents);
    }

    [Fact]
    public void EveryMineCountCanRevealAllGemsAndPaysTheFrozenCurrentPolicyWithoutStoppingEarly()
    {
        var engine = new MinesEngine(new FixedBoard());
        for (var mines = 1; mines <= 24; mines++)
        {
            var round = engine.Start(500, mines);
            for (var tile = mines; tile < 25; tile++)
            {
                Assert.Equal(MinesRoundStatus.Active, round.Status);
                Assert.Equal(MinesPayoutOracle.Mine(mines, round.SafeReveals).FloorTimes(500), engine.Project(round).NextMinePayoutCents);
                round = engine.Reveal(round, tile);
            }
            Assert.Equal(MinesRoundStatus.Cleared, round.Status);
            Assert.Equal(mines is 1 or 24 ? 11_250 : 25_000, round.PayoutCents);
            Assert.Equal(2, round.PayoutPolicyVersion);
            Assert.Throws<InvalidOperationException>(() => engine.Reveal(round, 0));
            Assert.Throws<InvalidOperationException>(() => engine.CashOut(round));
        }
    }

    [Fact]
    public void CurrentStateRejectsUnsupportedPoliciesInvalidWagersAndMisclassifiedOrIncorrectMinePayouts()
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => new MinesRoundState(100, 2, [0, 1], [], MinesRoundStatus.Active, 0, 1000, 0));
        Assert.Throws<ArgumentOutOfRangeException>(() => new MinesRoundState(100, 2, [0, 1], [], MinesRoundStatus.Active, 0, 1000, 3));
        Assert.Throws<ArgumentOutOfRangeException>(() => new MinesRoundState(100, 2, [0, 1], [], MinesRoundStatus.Active, 0, 100, 2));
        Assert.Throws<ArgumentOutOfRangeException>(() => new MinesRoundState(10, 2, [0, 1], [], MinesRoundStatus.Active, 0, 1000, 2));
        Assert.Throws<ArgumentOutOfRangeException>(() => new MinesRoundState(10000, 2, [0, 1], [], MinesRoundStatus.Active, 0, 1000, 2));
        var lateMine = Enumerable.Range(2, 21).Append(0).ToImmutableArray();
        Assert.Throws<ArgumentException>(() => new MinesRoundState(500, 2, [0, 1], lateMine, MinesRoundStatus.Lost, 0, 1000, 2));
        Assert.Throws<ArgumentException>(() => new MinesRoundState(500, 2, [0, 1], lateMine, MinesRoundStatus.Consolation, 21_251, 1000, 2));
        Assert.Throws<ArgumentException>(() => new MinesRoundState(500, 2, [0, 1], [0], MinesRoundStatus.Consolation, 1, 1000, 2));
        Assert.Throws<ArgumentException>(() => new MinesRoundState(500, 2, [0, 1], lateMine, MinesRoundStatus.Consolation, 21_250, 1000, 1));
        Assert.Throws<ArgumentOutOfRangeException>(() => MinesRules.MineMultiplier(2, 23));
        Assert.Throws<ArgumentOutOfRangeException>(() => MinesRules.ReturnCents(100, 2, 1, 1000, 3));
        Assert.Equal(0, (int)MinesRoundStatus.Active);
        Assert.Equal(1, (int)MinesRoundStatus.Lost);
        Assert.Equal(2, (int)MinesRoundStatus.CashedOut);
        Assert.Equal(3, (int)MinesRoundStatus.Cleared);
        Assert.Equal(4, (int)MinesRoundStatus.Consolation);
    }

    private sealed class FixedBoard : IMinesBoardGenerator
    {
        public System.Collections.Immutable.ImmutableArray<int> Generate(int mineCount) => [.. Enumerable.Range(0, mineCount)];
    }
}

/// <summary>Independent test oracle: sequential survival probabilities, not production combinations or curve helpers.</summary>
internal static class MinesPayoutOracle
{
    public static Ratio Survival(int mines, int safe)
    {
        var probability = Ratio.One;
        for (var pick = 0; pick < safe; pick++) probability *= Ratio.Of(25 - mines - pick, 25 - pick);
        return probability;
    }

    public static Ratio Safe(int mines, int safe)
    {
        var original = Ratio.Of(9, 10) / Survival(mines, safe);
        var total = 25 - mines;
        if (Ratio.Of(9, 10) / Survival(mines, total) <= Ratio.Of(50)) return original;
        var knee = Enumerable.Range(1, total).Last(pick => Ratio.Of(9, 10) / Survival(mines, pick) <= Ratio.Of(50));
        if (safe <= knee) return original;
        var anchor = Ratio.Of(9, 10) / Survival(mines, knee);
        var progress = Ratio.Of(safe - knee, total - knee);
        return (Ratio.One - progress) * anchor + progress * Ratio.Of(50);
    }

    public static Ratio Mine(int mines, int safe)
    {
        var before = Survival(mines, safe);
        var after = Survival(mines, safe + 1);
        return (before * Safe(mines, safe) - after * Safe(mines, safe + 1)) / (before - after);
    }
}

internal readonly record struct Ratio(BigInteger Numerator, BigInteger Denominator)
{
    public static Ratio Zero => Of(0);
    public static Ratio One => Of(1);
    public static Ratio Of(BigInteger numerator) => Of(numerator, BigInteger.One);
    public static Ratio Of(BigInteger numerator, BigInteger denominator)
    {
        if (denominator == 0) throw new DivideByZeroException();
        if (denominator < 0) (numerator, denominator) = (-numerator, -denominator);
        var divisor = BigInteger.GreatestCommonDivisor(numerator, denominator);
        return new(numerator / divisor, denominator / divisor);
    }
    public decimal AsDecimal() => (decimal)Numerator / (decimal)Denominator;
    public long FloorTimes(long wager) => (long)(wager * Numerator / Denominator);
    public static Ratio operator +(Ratio a, Ratio b) => Of(a.Numerator * b.Denominator + b.Numerator * a.Denominator, a.Denominator * b.Denominator);
    public static Ratio operator -(Ratio a, Ratio b) => Of(a.Numerator * b.Denominator - b.Numerator * a.Denominator, a.Denominator * b.Denominator);
    public static Ratio operator *(Ratio a, Ratio b) => Of(a.Numerator * b.Numerator, a.Denominator * b.Denominator);
    public static Ratio operator /(Ratio a, Ratio b) => Of(a.Numerator * b.Denominator, a.Denominator * b.Numerator);
    public static bool operator <=(Ratio a, Ratio b) => a.Numerator * b.Denominator <= b.Numerator * a.Denominator;
    public static bool operator >=(Ratio a, Ratio b) => a.Numerator * b.Denominator >= b.Numerator * a.Denominator;
    public static bool operator <(Ratio a, Ratio b) => !(a >= b);
    public static bool operator >(Ratio a, Ratio b) => !(a <= b);
}
