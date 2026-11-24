using FortuneForge.Games.Slots;
using Xunit;

namespace FortuneForge.Games.PharaohsAscent.Tests;

public sealed class SealedTombRulesTests
{
    [Fact]
    public void SixNormallyChargedScarabSpinsFundOneTicketAtTheirAverageWager()
    {
        var state = SealedTombState.Empty;
        foreach (var wager in new long[] { 2, 4, 6, 8, 10, 12 })
            state = SealedTombRules.Accrue(state, Guid.NewGuid(), wager, false, 3);
        Assert.Equal(0, state.Sigils);
        Assert.Equal(0, state.SigilWagerPointTotal);
        Assert.Equal(7, Assert.Single(state.Tickets).FundedWagerPoints);
        Assert.Equal(6, state.Revision);
        Assert.Equal(SealedTombState.Serialize(state), SealedTombState.Serialize(SealedTombState.Parse(SealedTombState.Serialize(state))));
        Assert.Equal(6, state.View().Funding.RequiredSigils);
    }

    [Fact]
    public void FreeSpinsAndSingleScarabsCannotCreateFundedSigils()
    {
        var state = SealedTombRules.Accrue(SealedTombState.Empty, Guid.NewGuid(), 100, true, 5);
        state = SealedTombRules.Accrue(state, Guid.NewGuid(), 100, false, 1);
        Assert.Equal(0, state.Sigils);
        Assert.Empty(state.Tickets);
        state = SealedTombRules.Accrue(state, Guid.NewGuid(), 100, false, 5);
        Assert.Equal(1, state.Sigils);
        Assert.Equal(100, state.View().Funding.AverageWagerPoints);
    }

    [Fact]
    public void EntrySecuresFiveTimesTheFundedStakeWithoutDrawingAWagerOrPrize()
    {
        var transition = Act(Ticket(13), "enter", new ZeroDraws());
        var run = transition.After.ActiveRun!;
        Assert.Equal(0, transition.PayoutPoints);
        Assert.Empty(transition.After.Tickets);
        Assert.Equal(6, run.TorchesRemaining);
        Assert.Equal(65, run.BankedPoints);
        Assert.Equal(new[] { "coin", "relic", "key" }, run.Choices.Select(choice => choice.Kind));
        var collected = Act(transition.After, "collect", new NoDraws());
        Assert.Null(collected.After.ActiveRun);
        Assert.Equal(65, collected.PayoutPoints);
        Assert.Equal("collect", collected.After.LastRun!.Reason);
        Assert.Throws<SealedTombConflictException>(() => SealedTombRules.Accrue(collected.After, Guid.NewGuid(), 2, false, 0));
        Assert.Null(Act(collected.After, "dismiss", new NoDraws()).After.LastRun);
    }

    [Theory]
    [InlineData(0, 2)]
    [InlineData(49, 2)]
    [InlineData(50, 3)]
    [InlineData(84, 3)]
    [InlineData(85, 4)]
    [InlineData(99, 4)]
    public void CoinRoomsUseTheirExactProductionThresholds(int roll, int multiplier)
    {
        var entered = Act(Ticket(100), "enter", new ZeroDraws()).After;
        var opened = Open(entered, "coin", new SequenceDraws(roll, 0));
        Assert.Equal(100 * multiplier, opened.After.ActiveRun!.RoomPoints);
        Assert.Equal(500 + 100 * multiplier, opened.After.ActiveRun.BankedPoints);
        Assert.Equal(0, opened.PayoutPoints);
        Assert.Equal(5, opened.After.ActiveRun.TorchesRemaining);
    }

    [Fact]
    public void SixCoinRoomsAutomaticallySettleAllTreasureAndNeverBurnIt()
    {
        var state = Act(Ticket(100), "enter", new ZeroDraws()).After;
        long paid = 0;
        for (var room = 0; room < 6; room++)
        {
            var opened = Open(state, "coin", new ZeroDraws());
            state = opened.After;
            paid += opened.PayoutPoints;
            if (room < 5) Assert.Equal(0, opened.PayoutPoints);
        }
        Assert.Null(state.ActiveRun);
        Assert.Equal(1_700, paid);
        Assert.Equal(6, state.LastRun!.RoomsOpened);
        Assert.Equal("torches", state.LastRun.Reason);
    }

    [Fact]
    public void FullRelicSetsRetrospectivelyMultiplyRoomPrizesButNotTheEntryGuarantee()
    {
        var draws = new SequenceDraws(0, 1, 2, 0, 1, 2);
        var state = Act(Ticket(100), "enter", draws).After;
        for (var step = 0; step < 6; step++)
        {
            var opened = Open(state, "relic", draws);
            state = opened.After;
            if (step == 2)
            {
                Assert.Equal(2, state.ActiveRun!.RelicMultiplier);
                Assert.Equal(1_100, state.ActiveRun.BankedPoints);
            }
        }
        Assert.Equal(new SealedTombRelics(2, 2, 2), state.LastRun!.Relics);
        Assert.Equal(3, state.LastRun.RelicMultiplier);
        Assert.Equal(500, state.LastRun.BasePoints);
        Assert.Equal(600, state.LastRun.RoomPoints);
        Assert.Equal(2_300, state.LastRun.PayoutPoints);
    }

    [Theory]
    [InlineData(0, 8)]
    [InlineData(59, 8)]
    [InlineData(60, 12)]
    [InlineData(89, 12)]
    [InlineData(90, 20)]
    [InlineData(99, 20)]
    public void ThreeKeysUnlockASeparateSarcophagusFinaleWithNoAdditionalTorchCost(int roll, int multiplier)
    {
        var state = Act(Ticket(100), "enter", new ZeroDraws()).After;
        for (var key = 0; key < 3; key++) state = Open(state, "key", new ZeroDraws()).After;
        Assert.Equal("finale", state.ActiveRun!.Phase);
        Assert.Empty(state.ActiveRun.Choices);
        Assert.Equal(3, state.ActiveRun.TorchesRemaining);
        Assert.Equal(800, state.ActiveRun.BankedPoints);
        Assert.Throws<SealedTombConflictException>(() => Act(state, "collect", new NoDraws()));
        var completed = Act(state, "open-sarcophagus", new SequenceDraws(roll));
        Assert.Equal(800 + multiplier * 100, completed.PayoutPoints);
        Assert.Equal(multiplier * 100, completed.After.LastRun!.FinalePoints);
        Assert.Equal("sarcophagus", completed.After.LastRun.Reason);
    }

    [Fact]
    public void StaleRevisionsForgedChoicesAndLockedFinalesFailBeforeADraw()
    {
        var state = Act(Ticket(100), "enter", new ZeroDraws()).After;
        Assert.Throws<SealedTombConflictException>(() => SealedTombRules.Act(state, new("collect", state.Revision - 1), new NoDraws()));
        Assert.Throws<SealedTombConflictException>(() => SealedTombRules.Act(state, new("open-room", state.Revision, "forged"), new NoDraws()));
        Assert.Throws<SealedTombConflictException>(() => Act(state, "open-sarcophagus", new NoDraws()));
        Assert.Throws<ArgumentException>(() => SealedTombRules.Act(state, new("collect", state.Revision, "coin"), new NoDraws()));
    }

    [Fact]
    public void PersistenceRejectsUnfundedTicketsAndInconsistentRoomHistory()
    {
        Assert.Throws<InvalidOperationException>(() => SealedTombState.Serialize(Ticket(1)));
        Assert.Throws<InvalidOperationException>(() => SealedTombState.Serialize(Ticket(2_001)));
        Assert.Throws<InvalidOperationException>(() => SealedTombState.Serialize(SealedTombState.Empty with { Sigils = 1, SigilWagerPointTotal = 0 }));
        var run = Act(Ticket(100), "enter", new ZeroDraws()).After;
        Assert.Throws<InvalidOperationException>(() => SealedTombState.Serialize(run with
        { ActiveRun = run.ActiveRun! with { Path = [new(1, "coin", null, 250)] } }));
    }

    internal static SealedTombState Ticket(long stake) => SealedTombState.Empty with
    { Tickets = [new("tomb-test-funded-ticket", stake)] };
    private static SealedTombTransition Act(SealedTombState state, string action, IGameRandomSource random) =>
        SealedTombRules.Act(state, new(action, state.Revision), random);
    private static SealedTombTransition Open(SealedTombState state, string kind, IGameRandomSource random) =>
        SealedTombRules.Act(state, new("open-room", state.Revision, state.ActiveRun!.Choices.Single(choice => choice.Kind == kind).Id), random);

    private sealed class ZeroDraws : IGameRandomSource { public int Next(int maximumExclusive) => 0; }
    private sealed class NoDraws : IGameRandomSource { public int Next(int maximumExclusive) => throw new InvalidOperationException("Unexpected draw."); }
    private sealed class SequenceDraws(params int[] values) : IGameRandomSource
    {
        private int cursor;
        public int Next(int maximumExclusive)
        {
            Assert.True(cursor < values.Length, "The production action drew more random values than expected.");
            var value = values[cursor++];
            Assert.InRange(value, 0, maximumExclusive - 1);
            return value;
        }
    }
}
