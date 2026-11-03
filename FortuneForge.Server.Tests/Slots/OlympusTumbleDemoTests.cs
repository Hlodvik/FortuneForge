using FortuneForge.Server.Slots.Bonuses;
using FortuneForge.Server.Slots.Models;
using Xunit;

namespace FortuneForge.Server.Tests.Slots;

public sealed class OlympusTumbleDemoTests
{
    [Fact]
    public void ShowcaseUsesTwoDeterministicStepsAndKeepsSurvivorOrder()
    {
        var result = OlympusTumbleDemo.Resolve(2, true, _ => throw new InvalidOperationException());

        Assert.Equal(6, result.InitialReels.Count);
        Assert.All(result.InitialReels, reel => Assert.Equal(5, reel.Count));
        Assert.Equal(2, result.TotalPoints);
        Assert.Collection(result.Steps, first =>
        {
            var award = Assert.Single(first.Awards);
            Assert.Equal("2", award.Symbol);
            Assert.Equal(1, award.AmountPoints);
            Assert.Equal(8, award.Positions.Count);
            Assert.Equal(new[] { "3", "4", "3", "8", "4" }, first.Reels[0]);
            Assert.Equal(new[] { 2, 2, 1, 0, 0 }, first.DropRows[0]);
        }, second =>
        {
            var award = Assert.Single(second.Awards);
            Assert.Equal("3", award.Symbol);
            Assert.Equal(1, award.AmountPoints);
            Assert.Equal(8, award.Positions.Count);
        });
    }

    [Fact]
    public void TwoWinningSetsClearInTheSameStepWithSeparatePayouts()
    {
        var otherSymbols = new[] { 34, 51, 66, 77, 85, 91, 96 };
        var draws = 0;
        var result = OlympusTumbleDemo.Resolve(2, false, _ =>
        {
            var index = draws++;
            if (index < 8) return 0;
            if (index < 16) return 17;
            return otherSymbols[(index - 16) % otherSymbols.Length];
        });

        var step = Assert.Single(result.Steps);
        Assert.Equal(16, step.Cleared.Count);
        Assert.Equal(2, result.TotalPoints);
        Assert.Collection(step.Awards, first =>
        {
            Assert.Equal("2", first.Symbol);
            Assert.Equal(1, first.AmountPoints);
            Assert.Equal(new GridPosition(0, 0), first.Positions[0]);
            Assert.Equal(new GridPosition(1, 2), first.Positions[7]);
        }, second =>
        {
            Assert.Equal("3", second.Symbol);
            Assert.Equal(1, second.AmountPoints);
            Assert.Equal(new GridPosition(1, 3), second.Positions[0]);
            Assert.Equal(new GridPosition(3, 0), second.Positions[7]);
        });
    }

    [Fact]
    public void NonWinningBoardDoesNotRefillOrPay()
    {
        var draws = 0;
        var symbols = new[] { 0, 17, 34, 51, 66, 77 };
        var result = OlympusTumbleDemo.Resolve(2, false, _ => symbols[draws++ % symbols.Length]);

        Assert.Empty(result.Steps);
        Assert.Equal(0, result.TotalPoints);
        Assert.Equal(30, draws);
    }

    [Theory]
    [InlineData(8, 9)]
    [InlineData(10, 18)]
    [InlineData(12, 36)]
    public void HalfWagerAwardsScaleExactlyAcrossWagers(int matchCount, long minimumWagerAward)
    {
        var otherSymbols = new[] { 0, 17, 34, 51, 66, 85, 91, 96 };
        foreach (var wager in new long[] { 2, 6, 100, 2000 })
        {
            var draws = 0;
            var result = OlympusTumbleDemo.Resolve(wager, false, _ =>
            {
                var index = draws++;
                return index < matchCount ? 77 : otherSymbols[(index - matchCount) % otherSymbols.Length];
            });

            var award = Assert.Single(Assert.Single(result.Steps).Awards);
            Assert.Equal("7", award.Symbol);
            Assert.Equal(matchCount, award.Positions.Count);
            Assert.Equal(minimumWagerAward * (wager / 2), award.AmountPoints);
        }
    }

    [Fact]
    public void RepeatedMaximumPayoutWinsStopAtTheTumbleLimit()
    {
        var draws = 0;
        var result = OlympusTumbleDemo.Resolve(2_000, false, maximum =>
        {
            Assert.Equal(100, maximum);
            return draws++ % 2 == 0 ? 96 : 91;
        });

        Assert.Equal(12, result.Steps.Count);
        Assert.Equal(2_880_000, result.TotalPoints);
        Assert.Equal(390, draws);
        Assert.All(result.Steps, step =>
        {
            Assert.Equal(30, step.Cleared.Count);
            Assert.Equal(240_000, step.AmountPoints);
            Assert.All(step.DropRows.SelectMany(reel => reel), rows => Assert.Equal(5, rows));
        });
    }

    [Theory]
    [InlineData(8, 6, 5)]
    [InlineData(10, 12, 10)]
    [InlineData(12, 24, 20)]
    public void VersionThreeCalibratesSymbolSixAtEveryTierWithoutChangingTheDraws(
        int matchCount, long versionTwoMinimumAward, long versionThreeMinimumAward)
    {
        var otherSymbols = new[] { 0, 17, 34, 51, 77, 85, 91, 96 };
        foreach (var wager in new long[] { 2, 6, 100, 2000 })
        {
            OlympusTumbleOutcome Resolve(string version)
            {
                var draws = 0;
                return OlympusTumbleDemo.Resolve(version, wager, false, maximum =>
                {
                    Assert.Equal(100, maximum);
                    var index = draws++;
                    return index < matchCount ? 66 : otherSymbols[(index - matchCount) % otherSymbols.Length];
                });
            }

            var versionTwo = Resolve(OlympusTumbleDemo.EngineVersionV2);
            var versionThree = Resolve(OlympusTumbleDemo.EngineVersion);
            var oldStep = Assert.Single(versionTwo.Steps);
            var newStep = Assert.Single(versionThree.Steps);
            Assert.Equal("6", Assert.Single(newStep.Awards).Symbol);
            Assert.Equal(matchCount, newStep.Cleared.Count);
            Assert.Equal(oldStep.Cleared, newStep.Cleared);
            Assert.Equal(oldStep.Reels.SelectMany(reel => reel), newStep.Reels.SelectMany(reel => reel));
            Assert.Equal(versionTwoMinimumAward * (wager / 2), versionTwo.TotalPoints);
            Assert.Equal(versionThreeMinimumAward * (wager / 2), versionThree.TotalPoints);
        }
    }
}
