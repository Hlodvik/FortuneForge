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
    IManagedPlayerQueuer queuer) : IHeartsManagedPlayerRoster
{
    public async Task<IReadOnlyList<HeartsManagedPlayer>> ReserveAsync(
        string assignmentId,
        int count,
        IReadOnlyCollection<string> excludedProfileIds,
        DateTime nowUtc,
        CancellationToken cancellationToken) =>
        (await queuer.ReserveAsync(
            ManagedPlayerGames.Hearts,
            assignmentId,
            count,
            excludedProfileIds,
            nowUtc,
            cancellationToken))
        .Select(profile => new HeartsManagedPlayer(
            profile.UserId,
            profile.PlayerName,
            profile.SkillLevel))
        .ToArray();

    public Task HeartbeatAsync(
        string assignmentId,
        IReadOnlyCollection<string> profileIds,
        DateTime nowUtc,
        CancellationToken cancellationToken) =>
        queuer.HeartbeatAsync(
            ManagedPlayerGames.Hearts,
            assignmentId,
            profileIds,
            nowUtc,
            cancellationToken);

    public Task ReleaseAsync(
        string assignmentId,
        IReadOnlyCollection<string> profileIds,
        DateTime nowUtc,
        CancellationToken cancellationToken) =>
        queuer.ReleaseAsync(assignmentId, profileIds, nowUtc, cancellationToken);
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
