using FortuneForge.Games.Abstractions.Wallet;
using FortuneForge.Games.Hosting;
using Google.Cloud.Firestore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace FortuneForge.Server.Cards.Solitaire;

public static class CompetitiveSolitaireBackendRegistration
{
    public static IServiceCollection AddCompetitiveSolitaireBackend(this IServiceCollection services, IConfiguration configuration)
    {
        services.TryAddSingleton(TimeProvider.System);
        services.AddGameWalletClient(CompetitiveSolitaireEndpoints.GameId);
        services.AddGameWalletBatchClient(CompetitiveSolitaireEndpoints.GameId);
        services.AddManagedPlayerRosterClient(CompetitiveSolitaireEndpoints.GameId);
        services.AddSingleton(new CompetitiveSolitaireOptions
        {
            AllowSingleHumanBotFill = configuration.GetValue("Cards:CompetitiveSolitaire:AllowSingleHumanBotFill", false),
            AllowNewPaidRounds = configuration.GetValue("Solitaire:AllowNewPaidRounds", false),
        });
        services.AddSingleton(provider => new FirestoreCompetitiveSolitaireStore(provider.GetRequiredService<FirestoreDb>(),
            provider.GetRequiredService<IGameWalletBatchClient>(), provider.GetRequiredService<IGameWalletAccountReader>(),
            provider.GetRequiredService<IManagedPlayerRosterClient>(), provider.GetRequiredService<CompetitiveSolitaireOptions>(),
            provider.GetRequiredService<ILogger<FirestoreCompetitiveSolitaireStore>>()));
        services.AddSingleton<ICompetitiveSolitaireStore>(provider => provider.GetRequiredService<FirestoreCompetitiveSolitaireStore>());
        services.AddSingleton<CompetitiveSolitaireService>();
        services.AddHostedService<CompetitiveSolitaireQueueWorker>();
        return services;
    }
}

internal sealed class CompetitiveSolitaireQueueWorker(FirestoreCompetitiveSolitaireStore store,
    ILogger<CompetitiveSolitaireQueueWorker> logger) : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        using var timer = new PeriodicTimer(TimeSpan.FromSeconds(5));
        while (await timer.WaitForNextTickAsync(stoppingToken))
        {
            try { await store.SweepAsync(DateTime.UtcNow, stoppingToken); }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested) { return; }
            catch (Exception exception) { logger.LogWarning(exception, "Competitive Solitaire recovery and deadline sweep failed."); }
        }
    }
}
