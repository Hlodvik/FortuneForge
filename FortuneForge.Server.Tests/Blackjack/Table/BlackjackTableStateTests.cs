using System.Text.Json;
using FortuneForge.Server.Cards.Blackjack;
using FortuneForge.Server.Cards.Blackjack.Table;
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
    [InlineData(5, 5, 0)]
    public async Task FreeGraceStartSeatsUpToFiveHumansThenOnlyEnoughBotsForThree(
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
        Assert.Equal(humans, table.Players.Count(player => !player.IsBot));
        Assert.Equal(bots, table.Players.Count(player => player.IsBot));
        Assert.Empty(store.Ledger);
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
    public async Task WagerCanBeAdjustedAndLeavingBettingReleasesCommittedAmountExactlyOnce()
    {
        var store = Store();
        store.SetBalance("human", 10_000);
        var play = await JoinAtTable(store, "human", "RiverStone");

        var first = await store.WagerAsync("human", play.Table.TableId, 500, play.Version, Key("wager-500"), Start.AddSeconds(7), default);
        Assert.Equal(9_500, store.Balance("human"));
        var firstPlay = Assert.IsType<BlackjackTablePlaySessionResponse>(first.Session);
        var adjusted = await store.WagerAsync("human", play.Table.TableId, 300, firstPlay.Version, Key("wager-300"), Start.AddSeconds(7), default);
        Assert.Equal(9_700, store.Balance("human"));
        var adjustedPlay = Assert.IsType<BlackjackTablePlaySessionResponse>(adjusted.Session);

        await store.LeaveAsync("human", play.Table.TableId, adjustedPlay.Version, Key("leave-betting"), Start.AddSeconds(7).AddMilliseconds(400), default);
        await store.LeaveAsync("human", play.Table.TableId, adjustedPlay.Version, Key("leave-betting"), Start.AddSeconds(7).AddMilliseconds(500), default);

        Assert.Equal(10_000, store.Balance("human"));
        Assert.Equal(new long[] { -500, 200, 300 }, store.Ledger.Select(entry => entry.AmountCents));
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
        Assert.Null(acted.Table.ActiveSeat);

        var tooSoon = Assert.IsType<BlackjackTablePlaySessionResponse>((await store.GetSessionAsync(
            "human", Start.AddSeconds(8).AddMilliseconds(300), default)).Session);
        Assert.Equal("action-settle", tooSoon.Table.Transition);
        var next = Assert.IsType<BlackjackTablePlaySessionResponse>((await store.GetSessionAsync(
            "human", Start.AddSeconds(8).AddMilliseconds(700), default)).Session);
        Assert.Equal("turn-pause", next.Table.Transition);
        Assert.NotNull(next.Table.NextTransitionAtUtc);

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
        var startedAt = Start.AddSeconds(8);
        var started = await StartRound(store, "human", startedAt);
        var humanSeat = started.Table.Seats.Single(seat => seat.IsCurrentPlayer).Seat;

        Assert.Equal(humanSeat, started.Table.ActiveSeat);
        Assert.Equal(startedAt.AddMinutes(1), started.Table.ActionDeadlineAtUtc);
        store.TableForTest(play.Table.TableId).Players.Single(player => player.ActorId == "human")
            .ConsecutiveMissedActionRounds = 1;

        var hitAt = startedAt.AddMilliseconds(100);
        var hit = Assert.IsType<BlackjackTablePlaySessionResponse>((await store.ActionAsync(
            "human", play.Table.TableId, BlackjackActions.Hit, started.Version, Key("timer-hit"), hitAt, default)).Session);
        Assert.Equal("action-settle", hit.Table.Transition);
        Assert.Null(hit.Table.ActiveSeat);
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

        var secondWager = Assert.IsType<BlackjackTablePlaySessionResponse>((await store.WagerAsync(
            "human", play.Table.TableId, 100, firstBetting.Version, Key("timeout-wager-two"),
            Start.AddSeconds(100), default)).Session);
        var second = await StartRound(store, "human", Start.AddSeconds(101));
        Assert.True(second.Version > secondWager.Version);
        await store.GetSessionAsync("human", second.Table.ActionDeadlineAtUtc!.Value, default);
        Assert.True(player.LeavingAfterRound);
        Assert.Equal(2, player.ConsecutiveMissedActionRounds);

        for (var step = 0; step < 30 && store.StateForTest.Tables.ContainsKey(play.Table.TableId); step++)
            await store.SweepAsync(Start.AddSeconds(165 + step * 2), default);
        var idle = await store.GetSessionAsync("human", Start.AddMinutes(4), default);
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
            "human", Start.AddSeconds(66), default)).Session);
        var table = store.TableForTest(play.Table.TableId);
        Assert.Equal(1, table.Players.Single(player => player.ActorId == "human").ConsecutiveMissedRounds);
        Assert.Equal(Start.AddSeconds(126), firstMiss.Table.WagerDeadlineAtUtc);

        var kicked = await store.GetSessionAsync("human", Start.AddSeconds(126), default);
        Assert.IsType<BlackjackTableIdleSessionResponse>(kicked.Session);
        Assert.DoesNotContain(table.Players, player => player.ActorId == "human");
        Assert.Equal(BlackjackTablePhases.Closed, table.Phase);
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
    public void TimingOutBothSplitHandsCountsOnlyOneMissedRound()
    {
        var player = new BlackjackTablePlayer
        {
            ActorId = "human",
            PublicSeatId = "seat-human",
            DisplayName = "SlowPine",
            IsBot = false,
            BotSkillLevel = null,
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
        Assert.Equal(1, player.ConsecutiveMissedActionRounds);

        var secondReady = firstTimeout.Add(BlackjackTableEngine.ActionSettleDuration);
        BlackjackTableEngine.AdvanceAutomatedTurns(table, secondReady);
        BlackjackTableEngine.AdvanceAutomatedTurns(table, secondReady.Add(BlackjackTableEngine.ActionDuration));

        Assert.Equal(1, player.ConsecutiveMissedActionRounds);
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
        Assert.Null(split.Table.ActiveSeat);
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
            IsBot = false, BotSkillLevel = null, Seat = 0, SessionId = "session-human",
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
            IsBot = false, BotSkillLevel = null, Seat = 0, SessionId = "session-human",
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

        for (var step = 0; step < 20 && store.StateForTest.Tables.ContainsKey(play.Table.TableId); step++)
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
        for (var step = 0; step < 20; step++)
        {
            session = Assert.IsType<BlackjackTablePlaySessionResponse>((await store.GetSessionAsync(
                userId, now.AddSeconds(step * 2), default)).Session);
            if (session.Table.Phase == BlackjackTablePhases.Betting) return session;
        }
        throw new Xunit.Sdk.XunitException("Blackjack round did not return to betting.");
    }

    private static async Task<BlackjackTablePlaySessionResponse> StartRound(
        InMemoryBlackjackTableStore store,
        string userId,
        DateTime now) => Assert.IsType<BlackjackTablePlaySessionResponse>((await store.GetSessionAsync(
            userId, now, default)).Session);

    private static InMemoryBlackjackTableStore Store(
        IReadOnlyList<string>? deck = null,
        ulong seed = 3UL) =>
        new(() => (deck ?? DoubleDeck()).ToArray(), () => seed);

    private static string Key(string value) => value.PadRight(16, 'x');

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
