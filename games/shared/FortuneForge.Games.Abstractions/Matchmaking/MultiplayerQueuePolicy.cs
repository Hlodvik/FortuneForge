namespace FortuneForge.Games.Abstractions.Matchmaking;

public record MultiplayerQueueRules(int MinimumHumanPlayers, int MaximumHumanPlayers)
{
    public void Validate()
    {
        if (MinimumHumanPlayers < 1)
            throw new ArgumentOutOfRangeException(nameof(MinimumHumanPlayers));
        if (MaximumHumanPlayers < MinimumHumanPlayers)
            throw new ArgumentOutOfRangeException(nameof(MaximumHumanPlayers));
    }
}

public record MultiplayerQueuePlan<TTicket>(IReadOnlyList<TTicket> HumanTickets)
{
    public bool IsReady => HumanTickets.Count > 0;

    public int ManagedSeatsRequired(int targetOccupancy) => Math.Max(0, targetOccupancy - HumanTickets.Count);
}

public static class MultiplayerQueuePolicy
{
    public static TTable? FindLiveTable<TTable>(IEnumerable<TTable> tables, Func<TTable, bool> canAcceptHuman,
        Func<TTable, DateTime> createdAtUtc, Func<TTable, string> tableId) where TTable : class =>
        tables.Where(canAcceptHuman)
            .OrderBy(createdAtUtc)
            .ThenBy(tableId, StringComparer.Ordinal)
            .FirstOrDefault();

    public static MultiplayerQueuePlan<TTicket> PlanQueue<TTicket>(IEnumerable<TTicket> tickets, MultiplayerQueueRules rules,
        DateTime nowUtc, Func<TTicket, bool> isEligible, Func<TTicket, DateTime> joinedAtUtc,
        Func<TTicket, DateTime> graceEndsAtUtc, Func<TTicket, string> ticketId)
    {
        rules.Validate();
        var eligible = tickets.Where(isEligible)
            .OrderBy(joinedAtUtc)
            .ThenBy(ticketId, StringComparer.Ordinal)
            .ToArray();
        if (eligible.Length < rules.MinimumHumanPlayers || nowUtc < graceEndsAtUtc(eligible[0]))
            return new([]);
        return new(eligible.Take(rules.MaximumHumanPlayers).ToArray());
    }
}
