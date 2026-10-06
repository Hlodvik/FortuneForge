using Google.Cloud.Firestore;
using Microsoft.Extensions.Options;
using FortuneForge.Server.Bots;
using FortuneForge.Server.Matchmaking;

namespace FortuneForge.Server.Cards.TexasHoldem.Credit;

public static class CreditHoldemConfiguration
{
    public static IServiceCollection AddCreditHoldem(
        this IServiceCollection services,
        IConfiguration configuration)
    {
        services.Configure<CreditHoldemOptions>(
            configuration.GetSection(CreditHoldemOptions.SectionName));
        services.AddSingleton<ICreditHoldemStore>(provider =>
            new FirestoreCreditHoldemStore(
                provider.GetRequiredService<FirestoreDb>(),
                provider.GetRequiredService<IOptions<CreditHoldemOptions>>().Value.AllowSingleHumanBotFill,
                provider.GetRequiredService<IMultiplayerMatchmaker>(),
                provider.GetRequiredService<IManagedTablePopulationDirector>()));
        services.AddSingleton<CreditHoldemService>();
        services.AddHostedService<CreditHoldemWorker>();
        return services;
    }
}

public sealed class CreditHoldemOptions
{
    public const string SectionName = "Cards:CreditTexasHoldem";
    public bool AllowSingleHumanBotFill { get; set; }
}

internal sealed class CreditHoldemWorker(
    ICreditHoldemStore store,
    IConfiguration configuration,
    ILogger<CreditHoldemWorker> logger) : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        using var timer = new PeriodicTimer(TimeSpan.FromMilliseconds(350));
        while (await timer.WaitForNextTickAsync(stoppingToken))
        {
            if (!CreditHoldemController.IsEnabled(configuration)) continue;
            try { await store.SweepAsync(DateTime.UtcNow, stoppingToken); }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested) { return; }
            catch (Exception exception)
            {
                logger.LogError(exception, "Texas Hold'em deadline worker sweep failed.");
            }
        }
    }
}
