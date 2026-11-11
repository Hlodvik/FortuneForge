using FortuneForge.Games.JadeDragon;
using FortuneForge.Games.Slots;
using Xunit;

namespace FortuneForge.Server.Tests.Slots;

public sealed class JadeDragonCascadeTests
{
    [Theory]
    [InlineData(false, 47L, 10, 1)]
    [InlineData(true, 94L, 5, 2)]
    public void ReplayPaysEveryMatchingWayThenDropsSurvivorsAndCountsFinalPearls(
        bool freeSpin, long expectedPoints, int expectedFreeSpins, int expectedMultiplier)
    {
        int[] draws =
        [
            0, 0, 96, 96,
            0, 82, 96, 35,
            0, 82, 50, 66,
            50, 82, 21, 35,
            50, 66, 35, 21,
            21, 50, 66, 35
        ];
        var result = JadeDragonCascadeEngine.Replay(20, freeSpin, draws);
        var step = Assert.Single(result.Steps);
        var award = Assert.Single(step.Awards);
        Assert.Equal("dragon", award.Symbol);
        Assert.Equal(3, award.MatchLength);
        Assert.Equal(2, award.Ways);
        Assert.Equal(new GridPosition[] { new(0, 0), new(0, 1), new(1, 0), new(2, 0) }, step.Cleared);
        Assert.Equal(new[] { "lotus", "coin", "pearl", "pearl" }, step.Reels[0]);
        Assert.Equal(new[] { 2, 2, 0, 0 }, step.DropRows[0]);
        Assert.Equal(new[] { 1, 0, 0, 0 }, step.DropRows[1]);
        Assert.Equal(expectedMultiplier, step.Multiplier);
        Assert.Equal(expectedPoints, result.TotalPoints);
        Assert.Equal(result.TotalPoints, step.AmountPoints);
        Assert.Equal(expectedFreeSpins, result.FreeSpinsAwarded);
    }

    [Fact]
    public void CappedWinKeepsTheBoardAndDoesNotDrawAnExtraRefill()
    {
        var draws = new int[100];
        var result = JadeDragonCascadeEngine.Replay(20, false, draws);
        Assert.Equal(new[] { 1, 2, 3, 5, 5 }, result.Steps.Select(step => step.Multiplier));
        Assert.Equal(2_500_000, result.TotalPoints);
        Assert.Equal(result.TotalPoints, result.Steps.Sum(step => step.AmountPoints));
        Assert.Empty(result.Steps[^1].Cleared);
        Assert.All(result.Steps[^1].DropRows.SelectMany(rows => rows), distance => Assert.Equal(0, distance));
        Assert.Throws<ArgumentException>(() => JadeDragonCascadeEngine.Replay(20, false, new int[101]));
        Assert.Throws<ArgumentException>(() => JadeDragonCascadeEngine.Replay(20, false, new int[99]));
    }

    [Fact]
    public void RoundsHalfACentAtEachWinningBoard()
    {
        int[] draws =
        [
            21, 0, 0, 0,
            21, 35, 35, 35,
            21, 50, 50, 50,
            82, 82, 82, 82,
            0, 0, 0, 0,
            96, 0, 10
        ];
        var step = Assert.Single(JadeDragonCascadeEngine.Replay(160, false, draws).Steps);
        Assert.Equal(93.5m, Assert.Single(step.Awards).UnroundedPoints);
        Assert.Equal(94L, step.AmountPoints);
    }

    [Fact]
    public void MinimumWagerStillCreditsTheSmallestWinningBoard()
    {
        int[] draws =
        [
            82, 0, 0, 0,
            82, 35, 35, 35,
            82, 50, 50, 50,
            21, 21, 21, 21,
            0, 0, 0, 0,
            96, 0, 10
        ];
        var step = Assert.Single(JadeDragonCascadeEngine.Replay(2, false, draws).Steps);
        Assert.Equal(.4675m, Assert.Single(step.Awards).UnroundedPoints);
        Assert.Equal(1L, step.AmountPoints);
    }
}
