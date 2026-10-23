using System.Collections.Immutable;
using System.Text.Json;

namespace FortuneForge.Games.Mines.Tests;

public sealed class MinesEngineTests
{
    [Fact]
    public void InvalidWagerOrMineCountNeverGeneratesABoard()
    {
        var generator = new FixedBoard(0, 1, 2);
        var engine = new MinesEngine(generator);
        Assert.Throws<ArgumentOutOfRangeException>(() => engine.Start(9, 3));
        Assert.Throws<ArgumentOutOfRangeException>(() => engine.Start(10001, 3));
        Assert.Throws<ArgumentOutOfRangeException>(() => engine.Start(100, 0));
        Assert.Throws<ArgumentOutOfRangeException>(() => engine.Start(100, 25));
        Assert.Equal(0, generator.Calls);
    }

    [Fact]
    public void SecureGeneratorProducesExactlyTheFixedNumberOfDistinctValidMines()
    {
        var generator = new SecureMinesBoardGenerator();
        for (var mineCount = 1; mineCount <= 24; mineCount++)
        {
            for (var iteration = 0; iteration < 32; iteration++)
            {
                var board = generator.Generate(mineCount);
                Assert.Equal(mineCount, board.Length);
                Assert.Equal(mineCount, board.Distinct().Count());
                Assert.All(board, tile => Assert.InRange(tile, 0, 24));
                Assert.Equal(board.Order(), board);
            }
        }
        Assert.Throws<ArgumentOutOfRangeException>(() => generator.Generate(0));
        Assert.Throws<ArgumentOutOfRangeException>(() => generator.Generate(25));
    }

    [Fact]
    public void FullCashOutFlowKeepsBoardWagerAndMineCountAndLeavesPriorStateUntouched()
    {
        var generator = new FixedBoard(0, 1, 2);
        var engine = new MinesEngine(generator);
        var start = engine.Start(1234, 3);
        Assert.Equal(MinesRoundStatus.Active, start.Status);
        Assert.Empty(start.RevealedTiles);
        Assert.Throws<InvalidOperationException>(() => engine.CashOut(start));
        var one = engine.Reveal(start, 10);
        var two = engine.Reveal(one, 15);
        var cashOut = engine.CashOut(two);
        Assert.Empty(start.RevealedTiles);
        Assert.Equal(new[] { 10 }, one.RevealedTiles);
        Assert.Equal(new[] { 10, 15 }, two.RevealedTiles);
        Assert.Equal(MinesRoundStatus.Active, two.Status);
        Assert.Equal(MinesRoundStatus.CashedOut, cashOut.Status);
        Assert.Equal(MinesRules.ReturnCents(1234, 3, 2), cashOut.PayoutCents);
        Assert.All(new[] { start, one, two, cashOut }, state =>
        {
            Assert.Equal(1234, state.WagerCents);
            Assert.Equal(3, state.MineCount);
            Assert.Equal(new[] { 0, 1, 2 }, state.MinePositions);
        });
        Assert.Equal(1, generator.Calls);
        AssertTerminal(engine, cashOut);
        var view = engine.Project(cashOut);
        Assert.Equal(cashOut.PayoutCents, view.PayoutCents);
        Assert.False(view.CanCashOut);
        Assert.Null(view.NextMultiplier);
        Assert.DoesNotContain(view.Tiles, tile => tile.Kind == MinesTileKind.Hidden);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(1)]
    [InlineData(2)]
    public void HittingAnyPredrawnMineImmediatelyLosesAndRevealsWholeBoard(int mine)
    {
        var engine = new MinesEngine(new FixedBoard(0, 1, 2));
        var round = engine.Reveal(engine.Start(100, 3), 24);
        round = engine.Reveal(round, mine);
        Assert.Equal(MinesRoundStatus.Lost, round.Status);
        Assert.Equal(0, round.PayoutCents);
        Assert.Equal(new[] { 24, mine }, round.RevealedTiles);
        var view = engine.Project(round);
        Assert.Equal(1, view.SafeReveals);
        Assert.Equal(0m, view.CurrentMultiplier);
        Assert.Equal(0, view.CashOutCents);
        Assert.Equal(3, view.Tiles.Count(tile => tile.Kind == MinesTileKind.Mine));
        Assert.Equal(22, view.Tiles.Count(tile => tile.Kind == MinesTileKind.Gem));
        Assert.True(view.Tiles[mine].IsRevealed);
        AssertTerminal(engine, round);
    }

    [Fact]
    public void AllSafeTilesAutomaticallySettleExactlyOnceForEveryMineCount()
    {
        for (var mineCount = 1; mineCount <= 24; mineCount++)
        {
            var engine = new MinesEngine(new FixedBoard([.. Enumerable.Range(0, mineCount)]));
            var round = engine.Start(10000, mineCount);
            for (var tile = mineCount; tile < 25; tile++) round = engine.Reveal(round, tile);
            Assert.Equal(MinesRoundStatus.Cleared, round.Status);
            Assert.Equal(MinesRules.ReturnCents(10000, mineCount, 25 - mineCount), round.PayoutCents);
            var view = engine.Project(round);
            Assert.Equal(0, view.GemsRemaining);
            Assert.Null(view.NextMultiplier);
            Assert.Equal(0, view.CashOutCents);
            AssertTerminal(engine, round);
        }
    }

    [Fact]
    public void InvalidOrRepeatedTileCannotAdvanceRound()
    {
        var engine = new MinesEngine(new FixedBoard(0, 1, 2));
        var round = engine.Start(100, 3);
        Assert.Throws<ArgumentOutOfRangeException>(() => engine.Reveal(round, -1));
        Assert.Throws<ArgumentOutOfRangeException>(() => engine.Reveal(round, 25));
        round = engine.Reveal(round, 24);
        Assert.Throws<InvalidOperationException>(() => engine.Reveal(round, 24));
        Assert.Equal(new[] { 24 }, round.RevealedTiles);
        Assert.Equal(112, engine.Project(round).CashOutCents);
    }

    [Fact]
    public void ActiveProjectionCannotDistinguishUnrevealedBoards()
    {
        var firstEngine = new MinesEngine(new FixedBoard(0, 1, 2));
        var secondEngine = new MinesEngine(new FixedBoard(20, 21, 22));
        var first = firstEngine.Reveal(firstEngine.Start(100, 3), 12);
        var second = secondEngine.Reveal(secondEngine.Start(100, 3), 12);
        var firstJson = JsonSerializer.Serialize(firstEngine.Project(first));
        Assert.Equal(firstJson, JsonSerializer.Serialize(secondEngine.Project(second)));
        Assert.DoesNotContain("MinePositions", firstJson);
        Assert.DoesNotContain("RevealedTiles", firstJson);
        var view = firstEngine.Project(first);
        Assert.Equal(24, view.Tiles.Count(tile => tile.Kind == MinesTileKind.Hidden));
        Assert.Equal(MinesTileKind.Gem, view.Tiles[12].Kind);
        Assert.DoesNotContain(view.Tiles, tile => tile.Kind == MinesTileKind.Mine);
        Assert.True(view.CanCashOut);
        Assert.Equal(MinesRules.Multiplier(3, 2), view.NextMultiplier);
    }

    [Fact]
    public void NewRoundProjectionContainsNoMineInformationAndRequiresFirstGem()
    {
        var engine = new MinesEngine(new FixedBoard(0, 1, 2));
        var view = engine.Project(engine.Start(100, 3));
        Assert.Equal(25, view.Tiles.Length);
        Assert.All(view.Tiles, tile =>
        {
            Assert.Equal(MinesTileKind.Hidden, tile.Kind);
            Assert.False(tile.IsRevealed);
        });
        Assert.False(view.CanCashOut);
        Assert.Equal(0, view.CashOutCents);
        Assert.Equal(1m, view.CurrentMultiplier);
        Assert.Equal(1.125m, view.NextMultiplier);
    }

    [Fact]
    public void PrivatePersistenceRoundTripsAndResumesIdentically()
    {
        var engine = new MinesEngine(new FixedBoard(0, 1, 2));
        var before = engine.Reveal(engine.Start(100, 3), 24);
        var json = JsonSerializer.Serialize(before);
        var restored = JsonSerializer.Deserialize<MinesRoundState>(json)!;
        restored.Validate();
        Assert.Equal(json, JsonSerializer.Serialize(restored));
        Assert.Equal(JsonSerializer.Serialize(engine.CashOut(before)), JsonSerializer.Serialize(engine.CashOut(restored)));
        var loss = engine.Reveal(restored, 0);
        Assert.Equal(MinesRoundStatus.Lost, loss.Status);
    }

    [Fact]
    public void StateConstructorRejectsCorruptedPersistence()
    {
        ImmutableArray<int> mines = [0, 1, 2];
        Assert.Throws<ArgumentException>(() => new MinesRoundState(100, 3, [0, 0, 1], [], MinesRoundStatus.Active, 0));
        Assert.Throws<ArgumentException>(() => new MinesRoundState(100, 3, [2, 1, 0], [], MinesRoundStatus.Active, 0));
        Assert.Throws<ArgumentException>(() => new MinesRoundState(100, 3, [-1, 1, 2], [], MinesRoundStatus.Active, 0));
        Assert.Throws<ArgumentException>(() => new MinesRoundState(100, 3, mines, [24, 24], MinesRoundStatus.Active, 0));
        Assert.Throws<ArgumentException>(() => new MinesRoundState(100, 3, mines, [0], MinesRoundStatus.Active, 0));
        Assert.Throws<ArgumentException>(() => new MinesRoundState(100, 3, mines, [0, 24], MinesRoundStatus.Lost, 0));
        Assert.Throws<ArgumentException>(() => new MinesRoundState(100, 3, mines, [0, 1], MinesRoundStatus.Lost, 0));
        Assert.Throws<ArgumentException>(() => new MinesRoundState(100, 3, mines, [], MinesRoundStatus.CashedOut, 100));
        Assert.Throws<ArgumentException>(() => new MinesRoundState(100, 3, mines, [24], MinesRoundStatus.CashedOut, 113));
        Assert.Throws<ArgumentException>(() => new MinesRoundState(100, 3, mines, [24], MinesRoundStatus.Cleared, 112));
        Assert.Throws<ArgumentException>(() => new MinesRoundState(100, 3, mines, [], MinesRoundStatus.Active, 1));
    }

    [Fact]
    public void ModuleDescriptorIsValidAndClaimsSinglePlayerCapabilities()
    {
        MinesModule.Descriptor.Validate();
        Assert.Equal("mines", MinesModule.Descriptor.Id);
        Assert.False(MinesModule.Descriptor.Capabilities.HasFlag(Abstractions.GameCapability.Multiplayer));
    }

    private static void AssertTerminal(MinesEngine engine, MinesRoundState state)
    {
        Assert.Throws<InvalidOperationException>(() => engine.CashOut(state));
        Assert.Throws<InvalidOperationException>(() => engine.Reveal(state, 23));
    }

    private sealed class FixedBoard(params int[] mines) : IMinesBoardGenerator
    {
        public int Calls { get; private set; }
        public ImmutableArray<int> Generate(int mineCount)
        {
            Calls++;
            return [.. mines];
        }
    }
}
