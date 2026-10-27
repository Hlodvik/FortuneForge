using System.Text.Json;
using FortuneForge.Games.ThreeCardPoker;
using FortuneForge.Server.Bots.ThreeCardPoker;
using FortuneForge.Server.Cards.ThreeCardPoker.Table;
using Xunit;

namespace FortuneForge.Server.Tests.ThreeCardPoker;

public sealed class ThreeCardPokerTableStateTests
{
    private static readonly DateTime Start = new(2026, 10, 12, 12, 0, 0, DateTimeKind.Utc);

    [Fact]
    public void WagerLimitsUseWholeRandBetsFromOneToFiveHundred()
    {
        var status = ThreeCardPokerTableController.StatusContract();
        Assert.Equal((1m, 500m, 1m),
            (status.MinimumWager, status.MaximumWager, status.WagerIncrement));
        Assert.Equal(100, ThreeCardPokerMoney.ToWagerCents(1m));
        Assert.Equal(200, ThreeCardPokerMoney.ToWagerCents(2m));
        Assert.Equal(50_000, ThreeCardPokerMoney.ToWagerCents(500m));

        Assert.Throws<ArgumentOutOfRangeException>(() => ThreeCardPokerMoney.ToWagerCents(0.50m));
        Assert.Throws<ArgumentOutOfRangeException>(() => ThreeCardPokerMoney.ToWagerCents(1.50m));
        Assert.Throws<ArgumentOutOfRangeException>(() => ThreeCardPokerMoney.ToWagerCents(501m));
        Assert.Throws<ArgumentOutOfRangeException>(() => ThreeCardPokerMoney.ToWagerCents(1.005m));
    }

    [Fact]
    public async Task AnteAndPairPlusAcceptMixedOneTwoAndFiveRandChipTotals()
    {
        var (store, table, actor, now) = await AllHumanTable();
        var result = Assert.IsType<ThreeCardPokerTablePlaySessionResponse>((await store.WagerAsync(
            actor, table.Table.TableId, 800, 700, table.Version, Key("mixed-chip-wager"), now, default)).Session);

        Assert.Equal(8_500, store.Balance(actor));
        Assert.Equal(-1_500, Assert.Single(store.Ledger).AmountCents);
        var seat = Assert.Single(result.Table.Seats, player => player.IsCurrentPlayer);
        Assert.Equal(8m, seat.Ante);
        Assert.Equal(7m, seat.PairPlus);
    }

    [Fact]
    public void ManagedWagerAdjustmentsRemainFiftyCentSteps()
    {
        var winningPlayer = new ThreeCardPokerTablePlayer
        {
            ActorId = "managed-winner",
            PublicSeatId = "winner-seat",
            DisplayName = "Winner",
            Seat = 0,
            SessionId = "winner-session",
            SessionStartedAtUtc = Start,
            HostMetadata = ThreeCardPokerManagedSeat.Create(1, 500, 0),
            PayoutCents = 2_000
        };
        ThreeCardPokerTableCoordinator.UpdateManagedWagerHabit(winningPlayer, 500);
        Assert.InRange(ThreeCardPokerManagedSeat.BaseWager(winningPlayer), 550, 700);
        Assert.Equal(0, ThreeCardPokerManagedSeat.BaseWager(winningPlayer) % 50);

        var losingPlayer = new ThreeCardPokerTablePlayer
        {
            ActorId = "managed-loser",
            PublicSeatId = "loser-seat",
            DisplayName = "Loser",
            Seat = 1,
            SessionId = "loser-session",
            SessionStartedAtUtc = Start,
            HostMetadata = ThreeCardPokerManagedSeat.Create(1, 500, 0),
            PayoutCents = 0
        };
        ThreeCardPokerManagedSeat.SetConsecutiveLosses(losingPlayer, 2);
        ThreeCardPokerTableCoordinator.UpdateManagedWagerHabit(losingPlayer, 500);
        Assert.InRange(ThreeCardPokerManagedSeat.BaseWager(losingPlayer), 350, 450);
        Assert.Equal(0, ThreeCardPokerManagedSeat.BaseWager(losingPlayer) % 50);
    }

    [Fact]
    public async Task JoinImmediatelyStartsAPrivateHumanVersusDealerTable()
    {
        var store = Store();
        var tableIds = new HashSet<string>(StringComparer.Ordinal);
        for (var index = 0; index < 4; index++)
        {
            var user = $"human-{index}";
            store.SetBalance(user, 10_000);
            var joined = await store.JoinAsync(user, $"Player {index}", 0, Key($"join-{index}"), Start, default);
            var session = Assert.IsType<ThreeCardPokerTablePlaySessionResponse>(joined.Session);
            Assert.True(tableIds.Add(session.Table.TableId));
            Assert.Equal("human-wager", session.Table.Transition);
            Assert.Equal(0, session.Table.Round);
            Assert.True(Assert.Single(session.Table.Seats).IsCurrentPlayer);
            Assert.Equal(user, Assert.Single(store.TableForTest(session.Table.TableId).Players).ActorId);
            Assert.Equal(10_000, store.Balance(user));
        }
        Assert.Equal(4, tableIds.Count);
        Assert.Empty(store.StateForTest.Tickets);
        Assert.Empty(store.Ledger);
        var status = ThreeCardPokerTableController.StatusContract();
        Assert.Equal(1, status.MinimumStartOccupancy);
        Assert.Equal(1, status.TableCapacity);
        Assert.Equal(0, status.HumanGraceSeconds);
        Assert.Equal(0, status.ActionDeadlineSeconds);
    }

    [Fact]
    public async Task JoiningWhileAnotherRoundIsActiveStillCreatesAPrivateTable()
    {
        var store = Store();
        store.SetBalance("alice", 10_000);
        store.SetBalance("bob", 10_000);
        var first = Assert.IsType<ThreeCardPokerTablePlaySessionResponse>((await store.JoinAsync(
            "alice", "Alice", 0, Key("join-alice"), Start, default)).Session);
        await store.WagerAsync("alice", first.Table.TableId, 100, 0, first.Version, Key("wager-alice"), Start, default);
        var second = Assert.IsType<ThreeCardPokerTablePlaySessionResponse>((await store.JoinAsync(
            "bob", "Bob", 0, Key("join-bob"), Start, default)).Session);
        Assert.NotEqual(first.Table.TableId, second.Table.TableId);
        Assert.Single(second.Table.Seats);
        Assert.Equal("bob", Assert.Single(store.TableForTest(second.Table.TableId).Players).ActorId);
    }

    [Fact]
    public async Task DuplicateJoinReplaysWithoutCreatingAnotherTableOrCharging()
    {
        var store = Store();
        store.SetBalance("human", 10_000);
        var first = Assert.IsType<ThreeCardPokerTablePlaySessionResponse>((await store.JoinAsync(
            "human", "Player", 0, Key("join"), Start, default)).Session);
        for (var index = 0; index < 2; index++)
        {
            var replayed = Assert.IsType<ThreeCardPokerTablePlaySessionResponse>((await store.JoinAsync(
                "human", "Player", 0, Key("join"), Start.AddSeconds(1), default)).Session);
            Assert.Equal(first.Table.TableId, replayed.Table.TableId);
        }
        Assert.Single(store.StateForTest.Tables);
        Assert.Empty(store.Ledger);
        Assert.Equal(10_000, store.Balance("human"));
    }

    [Fact]
    public async Task PersistedMultiplayerBettingTableRefundsPendingWagersBeforeClosing()
    {
        var store = Store();
        const string tableId = "legacy-betting-table";
        var table = LegacyTable(tableId, ThreeCardPokerTablePhases.Betting);
        table.Players[0].NextWagerCents = 100;
        table.Players[0].NextPairPlusCents = 50;
        table.Players[1].NextWagerCents = 200;
        store.StateForTest.Tables[tableId] = table;
        foreach (var player in table.Players)
            store.StateForTest.Sessions[player.ActorId] = new(ThreeCardPokerTableSessionKinds.Table, null, tableId);
        store.SetBalance("alice", 9_850);
        store.SetBalance("bob", 9_800);

        var session = await store.GetSessionAsync("alice", Start, default);
        Assert.IsType<ThreeCardPokerTableIdleSessionResponse>(session.Session);
        Assert.Equal(ThreeCardPokerTablePhases.Closed, table.Phase);
        Assert.Equal(10_000, store.Balance("alice"));
        Assert.Equal(10_000, store.Balance("bob"));
        Assert.Equal(new long[] { 150, 200 }, store.Ledger.OrderBy(value => value.UserId).Select(value => value.AmountCents));
        Assert.IsType<ThreeCardPokerTableIdleSessionResponse>((await store.GetSessionAsync("bob", Start, default)).Session);
    }

    [Fact]
    public async Task PersistedMultiplayerRoundSettlesBeforeItsTableIsRetired()
    {
        var store = Store();
        const string tableId = "legacy-settlement-table";
        var table = LegacyTable(tableId, ThreeCardPokerTablePhases.Settlement);
        table.RoundNumber = 1;
        table.RoundAccountingSettled = false;
        foreach (var player in table.Players)
        {
            player.WagerCents = player.TotalWagerCents = 100;
            player.PayoutCents = 200;
            store.StateForTest.Sessions[player.ActorId] = new(ThreeCardPokerTableSessionKinds.Table, null, tableId);
            store.SetBalance(player.ActorId, 9_900);
        }
        store.StateForTest.Tables[tableId] = table;

        var settled = Assert.IsType<ThreeCardPokerTablePlaySessionResponse>((await store.GetSessionAsync(
            "alice", Start, default)).Session);
        Assert.Equal(ThreeCardPokerTablePhases.Settled, settled.Table.Phase);
        Assert.Equal(10_100, store.Balance("alice"));
        Assert.Equal(10_100, store.Balance("bob"));
        Assert.Single(await store.GetHistoryAsync("alice", 10, default));
        Assert.Single(await store.GetHistoryAsync("bob", 10, default));

        var afterDisplay = await store.GetSessionAsync("alice", Start.Add(ThreeCardPokerTableEngine.SettlementDisplayDuration), default);
        Assert.IsType<ThreeCardPokerTableIdleSessionResponse>(afterDisplay.Session);
        Assert.IsType<ThreeCardPokerTableIdleSessionResponse>((await store.GetSessionAsync("bob", Start.AddSeconds(5), default)).Session);
        Assert.Equal(ThreeCardPokerTablePhases.Closed, table.Phase);
        Assert.Equal(10_100, store.Balance("alice"));
        Assert.Equal(10_100, store.Balance("bob"));
        Assert.Equal(2, store.Ledger.Count);
    }

    [Fact]
    public async Task PersistedQueueTicketMovesToAPrivateTableWithoutWaitingForGrace()
    {
        var store = Store();
        var ticketId = ThreeCardPokerTableIds.Hash("old-queue-ticket");
        store.StateForTest.Tickets.Add(new(ticketId, "alice", "old-seat", "Alice", "old-shared-table", 3,
            "queued", 1, Start.AddSeconds(-1), Start.AddMinutes(1)));
        store.StateForTest.Sessions["alice"] = new(ThreeCardPokerTableSessionKinds.Queue, ticketId, null);

        var session = Assert.IsType<ThreeCardPokerTablePlaySessionResponse>((await store.GetSessionAsync(
            "alice", Start, default)).Session);
        Assert.Equal("human-wager", session.Table.Transition);
        Assert.Null(session.Table.WagerDeadlineAtUtc);
        Assert.Equal("alice", Assert.Single(store.TableForTest(session.Table.TableId).Players).ActorId);
        Assert.Single(store.StateForTest.Tickets, value => value.TicketId == ticketId && value.Status == "matched");
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
    public async Task LeavingAndRejoiningCreatesAPrivateTableWithDistinctRefunds()
    {
        var (store, session, actor, now) = await AllHumanTable();
        var tableId = session.Table.TableId;
        var first = Assert.IsType<ThreeCardPokerTablePlaySessionResponse>((await store.WagerAsync(
            actor, tableId, 100, 100, session.Version, Key("first-wager"), now, default)).Session);
        await store.LeaveAsync(actor, tableId, first.Version, Key("first-leave"), now, default);
        var rejoined = await store.JoinAsync(actor, "Player", 0, Key("rejoin"), now, default);
        session = Assert.IsType<ThreeCardPokerTablePlaySessionResponse>(rejoined.Session);
        Assert.NotEqual(tableId, session.Table.TableId);
        tableId = session.Table.TableId;
        var second = Assert.IsType<ThreeCardPokerTablePlaySessionResponse>((await store.WagerAsync(
            actor, tableId, 200, 100, session.Version, Key("second-wager"), now, default)).Session);
        await store.LeaveAsync(actor, tableId, second.Version, Key("second-leave"), now, default);
        Assert.Equal(10_000, store.Balance(actor));
        Assert.Equal(new long[] { -200, 200, -300, 300 }, store.Ledger.Where(entry => entry.UserId == actor).Select(entry => entry.AmountCents));
        Assert.Equal(4, store.Ledger.Select(entry => entry.Id).Distinct().Count());
    }
    [Fact]
    public async Task FailedAndStaleWagersLeaveWalletAndCommittedWagerUnchanged()
    {
        var (store, table, actor, now) = await AllHumanTable();
        store.SetBalance(actor, 100);
        await Assert.ThrowsAsync<ThreeCardPokerTableInsufficientCreditsException>(() => store.WagerAsync(
            actor, table.Table.TableId, 100, 100, table.Version, Key("too-much"), now, default));
        Assert.Equal(100, store.Balance(actor));
        Assert.Empty(store.Ledger);
        Assert.Equal(0, store.TableForTest(table.Table.TableId).Players.Single(player => player.ActorId == actor).NextWagerCents);

        store.SetBalance(actor, 10_000);
        await store.WagerAsync(actor, table.Table.TableId, 100, 100, table.Version, Key("wager"), now, default);
        await Assert.ThrowsAsync<ThreeCardPokerTableConflictException>(() => store.WagerAsync(
            actor, table.Table.TableId, 200, 100, table.Version, Key("wager"), now, default));
        await Assert.ThrowsAsync<ThreeCardPokerTableConflictException>(() => store.WagerAsync(
            actor, table.Table.TableId, 100, 100, table.Version, Key("stale"), now, default));
        Assert.Equal(9_800, store.Balance(actor));
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
    [InlineData("play", 10_200, 300, 500)]
    [InlineData("fold", 10_000, 200, 200)]
    public async Task PlayOrFoldAndItsPayoutAreRecordedExactlyOnce(string action, long expectedBalance, long expectedCommitted, long expectedPayout)
    {
        var (store, table, actor, now) = await WagerAndDeal();
        var acted = await store.ActionAsync(actor, table.Table.TableId, action, table.Version, Key("decision"), now, default);
        await store.ActionAsync(actor, table.Table.TableId, action, table.Version, Key("decision"), now, default);
        Assert.Equal(action == ThreeCardPokerActions.Play ? 9_700 : 9_800, store.Balance(actor));
        await Assert.ThrowsAsync<ThreeCardPokerTableConflictException>(() => store.ActionAsync(
            actor, table.Table.TableId, action == "play" ? "fold" : "play", table.Version, Key("decision"), now, default));
        await Assert.ThrowsAsync<ThreeCardPokerTableConflictException>(() => store.ActionAsync(
            actor, table.Table.TableId, action, table.Version, Key("stale-decision"), now, default));

        var (settled, settledAt) = await CompleteRound(store, actor, now);
        var own = settled.Table.Seats.Single(seat => seat.IsCurrentPlayer);
        Assert.Equal(expectedBalance, store.Balance(actor));
        Assert.Equal(expectedCommitted / 100m, own.TotalWager);
        Assert.Equal(expectedPayout / 100m, own.Payout);
        Assert.Equal(2m, own.PairPlusPayout);
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
    public async Task SoloPlayerCanSitOutAndReceiveANewWagerTurnAfterTheCountdown()
    {
        var (store, session, viewer, now) = await AllHumanTable();
        var tableId = session.Table.TableId;
        await store.SitOutAsync(viewer, tableId, session.Version, Key("sitout"), now, default);
        var countdown = Assert.IsType<ThreeCardPokerTablePlaySessionResponse>((await store.GetSessionAsync(viewer, now, default)).Session);
        Assert.Equal("next-round-countdown", countdown.Table.Transition);
        Assert.Null(countdown.Table.ActiveSeat);
        Assert.Empty(store.Ledger);
        var resumed = Assert.IsType<ThreeCardPokerTablePlaySessionResponse>((await store.GetSessionAsync(
            viewer, now.Add(ThreeCardPokerTableEngine.NextRoundCountdownDuration), default)).Session);
        Assert.Equal("human-wager", resumed.Table.Transition);
        Assert.NotNull(resumed.Table.ActiveSeat);
        Assert.Equal(0, resumed.Table.Round);
        Assert.Single(resumed.Table.Seats);
        Assert.All(store.TableForTest(tableId).Players, player => Assert.Equal(0, player.ConsecutiveMissedRounds));
        Assert.Empty(store.Ledger);
    }

    [Fact]
    public async Task SoloWagerRemainsOpenButAbandonedHandFoldsAndSettlesAfterThirtyMinutes()
    {
        var (store, initial, actor, now) = await AllHumanTable();
        Assert.Null(initial.Table.WagerDeadlineAtUtc);
        var later = Assert.IsType<ThreeCardPokerTablePlaySessionResponse>((await store.GetSessionAsync(
            actor, now.AddHours(3), default)).Session);
        Assert.Equal("human-wager", later.Table.Transition);
        Assert.Null(later.Table.WagerDeadlineAtUtc);
        Assert.Single(later.Table.Seats);

        var (roundStore, active, roundActor, _) = await WagerAndDeal();
        Assert.Null(active.Table.ActionDeadlineAtUtc);
        var internalDeadline = Assert.IsType<DateTime>(roundStore.TableForTest(active.Table.TableId).ActionDeadlineAtUtc);
        Assert.Equal(ThreeCardPokerTableCoordinator.SoloActionDuration,
            internalDeadline - roundStore.TableForTest(active.Table.TableId).UpdatedAtUtc);
        var stillActive = Assert.IsType<ThreeCardPokerTablePlaySessionResponse>((await roundStore.GetSessionAsync(
            roundActor, internalDeadline.AddTicks(-1), default)).Session);
        Assert.Equal(ThreeCardPokerTablePhases.Active, stillActive.Table.Phase);
        Assert.Contains(ThreeCardPokerActions.Play, stillActive.Table.LegalActions);
        Assert.Null(stillActive.Table.ActionDeadlineAtUtc);
        Assert.Equal(0, stillActive.Table.RemainingActionMilliseconds);

        await roundStore.SweepAsync(internalDeadline, default);
        var folded = roundStore.TableForTest(active.Table.TableId).Players.Single();
        Assert.Equal("folded", folded.Status);
        Assert.Equal(0, folded.PlayWagerCents);
        Assert.Equal(9_800, roundStore.Balance(roundActor));

        var settledAt = internalDeadline.Add(ThreeCardPokerTableEngine.ActionSettleDuration)
            .Add(ThreeCardPokerTableEngine.DealerCardDuration);
        await roundStore.SweepAsync(internalDeadline.Add(ThreeCardPokerTableEngine.ActionSettleDuration), default);
        await roundStore.SweepAsync(settledAt, default);
        await roundStore.SweepAsync(settledAt.AddSeconds(1), default);
        Assert.Equal(10_000, roundStore.Balance(roundActor));
        Assert.Equal(2, roundStore.Ledger.Count);
        Assert.Single(roundStore.Revenue);
        var history = Assert.Single(await roundStore.GetHistoryAsync(roundActor, 10, default));
        Assert.Equal(2m, history.WagerCredits);
        Assert.Equal(2m, history.PayoutCredits);
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

    private static InMemoryThreeCardPokerTableStore Store() => new(() => PairDeck(1), () => 3UL);
    private static string Key(string value) => value.PadRight(16, 'x');

    private static async Task<(InMemoryThreeCardPokerTableStore Store, ThreeCardPokerTablePlaySessionResponse Session, string Actor, DateTime Now)> AllHumanTable()
    {
        var store = Store();
        store.SetBalance("alice", 10_000);
        var session = Assert.IsType<ThreeCardPokerTablePlaySessionResponse>((await store.JoinAsync(
            "alice", "Player", 0, Key("join-alice"), Start, default)).Session);
        return (store, session, "alice", Start);
    }

    private static async Task<(InMemoryThreeCardPokerTableStore Store, ThreeCardPokerTablePlaySessionResponse Session, string Actor, DateTime Now)> WagerAndDeal()
    {
        var (store, session, _, now) = await AllHumanTable();
        var tableId = session.Table.TableId;
        var first = "alice";
        for (var step = 0; step < 100; step++)
        {
            session = Assert.IsType<ThreeCardPokerTablePlaySessionResponse>((await store.GetSessionAsync(first, now, default)).Session);
            if (session.Table.Phase == ThreeCardPokerTablePhases.Active && session.Table.Transition is null)
                return (store, session, first, now);
            if (session.Table.Phase == ThreeCardPokerTablePhases.Betting && session.Table.Transition == "human-wager")
            {
                await store.WagerAsync(first, tableId, 100, 100, session.Version, Key("ante-alice"), now, default);
            }
            now = now.AddMilliseconds(200);
        }
        throw new Xunit.Sdk.XunitException("Three Card Poker did not deal after the solo wager.");
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

    private static ThreeCardPokerTableState LegacyTable(string tableId, string phase) => new()
    {
        TableId = tableId, CreatedAtUtc = Start, UpdatedAtUtc = Start, Phase = phase,
        Players = new[] { "alice", "bob" }.Select((actor, seat) => new ThreeCardPokerTablePlayer
        {
            ActorId = actor, PublicSeatId = $"legacy-seat-{seat}", DisplayName = actor, Seat = seat,
            SessionId = $"legacy-session-{seat}", SessionStartedAtUtc = Start
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
