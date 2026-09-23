using System.Collections.Immutable;
using FortuneForge.Games.Abstractions;
using FortuneForge.Games.Craps;
using FortuneForge.Games.Dice;

namespace FortuneForge.Games.Tests;

public sealed class CrapsEngineTests
{
    [Theory]
    [InlineData(3, 4, CrapsRollResult.NaturalWin, 20)]
    [InlineData(1, 1, CrapsRollResult.CrapsLoss, 0)]
    [InlineData(6, 6, CrapsRollResult.CrapsLoss, 0)]
    public void ComeOutNaturalsAndCrapsResolvePassLine(
        int first,
        int second,
        CrapsRollResult expected,
        int expectedReturn)
    {
        var state = CrapsEngine.StartPassLine(new CrapsPassLineBet("player", 10m));

        var result = CrapsEngine.Roll(state, Roll(first, second));

        Assert.Equal(CrapsPassLinePhase.Resolved, result.State.Phase);
        Assert.Equal(expected, result.Outcome.Result);
        Assert.Equal(expectedReturn, result.Outcome.TotalReturn);
    }

    [Fact]
    public void PointHitWinsPassLineAfterNonDecisionRoll()
    {
        var state = CrapsEngine.StartPassLine(new CrapsPassLineBet("player", 25m));
        var point = CrapsEngine.Roll(state, Roll(2, 2));
        var noDecision = CrapsEngine.Roll(point.State, Roll(2, 3));

        var winner = CrapsEngine.Roll(noDecision.State, Roll(1, 3));

        Assert.Equal(4, winner.State.Point);
        Assert.Equal(CrapsRollResult.PointHit, winner.Outcome.Result);
        Assert.Equal(50m, winner.Outcome.TotalReturn);
        Assert.Equal(3, winner.State.Rolls.Length);
    }

    [Fact]
    public void SevenOutLosesPassLineAfterPointIsSet()
    {
        var state = CrapsEngine.StartPassLine(new CrapsPassLineBet("player", 10m));
        var point = CrapsEngine.Roll(state, Roll(3, 3));

        var result = CrapsEngine.Roll(point.State, Roll(3, 4));

        Assert.Equal(CrapsRollResult.SevenOut, result.Outcome.Result);
        Assert.Equal(0m, result.Outcome.TotalReturn);
        Assert.Throws<CrapsRuleException>(() => CrapsEngine.Roll(result.State, Roll(3, 3)));
    }

    [Fact]
    public void PassLineContractRejectsInvalidStake()
    {
        Assert.Throws<ArgumentOutOfRangeException>(() =>
            CrapsEngine.StartPassLine(new CrapsPassLineBet("player", 0m)));
        var state = CrapsEngine.StartPassLine(new CrapsPassLineBet("player", 10m));
        Assert.Throws<InvalidOperationException>(() => CrapsEngine.Roll(state, default));
    }

    [Fact]
    public void DescriptorDeclaresMinimalCasinoSkeleton()
    {
        var descriptor = CrapsModule.Descriptor;

        Assert.Equal("craps", descriptor.Id);
        Assert.Equal("0.1.1", descriptor.PackageVersion);
        Assert.Equal(GameCategory.Casino, descriptor.Category);
        Assert.True(descriptor.Capabilities.HasFlag(GameCapability.FreePlay));
        Assert.False(descriptor.Capabilities.HasFlag(GameCapability.Credits));
        Assert.False(descriptor.Capabilities.HasFlag(GameCapability.Multiplayer));
        Assert.False(descriptor.Capabilities.HasFlag(GameCapability.History));
    }

    private static DicePair Roll(int first, int second) => new(new DieValue(first), new DieValue(second));

    [Fact]
    public void StartPassLine_RejectsEmptyPlayerId()
    {
        Assert.Throws<ArgumentException>(() => CrapsEngine.StartPassLine(new CrapsPassLineBet(string.Empty, 10m)));
    }

    [Fact]
    public void NoDecision_DoesNotResolve_TotalReturnIsNull()
    {
        var bet = new CrapsPassLineBet("player", 5m);
        var state = new CrapsPassLineState(bet, CrapsPassLinePhase.Point, 6, ImmutableArray<DicePair>.Empty, null);
        var dice = new DicePair(new DieValue(2), new DieValue(3)); // total 5 -> no decision

        var trans = CrapsEngine.Roll(state, dice);

        Assert.Equal(CrapsRollResult.NoDecision, trans.Outcome.Result);
        Assert.False(trans.Outcome.IsTerminal);
        Assert.Null(trans.Outcome.TotalReturn);
    }
}
