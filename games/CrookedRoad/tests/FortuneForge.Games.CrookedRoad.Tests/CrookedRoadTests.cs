using System.Text.Json;
using FortuneForge.Games.Abstractions.Auditing;
using FortuneForge.Games.CrookedRoad.Server;
using FortuneForge.Games.Slots;
using FortuneForge.Games.Slots.Persistence;
using Xunit;

namespace FortuneForge.Games.CrookedRoad.Tests;

public sealed class CrookedRoadTests
{
    private sealed class HighRandom : IGameRandomSource { public int Next(int maximumExclusive) => maximumExclusive - 1; }
    private sealed class ZeroRandom : IGameRandomSource { public int Next(int maximumExclusive) => 0; }
    private static CrookedRoadOutcome Paid(CrookedRoadSavedState state, long wager = 100) =>
        CrookedRoadGame.Spin(state, wager, false, Guid.NewGuid(), DateTime.UtcNow, new HighRandom());

    [Fact]
    public void ExactFullModelIsBelowReleaseCeilingIncludingAllFundedAndScatterTickets()
    {
        Assert.InRange(CrookedRoadMath.FullReturnUpperBound.Value, .9448, .9449);
        var bound = CrookedRoadMath.FullReturnUpperBound;
        Assert.True(bound.Numerator * 100 < bound.Denominator * 95);
        Assert.InRange(CrookedRoadMath.FortExpectedScore.Value, 0.1, 45);
        foreach (var origin in Enumerable.Range(0, 3)) Assert.True(CrookedRoadMath.WellExpectedScoreNumerator(origin) > 0);
        Assert.Equal(10, CrookedRoadRules.Data.Paylines.Length);
        Assert.Equal((2800, "7", 5), CrookedRoadRules.LineScore(["ACE", "ACE", "ACE", "ACE", "ACE"]));
    }

    [Fact]
    public void PaidFundingUsesAllTwentyWeightedWagersAndTileOverflowIsLossless()
    {
        var state = CrookedRoadState.Empty;
        for (var index = 0; index < 20; index++) state = Paid(state, index < 10 ? 100 : 5000).After;
        var ticket = Assert.Single(state.Tickets);
        Assert.Equal(2550, ticket.FundedWagerPoints);
        Assert.Equal(new CrookedRoadFunding(0, 20, 0), state.Funding);
        Assert.Equal(22, state.Hand.Length + state.QueuedTiles.Length + state.ReserveTileCounts.Values.Sum());
        Assert.Equal(2, state.Hand.Length);
        Assert.Equal(12, state.QueuedTiles.Length);
        Assert.True(state.ReserveTileCounts.Values.Sum() > 0);
        Assert.Equal(CrookedRoadState.Serialize(state), CrookedRoadState.Serialize(CrookedRoadState.Parse(CrookedRoadState.Serialize(state))));
    }

    [Fact]
    public void FullTicketBankRejectsBeforeAnyDrawAndBonusDoesNotEarnAnotherTicketOrTile()
    {
        var state = CrookedRoadState.Empty with { Tickets = Enumerable.Range(0, 16).Select(index => new CrookedRoadTicket($"ticket-{index}", 100)).ToArray() };
        var exception = Assert.Throws<CrookedRoadConflictException>(() => Paid(state));
        Assert.Equal("road-ticket-bank-full", exception.Code);
        var active = state with { Tickets = [], ActiveBonus = CrookedRoadGame.Begin(new("funded", 100), "fort", new HighRandom()) };
        var result = CrookedRoadGame.Spin(active, 100, true, Guid.NewGuid(), DateTime.UtcNow, new HighRandom());
        Assert.Equal(active.Funding, result.After.Funding);
        Assert.Equal(active.Hand, result.After.Hand);
        Assert.Empty(result.After.Tickets);
    }

    [Theory]
    [InlineData("well", 3)]
    [InlineData("fort", 1)]
    [InlineData("hill", 3)]
    public void DestinationsHaveDistinctFiniteGameplayAndOnlyFundedZeroWagerSteps(string destination, int expectedSteps)
    {
        var state = CrookedRoadState.Empty with { ActiveBonus = CrookedRoadGame.Begin(new("funded", 777), destination, new ZeroRandom()) };
        var steps = 0;
        var total = 0L;
        while (state.ActiveBonus is not null)
        {
            if (CrookedRoadViews.ActionRequired(state))
                state = CrookedRoadMap.Act(state, new(state.Revision, destination == "well" ? "choose-spring" : "choose-door", Choice: 1),
                    (_, _) => throw new InvalidOperationException());
            var result = CrookedRoadGame.Spin(state, 777, true, Guid.NewGuid(), DateTime.UtcNow, new ZeroRandom());
            steps++;
            total += result.Payout.TotalPoints;
            Assert.Equal(5, result.Reels.Length);
            Assert.All(result.Reels, reel => Assert.Equal(3, reel.Length));
            Assert.Empty(result.Payout.Paylines);
            Assert.Equal(destination, result.SpinFeature!.Kind);
            Assert.Equal(destination == "well", result.SpinFeature.Bonus.Board is not null);
            Assert.Equal(destination == "fort", result.SpinFeature.Bonus.Held is not null);
            state = result.After;
        }
        Assert.Equal(expectedSteps, steps);
        Assert.Equal(1, state.CompletedJourneys);
        Assert.True(total >= 0);
    }

    [Fact]
    public void HillDoorDeckNeverAppearsInPublicStateAndChoiceCannotBeChangedAfterSelection()
    {
        var state = CrookedRoadState.Empty with { ActiveBonus = CrookedRoadGame.Begin(new("funded", 100), "hill", new ZeroRandom()) };
        var json = JsonSerializer.Serialize(CrookedRoadViews.Public(state), new JsonSerializerOptions(JsonSerializerDefaults.Web));
        Assert.DoesNotContain("hiddenDoors", json);
        Assert.All(CrookedRoadViews.Public(state).ActiveBonus!.Doors, door => Assert.Null(door.PrizePoints));
        Assert.Throws<CrookedRoadConflictException>(() => CrookedRoadGame.Admit(state, 100, true));
        state = CrookedRoadMap.Act(state, new(state.Revision, "choose-door", Choice: 0), (_, _) => throw new InvalidOperationException());
        Assert.Throws<CrookedRoadConflictException>(() => CrookedRoadMap.Act(state, new(state.Revision, "choose-door", Choice: 1), (_, _) => throw new InvalidOperationException()));
        var outcome = CrookedRoadGame.Spin(state, 100, true, Guid.NewGuid(), DateTime.UtcNow, new HighRandom());
        Assert.Single(outcome.SpinFeature!.Bonus.Doors, door => door.Revealed);
        Assert.Equal(state.ActiveBonus!.HiddenDoors![0][0] * 100, outcome.Payout.TotalPoints);
        Assert.True(CrookedRoadViews.ActionRequired(outcome.After));
    }

    [Fact]
    public void NativeFortStateRejectsOversizedCoinValuesWithoutNarrowingThem()
    {
        var bonus = CrookedRoadGame.Begin(new("funded", 100), "fort", new ZeroRandom());
        var held = new long[9];
        held[0] = 4_294_967_297L;
        Assert.Throws<InvalidOperationException>(() => CrookedRoadState.Serialize(CrookedRoadState.Empty with
            { ActiveBonus = bonus with { Held = held } }));
    }

    [Fact]
    public void NativeStateCannotHoldTheSameFundedTicketBothInBankAndActive()
    {
        var ticket = new CrookedRoadTicket("funded", 100);
        Assert.Throws<InvalidOperationException>(() => CrookedRoadState.Serialize(CrookedRoadState.Empty with
            { Tickets = [ticket], ActiveBonus = CrookedRoadGame.Begin(ticket, "fort", new HighRandom()) }));
    }

    [Fact]
    public void RoadUsesReciprocalPortsAndSetOffReturnsReusableRoutePiecesFirst()
    {
        var board = new CrookedRoadTile?[9];
        board[7] = new("start", "straight", 0);
        board[4] = new("turn", "corner", 2);
        board[3] = new("exit", "straight", 1);
        board[8] = new("unused", "corner", 0);
        Assert.Equal(new[] { 7, 4, 3 }, CrookedRoadMap.Path(board, "well"));
        Assert.Empty(CrookedRoadMap.Path(board, "fort"));
        var state = CrookedRoadState.Empty with { Board = board, Tickets = [new("funded", 100)] };
        var next = CrookedRoadMap.Act(state, new(0, "set-off", Destination: "well"),
            (ticket, destination) => CrookedRoadGame.Begin(ticket, destination, new ZeroRandom()));
        Assert.Empty(next.Tickets);
        Assert.Null(next.Board[7]); Assert.Null(next.Board[4]); Assert.Null(next.Board[3]);
        Assert.Equal(board[8], next.Board[8]);
        Assert.Equal("well", next.ActiveBonus!.Kind);
        Assert.Equal(new[] { "start", "turn" }, next.Hand.Select(tile => tile.Id));
        Assert.Equal("exit", next.QueuedTiles[0].Id);
        Assert.Equal(6, next.Board.Count(tile => tile is not null) + next.Hand.Length + next.QueuedTiles.Length + next.ReserveTileCounts.Values.Sum());
        Assert.Equal(new[] { 7, 4, 3 }, next.LastTravel!.Path);
        Assert.NotNull(board[7]);
        board[4] = board[4]! with { Rotation = 0 };
        Assert.Empty(CrookedRoadMap.Path(board, "well"));
    }

    [Fact]
    public void NativePreparedEvidenceReplaysAndRejectsChangedDrawsFundingAndAmounts()
    {
        var before = CrookedRoadRoundStateCodec.Storage.Decode(new Dictionary<string, object>(), 0);
        var now = DateTime.UtcNow;
        var request = new CrookedRoadRoundRequest(Guid.NewGuid().ToString(), 100, false);
        var tape = new CrookedRoadTape(new HighRandom());
        var outcome = CrookedRoadGame.Spin(CrookedRoadState.Empty, 100, false, Guid.NewGuid(), now, tape);
        var after = before with { NativeStateJson = CrookedRoadState.Serialize(outcome.After) };
        var round = new CrookedRoadPreparedRound(Guid.NewGuid(), "account", request, before, after, outcome, tape.Finish(),
            CrookedRoadEvidence.Hash(outcome), 100, outcome.Payout.TotalPoints, now, null);
        CrookedRoadEvidence.Validate(round);
        Assert.Throws<InvalidOperationException>(() => CrookedRoadEvidence.Validate(round with { PayoutCents = 1 }));
        Assert.Throws<InvalidOperationException>(() => CrookedRoadEvidence.Validate(round with { Draws = round.Draws.Append(new(2, 0)).ToArray() }));
        Assert.Throws<InvalidOperationException>(() => CrookedRoadEvidence.Validate(round with { StateAfter = after with
        { NativeStateJson = CrookedRoadState.Serialize(outcome.After with { Funding = new(1, 20, 200) }) } }));
        var stored = CrookedRoadRoundStateCodec.Storage.Write(after);
        Assert.True(after.Matches(CrookedRoadRoundStateCodec.Storage.Decode(stored, 0)));
    }
}
