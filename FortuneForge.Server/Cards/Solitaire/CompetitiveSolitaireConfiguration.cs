using FortuneForge.Server.Matchmaking;
using Google.Cloud.Firestore;

namespace FortuneForge.Server.Cards.Solitaire;

public static class CompetitiveSolitaireConfiguration
{
    public static IServiceCollection AddCompetitiveSolitaireQueues(
        this IServiceCollection services,
        IConfiguration configuration)
    {
        services.AddSingleton(new CompetitiveSolitaireOptions
        {
            AllowSingleHumanBotFill = SolitaireController.IsSingleHumanBotFillEnabled(configuration)
        });
        services.AddSingleton(provider => new FirestoreCompetitiveSolitaireStore(
            provider.GetRequiredService<FirestoreDb>(),
            provider.GetRequiredService<CompetitiveSolitaireOptions>(),
            provider.GetRequiredService<IMultiplayerMatchmaker>()));
        services.AddSingleton<ICompetitiveSolitaireStore>(provider =>
            provider.GetRequiredService<FirestoreCompetitiveSolitaireStore>());
        services.AddSingleton<CompetitiveSolitaireService>();
        services.AddHostedService<CompetitiveSolitaireQueueWorker>();
        return services;
    }
}

internal sealed class CompetitiveSolitaireQueueWorker(
    FirestoreCompetitiveSolitaireStore store,
    IConfiguration configuration,
    ILogger<CompetitiveSolitaireQueueWorker> logger) : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        using var timer = new PeriodicTimer(TimeSpan.FromSeconds(5));
        while (await timer.WaitForNextTickAsync(stoppingToken))
        {
            if (!SolitaireController.IsEnabled(configuration)) continue;
            try { await store.SweepAsync(DateTime.UtcNow, stoppingToken); }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested) { return; }
            catch (Exception exception)
            {
                logger.LogError(exception, "Competitive Solitaire queue and deadline worker sweep failed.");
            }
        }
    }
}
