using FortuneForge.Server.Matchmaking;
using Xunit;

namespace FortuneForge.Server.Tests.Matchmaking;

public sealed class MultiplayerMatchmakerTests
{
    private static readonly DateTime Start =
        new(2026, 10, 3, 12, 0, 0, DateTimeKind.Utc);

    [Fact]
    public void PlanQueue_WaitsForHumanGraceBeforeRequestingManagedSeats()
    {
        var matchmaker = new MultiplayerMatchmaker();
        var tickets = new[]
        {
            new Ticket("first", Start, Start.AddSeconds(5))
        };

        var plan = Plan(matchmaker, tickets, Start.AddSeconds(4));

        Assert.False(plan.IsReady);
        Assert.Empty(plan.HumanTickets);
    }

    [Fact]
    public void PlanQueue_UsesEveryWaitingHumanBeforeManagedSeats()
    {
        var matchmaker = new MultiplayerMatchmaker();
        var tickets = new[]
        {
            new Ticket("third", Start.AddMilliseconds(2), Start.AddSeconds(5)),
            new Ticket("first", Start, Start.AddSeconds(5)),
            new Ticket("second", Start.AddMilliseconds(1), Start.AddSeconds(5))
        };

        var plan = Plan(matchmaker, tickets, Start.AddSeconds(5));

        Assert.Equal(["first", "second", "third"],
            plan.HumanTickets.Select(ticket => ticket.Id));
        Assert.Equal(0, plan.ManagedSeatsRequired(2));
    }

    [Fact]
    public void FindLiveTable_ChoosesOldestCompatibleTable()
    {
        var matchmaker = new MultiplayerMatchmaker();
        var tables = new[]
        {
            new Table("later", Start.AddMinutes(1), true),
            new Table("closed", Start.AddMinutes(-1), false),
            new Table("first", Start, true)
        };

        var table = matchmaker.FindLiveTable(
            tables,
            value => value.AcceptingHumans,
            value => value.CreatedAtUtc,
            value => value.Id);

        Assert.Equal("first", table?.Id);
    }

    private static MultiplayerQueuePlan<Ticket> Plan(
        IMultiplayerMatchmaker matchmaker,
        IEnumerable<Ticket> tickets,
        DateTime nowUtc) =>
        matchmaker.PlanQueue(
            tickets,
            new MultiplayerQueueRules(1, 5),
            nowUtc,
            _ => true,
            ticket => ticket.JoinedAtUtc,
            ticket => ticket.GraceEndsAtUtc,
            ticket => ticket.Id);

    private sealed record Ticket(string Id, DateTime JoinedAtUtc, DateTime GraceEndsAtUtc);
    private sealed record Table(string Id, DateTime CreatedAtUtc, bool AcceptingHumans);
}
