using FortuneForge.Games.TexasHoldem;
using FortuneForge.Server.Bots;
using FortuneForge.Server.Bots.TexasHoldem;
using FortuneForge.Server.Cards.TexasHoldem.Credit;
using Xunit;

namespace FortuneForge.Server.Tests.Cards.TexasHoldem.Credit;

public sealed class TexasHoldemManagedPlayerTests
{
    private static readonly DateTime Start = new(2026, 9, 30, 12, 0, 0, DateTimeKind.Utc);

    [Fact]
    public void EachManagedProfileGetsItsOwnPrivateActionClock()
    {
        var profiles = TexasHoldemManagedPlayers.CreateLocalProfiles(2, Start);
        var ticket = new CreditHoldemTicket(
            "ticket", "account-player", "seat-account", "Alice", "table", "queued", 1,
            Start, Start, null);
        var seats = TexasHoldemManagedPlayers.BuildSeats(
            [ticket],
            profiles,
            new Dictionary<string, long> { [ticket.UserId] = 5_000 },
            42,
            CreditHoldemTableRules.Resolve(CreditHoldemTableRules.StandardId));
        var match = CreditHoldemEngine.Deal("match", seats, "table", 42, Start);
        var managed = match.Players.Where(TexasHoldemManagedPlayers.IsManaged).ToArray();

        Assert.Equal(2, managed.Length);
        Assert.NotEqual(
            TexasHoldemManagedPlayers.PrivateActionDueAt(match, managed[0]),
            TexasHoldemManagedPlayers.PrivateActionDueAt(match, managed[1]));
    }

    [Fact]
    public void ManagedPolicyReturnsOnlyAnEngineLegalAction()
    {
        var profiles = TexasHoldemManagedPlayers.CreateLocalProfiles(2, Start);
        var ticket = new CreditHoldemTicket(
            "ticket", "account-player", "seat-account", "Alice", "table", "queued", 1,
            Start, Start, null);
        var seats = TexasHoldemManagedPlayers.BuildSeats(
            [ticket], profiles, new Dictionary<string, long> { [ticket.UserId] = 5_000 }, 74,
            CreditHoldemTableRules.Resolve(CreditHoldemTableRules.StandardId));
        var match = CreditHoldemEngine.Deal("match", seats, "table", 74, Start);
        var active = match.Players.Single(player => player.Seat == match.ActiveSeat);
        if (!TexasHoldemManagedPlayers.IsManaged(active))
        {
            match.ActiveSeat = match.Players.First(TexasHoldemManagedPlayers.IsManaged).Seat;
            active = match.Players.Single(player => player.Seat == match.ActiveSeat);
        }
        var legal = CreditHoldemEngine.LegalActions(match, active);
        var decision = TexasHoldemManagedActionPolicy.Choose(
            match, active, TexasHoldemManagedPlayers.Skill(active));

        Assert.Contains(decision.Action, legal);
    }

    [Fact]
    public void ProjectionDoesNotExposeManagedPlayersPrivateOrFallbackDeadline()
    {
        var profiles = TexasHoldemManagedPlayers.CreateLocalProfiles(2, Start);
        var ticket = new CreditHoldemTicket(
            "ticket", "account-player", "seat-account", "Alice", "table", "queued", 1,
            Start, Start, null);
        var seats = TexasHoldemManagedPlayers.BuildSeats(
            [ticket], profiles, new Dictionary<string, long> { [ticket.UserId] = 5_000 }, 74,
            CreditHoldemTableRules.Resolve(CreditHoldemTableRules.StandardId));
        var match = CreditHoldemEngine.Deal("match", seats, "table", 74, Start);
        match.ActiveSeat = match.Players.First(TexasHoldemManagedPlayers.IsManaged).Seat;

        var table = CreditHoldemProjection.Table(match, ticket.UserId, Start);

        Assert.Null(table.ActionDeadlineAtUtc);
        Assert.Equal(0, table.RemainingActionMilliseconds);
    }

    [Fact]
    public void ManagedPlayers_receive_independent_four_to_twenty_hand_table_sessions()
    {
        var profiles = TexasHoldemManagedPlayers.CreateLocalProfiles(2, Start);
        var ticket = new CreditHoldemTicket(
            "ticket", "account-player", "seat-account", "Alice", "table", "queued", 1,
            Start, Start, null);
        var seats = TexasHoldemManagedPlayers.BuildSeats(
            [ticket], profiles, new Dictionary<string, long> { [ticket.UserId] = 5_000 }, 91,
            CreditHoldemTableRules.Resolve(CreditHoldemTableRules.StandardId), "match-table");
        var match = CreditHoldemEngine.Deal("match-table", seats, "table", 91, Start);
        var managedIds = match.Players.Where(TexasHoldemManagedPlayers.IsManaged)
            .Select(player => player.ActorId).ToArray();

        var departureHands = managedIds.Select(id => Enumerable.Range(1, 21)
            .First(hand =>
            {
                match.HandNumber = hand;
                return TexasHoldemManagedPlayers.DepartingAfterCurrentHand(match)
                    .Any(player => player.ActorId == id);
            })).ToArray();

        Assert.All(departureHands, hand => Assert.InRange(hand, 5, 21));
        match.HandNumber = departureHands.Min();
        var departing = TexasHoldemManagedPlayers.DepartingAfterCurrentHand(match);
        var beforeDeparture = match.Players.Count;
        var removed = TexasHoldemManagedPlayers.RemoveDepartingPlayers(match);
        Assert.Equal(departing.Select(player => player.ActorId).Order(), removed.Order());
        Assert.Equal(beforeDeparture - departing.Count, match.Players.Count);
        Assert.DoesNotContain(match.Players, player =>
            departing.Any(old => old.ActorId == player.ActorId));

        var arrivals = TexasHoldemManagedPlayers.CreateLocalProfiles(1, Start.AddMinutes(1));
        TexasHoldemManagedPlayers.AddPlayers(match, arrivals);
        Assert.Contains(match.Players, player => player.ActorId == arrivals[0].UserId);
    }
}
