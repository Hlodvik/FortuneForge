using FortuneForge.Server.Bots;
using Shared = FortuneForge.Games.Abstractions.Matchmaking;

namespace FortuneForge.Server.Matchmaking;

internal sealed record MultiplayerQueueRules(int MinimumHumanPlayers, int MaximumHumanPlayers)
    : Shared.MultiplayerQueueRules(MinimumHumanPlayers, MaximumHumanPlayers);

internal sealed record MultiplayerQueuePlan<TTicket>(IReadOnlyList<TTicket> HumanTickets)
    : Shared.MultiplayerQueuePlan<TTicket>(HumanTickets);

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

    Task<bool> TryRenewManagedPlayersAsync(
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
        where TTable : class => Shared.MultiplayerQueuePolicy.FindLiveTable(tables, canAcceptHuman, createdAtUtc, tableId);

    public MultiplayerQueuePlan<TTicket> PlanQueue<TTicket>(
        IEnumerable<TTicket> tickets,
        MultiplayerQueueRules rules,
        DateTime nowUtc,
        Func<TTicket, bool> isEligible,
        Func<TTicket, DateTime> joinedAtUtc,
        Func<TTicket, DateTime> graceEndsAtUtc,
        Func<TTicket, string> ticketId)
        => new(Shared.MultiplayerQueuePolicy.PlanQueue(tickets, rules, nowUtc, isEligible, joinedAtUtc, graceEndsAtUtc, ticketId).HumanTickets);

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

    public Task<bool> TryRenewManagedPlayersAsync(
        string gameId,
        string assignmentId,
        IReadOnlyCollection<string> profileIds,
        DateTime nowUtc,
        CancellationToken cancellationToken) =>
        managedPlayerQueuer is null || profileIds.Count == 0
            ? Task.FromResult(true)
            : managedPlayerQueuer.TryRenewAsync(
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
