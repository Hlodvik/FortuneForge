using FortuneForge.Server.Accounts.Models;
using Xunit;

namespace FortuneForge.Server.Tests.Accounts;

public sealed class RandMoneyTests
{
    [Fact]
    public void ExistingWholeRandBalance_KeepsItsExactValue() =>
        Assert.Equal(10_000m, RandMoney.CentsToRand(RandMoney.CombineCents(10_000, 0)));

    [Theory]
    [InlineData(2, 50)]
    [InlineData(4, 100)]
    [InlineData(6, 150)]
    public void TwentyFiveCentPoints_ProduceHalfRandWagerSteps(long points, long expectedCents) =>
        Assert.Equal(expectedCents, RandMoney.PointsToCents(points, 25));

    [Fact]
    public void LegacyWholeRandFreeSpinWager_PreservesItsRandValue() =>
        Assert.Equal(200, RandMoney.CentsToPoints(50 * RandMoney.CentsPerRand, 25));
}
