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
        Assert.Throws<ArgumentOutOfRangeException>(() => engine.Start(99, 3));
        Assert.Throws<ArgumentOutOfRangeException>(() => engine.Start(501, 3));
        Assert.Throws<ArgumentOutOfRangeException>(() => engine.Start(10001, 3));
        Assert.Throws<ArgumentOutOfRangeException>(() => engine.Start(100, 0));
        Assert.Throws<ArgumentOutOfRangeException>(() => engine.Start(100, 25));
        Assert.Equal(0, generator.Calls);
    }

    [Theory]
    [InlineData(100)]
    [InlineData(500)]
    public void NewStartAcceptsTheOneThroughFiveRandBoundaries(long wager)
    {
        var generator = new FixedBoard(0, 1, 2);
        var round = new MinesEngine(generator).Start(wager, 3);
        Assert.Equal(wager, round.WagerCents);
        Assert.Equal(1000, round.HouseEdgeBasisPoints);
        Assert.Equal(2, round.PayoutPolicyVersion);
        Assert.Equal(1, generator.Calls);
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
        var start = engine.Start(234, 3);
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
        Assert.Equal(MinesRules.ReturnCents(234, 3, 2), cashOut.PayoutCents);
        Assert.All(new[] { start, one, two, cashOut }, state =>
        {
            Assert.Equal(234, state.WagerCents);
            Assert.Equal(3, state.MineCount);
            Assert.Equal(new[] { 0, 1, 2 }, state.MinePositions);
            Assert.Equal(1000, state.HouseEdgeBasisPoints);
            Assert.Equal(2, state.PayoutPolicyVersion);
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
            var round = engine.Start(500, mineCount);
            for (var tile = mineCount; tile < 25; tile++) round = engine.Reveal(round, tile);
            Assert.Equal(MinesRoundStatus.Cleared, round.Status);
            Assert.Equal(MinesRules.ReturnCents(500, mineCount, 25 - mineCount), round.PayoutCents);
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
        Assert.Equal(102, engine.Project(round).CashOutCents);
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
        Assert.Equal(MinesRules.Multiplier(3, 1), view.NextMultiplier);
        Assert.Equal(1000, view.HouseEdgeBasisPoints);
        Assert.Equal(2, view.PayoutPolicyVersion);
        Assert.Equal(0, view.NextMinePayoutCents);
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
        Assert.Equal(1000, restored.HouseEdgeBasisPoints);
        Assert.Equal(2, restored.PayoutPolicyVersion);
        Assert.Equal(JsonSerializer.Serialize(engine.CashOut(before)), JsonSerializer.Serialize(engine.CashOut(restored)));
        var loss = engine.Reveal(restored, 0);
        Assert.Equal(MinesRoundStatus.Lost, loss.Status);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void LegacyActiveJsonKeepsItsAcceptedPayoutScheduleThroughRevealCashOutAndProjection(bool webJson)
    {
        var options = webJson ? new JsonSerializerOptions(JsonSerializerDefaults.Web) : new JsonSerializerOptions();
        // Construct the exact old persistence shape: no payout-schedule field existed.
        var oldShape = new
        {
            WagerCents = 100L, MineCount = 3, MinePositions = new[] { 0, 1, 2 },
            RevealedTiles = new[] { 24 }, Status = MinesRoundStatus.Active, PayoutCents = 0L,
        };
        var round = JsonSerializer.Deserialize<MinesRoundState>(JsonSerializer.Serialize(oldShape, options), options)!;
        var engine = new MinesEngine();
        Assert.Equal(100, round.HouseEdgeBasisPoints);
        Assert.Equal(1, round.PayoutPolicyVersion);
        var view = engine.Project(round);
        Assert.Equal(100, view.HouseEdgeBasisPoints);
        Assert.Equal(1.125m, view.CurrentMultiplier);
        Assert.Equal(112, view.CashOutCents);
        Assert.Equal(112, engine.CashOut(round).PayoutCents);
        var next = engine.Reveal(round, 23);
        Assert.Equal(100, next.HouseEdgeBasisPoints);
        Assert.Equal(128, engine.Project(next).CashOutCents);
        var paid = engine.CashOut(next);
        Assert.Equal(100, paid.HouseEdgeBasisPoints);
        Assert.Equal(128, paid.PayoutCents);
        Assert.Equal(MinesRules.Multiplier(3, 2, 100, 1), engine.Project(paid).CurrentMultiplier);
        var replay = JsonSerializer.Deserialize<MinesRoundState>(JsonSerializer.Serialize(paid, options), options)!;
        Assert.Equal(128, replay.PayoutCents);
        Assert.Equal(100, replay.HouseEdgeBasisPoints);
    }

    [Fact]
    public void LegacyTerminalJsonValidatesAndProjectsOriginalReturnIncludingItsLargerMaximum()
    {
        var engine = new MinesEngine();
        var cashOutJson = """
            {"WagerCents":100,"MineCount":3,"MinePositions":[0,1,2],"RevealedTiles":[24],"Status":2,"PayoutCents":112}
            """;
        var cashOut = JsonSerializer.Deserialize<MinesRoundState>(cashOutJson)!;
        Assert.Equal(112, engine.Project(cashOut).PayoutCents);
        Assert.Equal(1.125m, engine.Project(cashOut).CurrentMultiplier);
        Assert.Equal(100, engine.Project(cashOut).HouseEdgeBasisPoints);

        var maximumOldShape = new
        {
            WagerCents = 10000L, MineCount = 12, MinePositions = Enumerable.Range(0, 12).ToArray(),
            RevealedTiles = Enumerable.Range(12, 13).ToArray(), Status = MinesRoundStatus.Cleared,
            PayoutCents = 51_482_970_000L,
        };
        var cleared = JsonSerializer.Deserialize<MinesRoundState>(JsonSerializer.Serialize(maximumOldShape))!;
        cleared.Validate();
        Assert.Equal(5_148_297m, engine.Project(cleared).CurrentMultiplier);
        Assert.Equal(51_482_970_000, engine.Project(cleared).PayoutCents);
        AssertTerminal(engine, cleared);
    }

    [Fact]
    public void LegacyFinalSafeRevealStillAutomaticallySettlesAtTheAcceptedSchedule()
    {
        var round = new MinesRoundState(10000, 12, [.. Enumerable.Range(0, 12)],
            [.. Enumerable.Range(12, 12)], MinesRoundStatus.Active, 0);
        var engine = new MinesEngine();
        var cleared = engine.Reveal(round, 24);
        Assert.Equal(MinesRoundStatus.Cleared, cleared.Status);
        Assert.Equal(100, cleared.HouseEdgeBasisPoints);
        Assert.Equal(51_482_970_000, cleared.PayoutCents);
    }

    [Theory]
    [InlineData(100, 10000, 11250, 12857)]
    [InlineData(1000, 10000, 10227, 11688)]
    [InlineData(100, 10, 11, 12)]
    [InlineData(1000, 10, 10, 11)]
    public void PreviouslyAcceptedWideWagersResumeAndCashOutAtTheirFrozenSchedule(
        int edge, long wager, long firstPayout, long secondPayout)
    {
        var oldShape = new
        {
            WagerCents = wager, MineCount = 3, MinePositions = new[] { 0, 1, 2 },
            RevealedTiles = new[] { 24 }, Status = MinesRoundStatus.Active, PayoutCents = 0L,
            HouseEdgeBasisPoints = edge,
        };
        var engine = new MinesEngine();
        Assert.Throws<ArgumentOutOfRangeException>(() => engine.Start(wager, 3));
        var restored = JsonSerializer.Deserialize<MinesRoundState>(JsonSerializer.Serialize(oldShape))!;
        Assert.Equal(firstPayout, engine.Project(restored).CashOutCents);
        Assert.Equal(firstPayout, engine.CashOut(restored).PayoutCents);
        var continued = engine.Reveal(restored, 23);
        Assert.Equal(wager, continued.WagerCents);
        Assert.Equal(edge, continued.HouseEdgeBasisPoints);
        Assert.Equal(secondPayout, engine.CashOut(continued).PayoutCents);
    }

    [Theory]
    [InlineData(100, 51_482_970_000L)]
    [InlineData(1000, 46_802_700_000L)]
    public void PreviouslyAcceptedHundredRandRoundsCanStillClearTheEntireBoardAndRestoreItsMaximum(int edge, long maximum)
    {
        var round = new MinesRoundState(10000, 12, [.. Enumerable.Range(0, 12)],
            [.. Enumerable.Range(12, 12)], MinesRoundStatus.Active, 0, edge);
        var restored = JsonSerializer.Deserialize<MinesRoundState>(JsonSerializer.Serialize(round))!;
        var engine = new MinesEngine();
        var cleared = engine.Reveal(restored, 24);
        Assert.Equal(MinesRoundStatus.Cleared, cleared.Status);
        Assert.Equal(edge, cleared.HouseEdgeBasisPoints);
        Assert.Equal(maximum, cleared.PayoutCents);
        var finalRestore = JsonSerializer.Deserialize<MinesRoundState>(JsonSerializer.Serialize(cleared))!;
        Assert.Equal(maximum, engine.Project(finalRestore).PayoutCents);
        AssertTerminal(engine, finalRestore);
    }

    [Theory]
    [InlineData(1, 93)]
    [InlineData(2, 97)]
    public void NewLowMineRoundsCanCashOutFirstGemForLessThanTheStake(int mines, long payout)
    {
        var engine = new MinesEngine(new FixedBoard([.. Enumerable.Range(0, mines)]));
        var round = engine.Reveal(engine.Start(100, mines), 24);
        var view = engine.Project(round);
        Assert.Equal(1000, round.HouseEdgeBasisPoints);
        Assert.True(view.CanCashOut);
        Assert.True(view.CurrentMultiplier < 1);
        Assert.Equal(payout, view.CashOutCents);
        Assert.Equal(payout, engine.CashOut(round).PayoutCents);
    }

    [Fact]
    public void PersistenceCannotSubstituteAnUnknownOrIncorrectPayoutSchedule()
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => new MinesRoundState(100, 3, [0, 1, 2], [],
            MinesRoundStatus.Active, 0, 999));
        Assert.Throws<ArgumentException>(() => new MinesRoundState(100, 3, [0, 1, 2], [24],
            MinesRoundStatus.CashedOut, 112, 1000));
        Assert.Throws<ArgumentException>(() => new MinesRoundState(100, 3, [0, 1, 2], [24],
            MinesRoundStatus.CashedOut, 102, 100));
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
