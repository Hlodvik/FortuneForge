using Google.Cloud.Firestore;
using FortuneForge.Server.Bots;
using FortuneForge.Server.Matchmaking;

namespace FortuneForge.Server.Cards.ThreeCardPoker.Table;

public static class ThreeCardPokerTableConfiguration
{
    public static IServiceCollection AddThreeCardPokerTables(this IServiceCollection services)
    {
        services.AddSingleton<IThreeCardPokerTableStore>(provider =>
            new FirestoreThreeCardPokerTableStore(
                provider.GetRequiredService<FirestoreDb>(),
                null,
                null,
                provider.GetRequiredService<IMultiplayerMatchmaker>(),
                provider.GetRequiredService<IManagedTablePopulationDirector>()));
        services.AddSingleton<ThreeCardPokerTableService>();
        services.AddHostedService<ThreeCardPokerTableWorker>();
        return services;
    }
}

internal sealed class ThreeCardPokerTableWorker(
    IThreeCardPokerTableStore store,
    IConfiguration configuration,
    ILogger<ThreeCardPokerTableWorker> logger) : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        using var timer = new PeriodicTimer(TimeSpan.FromMilliseconds(500));
        while (await timer.WaitForNextTickAsync(stoppingToken))
        {
            if (!ThreeCardPokerTableController.IsEnabled(configuration)) continue;
            try { await store.SweepAsync(DateTime.UtcNow, stoppingToken); }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested) { return; }
            catch (Exception exception)
            {
                logger.LogError(exception, "Three Card Poker table deadline worker sweep failed.");
            }
        }
    }
}
