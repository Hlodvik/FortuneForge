using FortuneForge.Server.Games.LiarsDice;

namespace FortuneForge.Server.Bots.LiarsDice;

public sealed record LiarsDiceManagedPlayer(
    string UserId,
    string PlayerName,
    int SkillLevel);

public interface ILiarsDiceManagedPlayerRoster
{
    Task<IReadOnlyList<LiarsDiceManagedPlayer>> ReserveAsync(
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

internal sealed class LiarsDiceManagedPlayerRoster(
    IManagedPlayerQueuer queuer) : ILiarsDiceManagedPlayerRoster
{
    public async Task<IReadOnlyList<LiarsDiceManagedPlayer>> ReserveAsync(
        string assignmentId,
        int count,
        IReadOnlyCollection<string> excludedProfileIds,
        DateTime nowUtc,
        CancellationToken cancellationToken) =>
        (await queuer.ReserveAsync(
            ManagedPlayerGames.LiarsDice,
            assignmentId,
            count,
            excludedProfileIds,
            nowUtc,
            cancellationToken))
        .Select(profile => new LiarsDiceManagedPlayer(
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
            ManagedPlayerGames.LiarsDice,
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

public static class LiarsDiceManagedPlayerConfiguration
{
    public static IServiceCollection AddLiarsDiceManagedPlayers(
        this IServiceCollection services)
    {
        services.AddSingleton<ILiarsDiceManagedPlayerRoster, LiarsDiceManagedPlayerRoster>();
        services.AddHostedService<LiarsDiceManagedTurnWorker>();
        return services;
    }
}

internal sealed class LiarsDiceManagedTurnWorker(
    LiarsDiceGameService games,
    ILogger<LiarsDiceManagedTurnWorker> logger) : BackgroundService
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
                logger.LogError(exception, "A Liar's Dice table could not advance its scheduled turn.");
            }
        }
    }
}
