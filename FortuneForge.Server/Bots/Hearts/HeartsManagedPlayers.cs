using FortuneForge.Games.TrickTaking;
using FortuneForge.Server.Admin.Management;
using FortuneForge.Server.Games.Hearts;

namespace FortuneForge.Server.Bots.Hearts;

public sealed record HeartsManagedPlayer(
    string UserId,
    string PlayerName,
    int SkillLevel);

public interface IHeartsManagedPlayerRoster
{
    Task<IReadOnlyList<HeartsManagedPlayer>> ReserveAsync(
        string assignmentId,
        int count,
        IReadOnlyCollection<string> excludedProfileIds,
        DateTime nowUtc,
        CancellationToken cancellationToken);

    Task HeartbeatAsync(
        string assignmentId,
        IReadOnlyCollection<string> profileIds,
        DateTime nowUtc,
        CancellationToken cancellationToken);

    Task ReleaseAsync(
        string assignmentId,
        IReadOnlyCollection<string> profileIds,
        DateTime nowUtc,
        CancellationToken cancellationToken);
}

internal sealed class HeartsManagedPlayerRoster(
    IManagedPlayerAssignmentStore assignments) : IHeartsManagedPlayerRoster
{
    public async Task<IReadOnlyList<HeartsManagedPlayer>> ReserveAsync(
        string assignmentId,
        int count,
        IReadOnlyCollection<string> excludedProfileIds,
        DateTime nowUtc,
        CancellationToken cancellationToken)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(assignmentId);
        if (count is < 0 or > 3) throw new ArgumentOutOfRangeException(nameof(count));
        cancellationToken.ThrowIfCancellationRequested();
        if (count == 0) return [];
        // House seats are not shared human-looking profiles. The administrator
        // can still pause new computer opponents without interrupting active play.
        if (assignments is IManagedPlayerAdmissionPolicy policy &&
            !await policy.AllowsNewAssignmentsAsync(ManagedPlayerGames.Hearts, cancellationToken))
            return [];
        return new[] { PlayerSeat.East, PlayerSeat.South, PlayerSeat.West }
            .Take(count)
            .Select(seat => HeartsHousePlayers.Create(assignmentId, seat))
            .ToArray();
    }

    public Task HeartbeatAsync(
        string assignmentId,
        IReadOnlyCollection<string> profileIds,
        DateTime nowUtc,
        CancellationToken cancellationToken) => Task.CompletedTask;

    public Task ReleaseAsync(
        string assignmentId,
        IReadOnlyCollection<string> profileIds,
        DateTime nowUtc,
        CancellationToken cancellationToken) => Task.CompletedTask;
}

public static class HeartsManagedPlayerConfiguration
{
    public static IServiceCollection AddHeartsManagedPlayers(
        this IServiceCollection services)
    {
        services.AddSingleton<IHeartsManagedPlayerRoster, HeartsManagedPlayerRoster>();
        services.AddHostedService<HeartsManagedTurnWorker>();
        return services;
    }
}

internal sealed class HeartsManagedTurnWorker(
    HeartsGameService games,
    ILogger<HeartsManagedTurnWorker> logger) : BackgroundService
{
    private static readonly TimeSpan SweepInterval = TimeSpan.FromMilliseconds(100);

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        using var timer = new PeriodicTimer(SweepInterval);
        while (await timer.WaitForNextTickAsync(stoppingToken))
        {
            try
            {
                await games.AdvanceDueManagedTurnsAsync(stoppingToken);
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                return;
            }
            catch (Exception exception)
            {
                logger.LogError(exception, "A Hearts table could not advance its scheduled turn.");
            }
        }
    }
}
