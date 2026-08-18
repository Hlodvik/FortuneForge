using System.Text.Json;
using FortuneForge.Server.Cards.TexasHoldem.Credit;
using Microsoft.Extensions.Configuration;
using Xunit;

namespace FortuneForge.Server.Tests.Cards.TexasHoldem.Credit;

public sealed class CreditHoldemStoreTests
{
    private static readonly DateTime Start = new(2026, 8, 16, 12, 0, 0, DateTimeKind.Utc);

    [Fact]
    public async Task QueueJoinAndLeaveAreFreeAndIdempotent()
    {
        var store = NewStore();
        var before = store.Balance("u1");

        var joined = await store.JoinAsync("u1", "Alice", 0, "join-free-1", 11, Start, default);
        var queue = Assert.IsType<CreditHoldemQueueSessionResponse>(joined.Session);
        var replay = await store.JoinAsync("u1", "Alice", 0, "join-free-1", 11, Start, default);
        await store.CancelAsync("u1", queue.TicketId, queue.Version, "leave-free-1", Start, default);

        Assert.IsType<CreditHoldemQueueSessionResponse>(replay.Session);
        Assert.Equal(before, store.Balance("u1"));
        Assert.Equal(0, store.LedgerCount);
        Assert.IsType<CreditHoldemIdleSessionResponse>((await store.GetSessionAsync("u1", Start, default)).Session);
    }

    [Fact]
    public async Task BlindsAndActionsCommitCreditsOnlyAfterServerValidation()
    {
        var store = NewStore();
        var match = await StartMatch(store);
        var table = match.Table;
        var internalMatch = store.MatchForTest(table.MatchId);
        var actor = internalMatch.Players.Single(player => player.Seat == table.ActiveSeat);
        Assert.False(actor.IsBot);
        var actorView = Assert.IsType<CreditHoldemMatchSessionResponse>(
            (await store.GetSessionAsync(actor.ActorId, Start.AddSeconds(6), default)).Session);
        var publicActor = actorView.Table.Seats.Single(seat => seat.IsCurrentPlayer);
        var required = actorView.Table.CurrentBet - publicActor.CommittedRound;
        Assert.True(required > 0);
        var before = store.Balance(actor.ActorId);

        var response = await store.ActionAsync(
            actor.ActorId, table.MatchId,
            new CreditHoldemActionRequest(CreditHoldemActions.Call, actorView.Version),
            "call-validated-1", Start.AddSeconds(7), default);
        var replay = await store.ActionAsync(
            actor.ActorId, table.MatchId,
            new CreditHoldemActionRequest(CreditHoldemActions.Call, actorView.Version),
            "call-validated-1", Start.AddSeconds(7), default);

        Assert.Equal(before - required, store.Balance(actor.ActorId));
        Assert.Equal(response.Session.Version, replay.Session.Version);
        await Assert.ThrowsAsync<CreditHoldemConflictException>(() => store.ActionAsync(
            actor.ActorId, table.MatchId,
            new CreditHoldemActionRequest(CreditHoldemActions.Call, actorView.Version),
            "different-key", Start.AddSeconds(7), default));
    }

    [Fact]
    public async Task HandSettlementCreditsRealWinnerExactlyOnceAndSignsHouseNet()
    {
        var store = NewStore();
        var session = await StartMatch(store);
        var match = store.MatchForTest(session.Table.MatchId);
        CreditHoldemEngine.ForceComplete(match, Start.AddSeconds(8));

        var first = Assert.IsType<CreditHoldemResultSessionResponse>(
            (await store.GetSessionAsync("u1", Start.AddSeconds(8), default)).Session);
        var balanceAfter = store.Balance("u1");
        var ledgerCount = store.LedgerCount;
        var replay = Assert.IsType<CreditHoldemResultSessionResponse>(
            (await store.GetSessionAsync("u1", Start.AddSeconds(9), default)).Session);

        Assert.Equal(balanceAfter, store.Balance("u1"));
        Assert.Equal(ledgerCount, store.LedgerCount);
        Assert.Equal(1, store.RevenueCount);
        Assert.Equal(first.HumanCommittedCredits - first.HumanPayoutCredits, first.HouseNetCredits);
        Assert.Equal(first.Version, replay.Version);
        Assert.Equal(first.HumanPayoutCredits, replay.HumanPayoutCredits);
        Assert.DoesNotContain("claim", JsonSerializer.Serialize(first), StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task NextHandKeepsTableIdentityAndRotatesHandWithoutNewQueueCharge()
    {
        var store = NewStore();
        var session = await StartMatch(store);
        var prior = store.MatchForTest(session.Table.MatchId);
        CreditHoldemEngine.ForceComplete(prior, Start.AddSeconds(8));
        var result = Assert.IsType<CreditHoldemResultSessionResponse>(
            (await store.GetSessionAsync("u1", Start.AddSeconds(8), default)).Session);

        var next = Assert.IsType<CreditHoldemMatchSessionResponse>((await store.NextHandAsync(
            "u1", result.MatchId, result.Version, "next-hand-1", 77,
            Start.AddSeconds(10), default)).Session);

        Assert.Equal(result.MatchId, next.Table.MatchId);
        Assert.Equal(result.HandNumber + 1, next.Table.HandNumber);
        Assert.Equal("active", next.Table.Status);
    }

    [Fact]
    public async Task LeaveDuringHandFoldsTheHumanAndPreservesCommittedChips()
    {
        var store = NewStore();
        var session = await StartMatch(store);
        var committedBefore = store.MatchForTest(session.Table.MatchId).Players
            .Single(player => player.ActorId == "u1").CommittedHand;

        var left = await store.LeaveAsync(
            "u1", session.Table.MatchId, session.Version, "leave-table-1", Start.AddSeconds(7), default);
        var match = store.MatchForTest(session.Table.MatchId);

        Assert.IsType<CreditHoldemIdleSessionResponse>(left.Session);
        var player = match.Players.Single(value => value.ActorId == "u1");
        Assert.Equal("folded", player.Status);
        Assert.Equal(committedBefore, player.CommittedHand);
        Assert.Contains("u1", match.LeavingActorIds);
    }

    [Fact]
    public async Task LateHumanWaitsForBoundaryAndThenUsesAnOpenSeat()
    {
        var store = NewStore();
        store.SetBalance("u3", 10_000);
        var session = await StartMatch(store);
        var pending = Assert.IsType<CreditHoldemQueueSessionResponse>((await store.JoinAsync(
            "u3", "Casey", 0, "late-seat-1", 33, Start.AddSeconds(7), default)).Session);
        Assert.Equal("pending-next-hand", pending.Players.Single().Status);

        var prior = store.MatchForTest(session.Table.MatchId);
        CreditHoldemEngine.ForceComplete(prior, Start.AddSeconds(8));
        var result = Assert.IsType<CreditHoldemResultSessionResponse>(
            (await store.GetSessionAsync("u1", Start.AddSeconds(8), default)).Session);
        var next = Assert.IsType<CreditHoldemMatchSessionResponse>((await store.NextHandAsync(
            "u1", result.MatchId, result.Version, "next-with-late-1", 44,
            Start.AddSeconds(10), default)).Session);

        Assert.Contains(next.Table.Seats, seat => seat.DisplayName == "Casey");
        Assert.InRange(next.Table.Seats.Count, CreditHoldemMoney.MinimumStartPlayers, CreditHoldemMoney.MaximumSeats);
    }

    [Fact]
    public void StatusHasFixedBlindsAndNoPoolOrFeeContract()
    {
        var source = new ConfigurationBuilder().AddInMemoryCollection().Build();
        var status = CreditHoldemController.StatusContract(source);
        var json = JsonSerializer.Serialize(status, new JsonSerializerOptions(JsonSerializerDefaults.Web));

        Assert.Equal((0.5m, 1m), (status.SmallBlindCredits, status.BigBlindCredits));
        Assert.DoesNotContain("buyIn", json, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("fee", json, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("pool", json, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task PublicProjectionRedactsOpponentsAndAllImplementationMetadata()
    {
        var store = NewStore();
        var session = await StartMatch(store);
        var json = JsonSerializer.Serialize(session, new JsonSerializerOptions(JsonSerializerDefaults.Web));

        Assert.DoesNotContain("dealSeed", json, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("botSkill", json, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("actorId", json, StringComparison.OrdinalIgnoreCase);
        Assert.All(session.Table.Seats.Where(seat => !seat.IsCurrentPlayer), seat =>
            Assert.All(seat.HoleCards, card => Assert.True(card.Hidden)));
    }

    [Fact]
    public async Task HistoryListsActiveAndCompletedHandsAndSeenNeverMovesMoney()
    {
        var store = NewStore();
        var session = await StartMatch(store);
        var active = Assert.Single((await store.HistoryAsync("u1", 30, default)).Items);
        Assert.Equal(("active", true, 1), (active.Status, active.Seen, active.HandNumber));

        var match = store.MatchForTest(session.Table.MatchId);
        CreditHoldemEngine.ForceComplete(match, Start.AddSeconds(8));
        _ = await store.GetSessionAsync("u1", Start.AddSeconds(8), default);
        var completed = Assert.Single((await store.HistoryAsync("u1", 30, default)).Items);
        var balance = store.Balance("u1");
        var seen = await store.MarkHistorySeenAsync("u1", completed.EventId, Start.AddSeconds(9), default);

        Assert.Equal("completed", completed.Status);
        Assert.False(completed.Seen);
        Assert.True(seen.Seen);
        Assert.Equal(balance, store.Balance("u1"));
        Assert.Single((await store.HistoryAsync("u1", 1, default)).Items);
    }

    private static InMemoryCreditHoldemStore NewStore()
    {
        var store = new InMemoryCreditHoldemStore();
        store.SetBalance("u1", 10_000);
        store.SetBalance("u2", 10_000);
        return store;
    }

    private static async Task<CreditHoldemMatchSessionResponse> StartMatch(InMemoryCreditHoldemStore store)
    {
        await store.JoinAsync("u1", "Alice", 0, "join-u1", 11, Start, default);
        await store.JoinAsync("u2", "Bruno", 0, "join-u2", 12, Start, default);
        return Assert.IsType<CreditHoldemMatchSessionResponse>(
            (await store.GetSessionAsync("u1", Start.AddSeconds(6), default)).Session);
    }
}
