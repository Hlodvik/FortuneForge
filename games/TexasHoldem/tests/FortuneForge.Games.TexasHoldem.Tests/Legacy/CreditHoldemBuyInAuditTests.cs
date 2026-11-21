using FortuneForge.Server.Cards.TexasHoldem.Credit;
using Xunit;

namespace FortuneForge.Server.Tests.Cards.TexasHoldem.Credit;

/// <summary>Independent money-flow checks: wallet money and reserved table chips must not overlap.</summary>
public sealed class CreditHoldemBuyInAuditTests
{
    private static readonly DateTime Start = new(2026, 10, 24, 18, 0, 0, DateTimeKind.Utc);

    [Fact]
    public async Task ReservationReplayCannotChangeAmountAndCancelReturnsTheReservationOnlyOnce()
    {
        var store = NewStore();
        var joined = await store.JoinAsync("audit", "Player", 0, "join-chosen", 17, Start, default,
            buyInCents: 2_500);
        var ticket = Assert.IsType<CreditHoldemQueueSessionResponse>(joined.Session);
        Assert.Equal(7_500, joined.BalanceCents);

        var replay = await store.JoinAsync("audit", "Player", 0, "join-chosen", 17, Start, default,
            buyInCents: 2_500);
        Assert.Equal(7_500, replay.BalanceCents);
        await Assert.ThrowsAsync<CreditHoldemConflictException>(() => store.JoinAsync(
            "audit", "Player", 0, "join-chosen", 17, Start, default, buyInCents: 5_000));

        var canceled = await store.CancelAsync("audit", ticket.TicketId, ticket.Version,
            "cancel-chosen", Start, default);
        var canceledAgain = await store.CancelAsync("audit", ticket.TicketId, ticket.Version,
            "cancel-chosen", Start, default);
        Assert.IsType<CreditHoldemIdleSessionResponse>(canceled.Session);
        Assert.Equal(10_000, canceled.BalanceCents);
        Assert.Equal(10_000, canceledAgain.BalanceCents);
        Assert.Equal(2, store.LedgerCount);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-1)]
    [InlineData(99)]
    [InlineData(10_001)]
    public async Task InvalidBuyInCannotTouchWalletOrCreateASession(int buyIn)
    {
        var store = NewStore();
        await Assert.ThrowsAnyAsync<ArgumentException>(() => store.JoinAsync(
            "audit", "Player", 0, "invalid", 17, Start, default, buyInCents: buyIn));
        Assert.Equal(10_000, store.Balance("audit"));
        Assert.Equal(0, store.LedgerCount);
        Assert.IsType<CreditHoldemIdleSessionResponse>((await store.GetSessionAsync("audit", Start, default)).Session);
    }

    [Fact]
    public async Task SpendingWalletElsewhereDoesNotSpendOrInvalidateReservedTableChips()
    {
        var store = NewStore(allowSingle: true);
        var session = await StartSolo(store, "reserve-full", 10_000);
        Assert.Equal(0, store.Balance("audit"));
        var match = store.MatchForTest(session.Table.MatchId);
        var player = match.Players.Single(value => value.ActorId == "audit");
        Assert.Equal(10_000, player.Stack + player.CommittedHand);

        // Finish managed turns without moving wall-clock time, to keep this a money-flow test.
        while (match.Status == "active" && match.ActiveSeat != player.Seat)
        {
            var actor = match.Players.Single(value => value.Seat == match.ActiveSeat);
            var legal = CreditHoldemEngine.LegalActions(match, actor);
            _ = CreditHoldemEngine.ApplyAction(match, actor.ActorId,
                legal.Contains(CreditHoldemActions.Check) ? CreditHoldemActions.Check : CreditHoldemActions.Call,
                null, Start.AddSeconds(6));
        }
        Assert.Equal("active", match.Status);
        var legalPlayer = CreditHoldemEngine.LegalActions(match, player);
        var action = legalPlayer.Contains(CreditHoldemActions.Call) ? CreditHoldemActions.Call : CreditHoldemActions.Check;
        var response = await store.ActionAsync("audit", match.MatchId,
            new(action, match.Version), "reserved-action", Start.AddSeconds(6), default);
        Assert.Equal(0, response.BalanceCents);
        Assert.Equal(1, store.LedgerCount);
    }

    [Fact]
    public async Task ShowdownKeepsWinningsAtTableAndLeaveCashOutEqualsActualRemainingStack()
    {
        var store = NewStore(allowSingle: true);
        var session = await StartSolo(store, "reserve-quarter", 2_500);
        var match = store.MatchForTest(session.Table.MatchId);
        CreditHoldemEngine.ForceComplete(match, Start.AddSeconds(8));
        var result = Assert.IsType<CreditHoldemResultSessionResponse>(
            (await store.GetSessionAsync("audit", Start.AddSeconds(8), default)).Session);
        var player = match.Players.Single(value => value.ActorId == "audit");
        var remaining = player.Stack;
        Assert.Equal(7_500, store.Balance("audit"));
        Assert.Equal(player.WonHandChips / 100m, result.Standings.Single(value => value.IsCurrentPlayer).PayoutCredits);

        var left = await store.LeaveAsync("audit", match.MatchId, result.Version,
            "cash-out", Start.AddSeconds(9), default);
        var replay = await store.LeaveAsync("audit", match.MatchId, result.Version,
            "cash-out", Start.AddSeconds(9), default);
        Assert.IsType<CreditHoldemIdleSessionResponse>(left.Session);
        Assert.Equal(7_500 + remaining, left.BalanceCents);
        Assert.Equal(left.BalanceCents, replay.BalanceCents);
    }

    [Fact]
    public async Task LeavingActiveHandReturnsOnlyUncommittedChipsAndLaterSettlementCannotPayAgain()
    {
        var store = NewStore(allowSingle: true);
        var session = await StartSolo(store, "active-buyin", 2_500);
        var match = store.MatchForTest(session.Table.MatchId);
        var player = match.Players.Single(value => value.ActorId == "audit");
        var uncommitted = player.Stack;
        var committed = player.CommittedHand;
        var left = await store.LeaveAsync("audit", match.MatchId, match.Version,
            "active-cashout", Start.AddSeconds(6), default);
        Assert.Equal(7_500 + uncommitted, left.BalanceCents);
        Assert.Equal(committed, player.CommittedHand);
        var after = store.Balance("audit");
        CreditHoldemEngine.ForceComplete(match, Start.AddSeconds(7));
        await store.SweepAsync(Start.AddSeconds(8), default);
        _ = await store.GetSessionAsync("audit", Start.AddSeconds(9), default);
        Assert.Equal(after, store.Balance("audit"));
    }

    [Fact]
    public async Task InactiveCompletedTableReturnsRemainingChipsWithoutRequiringReconnect()
    {
        var store = NewStore(allowSingle: true);
        var session = await StartSolo(store, "timeout-buyin", 2_500);
        var match = store.MatchForTest(session.Table.MatchId);
        var completed = Start.AddSeconds(8);
        CreditHoldemEngine.ForceComplete(match, completed);
        _ = await store.GetSessionAsync("audit", completed, default);
        var remaining = match.Players.Single(value => value.ActorId == "audit").Stack;
        var release = completed.Add(CreditHoldemTableCoordinator.ResultInactivityDuration);
        await store.SweepAsync(release, default);
        Assert.Equal(7_500 + remaining, store.Balance("audit"));
        var ledger = store.LedgerCount;
        await store.SweepAsync(release.AddSeconds(1), default);
        Assert.Equal(ledger, store.LedgerCount);
        Assert.IsType<CreditHoldemIdleSessionResponse>((await store.GetSessionAsync("audit", release, default)).Session);
    }

    private static InMemoryCreditHoldemStore NewStore(bool allowSingle = false)
    {
        var store = new InMemoryCreditHoldemStore(allowSingleHumanBotFill: allowSingle);
        store.SetBalance("audit", 10_000);
        return store;
    }

    private static async Task<CreditHoldemMatchSessionResponse> StartSolo(
        InMemoryCreditHoldemStore store, string key, int buyIn)
    {
        _ = await store.JoinAsync("audit", "Player", 0, key, 17, Start, default, buyInCents: buyIn);
        return Assert.IsType<CreditHoldemMatchSessionResponse>(
            (await store.GetSessionAsync("audit", Start.Add(CreditHoldemEngine.HumanGrace), default)).Session);
    }
}
