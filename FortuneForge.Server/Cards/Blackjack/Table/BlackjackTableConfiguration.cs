using Google.Cloud.Firestore;

namespace FortuneForge.Server.Cards.Blackjack.Table;

public static class BlackjackTableConfiguration
{
    public static IServiceCollection AddBlackjackTables(this IServiceCollection services)
    {
        services.AddSingleton<IBlackjackTableStore>(provider =>
            new FirestoreBlackjackTableStore(provider.GetRequiredService<FirestoreDb>()));
        services.AddSingleton<BlackjackTableService>();
        services.AddHostedService<BlackjackTableWorker>();
        return services;
    }
}

internal sealed class BlackjackTableWorker(
    IBlackjackTableStore store,
    IConfiguration configuration,
    ILogger<BlackjackTableWorker> logger) : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        using var timer = new PeriodicTimer(TimeSpan.FromMilliseconds(500));
        while (await timer.WaitForNextTickAsync(stoppingToken))
        {
            if (!BlackjackTableController.IsEnabled(configuration)) continue;
            try { await store.SweepAsync(DateTime.UtcNow, stoppingToken); }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested) { return; }
            catch (Exception exception)
            {
                logger.LogError(exception, "Blackjack table deadline worker sweep failed.");
            }
        }
    }
}
