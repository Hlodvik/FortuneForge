using System.Text.Json;
using FortuneForge.Server.Cards.Blackjack;
using FortuneForge.Server.Cards.Blackjack.Table;
using FortuneForge.Server.Bots.Blackjack;
using Microsoft.Extensions.Configuration;
using Xunit;

namespace FortuneForge.Server.Tests.Blackjack.Table;

public sealed class BlackjackTableStateTests
{
    private static readonly DateTime Start = new(2026, 8, 15, 12, 0, 0, DateTimeKind.Utc);

    [Theory]
    [InlineData(1, 3, 2)]
    [InlineData(2, 3, 1)]
    [InlineData(3, 3, 0)]
    [InlineData(4, 4, 0)]
    public async Task FreeGraceStartReservesOneSeatForANewHumanAndFillsTheConfiguredMinimum(
        int humans,
        int occupied,
        int bots)
    {
        var store = Store();
        for (var index = 0; index < humans; index++)
        {
            var user = $"human-{index}";
            store.SetBalance(user, 10_000);
            await store.JoinAsync(user, $"Player{index}", 0, Key($"join-{index}"), Start, default);
            Assert.Equal(10_000, store.Balance(user));
        }

        var result = await store.GetSessionAsync("human-0", Start.AddSeconds(6), default);
        var session = Assert.IsType<BlackjackTablePlaySessionResponse>(result.Session);
        var table = store.TableForTest(session.Table.TableId);

        Assert.Equal(BlackjackTablePhases.Betting, table.Phase);
        Assert.Equal(occupied, table.Players.Count);
        Assert.Equal(humans, table.Players.Count(player => !BlackjackManagedSeat.IsManaged(player)));
        Assert.Equal(bots, table.Players.Count(BlackjackManagedSeat.IsManaged));
        Assert.Empty(store.Ledger);
    }

    [Fact]
    public async Task SingleHumanTablesStartWithOnlyTheRequiredManagedCohort()
    {
        var occupancies = new List<int>();
        foreach (var seed in new[] { 2UL, 3UL })
        {
            var store = Store(seed: seed);
            store.SetBalance("human", 10_000);
            await store.JoinAsync("human", "SoloJoin", 0, Key($"variable-{seed}"), Start, default);
            var session = Assert.IsType<BlackjackTablePlaySessionResponse>((await store.GetSessionAsync(
                "human", Start.AddSeconds(6), default)).Session);
            occupancies.Add(store.TableForTest(session.Table.TableId).Players.Count);
        }

        Assert.Equal(new[] { 3, 3 }, occupancies);
    }

    [Fact]
    public async Task AFullHumanCohortSplitsAcrossTablesInsteadOfUsingTheReservedSeat()
    {
        var store = Store();
        for (var index = 0; index < 5; index++)
        {
            var user = $"human-{index}";
            store.SetBalance(user, 10_000);
            await store.JoinAsync(user, $"Player{index}", 0, Key($"five-join-{index}"), Start, default);
        }

        await store.GetSessionAsync("human-0", Start.AddSeconds(6), default);

        Assert.Equal(2, store.StateForTest.Tables.Count);
        Assert.All(store.StateForTest.Tables.Values, table =>
        {
            Assert.InRange(table.Players.Count, BlackjackManagedTablePolicy.MinimumStartOccupancy,
                BlackjackManagedTablePolicy.MaximumOccupiedSeats);
            Assert.InRange(table.Players.Count(player => !BlackjackManagedSeat.IsManaged(player)), 1,
                BlackjackManagedTablePolicy.MaximumOccupiedSeats);
        });
        Assert.Equal(5, store.StateForTest.Tables.Values.Sum(table => table.Players.Count(player => !BlackjackManagedSeat.IsManaged(player))));
    }

    [Fact]
    public async Task InitialTurnOrderIsRandomizedOnceAcrossTheOccupiedLeftmostSeats()
    {
        var permutations = Enumerable.Range(1, 32)
            .Select(seed => string.Join(',', BlackjackTableCoordinator.RandomizedInitialSeats(5, (ulong)seed)))
            .ToArray();
        Assert.True(permutations.Distinct(StringComparer.Ordinal).Count() > 1);
        Assert.All(permutations, order => Assert.Equal(
            new[] { 0, 1, 2, 3, 4 },
            order.Split(',').Select(int.Parse).OrderBy(seat => seat).ToArray()));

        var store = Store(seed: 123UL);
        for (var index = 0; index < 3; index++)
        {
            var user = $"human-{index}";
            store.SetBalance(user, 10_000);
            await store.JoinAsync(
                user,
                $"Player{index}",
                0,
                Key($"ordered-join-{index}"),
                Start.AddMilliseconds(index),
                default);
        }

        var session = Assert.IsType<BlackjackTablePlaySessionResponse>((await store.GetSessionAsync(
            "human-0", Start.AddSeconds(6), default)).Session);
        var table = store.TableForTest(session.Table.TableId);
        var expectedSeats = BlackjackTableCoordinator.RandomizedInitialSeats(3, 123UL);

        for (var index = 0; index < 3; index++)
            Assert.Equal(expectedSeats[index], table.Players.Single(player => player.ActorId == $"human-{index}").Seat);
    }

    [Fact]
    public async Task LeavingAfterWageringReleasesTheCommittedAmountExactlyOnce()
    {
        var store = Store();
        store.SetBalance("human", 10_000);
        var play = await JoinAtTable(store, "human", "RiverStone");

        var first = await store.WagerAsync("human", play.Table.TableId, 500, play.Version, Key("wager-500"), Start.AddSeconds(7), default);
        Assert.Equal(9_500, store.Balance("human"));
        var firstPlay = Assert.IsType<BlackjackTablePlaySessionResponse>(first.Session);
        await store.LeaveAsync("human", play.Table.TableId, firstPlay.Version, Key("leave-betting"), Start.AddSeconds(7).AddMilliseconds(400), default);
        await store.LeaveAsync("human", play.Table.TableId, firstPlay.Version, Key("leave-betting"), Start.AddSeconds(7).AddMilliseconds(500), default);

        Assert.Equal(10_000, store.Balance("human"));
        Assert.Equal(new long[] { -500, 500 }, store.Ledger.Select(entry => entry.AmountCents));
    }

    [Fact]
    public async Task ExplicitSitOutKeepsTheSeatAndDoesNotCountAsInactivity()
    {
        var store = Store();
        store.SetBalance("human", 10_000);
        var play = await JoinAtTable(store, "human", "QuietRound");

        var result = await store.SitOutAsync(
            "human", play.Table.TableId, play.Version, Key("sit-out"), Start.AddSeconds(7), default);
        var session = Assert.IsType<BlackjackTablePlaySessionResponse>(result.Session);
        var player = store.TableForTest(play.Table.TableId).Players.Single(value => value.ActorId == "human");

        Assert.Contains(session.Table.Seats, seat => seat.IsCurrentPlayer);
        Assert.Equal("sitting-out", player.Status);
        Assert.Equal(0, player.ConsecutiveMissedRounds);
        Assert.Equal(10_000, store.Balance("human"));
        Assert.Empty(store.Ledger);
    }

    [Fact]
    public async Task ActionsAndDealerCardsAdvanceOneDurableStepAtATime()
    {
        var store = Store(DoubleDeck());
        store.SetBalance("human", 2_000);
        var play = await JoinAtTable(store, "human", "BrightRobin");
        await store.WagerAsync(
            "human", play.Table.TableId, 100, play.Version, Key("round-wager"), Start.AddSeconds(7), default);
        var started = await StartRound(store, "human", Start.AddSeconds(8));

        var acted = Assert.IsType<BlackjackTablePlaySessionResponse>((await store.ActionAsync(
            "human", play.Table.TableId, BlackjackActions.Stand, started.Version, Key("stand"), Start.AddSeconds(8), default)).Session);
        Assert.Equal("action-settle", acted.Table.Transition);
        Assert.Equal(started.Table.ActiveSeat, acted.Table.ActiveSeat);

        var tooSoon = Assert.IsType<BlackjackTablePlaySessionResponse>((await store.GetSessionAsync(
            "human", Start.AddSeconds(8).AddMilliseconds(300), default)).Session);
        Assert.Equal("action-settle", tooSoon.Table.Transition);
        var next = Assert.IsType<BlackjackTablePlaySessionResponse>((await store.GetSessionAsync(
            "human", Start.AddSeconds(8).AddMilliseconds(700), default)).Session);
        Assert.Null(next.Table.Transition);
        Assert.Null(next.Table.NextTransitionAtUtc);
        Assert.Null(next.Table.ActionDeadlineAtUtc);
        var managedTurn = store.TableForTest(play.Table.TableId).Players.Single(player =>
            player.Seat == next.Table.ActiveSeat && BlackjackManagedSeat.IsManaged(player));
        Assert.NotNull(BlackjackManagedSeat.ActionReadyAt(managedTurn));

        var dealerSnapshots = new List<int>();
        var now = Start.AddSeconds(10);
        for (var step = 0; step < 20; step++, now = now.AddMilliseconds(1_500))
        {
            var current = Assert.IsType<BlackjackTablePlaySessionResponse>((await store.GetSessionAsync("human", now, default)).Session);
            dealerSnapshots.Add(current.Table.Dealer.Cards.Count(card => !card.Hidden));
            if (current.Table.Phase == BlackjackTablePhases.Betting) break;
        }
        Assert.True(dealerSnapshots.Zip(dealerSnapshots.Skip(1), (left, right) => right - left).All(delta => delta <= 1));
    }

    [Fact]
    public async Task HumanMoveClockStartsOnTheirTurnAndResetsAfterAHit()
    {
        var store = Store(HitDeck());
        store.SetBalance("human", 2_000);
        var play = await JoinAtTable(store, "human", "SteadyFox");
        await store.WagerAsync(
            "human", play.Table.TableId, 100, play.Version, Key("timer-wager"), Start.AddSeconds(7), default);
        var started = await StartRound(store, "human", Start.AddSeconds(8));
        var startedAt = started.Table.UpdatedAtUtc;
        var humanSeat = started.Table.Seats.Single(seat => seat.IsCurrentPlayer).Seat;

        Assert.Equal(humanSeat, started.Table.ActiveSeat);
        Assert.Equal(startedAt.AddMinutes(1), started.Table.ActionDeadlineAtUtc);
        store.TableForTest(play.Table.TableId).Players.Single(player => player.ActorId == "human")
            .ConsecutiveMissedActionRounds = 1;

        var hitAt = startedAt.AddMilliseconds(100);
        var hit = Assert.IsType<BlackjackTablePlaySessionResponse>((await store.ActionAsync(
            "human", play.Table.TableId, BlackjackActions.Hit, started.Version, Key("timer-hit"), hitAt, default)).Session);
        Assert.Equal("action-settle", hit.Table.Transition);
        Assert.Equal(humanSeat, hit.Table.ActiveSeat);
        Assert.Equal(0, store.TableForTest(play.Table.TableId).Players.Single(player => player.ActorId == "human")
            .ConsecutiveMissedActionRounds);

        var resumedAt = hitAt.Add(BlackjackTableEngine.ActionSettleDuration);
        var resumed = Assert.IsType<BlackjackTablePlaySessionResponse>((await store.GetSessionAsync(
            "human", resumedAt, default)).Session);
        Assert.Equal(humanSeat, resumed.Table.ActiveSeat);
        Assert.Equal(resumedAt.AddMinutes(1), resumed.Table.ActionDeadlineAtUtc);
        Assert.Equal(13, resumed.Table.Seats.Single(seat => seat.IsCurrentPlayer).Hand.Score);

        var beforeTimeout = Assert.IsType<BlackjackTablePlaySessionResponse>((await store.GetSessionAsync(
            "human", resumedAt.AddMinutes(1).AddMilliseconds(-1), default)).Session);
        Assert.Equal(humanSeat, beforeTimeout.Table.ActiveSeat);
        var timedOut = Assert.IsType<BlackjackTablePlaySessionResponse>((await store.GetSessionAsync(
            "human", resumedAt.AddMinutes(1), default)).Session);
        Assert.Equal("action-settle", timedOut.Table.Transition);
        Assert.Equal(BlackjackActions.Stand, timedOut.Table.Seats.Single(seat => seat.IsCurrentPlayer).LastAction);
    }

    [Fact]
    public async Task TwoConsecutiveTimedOutPlayingRoundsRemoveTheHumanAfterSettlement()
    {
        var store = Store(DoubleDeck());
        store.SetBalance("human", 2_000);
        var play = await JoinAtTable(store, "human", "SlowComet");
        await store.WagerAsync(
            "human", play.Table.TableId, 100, play.Version, Key("timeout-wager-one"), Start.AddSeconds(7), default);
        var first = await StartRound(store, "human", Start.AddSeconds(8));

        await store.GetSessionAsync("human", first.Table.ActionDeadlineAtUtc!.Value, default);
        var firstBetting = await AdvanceUntilBetting(store, "human", Start.AddSeconds(70));
        var player = store.TableForTest(play.Table.TableId).Players.Single(value => value.ActorId == "human");
        Assert.Equal(1, player.ConsecutiveMissedActionRounds);
        Assert.False(player.LeavingAfterRound);

        var secondWagerAt = firstBetting.Table.UpdatedAtUtc.AddMilliseconds(100);
        var secondWager = Assert.IsType<BlackjackTablePlaySessionResponse>((await store.WagerAsync(
            "human", play.Table.TableId, 100, firstBetting.Version, Key("timeout-wager-two"),
            secondWagerAt, default)).Session);
        var second = await StartRound(store, "human", secondWagerAt.AddSeconds(1));
        Assert.True(second.Version > secondWager.Version);
        await store.GetSessionAsync("human", second.Table.ActionDeadlineAtUtc!.Value, default);
        Assert.True(player.LeavingAfterRound);
        Assert.Equal(2, player.ConsecutiveMissedActionRounds);

        var settlementSweepAt = second.Table.ActionDeadlineAtUtc!.Value.AddSeconds(10);
        for (var step = 0; step < 60 && store.StateForTest.Tables.ContainsKey(play.Table.TableId); step++)
            await store.SweepAsync(settlementSweepAt.AddSeconds(step * 6), default);
        var idle = await store.GetSessionAsync("human", settlementSweepAt.AddMinutes(10), default);
        Assert.IsType<BlackjackTableIdleSessionResponse>(idle.Session);
        Assert.True(!store.StateForTest.Tables.TryGetValue(play.Table.TableId, out var remaining) ||
                    remaining.Players.All(value => value.ActorId != "human"));
    }

    [Fact]
    public async Task MissingTwoConsecutiveWagerWindowsRemovesTheIdleHuman()
    {
        var store = Store();
        store.SetBalance("human", 2_000);
        var play = await JoinAtTable(store, "human", "QuietHarbor");

        var firstMiss = Assert.IsType<BlackjackTablePlaySessionResponse>((await store.GetSessionAsync(
            "human", play.Table.WagerDeadlineAtUtc!.Value, default)).Session);
        var table = store.TableForTest(play.Table.TableId);
        Assert.Equal(1, table.Players.Single(player => player.ActorId == "human").ConsecutiveMissedRounds);
        var secondWindow = await AdvanceUntilBetting(store, "human", firstMiss.Table.UpdatedAtUtc.AddSeconds(1));
        Assert.Equal(BlackjackTableEngine.WagerDuration,
            secondWindow.Table.WagerDeadlineAtUtc!.Value - secondWindow.Table.UpdatedAtUtc);

        var kicked = await store.GetSessionAsync("human", secondWindow.Table.WagerDeadlineAtUtc.Value, default);
        Assert.IsType<BlackjackTableIdleSessionResponse>(kicked.Session);
        Assert.DoesNotContain(table.Players, player => player.ActorId == "human");
        Assert.Equal(BlackjackTablePhases.Betting, table.Phase);
        Assert.All(table.Players, player => Assert.True(BlackjackManagedSeat.IsManaged(player)));
        Assert.Equal(BlackjackManagedTablePolicy.MinimumStartOccupancy - 1, table.Players.Count);
    }

    [Fact]
    public async Task ManagedPlayersFinishTheirIndependentSessionsAfterTheLastHumanLeaves()
    {
        var store = Store();
        store.SetBalance("human", 2_000);
        var play = await JoinAtTable(store, "human", "BriefVisitor");

        await store.LeaveAsync(
            "human", play.Table.TableId, play.Version, Key("leave-before-rounds"),
            Start.AddSeconds(7), default);
        var table = store.TableForTest(play.Table.TableId);
        Assert.All(table.Players, player => Assert.True(BlackjackManagedSeat.IsManaged(player)));

        for (var step = 1; step <= 400 && table.Phase != BlackjackTablePhases.Closed; step++)
            await store.SweepAsync(Start.AddSeconds(7 + step * 10), default);

        Assert.Equal(BlackjackTablePhases.Closed, table.Phase);
        Assert.InRange(table.RoundNumber, 4, 20);
    }

    [Fact]
    public async Task JoiningAnOpenBettingTableSeatsTheHumanImmediatelyAndKeepsOneSeatOpen()
    {
        var store = Store();
        store.SetBalance("first", 2_000);
        store.SetBalance("second", 2_000);
        var first = await JoinAtTable(store, "first", "FirstPlayer");
        var before = store.TableForTest(first.Table.TableId);
        Assert.Equal(2, before.Players.Count(BlackjackManagedSeat.IsManaged));

        var joined = await store.JoinAsync(
            "second", "SecondPlayer", 0, Key("join-live-table"), Start.AddSeconds(7), default);
        var session = Assert.IsType<BlackjackTablePlaySessionResponse>(joined.Session);

        Assert.Equal(first.Table.TableId, session.Table.TableId);
        Assert.Equal(2, before.Players.Count(player => !BlackjackManagedSeat.IsManaged(player)));
        Assert.Equal(2, before.Players.Count(BlackjackManagedSeat.IsManaged));
        Assert.True(before.Players.Count < BlackjackTableEngine.Capacity);
    }

    [Fact]
    public void GameEngineDoesNotOwnManagedPlayerTiming()
    {
        for (ulong seed = 1; seed <= 24; seed++)
        {
            var player = new BlackjackTablePlayer
            {
                ActorId = $"managed-{seed}", PublicSeatId = $"seat-{seed}", DisplayName = $"Player{seed}",
                Seat = 0, SessionId = $"session-{seed}",
                SessionStartedAtUtc = Start, NextWagerCents = 100
            };
            var table = new BlackjackTableState
            {
                TableId = $"table-{seed}", Players = [player], CreatedAtUtc = Start, UpdatedAtUtc = Start
            };
            BlackjackTableEngine.Deal(table, DirectPlayableDeck(), seed, Start);
            Assert.Equal(BlackjackTableEngine.ActionDuration, table.ActionDeadlineAtUtc!.Value - Start);
            Assert.Null(table.Transition);
        }
    }

    [Fact]
    public async Task ManagedPlayerRoundsUseTheSameResultHistoryPipeline()
    {
        var store = Store(NaturalDeck());
        store.SetBalance("human", 2_000);
        var play = await JoinAtTable(store, "human", "HistoryPlayer");
        var managedProfileId = store.TableForTest(play.Table.TableId).Players.First(BlackjackManagedSeat.IsManaged).ActorId;
        await store.WagerAsync(
            "human", play.Table.TableId, 100, play.Version, Key("history-wager"),
            Start.AddSeconds(7), default);
        await StartRound(store, "human", Start.AddSeconds(8));
        await AdvanceUntilBetting(store, "human", Start.AddSeconds(10));

        var result = Assert.Single(await store.GetHistoryAsync(managedProfileId, 20, default));
        Assert.Equal("blackjack", result.Game);
        Assert.Equal("credit-table", result.Mode);
        Assert.Equal(play.Table.TableId, result.TableId);
    }

    [Fact]
    public async Task ManagedPlayersChooseWagersOneSeatAtATime()
    {
        var store = Store();
        store.SetBalance("human", 2_000);
        var play = await JoinAtTable(store, "human", "WagerWatcher");
        var table = store.TableForTest(play.Table.TableId);
        var bots = table.Players.Where(BlackjackManagedSeat.IsManaged).OrderBy(player => player.Seat).ToArray();

        Assert.All(bots, bot => Assert.Equal(0, bot.NextWagerCents));
        Assert.Equal("human-wager", table.Transition);
        var wagerSession = Assert.IsType<BlackjackTablePlaySessionResponse>((await store.WagerAsync(
            "human", play.Table.TableId, 100, play.Version, Key("watcher-wager"), Start.AddSeconds(7), default)).Session);
        Assert.Single(bots, bot => bot.Status == "considering-wager");
        Assert.Null(wagerSession.Table.NextTransitionAtUtc);
        Assert.Null(wagerSession.Table.WagerDeadlineAtUtc);
        Assert.Empty(table.DealerCards);
        Assert.All(table.Players, player => Assert.Empty(player.Cards));
        var firstBot = bots.Single(bot => bot.Status == "considering-wager");
        var firstReadyAt = Assert.IsType<DateTime>(BlackjackManagedSeat.WagerReadyAt(firstBot));
        await store.SweepAsync(firstReadyAt, default);

        Assert.Single(bots, bot => bot.NextWagerCents > 0);
        Assert.Single(bots, bot => bot.Status == "considering-wager");
        Assert.Null(BlackjackManagedSeat.WagerReadyAt(firstBot));
        var secondBot = bots.Single(bot => bot.Status == "considering-wager");
        var secondReadyAt = Assert.IsType<DateTime>(BlackjackManagedSeat.WagerReadyAt(secondBot));
        Assert.True(secondReadyAt > firstReadyAt);
        Assert.Empty(table.DealerCards);
        Assert.All(table.Players, player => Assert.Empty(player.Cards));
        await store.SweepAsync(secondReadyAt, default);
        Assert.All(bots, bot => Assert.True(bot.NextWagerCents > 0));
        Assert.Equal(BlackjackTablePhases.Betting, table.Phase);
        Assert.Empty(table.DealerCards);
        Assert.All(table.Players, player => Assert.Empty(player.Cards));
    }

    [Fact]
    public void ManagedPlayerWagersHaveDistinctHabitsAndReactToResults()
    {
        var startingWagers = Enumerable.Range(0, 40)
            .Select(index => BlackjackTableCoordinator.InitialManagedWager($"managed-{index}"))
            .Distinct()
            .ToArray();
        var changeRates = Enumerable.Range(0, 40)
            .Select(index => BlackjackTableCoordinator.ManagedWagerChangeChance($"managed-{index}"))
            .Distinct()
            .ToArray();
        Assert.True(startingWagers.Length >= 6);
        Assert.Equal(4, changeRates.Length);

        var winner = ManagedWagerPlayer("managed-winner", 500);
        winner.PayoutCents = 1_500;
        BlackjackTableCoordinator.UpdateManagedWagerHabit(winner, 500);
        Assert.True(BlackjackManagedSeat.BaseWager(winner) > 500);

        var losingStreak = ManagedWagerPlayer("managed-loser", 1_000);
        BlackjackManagedSeat.SetConsecutiveLosses(losingStreak, 2);
        BlackjackTableCoordinator.UpdateManagedWagerHabit(losingStreak, 500);
        Assert.True(BlackjackManagedSeat.BaseWager(losingStreak) < 1_000);
    }

    [Fact]
    public async Task NaturalPayoutCreditsRealBalanceExactlyOnceAndTableStaysOpen()
    {
        var store = Store(NaturalDeck());
        store.SetBalance("human", 1_000);
        var play = await JoinAtTable(store, "human", "SilverPanda");
        await store.WagerAsync("human", play.Table.TableId, 100, play.Version, Key("natural-wager"), Start.AddSeconds(7), default);
        await StartRound(store, "human", Start.AddSeconds(8));

        var settled = await AdvanceUntilBetting(store, "human", Start.AddSeconds(10));
        Assert.Equal(1_150, store.Balance("human"));
        Assert.Equal(2, store.Ledger.Count);
        Assert.Single(store.Ledger, entry => entry.Type == "blackjack-table-wager");
        Assert.Single(store.Ledger, entry => entry.Type == "blackjack-table-payout");
        Assert.Single(store.Revenue);
        Assert.Equal(BlackjackTablePhases.Betting, settled.Table.Phase);
        Assert.Equal("table", settled.Kind);
        Assert.Empty(settled.Table.Dealer.Cards);
        Assert.All(settled.Table.Seats, seat =>
        {
            Assert.Empty(seat.Hand.Cards);
            Assert.Equal(0m, seat.TotalWager);
            Assert.Equal(0m, seat.Payout);
            Assert.Null(seat.Outcome);
        });

        var history = Assert.Single(await store.GetHistoryAsync("human", 20, default));
        Assert.Equal((1m, 2.50m, 1.50m), (history.WagerCredits, history.PayoutCredits, history.NetCredits));
        Assert.Equal(("completed", "paid", false), (history.ClaimStatus, history.SettlementStatus, history.Seen));
        var seen = await store.MarkHistorySeenAsync("human", history.ResultId, Start.AddMinutes(1), default);
        var seenReplay = await store.MarkHistorySeenAsync("human", history.ResultId, Start.AddMinutes(2), default);
        Assert.True(seen.Seen);
        Assert.Equal(seen.SeenAtUtc, seenReplay.SeenAtUtc);

        await store.GetSessionAsync("human", Start.AddMinutes(3), default);
        Assert.Equal(1_150, store.Balance("human"));
        Assert.Equal(2, store.Ledger.Count);
        Assert.Single(store.Revenue);
    }

    [Fact]
    public async Task SplitCreatesTwoHandsAndBothHandsCanDoubleWithDistinctExactOnceDebits()
    {
        IReadOnlyList<string> deck = DoubleDeck();
        var store = new InMemoryBlackjackTableStore(() => deck.ToArray(), () => 3UL);
        store.SetBalance("human", 2_000);
        var play = await JoinAtTable(store, "human", "SplitStone");
        deck = SplitDeck();
        await store.WagerAsync(
            "human", play.Table.TableId, 100, play.Version, Key("split-wager"), Start.AddSeconds(7), default);
        var started = await StartRound(store, "human", Start.AddSeconds(8));

        Assert.Contains(BlackjackActions.Split, started.Table.LegalActions);
        var split = Assert.IsType<BlackjackTablePlaySessionResponse>((await store.ActionAsync(
            "human", play.Table.TableId, BlackjackActions.Split, started.Version,
            Key("split-action"), Start.AddSeconds(8), default)).Session);
        Assert.Equal(1_800, store.Balance("human"));
        Assert.Equal(2, split.Table.Seats.Single(seat => seat.IsCurrentPlayer).Hands.Count);

        var firstReady = Assert.IsType<BlackjackTablePlaySessionResponse>((await store.GetSessionAsync(
            "human", Start.AddSeconds(8).Add(BlackjackTableEngine.ActionSettleDuration), default)).Session);
        Assert.Contains(BlackjackActions.Double, firstReady.Table.LegalActions);
        var firstDouble = Assert.IsType<BlackjackTablePlaySessionResponse>((await store.ActionAsync(
            "human", play.Table.TableId, BlackjackActions.Double, firstReady.Version,
            Key("split-double-one"), Start.AddSeconds(9), default)).Session);

        var secondReady = Assert.IsType<BlackjackTablePlaySessionResponse>((await store.GetSessionAsync(
            "human", Start.AddSeconds(9).Add(BlackjackTableEngine.ActionSettleDuration), default)).Session);
        Assert.Contains(BlackjackActions.Double, secondReady.Table.LegalActions);
        Assert.True(secondReady.Table.Seats.Single(seat => seat.IsCurrentPlayer).Hands[1].Active);
        await store.ActionAsync(
            "human", play.Table.TableId, BlackjackActions.Double, secondReady.Version,
            Key("split-double-two"), Start.AddSeconds(10), default);

        Assert.Equal(1_600, store.Balance("human"));
        var actionDebits = store.Ledger.Where(entry =>
            entry.Type is "blackjack-table-split" or "blackjack-table-double").ToArray();
        Assert.Equal(new long[] { -100, -100, -100 }, actionDebits.Select(entry => entry.AmountCents).ToArray());
        Assert.Equal(3, actionDebits.Select(entry => entry.Id).Distinct(StringComparer.Ordinal).Count());
        Assert.Equal(400, BlackjackTableEngine.TotalCommitted(store.TableForTest(play.Table.TableId)
            .Players.Single(player => player.ActorId == "human")));
    }

    [Fact]
    public void GameEngineDoesNotApplyPlayerTimeoutPolicy()
    {
        var player = new BlackjackTablePlayer
        {
            ActorId = "human",
            PublicSeatId = "seat-human",
            DisplayName = "SlowPine",
            Seat = 0,
            SessionId = "session-human",
            SessionStartedAtUtc = Start,
            NextWagerCents = 100
        };
        var table = new BlackjackTableState
        {
            TableId = "table",
            Players = [player],
            CreatedAtUtc = Start,
            UpdatedAtUtc = Start
        };
        BlackjackTableEngine.Deal(table, DirectSplitDeck(), 3UL, Start);
        BlackjackTableEngine.ApplyAction(table, "human", BlackjackActions.Split, Start);
        var firstReady = Start.Add(BlackjackTableEngine.ActionSettleDuration);
        BlackjackTableEngine.AdvanceAutomatedTurns(table, firstReady);
        var firstTimeout = firstReady.Add(BlackjackTableEngine.ActionDuration);
        BlackjackTableEngine.AdvanceAutomatedTurns(table, firstTimeout);
        Assert.Equal(0, player.ConsecutiveMissedActionRounds);

        var secondReady = firstTimeout.Add(BlackjackTableEngine.ActionSettleDuration);
        BlackjackTableEngine.AdvanceAutomatedTurns(table, secondReady);
        BlackjackTableEngine.AdvanceAutomatedTurns(table, secondReady.Add(BlackjackTableEngine.ActionDuration));

        Assert.Equal(0, player.ConsecutiveMissedActionRounds);
        Assert.False(player.LeavingAfterRound);
    }

    [Fact]
    public async Task SplitAcesReceiveExactlyOneCardEachAndCannotBePlayedAgain()
    {
        IReadOnlyList<string> deck = DoubleDeck();
        var store = new InMemoryBlackjackTableStore(() => deck.ToArray(), () => 3UL);
        store.SetBalance("human", 2_000);
        var play = await JoinAtTable(store, "human", "AceHarbor");
        deck = SplitAceDeck();
        await store.WagerAsync(
            "human", play.Table.TableId, 100, play.Version, Key("ace-wager"), Start.AddSeconds(7), default);
        var started = await StartRound(store, "human", Start.AddSeconds(8));

        var split = Assert.IsType<BlackjackTablePlaySessionResponse>((await store.ActionAsync(
            "human", play.Table.TableId, BlackjackActions.Split, started.Version,
            Key("ace-split"), Start.AddSeconds(8), default)).Session);
        var hands = split.Table.Seats.Single(seat => seat.IsCurrentPlayer).Hands;
        Assert.Equal(2, hands.Count);
        Assert.All(hands, hand => Assert.Equal(2, hand.Hand.Cards.Count));
        Assert.All(hands, hand => Assert.Equal("stood", hand.Status));
        Assert.Equal(started.Table.ActiveSeat, split.Table.ActiveSeat);
    }

    [Fact]
    public async Task EqualValueTenAndFaceCardCanSplit()
    {
        var store = Store(EqualTenSplitDeck());
        store.SetBalance("human", 2_000);
        var play = await JoinAtTable(store, "human", "TenWillow");
        await store.WagerAsync(
            "human", play.Table.TableId, 100, play.Version, Key("ten-split-wager"), Start.AddSeconds(7), default);
        var started = await StartRound(store, "human", Start.AddSeconds(8));

        Assert.Equal(new[] { "10", "K" }, started.Table.Seats.Single(seat => seat.IsCurrentPlayer)
            .Hand.Cards.Select(card => card.Rank).ToArray());
        Assert.Contains(BlackjackActions.Split, started.Table.LegalActions);
    }

    [Fact]
    public void TwentyOneAfterSplitPaysEvenMoneyInsteadOfNaturalThreeToTwo()
    {
        var player = new BlackjackTablePlayer
        {
            ActorId = "human", PublicSeatId = "seat-human", DisplayName = "EvenPine",
            Seat = 0, SessionId = "session-human",
            SessionStartedAtUtc = Start, NextWagerCents = 100
        };
        var table = new BlackjackTableState
        {
            TableId = "table", Players = [player], CreatedAtUtc = Start, UpdatedAtUtc = Start
        };
        BlackjackTableEngine.Deal(table, SplitTwentyOneDeck(), 3UL, Start);
        BlackjackTableEngine.ApplyAction(table, "human", BlackjackActions.Split, Start);

        Assert.Equal(21, BlackjackRules.Score(player.Cards).Score);
        Assert.Equal("stood", player.Status);
        player.Outcome = BlackjackOutcomes.PlayerWin;
        Assert.Equal(200, BlackjackTableEngine.PrimaryPayoutFor(player));
    }

    [Fact]
    public async Task DealerBlackjackPeekPreventsLateSurrender()
    {
        var store = Store(DealerBlackjackDeck());
        store.SetBalance("human", 2_000);
        var play = await JoinAtTable(store, "human", "PeekRobin");
        await store.WagerAsync(
            "human", play.Table.TableId, 100, play.Version, Key("peek-wager"), Start.AddSeconds(7), default);
        var started = await StartRound(store, "human", Start.AddSeconds(8));

        Assert.Equal(BlackjackTablePhases.Dealer, started.Table.Phase);
        Assert.DoesNotContain(BlackjackActions.Surrender, started.Table.LegalActions);
        Assert.Empty(started.Table.LegalActions);
    }

    [Fact]
    public async Task LateSurrenderReturnsHalfAndRecognizesOnlyTheHumanNetOnce()
    {
        var store = Store(SurrenderDeck());
        store.SetBalance("human", 1_000);
        var play = await JoinAtTable(store, "human", "QuietMaple");
        await store.WagerAsync(
            "human", play.Table.TableId, 100, play.Version, Key("surrender-wager"), Start.AddSeconds(7), default);
        var started = await StartRound(store, "human", Start.AddSeconds(8));

        Assert.Contains(BlackjackActions.Surrender, started.Table.LegalActions);
        await store.ActionAsync(
            "human", play.Table.TableId, BlackjackActions.Surrender, started.Version,
            Key("surrender-action"), Start.AddSeconds(8), default);
        await AdvanceUntilBetting(store, "human", Start.AddSeconds(10));

        Assert.Equal(950, store.Balance("human"));
        var history = Assert.Single(await store.GetHistoryAsync("human", 20, default));
        Assert.Equal((1m, 0.50m, -0.50m), (history.WagerCredits, history.PayoutCredits, history.NetCredits));
        var revenue = Assert.Single(store.Revenue);
        Assert.Equal((100L, 50L), (revenue.HumanWagerCents, revenue.HumanPayoutCents));
    }

    [Fact]
    public async Task InsuranceUsesHalfWagerAndPaysThreeTimesStakeAgainstDealerBlackjack()
    {
        var store = Store(InsuranceBlackjackDeck());
        store.SetBalance("human", 1_000);
        var play = await JoinAtTable(store, "human", "CoveredPine");
        await store.WagerAsync(
            "human", play.Table.TableId, 100, play.Version, Key("insurance-wager"), Start.AddSeconds(7), default);
        var started = await StartRound(store, "human", Start.AddSeconds(8));

        Assert.Equal(BlackjackTablePhases.Insurance, started.Table.Phase);
        Assert.Equal(new[] { BlackjackActions.Insurance, BlackjackActions.DeclineInsurance }, started.Table.LegalActions);
        store.TableForTest(play.Table.TableId).Players.Single(player => player.ActorId == "human")
            .ConsecutiveMissedActionRounds = 1;
        await store.ActionAsync(
            "human", play.Table.TableId, BlackjackActions.Insurance, started.Version,
            Key("insurance-action"), Start.AddSeconds(8), default);
        Assert.Equal(1, store.TableForTest(play.Table.TableId).Players.Single(player => player.ActorId == "human")
            .ConsecutiveMissedActionRounds);
        await AdvanceUntilBetting(store, "human", Start.AddSeconds(10));

        Assert.Equal(1_000, store.Balance("human"));
        Assert.Single(store.Ledger, entry => entry.Type == "blackjack-table-insurance" && entry.AmountCents == -50);
        var history = Assert.Single(await store.GetHistoryAsync("human", 20, default));
        Assert.Equal((1.50m, 1.50m, 0m), (history.WagerCredits, history.PayoutCredits, history.NetCredits));
        var revenue = Assert.Single(store.Revenue);
        Assert.Equal((150L, 150L), (revenue.HumanWagerCents, revenue.HumanPayoutCents));
    }

    [Fact]
    public void DealerNaturalOutcomeAndInsurancePayoutStayRedactedUntilHoleCardReveal()
    {
        var player = new BlackjackTablePlayer
        {
            ActorId = "human", PublicSeatId = "seat-human", DisplayName = "CoveredOak",
            Seat = 0, SessionId = "session-human",
            SessionStartedAtUtc = Start, NextWagerCents = 100
        };
        var table = new BlackjackTableState
        {
            TableId = "table", Players = [player], CreatedAtUtc = Start, UpdatedAtUtc = Start
        };
        BlackjackTableEngine.Deal(table, DirectInsuranceBlackjackDeck(), 3UL, Start);
        BlackjackTableEngine.ApplyAction(table, "human", BlackjackActions.Insurance, Start);
        BlackjackTableEngine.AdvanceAutomatedTurns(table, Start.Add(BlackjackTableEngine.ActionSettleDuration));
        Assert.Equal(BlackjackTablePhases.Dealer, table.Phase);
        Assert.Equal(1, table.DealerVisibleCardCount);
        Assert.Equal(BlackjackOutcomes.DealerBlackjack, player.Outcome);
        Assert.Equal(150, player.InsurancePayoutCents);

        var projected = BlackjackTableProjection.Table(table, "human", Start.AddSeconds(1));
        var seat = Assert.Single(projected.Seats);
        Assert.Null(seat.Outcome);
        Assert.Null(Assert.Single(seat.Hands).Outcome);
        Assert.Equal("waiting", seat.Status);
        Assert.Equal("waiting", seat.Hands[0].Status);
        Assert.Equal(0, seat.Payout);
        Assert.Equal(0, seat.Hands[0].Payout);
        Assert.Equal(0, seat.InsurancePayout);
        var json = JsonSerializer.Serialize(projected, new JsonSerializerOptions(JsonSerializerDefaults.Web));
        Assert.DoesNotContain(BlackjackOutcomes.DealerBlackjack, json, StringComparison.Ordinal);

        BlackjackTableEngine.AdvanceAutomatedTurns(
            table,
            Start.Add(BlackjackTableEngine.ActionSettleDuration).Add(BlackjackTableEngine.DealerCardDuration));
        var revealed = BlackjackTableProjection.Table(table, "human", Start.AddSeconds(2));
        Assert.Equal(BlackjackOutcomes.DealerBlackjack, Assert.Single(revealed.Seats).Outcome);
        Assert.Equal(1.50m, revealed.Seats[0].InsurancePayout);
    }

    [Fact]
    public async Task LeavingActiveRoundStandsThenSettlesCommittedWager()
    {
        var store = Store(DoubleDeck());
        store.SetBalance("human", 1_000);
        var play = await JoinAtTable(store, "human", "CalmOtter");
        await store.WagerAsync(
            "human", play.Table.TableId, 100, play.Version, Key("leave-wager"), Start.AddSeconds(7), default);
        var started = await StartRound(store, "human", Start.AddSeconds(8));

        var left = await store.LeaveAsync("human", play.Table.TableId, started.Version, Key("leave-active"), Start.AddSeconds(8), default);
        Assert.IsType<BlackjackTableIdleSessionResponse>(left.Session);
        Assert.True(store.TableForTest(play.Table.TableId).Players.Single(player => player.ActorId == "human").LeavingAfterRound);

        for (var step = 0; step < 80 && store.StateForTest.Tables.ContainsKey(play.Table.TableId) && store.Revenue.Count == 0; step++)
            await store.SweepAsync(Start.AddSeconds(10 + step * 2), default);
        Assert.DoesNotContain(store.Ledger, entry => entry.Type.Contains("refund", StringComparison.OrdinalIgnoreCase));
        Assert.Single(store.Revenue);
    }

    [Fact]
    public async Task WireStateRedactsDealerHoleAndInternalClassification()
    {
        var store = Store(DoubleDeck());
        store.SetBalance("human", 2_000);
        var play = await JoinAtTable(store, "human", "CopperRobin");
        await store.WagerAsync(
            "human", play.Table.TableId, 100, play.Version, Key("wire-wager"), Start.AddSeconds(7), default);
        var started = await StartRound(store, "human", Start.AddSeconds(8));
        Assert.False(started.Table.Dealer.Cards[0].Hidden);
        Assert.True(started.Table.Dealer.Cards[1].Hidden);
        Assert.Null(started.Table.Dealer.Cards[1].Rank);

        var json = JsonSerializer.Serialize(started, new JsonSerializerOptions(JsonSerializerDefaults.Web));
        Assert.DoesNotContain("isBot", json, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("skill", json, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("seed", json, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("actorId", json, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("bot:", json, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void FeatureIsOffByDefaultAndStatusPublishesExactLimits()
    {
        var configuration = new ConfigurationBuilder().AddInMemoryCollection().Build();
        Assert.False(BlackjackTableController.IsEnabled(configuration));
        var status = BlackjackTableController.StatusContract();
        Assert.Equal((0.50m, 100m, 0.50m), (status.MinimumWager, status.MaximumWager, status.WagerIncrement));
        Assert.Equal((3, 5), (status.MinimumStartOccupancy, status.TableCapacity));
        Assert.Equal(60, status.ActionDeadlineSeconds);
        Assert.Equal("3:2", status.BlackjackPayout);
        Assert.Equal("Dealer stands on all 17s", status.DealerRule);
        Assert.Equal(1, status.DeckCount);
    }

    private static async Task<BlackjackTablePlaySessionResponse> JoinAtTable(
        InMemoryBlackjackTableStore store,
        string userId,
        string displayName)
    {
        await store.JoinAsync(userId, displayName, 0, Key($"join-{userId}"), Start, default);
        return Assert.IsType<BlackjackTablePlaySessionResponse>((await store.GetSessionAsync(
            userId, Start.AddSeconds(6), default)).Session);
    }

    private static async Task<BlackjackTablePlaySessionResponse> AdvanceUntilBetting(
        InMemoryBlackjackTableStore store,
        string userId,
        DateTime now)
    {
        BlackjackTablePlaySessionResponse? session = null;
        for (var step = 0; step < 120; step++)
        {
            session = Assert.IsType<BlackjackTablePlaySessionResponse>((await store.GetSessionAsync(
                userId, now.AddSeconds(step * 2), default)).Session);
            var current = session.Table.Seats.SingleOrDefault(seat => seat.IsCurrentPlayer);
            if (session.Table.Phase == BlackjackTablePhases.Betting &&
                session.Table.Transition == "human-wager" &&
                session.Table.ActiveSeat == current?.Seat)
                return session;
        }
        throw new Xunit.Sdk.XunitException("Blackjack round did not return to betting.");
    }

    private static async Task<BlackjackTablePlaySessionResponse> StartRound(
        InMemoryBlackjackTableStore store,
        string userId,
        DateTime now)
    {
        BlackjackTablePlaySessionResponse? session = null;
        for (var step = 0; step < 180; step++)
        {
            session = Assert.IsType<BlackjackTablePlaySessionResponse>((await store.GetSessionAsync(
                userId, now.AddMilliseconds(step * 500), default)).Session);
            var current = session.Table.Seats.SingleOrDefault(seat => seat.IsCurrentPlayer);
            if (session.Table.Phase is BlackjackTablePhases.Dealer or BlackjackTablePhases.Insurance)
                return session;
            if (session.Table.Phase == BlackjackTablePhases.Active &&
                session.Table.ActiveSeat == current?.Seat &&
                session.Table.Transition is null)
                return session;
        }
        throw new Xunit.Sdk.XunitException($"Blackjack round did not leave the wager phase: updated={session?.Table.UpdatedAtUtc:o}, next={session?.Table.NextTransitionAtUtc:o}, transition={session?.Table.Transition}, seats={string.Join(',', session?.Table.Seats.Select(seat => $"{seat.DisplayName}:{seat.Wager}:{seat.Status}") ?? [])}.");
    }

    private static InMemoryBlackjackTableStore Store(
        IReadOnlyList<string>? deck = null,
        ulong seed = 3UL) =>
        new(() => (deck ?? DoubleDeck()).ToArray(), () => seed);

    private static string Key(string value) => value.PadRight(16, 'x');

    private static BlackjackTablePlayer ManagedWagerPlayer(string actorId, long baseWager) => new()
    {
        ActorId = actorId,
        PublicSeatId = $"seat-{actorId}",
        DisplayName = actorId,
        Seat = 0,
        SessionId = $"session-{actorId}",
        SessionStartedAtUtc = Start,
        HostMetadata = BlackjackManagedSeat.Create(3, baseWager, 600)
    };

    private static IReadOnlyList<string> NaturalDeck() => DeckWithPrefix(
        "A|spades", "2|clubs", "3|clubs", "9|hearts",
        "K|diamonds", "5|clubs", "6|clubs", "7|spades");

    private static IReadOnlyList<string> DoubleDeck() => DeckWithPrefix(
        "5|spades", "2|clubs", "3|clubs", "6|hearts",
        "6|diamonds", "5|clubs", "7|clubs", "10|spades", "K|hearts");

    private static IReadOnlyList<string> HitDeck() => DeckWithPrefix(
        "5|spades", "2|clubs", "3|clubs", "6|hearts",
        "6|diamonds", "5|clubs", "7|clubs", "10|spades", "2|diamonds");

    private static IReadOnlyList<string> SplitDeck() => DeckWithPrefix(
        "8|spades", "2|clubs", "3|clubs", "6|hearts",
        "8|diamonds", "5|clubs", "7|clubs", "10|spades",
        "3|diamonds", "2|hearts", "10|clubs", "9|clubs");

    private static IReadOnlyList<string> DirectSplitDeck() => DeckWithPrefix(
        "8|spades", "6|hearts", "8|diamonds", "10|spades",
        "3|diamonds", "2|hearts");

    private static IReadOnlyList<string> SplitAceDeck() => DeckWithPrefix(
        "A|spades", "2|clubs", "3|clubs", "6|hearts",
        "A|diamonds", "5|clubs", "7|clubs", "10|spades",
        "3|diamonds", "2|hearts");

    private static IReadOnlyList<string> EqualTenSplitDeck() => DeckWithPrefix(
        "10|spades", "2|clubs", "3|clubs", "6|hearts",
        "K|diamonds", "5|clubs", "7|clubs", "10|hearts",
        "3|diamonds", "2|hearts");

    private static IReadOnlyList<string> SplitTwentyOneDeck() => DeckWithPrefix(
        "10|spades", "6|clubs", "K|diamonds", "9|hearts", "A|clubs", "8|clubs");

    private static IReadOnlyList<string> DealerBlackjackDeck() => DeckWithPrefix(
        "9|spades", "2|clubs", "3|clubs", "10|hearts",
        "7|diamonds", "5|clubs", "6|clubs", "A|spades");

    private static IReadOnlyList<string> SurrenderDeck() => DeckWithPrefix(
        "10|spades", "2|clubs", "3|clubs", "6|hearts",
        "6|diamonds", "5|clubs", "7|clubs", "10|hearts");

    private static IReadOnlyList<string> InsuranceBlackjackDeck() => DeckWithPrefix(
        "10|spades", "2|clubs", "3|clubs", "A|hearts",
        "9|diamonds", "5|clubs", "7|clubs", "K|spades");

    private static IReadOnlyList<string> DirectInsuranceBlackjackDeck() => DeckWithPrefix(
        "9|spades", "A|hearts", "7|diamonds", "K|spades");

    private static IReadOnlyList<string> DirectPlayableDeck() => DeckWithPrefix(
        "5|spades", "6|hearts", "6|diamonds", "10|spades", "2|clubs");

    private static IReadOnlyList<string> DeckWithPrefix(params string[] prefix)
    {
        var suits = new[] { "clubs", "diamonds", "hearts", "spades" };
        var ranks = new[] { "A", "2", "3", "4", "5", "6", "7", "8", "9", "10", "J", "Q", "K" };
        return prefix.Concat(suits.SelectMany(suit => ranks.Select(rank => $"{rank}|{suit}")))
            .Distinct(StringComparer.Ordinal)
            .Take(52)
            .ToArray();
    }
}
