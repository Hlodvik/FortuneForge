using FortuneForge.Server.Bots.Blackjack;
using FortuneForge.Server.Cards.Blackjack.Table;
using Xunit;

namespace FortuneForge.Games.Blackjack.Tests;

public sealed class BlackjackManagedLeaseTests
{
    private static readonly DateTime Now = new(2026, 11, 21, 12, 0, 0, DateTimeKind.Utc);

    [Fact]
    public async Task Confirmed_lost_betting_seat_is_removed_without_spending_a_human_stake()
    {
        var state = State(); var table = state.Tables["table"];
        var managed = table.Players.Single(player => player.ActorId == "managed-profile");
        table.Transition = "managed-wager"; table.ActiveSeat = managed.Seat; table.PendingSeat = managed.Seat;
        var balances = new Dictionary<string, long> { ["alice"] = 9900 };
        var result = await Coordinator().GetAsync(state, balances, "alice", Now);
        Assert.DoesNotContain(table.Players, BlackjackManagedSeat.IsManaged);
        Assert.Equal(9900, result.Store.BalanceCents);
        Assert.Empty(result.Journal.Ledger);
        Assert.Equal("managed-profile", Assert.Single(result.Journal.ManagedPlayerReleases).ProfileId);
        Assert.NotEqual(managed.Seat, table.ActiveSeat);
    }

    [Theory]
    [InlineData(false, false)]
    [InlineData(true, false)]
    [InlineData(false, true)]
    [InlineData(true, true)]
    public async Task Confirmed_lost_dealt_seat_uses_passive_departure_and_preserves_its_committed_hand(bool insurance, bool legacyPause)
    {
        var state = State(); var table = state.Tables["table"];
        foreach (var player in table.Players) player.NextWagerCents = 100;
        var prefix = insurance ? new[] { "9|spades", "5|hearts", "A|diamonds", "8|clubs", "6|clubs", "10|hearts" }
            : new[] { "9|spades", "5|hearts", "6|diamonds", "8|clubs", "6|clubs", "10|hearts" };
        BlackjackTableEngine.Deal(table, Deck(prefix), 1931, Now);
        var managed = table.Players.Single(player => player.ActorId == "managed-profile");
        Assert.Equal(managed.Seat, table.ActiveSeat);
        if (legacyPause)
        {
            table.Transition = "turn-pause";
            table.NextTransitionAtUtc = Now.AddMinutes(1);
        }
        var originalCards = managed.Cards.ToArray();
        var balances = new Dictionary<string, long> { ["alice"] = 9900 };
        var coordinator = Coordinator();
        var result = await coordinator.GetAsync(state, balances, "alice", Now.AddSeconds(1));
        Assert.True(BlackjackTableCoordinator.ManagedLeaseLost(managed));
        Assert.True(managed.LeavingAfterRound);
        Assert.Equal(table.RoundNumber, BlackjackManagedSeat.DepartureRound(managed));
        Assert.Equal(originalCards, managed.Cards);
        Assert.Equal(100, managed.TotalWagerCents);
        Assert.Equal("left-table", managed.LastAction);
        if (insurance) Assert.False(managed.InsuranceAccepted);
        else Assert.Equal("stood", managed.Status);
        Assert.Empty(result.Journal.Ledger);
        Assert.Equal(9900, result.Store.BalanceCents);
        for (var step = 1; step <= 40 && table.Players.Contains(managed); step++)
        {
            var now = Now.AddSeconds(1 + step * 2);
            var advanced = await coordinator.GetAsync(state, balances, "alice", now);
            Assert.All(advanced.Journal.Ledger, entry => Assert.True(entry.AmountCents >= 0));
            if (table.Players.Contains(managed) && table.Transition is null && table.ActiveSeat == 1 &&
                table.Phase is BlackjackTablePhases.Active or BlackjackTablePhases.Insurance)
            {
                var action = table.Phase == BlackjackTablePhases.Insurance ? BlackjackActions.DeclineInsurance : BlackjackActions.Stand;
                var acted = await coordinator.ActionAsync(state, balances, "alice", table.TableId, action,
                    table.Version, "finish-human-" + step, now);
                Assert.All(acted.Journal.Ledger, entry => Assert.True(entry.AmountCents >= 0));
            }
        }
        Assert.DoesNotContain(managed, table.Players);
    }

    private static BlackjackTableCoordinator Coordinator() => new(allowManagedArrivals: false,
        ownsManagedPlayer: (_, _) => false);

    private static BlackjackTableLobbyState State()
    {
        var table = new BlackjackTableState
        {
            TableId = "table", Phase = BlackjackTablePhases.Betting, Version = 1, CreatedAtUtc = Now, UpdatedAtUtc = Now,
            Players =
            [
                new() { ActorId = "managed-profile", PublicSeatId = "seat_managed", SessionId = "managed-session", DisplayName = "Managed", Seat = 0, SessionStartedAtUtc = Now,
                    HostMetadata = BlackjackManagedSeat.Create(3, 100, 0), Status = "waiting-to-wager" },
                new() { ActorId = "alice", PublicSeatId = "seat_alice", SessionId = "alice-session", DisplayName = "Alice", Seat = 1, SessionStartedAtUtc = Now, Status = "awaiting-wager" },
            ],
        };
        return new()
        {
            Tables = new() { [table.TableId] = table },
            Sessions = new() { ["alice"] = new(BlackjackTableSessionKinds.Table, null, table.TableId) },
        };
    }

    private static IReadOnlyList<string> Deck(string[] prefix) => prefix.Concat(BlackjackRules.CreateShuffledDeck()
        .Where(card => !prefix.Contains(card)).Order(StringComparer.Ordinal)).ToArray();
}
