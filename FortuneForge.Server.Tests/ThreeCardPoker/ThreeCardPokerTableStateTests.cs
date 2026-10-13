using System.Text.Json;
using FortuneForge.Games.ThreeCardPoker;
using FortuneForge.Server.Bots.ThreeCardPoker;
using FortuneForge.Server.Cards.ThreeCardPoker.Table;
using Xunit;

namespace FortuneForge.Server.Tests.ThreeCardPoker;

public sealed class ThreeCardPokerTableStateTests
{
    private static readonly DateTime Start = new(2026, 10, 12, 12, 0, 0, DateTimeKind.Utc);

    [Theory]
    [InlineData(1, 3, 2)]
    [InlineData(2, 3, 1)]
    [InlineData(3, 3, 0)]
    [InlineData(4, 4, 0)]
    public async Task QueueUsesTheSameFreeGraceAndManagedFillAsBlackjack(int humans, int occupied, int managed)
    {
        var store = Store();
        for (var index = 0; index < humans; index++)
        {
            var user = $"human-{index}";
            store.SetBalance(user, 10_000);
            var joined = await store.JoinAsync(user, $"Player {index}", 0, Key($"join-{index}"), Start, default);
            Assert.IsType<ThreeCardPokerTableQueueSessionResponse>(joined.Session);
        }
        Assert.IsType<ThreeCardPokerTableQueueSessionResponse>((await store.GetSessionAsync(
            "human-0", Start.Add(ThreeCardPokerTableEngine.HumanGrace).AddTicks(-1), default)).Session);

        var session = Assert.IsType<ThreeCardPokerTablePlaySessionResponse>((await store.GetSessionAsync(
            "human-0", Start.Add(ThreeCardPokerTableEngine.HumanGrace), default)).Session);
        var table = store.TableForTest(session.Table.TableId);
        Assert.Equal(occupied, table.Players.Count);
        Assert.Equal(humans, table.Players.Count(player => !ThreeCardPokerManagedSeat.IsManaged(player)));
        Assert.Equal(managed, table.Players.Count(ThreeCardPokerManagedSeat.IsManaged));
        Assert.Equal(ThreeCardPokerTablePhases.Betting, session.Table.Phase);
        Assert.Empty(store.Ledger);
        for (var index = 0; index < humans; index++) Assert.Equal(10_000, store.Balance($"human-{index}"));
    }

    [Fact]
    public async Task FiveQueuedHumansSplitAcrossTablesAndKeepTheReservedSeatFree()
    {
        var store = Store();
        for (var index = 0; index < 5; index++)
            await store.JoinAsync($"human-{index}", $"Player {index}", 0, Key($"join-{index}"), Start, default);
        await store.GetSessionAsync("human-0", Start.Add(ThreeCardPokerTableEngine.HumanGrace), default);
        Assert.Equal(2, store.StateForTest.Tables.Count);
        Assert.All(store.StateForTest.Tables.Values, table => Assert.InRange(table.Players.Count, 3, 4));
        Assert.Equal(5, store.StateForTest.Tables.Values.Sum(table => table.Players.Count(player => !ThreeCardPokerManagedSeat.IsManaged(player))));
    }

    [Fact]
    public async Task QueueCancellationReplaysWithoutChargingOrCreatingATable()
    {
        var store = Store();
        store.SetBalance("human", 10_000);
        var queue = Assert.IsType<ThreeCardPokerTableQueueSessionResponse>((await store.JoinAsync(
            "human", "Player", 0, Key("queue"), Start, default)).Session);
        for (var index = 0; index < 2; index++)
        {
            var cancelled = await store.CancelAsync("human", queue.TicketId, queue.Version, Key("cancel"), Start.AddSeconds(1), default);
            Assert.IsType<ThreeCardPokerTableIdleSessionResponse>(cancelled.Session);
        }
        await store.SweepAsync(Start.AddMinutes(1), default);
        Assert.Empty(store.StateForTest.Tables);
        Assert.Empty(store.Ledger);
        Assert.Equal(10_000, store.Balance("human"));
    }

    [Fact]
    public async Task AnteAndPairPlusAreDebitedTogetherAndReleasedOnceWhenLeavingBeforeDealing()
    {
        var (store, table, actor, now) = await AllHumanTable();
        var first = await store.WagerAsync(actor, table.Table.TableId, 500, 200, table.Version, Key("wager"), now, default);
        await store.WagerAsync(actor, table.Table.TableId, 500, 200, table.Version, Key("wager"), now, default);
        Assert.Equal(9_300, store.Balance(actor));
        var latest = Assert.IsType<ThreeCardPokerTablePlaySessionResponse>(first.Session);
        for (var index = 0; index < 2; index++)
            await store.LeaveAsync(actor, latest.Table.TableId, latest.Version, Key("leave"), now, default);
        Assert.Equal(10_000, store.Balance(actor));
        Assert.Equal(new long[] { -700, 700 }, store.Ledger.Select(entry => entry.AmountCents));
    }

    [Fact]
    public async Task LeavingRejoiningAndLeavingTheSameTableCreatesDistinctRefunds()
    {
        var (store, session, actor, now) = await AllHumanTable();
        var tableId = session.Table.TableId;
        var first = Assert.IsType<ThreeCardPokerTablePlaySessionResponse>((await store.WagerAsync(
            actor, tableId, 100, 50, session.Version, Key("first-wager"), now, default)).Session);
        await store.LeaveAsync(actor, tableId, first.Version, Key("first-leave"), now, default);
        var rejoined = await store.JoinAsync(actor, "Player", 0, Key("rejoin"), now, default);
        session = Assert.IsType<ThreeCardPokerTablePlaySessionResponse>(rejoined.Session);
        Assert.Equal(tableId, session.Table.TableId);
        for (var step = 0; step < 10; step++)
        {
            session = Assert.IsType<ThreeCardPokerTablePlaySessionResponse>((await store.GetSessionAsync(actor, now, default)).Session);
            var active = store.TableForTest(tableId).Players.Single(player => player.Seat == session.Table.ActiveSeat).ActorId;
            if (active == actor) break;
            await store.SitOutAsync(active, tableId, session.Version, Key($"sitout-{active}"), now, default);
        }
        var second = Assert.IsType<ThreeCardPokerTablePlaySessionResponse>((await store.WagerAsync(
            actor, tableId, 150, 50, session.Version, Key("second-wager"), now, default)).Session);
        await store.LeaveAsync(actor, tableId, second.Version, Key("second-leave"), now, default);
        Assert.Equal(10_000, store.Balance(actor));
        Assert.Equal(new long[] { -150, 150, -200, 200 }, store.Ledger.Where(entry => entry.UserId == actor).Select(entry => entry.AmountCents));
        Assert.Equal(4, store.Ledger.Select(entry => entry.Id).Distinct().Count());
    }
    [Fact]
    public async Task FailedAndStaleWagersLeaveWalletAndCommittedWagerUnchanged()
    {
        var (store, table, actor, now) = await AllHumanTable();
        store.SetBalance(actor, 100);
        await Assert.ThrowsAsync<ThreeCardPokerTableInsufficientCreditsException>(() => store.WagerAsync(
            actor, table.Table.TableId, 100, 50, table.Version, Key("too-much"), now, default));
        Assert.Equal(100, store.Balance(actor));
        Assert.Empty(store.Ledger);
        Assert.Equal(0, store.TableForTest(table.Table.TableId).Players.Single(player => player.ActorId == actor).NextWagerCents);

        store.SetBalance(actor, 10_000);
        await store.WagerAsync(actor, table.Table.TableId, 100, 50, table.Version, Key("wager"), now, default);
        await Assert.ThrowsAsync<ThreeCardPokerTableConflictException>(() => store.WagerAsync(
            actor, table.Table.TableId, 200, 50, table.Version, Key("wager"), now, default));
        await Assert.ThrowsAsync<ThreeCardPokerTableConflictException>(() => store.WagerAsync(
            actor, table.Table.TableId, 100, 50, table.Version, Key("stale"), now, default));
        Assert.Equal(9_850, store.Balance(actor));
        Assert.Single(store.Ledger);
    }

    [Fact]
    public void ProjectionKeepsDealerAndOtherPlayersCardsAndHandRanksPrivateUntilReveal()
    {
        var table = ProjectionTable();
        ThreeCardPokerTableEngine.Deal(table, PairDeck(2), 3, Start);
        var projection = ThreeCardPokerTableProjection.Table(table, "alice", Start);
        var own = projection.Seats.Single(seat => seat.IsCurrentPlayer);
        Assert.All(own.Hand.Cards, card => Assert.False(card.Hidden));
        Assert.NotNull(own.Hand.Rank);
        var other = projection.Seats.Single(seat => !seat.IsCurrentPlayer);
        Assert.Equal(3, other.Hand.Cards.Count);
        Assert.Null(other.Hand.Rank);
        AssertHidden(other.Hand.Cards);
        AssertHidden(projection.Dealer.Cards);
        Assert.Null(projection.Dealer.Rank);

        using var json = JsonDocument.Parse(JsonSerializer.Serialize(projection, new JsonSerializerOptions(JsonSerializerDefaults.Web)));
        var otherJson = json.RootElement.GetProperty("seats").EnumerateArray().Single(seat => !seat.GetProperty("isCurrentPlayer").GetBoolean());
        foreach (var card in otherJson.GetProperty("hand").GetProperty("cards").EnumerateArray())
        {
            Assert.False(card.TryGetProperty("rank", out _));
            Assert.False(card.TryGetProperty("suit", out _));
        }
        Assert.DoesNotContain("alice", json.RootElement.GetRawText());
        Assert.DoesNotContain("bob", json.RootElement.GetRawText());
    }

    [Fact]
    public void DealerRevealDoesNotLeakAResultBeforeAllDealerCardsAreVisible()
    {
        var table = ProjectionTable();
        ThreeCardPokerTableEngine.Deal(table, PairDeck(2), 3, Start);
        table.Phase = ThreeCardPokerTablePhases.Dealer;
        table.DealerVisibleCardCount = 1;
        foreach (var player in table.Players)
        {
            player.Status = "completed";
            player.Outcome = "player-win";
            player.PayoutCents = 400;
            player.PairPlusPayoutCents = 100;
            player.AnteBonusPayoutCents = 100;
        }
        var projection = ThreeCardPokerTableProjection.Table(table, "alice", Start);
        AssertHidden(projection.Dealer.Cards);
        Assert.Null(projection.Dealer.Rank);
        Assert.All(projection.Seats, seat =>
        {
            Assert.Null(seat.Outcome);
            Assert.Equal(0m, seat.Payout);
            Assert.Equal(0m, seat.PairPlusPayout);
            Assert.Equal(0m, seat.AnteBonusPayout);
        });
    }

    [Theory]
    [InlineData("play", 10_150, 250, 400)]
    [InlineData("fold", 9_950, 150, 100)]
    public async Task PlayOrFoldAndItsPayoutAreRecordedExactlyOnce(string action, long expectedBalance, long expectedCommitted, long expectedPayout)
    {
        var (store, table, actor, now) = await WagerAndDeal();
        var acted = await store.ActionAsync(actor, table.Table.TableId, action, table.Version, Key("decision"), now, default);
        await store.ActionAsync(actor, table.Table.TableId, action, table.Version, Key("decision"), now, default);
        Assert.Equal(action == ThreeCardPokerActions.Play ? 9_750 : 9_850, store.Balance(actor));
        await Assert.ThrowsAsync<ThreeCardPokerTableConflictException>(() => store.ActionAsync(
            actor, table.Table.TableId, action == "play" ? "fold" : "play", table.Version, Key("decision"), now, default));
        await Assert.ThrowsAsync<ThreeCardPokerTableConflictException>(() => store.ActionAsync(
            actor, table.Table.TableId, action, table.Version, Key("stale-decision"), now, default));

        var (settled, settledAt) = await CompleteRound(store, actor, now);
        var own = settled.Table.Seats.Single(seat => seat.IsCurrentPlayer);
        Assert.Equal(expectedBalance, store.Balance(actor));
        Assert.Equal(expectedCommitted / 100m, own.TotalWager);
        Assert.Equal(expectedPayout / 100m, own.Payout);
        Assert.Equal(1m, own.PairPlusPayout);
        Assert.Equal(0m, own.AnteBonusPayout);
        Assert.Equal(action == "fold" ? "fold" : "dealer-not-qualified", own.Outcome);
        Assert.All(settled.Table.Dealer.Cards, card => Assert.False(card.Hidden));
        Assert.NotNull(settled.Table.Dealer.Rank);
        var count = store.Ledger.Count;
        for (var repeat = 0; repeat < 5; repeat++) await store.GetSessionAsync(actor, settledAt, default);
        Assert.Equal(count, store.Ledger.Count);
        Assert.Equal(expectedBalance, store.Balance(actor));
        Assert.Equal(store.Ledger.Count, store.Ledger.Select(entry => entry.Id).Distinct().Count());
        var history = Assert.Single(await store.GetHistoryAsync(actor, 10, default));
        Assert.Equal("three-card-poker", history.Game);
        Assert.Equal(expectedCommitted / 100m, history.WagerCredits);
        Assert.Equal(expectedPayout / 100m, history.PayoutCredits);
        Assert.Equal((expectedPayout - expectedCommitted) / 100m, history.NetCredits);
        var revenue = Assert.Single(store.Revenue);
        Assert.Equal(store.Ledger.Where(entry => entry.AmountCents < 0).Sum(entry => -entry.AmountCents), revenue.HumanWagerCents);
        Assert.Equal(store.Ledger.Where(entry => entry.AmountCents > 0).Sum(entry => entry.AmountCents), revenue.HumanPayoutCents);
        var marked = await store.MarkHistorySeenAsync(actor, history.ResultId, settledAt, default);
        var repeated = await store.MarkHistorySeenAsync(actor, history.ResultId, settledAt.AddSeconds(1), default);
        Assert.True(marked.Seen);
        Assert.Equal(marked.SeenAtUtc, repeated.SeenAtUtc);
        await Assert.ThrowsAsync<ThreeCardPokerTableNotFoundException>(() => store.MarkHistorySeenAsync(
            "outsider", history.ResultId, settledAt, default));
    }

    [Fact]
    public async Task AllHumansCanSitOutAndReceiveANewWagerTurnAfterTheCountdown()
    {
        var (store, session, viewer, now) = await AllHumanTable();
        var tableId = session.Table.TableId;
        for (var step = 0; step < 3; step++)
        {
            session = Assert.IsType<ThreeCardPokerTablePlaySessionResponse>((await store.GetSessionAsync(viewer, now, default)).Session);
            var actor = store.TableForTest(tableId).Players.Single(player => player.Seat == session.Table.ActiveSeat).ActorId;
            await store.SitOutAsync(actor, tableId, session.Version, Key($"sitout-{actor}"), now, default);
        }
        var countdown = Assert.IsType<ThreeCardPokerTablePlaySessionResponse>((await store.GetSessionAsync(viewer, now, default)).Session);
        Assert.Equal("next-round-countdown", countdown.Table.Transition);
        Assert.Null(countdown.Table.ActiveSeat);
        Assert.Empty(store.Ledger);
        var resumed = Assert.IsType<ThreeCardPokerTablePlaySessionResponse>((await store.GetSessionAsync(
            viewer, now.Add(ThreeCardPokerTableEngine.NextRoundCountdownDuration), default)).Session);
        Assert.Equal("human-wager", resumed.Table.Transition);
        Assert.NotNull(resumed.Table.ActiveSeat);
        Assert.Equal(0, resumed.Table.Round);
        Assert.Equal(3, resumed.Table.Seats.Count);
        Assert.All(store.TableForTest(tableId).Players, player => Assert.Equal(0, player.ConsecutiveMissedRounds));
        Assert.Empty(store.Ledger);
    }
    [Fact]
    public async Task UnaffordablePlayCanStillFoldWithoutLosingTheDecisionOrTakingAnotherDebit()
    {
        var (store, table, actor, now) = await WagerAndDeal();
        store.SetBalance(actor, 50);
        var ledgerCount = store.Ledger.Count;
        await Assert.ThrowsAsync<ThreeCardPokerTableInsufficientCreditsException>(() => store.ActionAsync(
            actor, table.Table.TableId, ThreeCardPokerActions.Play, table.Version, Key("no-play-funds"), now, default));
        Assert.Equal(50, store.Balance(actor));
        Assert.Equal(ledgerCount, store.Ledger.Count);
        var fresh = Assert.IsType<ThreeCardPokerTablePlaySessionResponse>((await store.GetSessionAsync(actor, now, default)).Session);
        Assert.Contains(ThreeCardPokerActions.Fold, fresh.Table.LegalActions);
        await store.ActionAsync(actor, table.Table.TableId, ThreeCardPokerActions.Fold, fresh.Version, Key("fold-instead"), now, default);
        Assert.Equal(50, store.Balance(actor));
    }

    private static void AssertHidden(IReadOnlyList<ThreeCardPokerTableCardResponse> cards) => Assert.All(cards, card =>
    {
        Assert.True(card.Hidden);
        Assert.Null(card.Rank);
        Assert.Null(card.Suit);
    });

    private static InMemoryThreeCardPokerTableStore Store() => new(() => PairDeck(3), () => 3UL);
    private static string Key(string value) => value.PadRight(16, 'x');

    private static async Task<(InMemoryThreeCardPokerTableStore Store, ThreeCardPokerTablePlaySessionResponse Session, string Actor, DateTime Now)> AllHumanTable()
    {
        var store = Store();
        foreach (var user in new[] { "alice", "bob", "charlie" })
        {
            store.SetBalance(user, 10_000);
            await store.JoinAsync(user, "Player", 0, Key($"join-{user}"), Start, default);
        }
        var now = Start.Add(ThreeCardPokerTableEngine.HumanGrace);
        var session = Assert.IsType<ThreeCardPokerTablePlaySessionResponse>((await store.GetSessionAsync("alice", now, default)).Session);
        var actor = store.TableForTest(session.Table.TableId).Players.Single(player => player.Seat == session.Table.ActiveSeat).ActorId;
        return (store, session, actor, now);
    }

    private static async Task<(InMemoryThreeCardPokerTableStore Store, ThreeCardPokerTablePlaySessionResponse Session, string Actor, DateTime Now)> WagerAndDeal()
    {
        var (store, session, _, now) = await AllHumanTable();
        var tableId = session.Table.TableId;
        var first = store.TableForTest(tableId).Players.OrderBy(player => player.Seat).First().ActorId;
        for (var step = 0; step < 100; step++)
        {
            session = Assert.IsType<ThreeCardPokerTablePlaySessionResponse>((await store.GetSessionAsync(first, now, default)).Session);
            if (session.Table.Phase == ThreeCardPokerTablePhases.Active && session.Table.Transition is null)
                return (store, session, first, now);
            if (session.Table.Phase == ThreeCardPokerTablePhases.Betting && session.Table.Transition == "human-wager")
            {
                var actor = store.TableForTest(tableId).Players.Single(player => player.Seat == session.Table.ActiveSeat).ActorId;
                await store.WagerAsync(actor, tableId, 100, actor == first ? 50 : 0, session.Version, Key($"ante-{actor}"), now, default);
            }
            now = now.AddMilliseconds(200);
        }
        throw new Xunit.Sdk.XunitException("Three Card Poker did not deal after the three human wagers.");
    }

    private static async Task<(ThreeCardPokerTablePlaySessionResponse Session, DateTime Now)> CompleteRound(
        InMemoryThreeCardPokerTableStore store, string viewer, DateTime now)
    {
        for (var step = 0; step < 100; step++)
        {
            now = now.AddSeconds(1);
            var session = Assert.IsType<ThreeCardPokerTablePlaySessionResponse>((await store.GetSessionAsync(viewer, now, default)).Session);
            if (session.Table.Phase == ThreeCardPokerTablePhases.Settled) return (session, now);
            if (session.Table.Phase == ThreeCardPokerTablePhases.Active && session.Table.Transition is null)
            {
                var actor = store.TableForTest(session.Table.TableId).Players.Single(player => player.Seat == session.Table.ActiveSeat).ActorId;
                await store.ActionAsync(actor, session.Table.TableId, ThreeCardPokerActions.Play, session.Version, Key($"play-{actor}"), now, default);
            }
        }
        throw new Xunit.Sdk.XunitException("Three Card Poker did not settle after all decisions.");
    }

    private static ThreeCardPokerTableState ProjectionTable() => new()
    {
        TableId = "table", CreatedAtUtc = Start, UpdatedAtUtc = Start,
        Players = new[] { "alice", "bob" }.Select((actor, seat) => new ThreeCardPokerTablePlayer
        {
            ActorId = actor, PublicSeatId = $"seat-{seat}", DisplayName = $"Player {seat}", Seat = seat,
            SessionId = $"session-{seat}", SessionStartedAtUtc = Start, NextWagerCents = 100
        }).ToList()
    };

    private static IReadOnlyList<string> PairDeck(int players)
    {
        var playerCards = new[]
        {
            new[] { "Q|spades", "Q|diamonds", "4|spades" },
            new[] { "2|clubs", "5|clubs", "8|clubs" },
            new[] { "3|clubs", "7|clubs", "K|clubs" }
        };
        var dealer = new[] { "J|hearts", "9|spades", "6|diamonds" };
        var prefix = Enumerable.Range(0, 3).SelectMany(pass => playerCards.Take(players).Select(cards => cards[pass]).Append(dealer[pass]));
        var deck = new[] { "clubs", "diamonds", "hearts", "spades" }.SelectMany(suit =>
            new[] { "A", "2", "3", "4", "5", "6", "7", "8", "9", "10", "J", "Q", "K" }.Select(rank => $"{rank}|{suit}"));
        return prefix.Concat(deck).Distinct(StringComparer.Ordinal).ToArray();
    }
}