using FortuneForge.Server.Bots;

namespace FortuneForge.Server.Matchmaking;

internal sealed record MultiplayerQueueRules(
    int MinimumHumanPlayers,
    int MaximumHumanPlayers)
{
    public void Validate()
    {
        if (MinimumHumanPlayers < 1)
            throw new ArgumentOutOfRangeException(nameof(MinimumHumanPlayers));
        if (MaximumHumanPlayers < MinimumHumanPlayers)
            throw new ArgumentOutOfRangeException(nameof(MaximumHumanPlayers));
    }
}

internal sealed record MultiplayerQueuePlan<TTicket>(
    IReadOnlyList<TTicket> HumanTickets)
{
    public bool IsReady => HumanTickets.Count > 0;

    public int ManagedSeatsRequired(int targetOccupancy) =>
        Math.Max(0, targetOccupancy - HumanTickets.Count);
}

internal interface IMultiplayerMatchmaker
{
    TTable? FindLiveTable<TTable>(
        IEnumerable<TTable> tables,
        Func<TTable, bool> canAcceptHuman,
        Func<TTable, DateTime> createdAtUtc,
        Func<TTable, string> tableId)
        where TTable : class;

    MultiplayerQueuePlan<TTicket> PlanQueue<TTicket>(
        IEnumerable<TTicket> tickets,
        MultiplayerQueueRules rules,
        DateTime nowUtc,
        Func<TTicket, bool> isEligible,
        Func<TTicket, DateTime> joinedAtUtc,
        Func<TTicket, DateTime> graceEndsAtUtc,
        Func<TTicket, string> ticketId);

    Task<IReadOnlyList<ManagedPlayerProfile>> ReserveManagedPlayersAsync(
        string gameId,
        string assignmentId,
        int count,
        IReadOnlyCollection<string> excludedProfileIds,
        DateTime nowUtc,
        CancellationToken cancellationToken,
        Func<int, DateTime, IReadOnlyList<ManagedPlayerProfile>>? createEphemeralPlayers = null);

    Task HeartbeatManagedPlayersAsync(
        string gameId,
        string assignmentId,
        IReadOnlyCollection<string> profileIds,
        DateTime nowUtc,
        CancellationToken cancellationToken);

    Task ReleaseManagedPlayersAsync(
        string assignmentId,
        IReadOnlyCollection<string> profileIds,
        DateTime nowUtc,
        CancellationToken cancellationToken);
}

internal sealed class MultiplayerMatchmaker(
    IManagedPlayerQueuer? managedPlayerQueuer = null) : IMultiplayerMatchmaker
{
    public TTable? FindLiveTable<TTable>(
        IEnumerable<TTable> tables,
        Func<TTable, bool> canAcceptHuman,
        Func<TTable, DateTime> createdAtUtc,
        Func<TTable, string> tableId)
        where TTable : class =>
        tables.Where(canAcceptHuman)
            .OrderBy(createdAtUtc)
            .ThenBy(tableId, StringComparer.Ordinal)
            .FirstOrDefault();

    public MultiplayerQueuePlan<TTicket> PlanQueue<TTicket>(
        IEnumerable<TTicket> tickets,
        MultiplayerQueueRules rules,
        DateTime nowUtc,
        Func<TTicket, bool> isEligible,
        Func<TTicket, DateTime> joinedAtUtc,
        Func<TTicket, DateTime> graceEndsAtUtc,
        Func<TTicket, string> ticketId)
    {
        rules.Validate();
        var eligible = tickets.Where(isEligible)
            .OrderBy(joinedAtUtc)
            .ThenBy(ticketId, StringComparer.Ordinal)
            .ToArray();
        if (eligible.Length < rules.MinimumHumanPlayers ||
            nowUtc < graceEndsAtUtc(eligible[0]))
            return new([]);
        return new(eligible.Take(rules.MaximumHumanPlayers).ToArray());
    }

    private static IReadOnlyList<ManagedPlayerProfile> CreateEphemeralManagedPlayers(
        string gameId,
        int count,
        DateTime nowUtc) =>
        Enumerable.Range(0, Math.Max(0, count))
            .Select(_ => ManagedPlayerIdentityFactory.Create(gameId, nowUtc))
            .ToArray();

    public Task<IReadOnlyList<ManagedPlayerProfile>> ReserveManagedPlayersAsync(
        string gameId,
        string assignmentId,
        int count,
        IReadOnlyCollection<string> excludedProfileIds,
        DateTime nowUtc,
        CancellationToken cancellationToken,
        Func<int, DateTime, IReadOnlyList<ManagedPlayerProfile>>? createEphemeralPlayers = null) =>
        managedPlayerQueuer is null
            ? Task.FromResult(createEphemeralPlayers is null
                ? CreateEphemeralManagedPlayers(gameId, count, nowUtc)
                : createEphemeralPlayers(count, nowUtc))
            : managedPlayerQueuer.ReserveAsync(
                gameId,
                assignmentId,
                count,
                excludedProfileIds,
                nowUtc,
                cancellationToken);

    public Task HeartbeatManagedPlayersAsync(
        string gameId,
        string assignmentId,
        IReadOnlyCollection<string> profileIds,
        DateTime nowUtc,
        CancellationToken cancellationToken) =>
        managedPlayerQueuer is null || profileIds.Count == 0
            ? Task.CompletedTask
            : managedPlayerQueuer.HeartbeatAsync(
                gameId, assignmentId, profileIds, nowUtc, cancellationToken);

    public Task ReleaseManagedPlayersAsync(
        string assignmentId,
        IReadOnlyCollection<string> profileIds,
        DateTime nowUtc,
        CancellationToken cancellationToken) =>
        managedPlayerQueuer is null || profileIds.Count == 0
            ? Task.CompletedTask
            : managedPlayerQueuer.ReleaseAsync(
                assignmentId, profileIds, nowUtc, cancellationToken);
}

public static class MultiplayerMatchmakingConfiguration
{
    public static IServiceCollection AddMultiplayerMatchmaking(this IServiceCollection services)
    {
        services.AddSingleton<IMultiplayerMatchmaker, MultiplayerMatchmaker>();
        return services;
    }
}
