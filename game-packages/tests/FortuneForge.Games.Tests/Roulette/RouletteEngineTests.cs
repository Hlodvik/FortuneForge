using System.Collections.Immutable;
using FortuneForge.Games.Abstractions;
using FortuneForge.Games.Roulette;

namespace FortuneForge.Games.Tests;

public sealed class RouletteEngineTests
{
    [Fact]
    public void StraightBetPaysThirtyFiveToOnePlusStake()
    {
        var bet = new RouletteBet("player", RouletteBetKind.Straight, 10m, 17);

        var result = RouletteEngine.Settle(bet, new RoulettePocket(17));

        Assert.True(result.Won);
        Assert.Equal(360m, result.TotalReturn);
    }

    [Theory]
    [InlineData(RouletteBetKind.Red)]
    [InlineData(RouletteBetKind.Black)]
    [InlineData(RouletteBetKind.Even)]
    [InlineData(RouletteBetKind.Odd)]
    [InlineData(RouletteBetKind.Low)]
    [InlineData(RouletteBetKind.High)]
    public void ZeroLosesEveryEvenMoneyBet(RouletteBetKind kind)
    {
        var result = RouletteEngine.Settle(
            new RouletteBet("player", kind, 5m),
            new RoulettePocket(0));

        Assert.False(result.Won);
        Assert.Equal(0m, result.TotalReturn);
    }

    [Fact]
    public void SpinSettlesAllBetsAndClosesRound()
    {
        var state = RouletteEngine.OpenRound("round-1");
        state = RouletteEngine.PlaceBet(state, new RouletteBet("alice", RouletteBetKind.Red, 5m));
        state = RouletteEngine.PlaceBet(state, new RouletteBet("bob", RouletteBetKind.Black, 5m));

        var spin = RouletteEngine.Spin(state, new RoulettePocket(1));

        Assert.Equal(RouletteRoundPhase.Settled, spin.State.Phase);
        Assert.Equal(10m, spin.Settlements.Single(result => result.PlayerId == "alice").TotalReturn);
        Assert.Equal(0m, spin.Settlements.Single(result => result.PlayerId == "bob").TotalReturn);
        Assert.Throws<RouletteRuleException>(() => RouletteEngine.Spin(spin.State, new RoulettePocket(2)));
    }

    [Fact]
    public void BetContractRejectsInvalidShapes()
    {
        var round = RouletteEngine.OpenRound("round-1");

        Assert.Throws<ArgumentOutOfRangeException>(() => new RoulettePocket(37));
        Assert.Throws<ArgumentException>(() => RouletteEngine.PlaceBet(
            round,
            new RouletteBet("player", RouletteBetKind.Straight, 5m)));
        Assert.Throws<ArgumentException>(() => RouletteEngine.PlaceBet(
            round,
            new RouletteBet("player", RouletteBetKind.Red, 5m, 12)));
        Assert.Throws<ArgumentOutOfRangeException>(() => RouletteEngine.PlaceBet(
            round,
            new RouletteBet("player", (RouletteBetKind)999, 5m)));
    }

    [Fact]
    public void DescriptorDeclaresSingleZeroCasinoSkeleton()
    {
        var descriptor = RouletteModule.Descriptor;

        Assert.Equal("roulette", descriptor.Id);
        Assert.Equal("0.1.1", descriptor.PackageVersion);
        Assert.Equal(GameCategory.Casino, descriptor.Category);
        Assert.True(descriptor.Capabilities.HasFlag(GameCapability.FreePlay));
        Assert.False(descriptor.Capabilities.HasFlag(GameCapability.Credits));
        Assert.False(descriptor.Capabilities.HasFlag(GameCapability.History));
    }

    [Theory]
    [InlineData(RouletteBetKind.Split, 18, "1,2")]
    [InlineData(RouletteBetKind.Street, 12, "1,2,3")]
    [InlineData(RouletteBetKind.Corner, 9, "1,2,4,5")]
    [InlineData(RouletteBetKind.SixLine, 6, "1,2,3,4,5,6")]
    public void InsideBetsUseStandardPayouts(RouletteBetKind kind, decimal multiplier, string numberText)
    {
        var numbers = numberText.Split(',').Select(int.Parse).ToArray();
        var result = RouletteEngine.Settle(new RouletteBet("player", kind, 10m, Numbers: numbers.ToImmutableArray()), new RoulettePocket(numbers[0]));

        Assert.True(result.Won);
        Assert.Equal(10m * multiplier, result.TotalReturn);
    }

    [Theory]
    [InlineData(RouletteBetKind.Column, 2, 2, 3)]
    [InlineData(RouletteBetKind.Dozen, 3, 25, 3)]
    public void ColumnAndDozenBetsUseStandardPayouts(RouletteBetKind kind, int selection, int winningNumber, decimal multiplier)
    {
        var result = RouletteEngine.Settle(new RouletteBet("player", kind, 10m, selection), new RoulettePocket(winningNumber));

        Assert.True(result.Won);
        Assert.Equal(10m * multiplier, result.TotalReturn);
    }

    [Fact]
    public void ColumnThreeWinsOnNumbersDivisibleByThree()
    {
        var result = RouletteEngine.Settle(new RouletteBet("player", RouletteBetKind.Column, 10m, 3), new RoulettePocket(6));

        Assert.True(result.Won);
        Assert.Equal(30m, result.TotalReturn);
    }

    [Fact]
    public void ZeroInsideCombinationsAreLegal()
    {
        var split = RouletteEngine.Settle(new RouletteBet("player", RouletteBetKind.Split, 10m, Numbers: [0, 1]), new RoulettePocket(0));
        var street = RouletteEngine.Settle(new RouletteBet("player", RouletteBetKind.Street, 10m, Numbers: [0, 2, 3]), new RoulettePocket(3));

        Assert.True(split.Won);
        Assert.True(street.Won);
    }

    [Fact]
    public void MultipleOverlappingBetsSettleIndependentlyAndCanBeRemoved()
    {
        var state = RouletteEngine.OpenRound("round-1");
        state = RouletteEngine.PlaceBet(state, new RouletteBet("player", RouletteBetKind.Straight, 10m, 17));
        state = RouletteEngine.PlaceBet(state, new RouletteBet("player", RouletteBetKind.Red, 5m));
        state = RouletteEngine.PlaceBet(state, new RouletteBet("player", RouletteBetKind.Even, 5m));

        state = RouletteEngine.RemoveBet(state, 1);
        var spin = RouletteEngine.Spin(state, new RoulettePocket(17));

        Assert.Equal(2, spin.Settlements.Length);
        Assert.Equal(360m, spin.Settlements[0].TotalReturn);
        Assert.Equal(0m, spin.Settlements[1].TotalReturn);
    }

    [Fact]
    public void ClearBetsRemovesAllOpenWagers()
    {
        var state = RouletteEngine.OpenRound("round-1");
        state = RouletteEngine.PlaceBet(state, new RouletteBet("player", RouletteBetKind.Red, 5m));
        state = RouletteEngine.ClearBets(state);

        Assert.Empty(state.Bets);
        Assert.Throws<RouletteRuleException>(() => RouletteEngine.Spin(state, new RoulettePocket(1)));
    }
}
