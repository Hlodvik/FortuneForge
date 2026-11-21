using FortuneForge.Server.Arcade.Competition;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace FortuneForge.Games.Arcade.Competition;

public static class ArcadeCompetitionRuntimeConfiguration
{
    public static IServiceCollection AddArcadeCompetitionStandings(this IServiceCollection services)
    {
        services.TryAddSingleton<TimeProvider>(TimeProvider.System);
        services.TryAddSingleton<ArcadeCompetitionRulesOptions>();
        services.TryAddSingleton<IArcadeCompetitionStore, FirestoreArcadeCompetitionStore>();
        services.TryAddSingleton<ArcadeCompetitionService>();
        return services;
    }

    public static IServiceCollection AddArcadeCompetitionSettlementScheduler<TRun>(this IServiceCollection services,
        string gameId, TimeSpan? interval = null) where TRun : class
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(gameId);
        var pollingInterval = interval ?? TimeSpan.FromSeconds(10);
        ArgumentOutOfRangeException.ThrowIfLessThanOrEqual(pollingInterval, TimeSpan.Zero);
        services.AddArcadeCompetitionStandings();
        services.TryAddSingleton<ArcadeCompetitionSettlementPlanningService>();
        services.TryAddSingleton<IArcadeCompetitionSettlementExecutor, ArcadeCompetitionSettlementExecutor>();
        services.AddHostedService(provider => new ArcadeCompetitionSettlementScheduler<TRun>(gameId, pollingInterval,
            provider.GetRequiredService<ArcadeCompetitionPaidEntries<TRun>>(),
            provider.GetRequiredService<IArcadeCompetitionStore>(),
            provider.GetRequiredService<IArcadeCompetitionSettlementExecutor>(),
            provider.GetRequiredService<TimeProvider>(),
            provider.GetRequiredService<ILogger<ArcadeCompetitionSettlementScheduler<TRun>>>()));
        return services;
    }
}

internal sealed class ArcadeCompetitionSettlementScheduler<TRun>(string gameId, TimeSpan interval,
    ArcadeCompetitionPaidEntries<TRun> entries, IArcadeCompetitionStore store, IArcadeCompetitionSettlementExecutor executor,
    TimeProvider time, ILogger<ArcadeCompetitionSettlementScheduler<TRun>> logger) : BackgroundService where TRun : class
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        using var timer = new PeriodicTimer(interval);
        try
        {
            do { await ProcessAsync(stoppingToken); }
            while (await timer.WaitForNextTickAsync(stoppingToken));
        }
        catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested) { }
    }

    private async Task ProcessAsync(CancellationToken token)
    {
        try { _ = await entries.RecoverPendingAsync(token); }
        catch (Exception exception) when (!token.IsCancellationRequested)
        {
            logger.LogWarning(exception, "An arcade entry for {GameId} remains pending", gameId);
        }

        IReadOnlyList<ArcadeCompetitionIdentity> due;
        try { due = await store.LoadDueSettlementCompetitionsAsync(time.GetUtcNow(), token); }
        catch (Exception exception) when (!token.IsCancellationRequested)
        {
            logger.LogWarning(exception, "Arcade settlement discovery failed for {GameId}", gameId);
            return;
        }
        foreach (var competition in due.Where(competition => competition.GameId == gameId))
        {
            try { _ = await executor.ExecuteAsync(competition, token); }
            catch (ArcadeCompetitionEntryPendingException) { }
            catch (Exception exception) when (!token.IsCancellationRequested)
            {
                logger.LogWarning(exception, "Arcade settlement remains pending for {CompetitionId}", competition.DocumentId);
            }
        }
    }
}
