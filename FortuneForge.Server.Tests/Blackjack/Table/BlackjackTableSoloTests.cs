using FortuneForge.Games.Blackjack;
using FortuneForge.Server.Cards.Blackjack.Table;
using Xunit;

namespace FortuneForge.Server.Tests.Blackjack.Table;

public sealed class BlackjackTableSoloTests
{
    private static readonly DateTime Start = new(2026, 10, 27, 12, 0, 0, DateTimeKind.Utc);

    [Fact]
    public void SoloStatusAdvertisesOneSeatAndNoHumanDeadlines()
    {
        var status = BlackjackTableController.StatusContract(multiplayerEnabled: false);
        Assert.Equal((1, 1, 0, 0),
            (status.MinimumStartOccupancy, status.TableCapacity,
             status.HumanGraceSeconds, status.ActionDeadlineSeconds));
        Assert.True(status.SplitAllowed);
        Assert.True(status.InsuranceAllowed);
        Assert.True(status.SurrenderAllowed);
    }

    [Fact]
    public async Task JoiningCreatesSeparateUntimedHumanTablesWithoutManagedSeats()
    {
        var store = Store();
        store.SetBalance("first", 10_000);
        store.SetBalance("second", 10_000);

        var first = Assert.IsType<BlackjackTablePlaySessionResponse>((await store.JoinAsync(
            "first", "First", 0, Key("first-join"), Start, default)).Session);
        var second = Assert.IsType<BlackjackTablePlaySessionResponse>((await store.JoinAsync(
            "second", "Second", 0, Key("second-join"), Start, default)).Session);

        Assert.NotEqual(first.Table.TableId, second.Table.TableId);
        Assert.All(new[] { first, second }, session =>
        {
            Assert.Equal("solo", session.Table.Mode);
            Assert.Equal(BlackjackTablePhases.Betting, session.Table.Phase);
            Assert.Equal("human-wager", session.Table.Transition);
            Assert.Equal(0, session.Table.ActiveSeat);
            Assert.Null(session.Table.WagerDeadlineAtUtc);
            Assert.Null(session.Table.ActionDeadlineAtUtc);
            Assert.Single(session.Table.Seats);
            Assert.True(session.Table.Seats[0].IsCurrentPlayer);
        });

        var stillWaiting = Assert.IsType<BlackjackTablePlaySessionResponse>((await store.GetSessionAsync(
            "first", Start.AddHours(6), default)).Session);
        Assert.Equal("human-wager", stillWaiting.Table.Transition);
        Assert.Null(stillWaiting.Table.WagerDeadlineAtUtc);
        Assert.Empty(store.Ledger);
    }

    [Fact]
    public async Task UncommittedOldMultiplayerTableDrainsWhenSoloModeIsEnabled()
    {
        var multiplayerStore = new InMemoryBlackjackTableStore(() => Deck().ToArray(), () => 1931UL);
        multiplayerStore.SetBalance("player", 10_000);
        await multiplayerStore.JoinAsync("player", "Player", 0, Key("old-join"), Start, default);
        var oldSession = Assert.IsType<BlackjackTablePlaySessionResponse>((await multiplayerStore.GetSessionAsync(
            "player", Start.AddSeconds(6), default)).Session);
        Assert.Equal("multiplayer", oldSession.Table.Mode);

        var soloCoordinator = new BlackjackTableCoordinator(multiplayerEnabled: false);
        var afterSwitch = soloCoordinator.Get(multiplayerStore.StateForTest,
            new Dictionary<string, long> { ["player"] = 10_000 }, "player", Start.AddSeconds(7));

        Assert.IsType<BlackjackTableIdleSessionResponse>(afterSwitch.Store.Session);
        Assert.Equal(BlackjackTablePhases.Closed,
            multiplayerStore.TableForTest(oldSession.Table.TableId).Phase);
    }

    [Fact]
    public async Task PaidRoundWaitsForHumanActionAndKeepsResultUntilNextWagerOrLeave()
    {
        var store = Store();
        store.SetBalance("player", 10_000);
        var joined = Assert.IsType<BlackjackTablePlaySessionResponse>((await store.JoinAsync(
            "player", "Player", 0, Key("join"), Start, default)).Session);
        await store.WagerAsync("player", joined.Table.TableId, 100, joined.Version,
            Key("first-wager"), Start.AddSeconds(1), default);

        var active = await WaitFor(store, "player", Start.AddSeconds(2), session =>
            session.Table.Phase == BlackjackTablePhases.Active && session.Table.Transition is null);
        Assert.Null(active.Table.ActionDeadlineAtUtc);
        var untimed = Assert.IsType<BlackjackTablePlaySessionResponse>((await store.GetSessionAsync(
            "player", Start.AddHours(8), default)).Session);
        Assert.Equal(BlackjackTablePhases.Active, untimed.Table.Phase);
        Assert.Null(untimed.Table.ActionDeadlineAtUtc);
        Assert.Contains(BlackjackActions.Stand, untimed.Table.LegalActions);

        await store.ActionAsync("player", joined.Table.TableId, BlackjackActions.Stand,
            untimed.Version, Key("stand"), Start.AddHours(8).AddSeconds(1), default);
        var settled = await WaitFor(store, "player", Start.AddHours(8).AddSeconds(2), session =>
            session.Table.Phase == "settlement" && session.Table.Transition is null);
        Assert.Equal(10_100, store.Balance("player"));
        Assert.Single(await store.GetHistoryAsync("player", 10, default));

        var held = Assert.IsType<BlackjackTablePlaySessionResponse>((await store.GetSessionAsync(
            "player", Start.AddDays(1), default)).Session);
        Assert.Equal("settlement", held.Table.Phase);
        Assert.Null(held.Table.NextTransitionAtUtc);
        Assert.Equal(settled.Table.Dealer.Cards.Count, held.Table.Dealer.Cards.Count);
        Assert.Equal(settled.Table.Seats[0].Hand.Cards.Count, held.Table.Seats[0].Hand.Cards.Count);

        var next = Assert.IsType<BlackjackTablePlaySessionResponse>((await store.WagerAsync(
            "player", joined.Table.TableId, 100, held.Version,
            Key("second-wager"), Start.AddDays(1).AddSeconds(1), default)).Session);
        Assert.Equal(BlackjackTablePhases.Betting, next.Table.Phase);
        Assert.Equal("wager-lock", next.Table.Transition);
        Assert.Equal(10_000, store.Balance("player"));

        var left = await store.LeaveAsync("player", joined.Table.TableId, next.Version,
            Key("leave"), Start.AddDays(1).AddSeconds(1).AddMilliseconds(100), default);
        Assert.IsType<BlackjackTableIdleSessionResponse>(left.Session);
        Assert.Equal(10_100, store.Balance("player"));
    }

    [Fact]
    public async Task LeavingDuringDealStillSettlesTheCommittedHand()
    {
        var store = Store();
        store.SetBalance("player", 10_000);
        var joined = Assert.IsType<BlackjackTablePlaySessionResponse>((await store.JoinAsync(
            "player", "Player", 0, Key("join"), Start, default)).Session);
        await store.WagerAsync("player", joined.Table.TableId, 100, joined.Version,
            Key("wager"), Start.AddSeconds(1), default);
        var dealing = Assert.IsType<BlackjackTablePlaySessionResponse>((await store.GetSessionAsync(
            "player", Start.AddSeconds(2), default)).Session);
        Assert.Equal(BlackjackTablePhases.Dealing, dealing.Table.Phase);

        await store.LeaveAsync("player", joined.Table.TableId, dealing.Version,
            Key("leave"), Start.AddSeconds(2).AddMilliseconds(50), default);
        for (var step = 0; step < 120; step++)
            await store.SweepAsync(Start.AddSeconds(3).AddMilliseconds(step * 250), default);

        Assert.Equal(BlackjackTablePhases.Closed, store.TableForTest(joined.Table.TableId).Phase);
        Assert.Single(await store.GetHistoryAsync("player", 10, default));
        Assert.Single(store.Ledger, entry => entry.Type == "blackjack-table-wager");
    }

    private static async Task<BlackjackTablePlaySessionResponse> WaitFor(
        InMemoryBlackjackTableStore store,
        string userId,
        DateTime start,
        Func<BlackjackTablePlaySessionResponse, bool> condition)
    {
        for (var step = 0; step < 120; step++)
        {
            var session = Assert.IsType<BlackjackTablePlaySessionResponse>((await store.GetSessionAsync(
                userId, start.AddMilliseconds(step * 250), default)).Session);
            if (condition(session)) return session;
        }
        throw new Xunit.Sdk.XunitException("The solo Blackjack table did not reach the expected state.");
    }

    private static InMemoryBlackjackTableStore Store() => new(
        () => Deck().ToArray(), () => 1931UL, multiplayerEnabled: false);

    private static IReadOnlyList<string> Deck()
    {
        var prefix = new[] { "10|spades", "6|hearts", "9|diamonds", "10|clubs", "8|spades" };
        var suits = new[] { "clubs", "diamonds", "hearts", "spades" };
        var ranks = new[] { "A", "2", "3", "4", "5", "6", "7", "8", "9", "10", "J", "Q", "K" };
        return prefix.Concat(suits.SelectMany(suit => ranks.Select(rank => $"{rank}|{suit}")))
            .Distinct(StringComparer.Ordinal).Take(52).ToArray();
    }

    private static string Key(string value) => value.PadRight(16, 'x');
}
