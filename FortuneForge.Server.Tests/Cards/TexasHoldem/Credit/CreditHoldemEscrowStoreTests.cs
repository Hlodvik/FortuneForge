using FortuneForge.Server.Cards.TexasHoldem.Credit;
using Xunit;

namespace FortuneForge.Server.Tests.Cards.TexasHoldem.Credit;

public sealed class CreditHoldemEscrowStoreTests
{
    private static readonly DateTime Start = new(2026, 10, 24, 19, 0, 0, DateTimeKind.Utc);

    [Fact]
    public async Task QueueExpiryReturnsReservationOnceEvenWithoutAConnectedPlayer()
    {
        var store = NewStore();
        var joined = await store.JoinAsync("one", "One", 0, "expire-queue", 11, Start, default, buyInCents: 2_500);
        Assert.Equal(7_500, joined.BalanceCents);
        await store.SweepAsync(Start.Add(CreditHoldemEscrow.QueueLifetime), default);
        await store.SweepAsync(Start.AddMinutes(6), default);
        var after = await store.GetSessionAsync("one", Start.AddMinutes(6), default);
        Assert.IsType<CreditHoldemIdleSessionResponse>(after.Session);
        Assert.Equal(10_000, after.BalanceCents);
        Assert.Equal(2, store.LedgerCount);
    }

    [Fact]
    public async Task ExistingTableWinsOverANewQueueAndReservedHumanReplacesAManagedSeatAtBoundary()
    {
        var store = NewStore(allowSingle: true);
        var first = await StartSolo(store);
        var match = store.MatchForTest(first.Table.MatchId);
        var pending = Assert.IsType<CreditHoldemQueueSessionResponse>((await store.JoinAsync(
            "two", "Two", 0, "human-takeover", 12, Start.AddSeconds(6), default, buyInCents: 4_000)).Session);
        Assert.Equal("pending-next-hand", Assert.Single(pending.Players).Status);
        Assert.Equal(4_000, Assert.Single(pending.Players).Stack);
        Assert.Equal(6_000, store.Balance("two"));
        Assert.DoesNotContain(match.Players, player => player.ActorId == "two");

        CreditHoldemEngine.ForceComplete(match, Start.AddSeconds(7));
        var result = Assert.IsType<CreditHoldemResultSessionResponse>((await store.GetSessionAsync("one", Start.AddSeconds(7), default)).Session);
        var before = match.Players.Single(player => player.ActorId == "one").Stack;
        var next = Assert.IsType<CreditHoldemMatchSessionResponse>((await store.NextHandAsync(
            "one", match.MatchId, result.Version, "deal-takeover", 13, Start.AddSeconds(8), default)).Session);
        Assert.Equal(match.MatchId, next.Table.MatchId);
        var internalNext = store.MatchForTest(match.MatchId);
        var joined = Assert.Single(internalNext.Players, player => player.ActorId == "two");
        Assert.Equal(4_000, joined.Stack + joined.CommittedHand);
        Assert.NotNull(joined.EscrowTicketId);
        var returning = internalNext.Players.Single(player => player.ActorId == "one");
        Assert.Equal(before, returning.Stack + returning.CommittedHand);
        Assert.Equal(7_500, store.Balance("one"));
        Assert.Equal(6_000, store.Balance("two"));
    }

    [Fact]
    public async Task PendingReservationCanBeCancelledAfterThePreviousHandSettles()
    {
        var store = NewStore(allowSingle: true);
        var first = await StartSolo(store);
        var pending = Assert.IsType<CreditHoldemQueueSessionResponse>((await store.JoinAsync(
            "two", "Two", 0, "pending-cancel", 12, Start.AddSeconds(6), default, buyInCents: 4_000)).Session);
        var match = store.MatchForTest(first.Table.MatchId);
        CreditHoldemEngine.ForceComplete(match, Start.AddSeconds(7));
        _ = await store.GetSessionAsync("one", Start.AddSeconds(7), default);
        var cancelled = await store.CancelAsync("two", pending.TicketId, pending.Version,
            "cancel-settled-pending", Start.AddSeconds(8), default);
        Assert.IsType<CreditHoldemIdleSessionResponse>(cancelled.Session);
        Assert.Equal(10_000, cancelled.BalanceCents);
        Assert.Empty(match.PendingTakeovers);
    }

    [Fact]
    public async Task StaleTableCommandsCannotDiscardANewReservationAtTheSameTable()
    {
        var store = NewStore(allowSingle: true);
        var first = await StartSolo(store);
        var match = store.MatchForTest(first.Table.MatchId);
        _ = await store.LeaveAsync("one", match.MatchId, match.Version, "leave-old", Start.AddSeconds(6), default);
        var rejoined = Assert.IsType<CreditHoldemQueueSessionResponse>((await store.JoinAsync(
            "one", "One", 0, "return-same-table", 12, Start.AddSeconds(7), default, buyInCents: 1_000)).Session);
        Assert.Equal("pending-next-hand", Assert.Single(rejoined.Players).Status);
        var reservedWallet = store.Balance("one");
        await Assert.ThrowsAsync<CreditHoldemConflictException>(() => store.LeaveAsync(
            "one", match.MatchId, match.Version, "stale-new-leave", Start.AddSeconds(7), default));
        await Assert.ThrowsAsync<CreditHoldemConflictException>(() => store.NextHandAsync(
            "one", match.MatchId, match.Version, "stale-next", 13, Start.AddSeconds(7), default));
        Assert.IsType<CreditHoldemQueueSessionResponse>((await store.GetSessionAsync("one", Start.AddSeconds(7), default)).Session);
        Assert.Equal(reservedWallet, store.Balance("one"));
        _ = await store.CancelAsync("one", rejoined.TicketId, rejoined.Version, "cancel-return", Start.AddSeconds(7), default);
        Assert.Equal(reservedWallet + 1_000, store.Balance("one"));
    }

    [Theory]
    [InlineData("1.001")]
    [InlineData("0")]
    [InlineData("100.01")]
    [InlineData("79228162514264337593543950335")]
    public void RequestedBuyInRejectsFractionalCentsAndOversizedValuesWithoutOverflow(string raw)
    {
        var value = decimal.Parse(raw, System.Globalization.CultureInfo.InvariantCulture);
        Assert.Throws<ArgumentOutOfRangeException>(() => CreditHoldemEscrow.RequestedCents(value, CreditHoldemTableRules.Resolve("standard")));
    }

    private static InMemoryCreditHoldemStore NewStore(bool allowSingle = false)
    {
        var store = new InMemoryCreditHoldemStore(allowSingle);
        store.SetBalance("one", 10_000);
        store.SetBalance("two", 10_000);
        return store;
    }

    private static async Task<CreditHoldemMatchSessionResponse> StartSolo(InMemoryCreditHoldemStore store)
    {
        _ = await store.JoinAsync("one", "One", 0, "start-solo", 11, Start, default, buyInCents: 2_500);
        return Assert.IsType<CreditHoldemMatchSessionResponse>((await store.GetSessionAsync(
            "one", Start.Add(CreditHoldemEngine.HumanGrace), default)).Session);
    }
}
