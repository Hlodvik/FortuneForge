using FortuneForge.Server.Cards.Blackjack.Bots;
using FortuneForge.Server.Cards.Solitaire.Bots;
using FortuneForge.Server.Cards.TexasHoldem.Bots;
using Google.Cloud.Firestore;
using Microsoft.Extensions.Options;

namespace FortuneForge.Server.Cards.Bots;

public static class CardBotServicesConfiguration
{
    public static IServiceCollection AddCardBotServices(
        this IServiceCollection services,
        IConfiguration configuration)
    {
        services.AddOptions<CardBotPlatformOptions>()
            .Bind(configuration.GetSection(CardBotPlatformOptions.SectionName));
        services.AddSingleton<IPostConfigureOptions<CardBotPlatformOptions>, CardBotOptionsPostConfigure>();
        services.AddSingleton<BotIdentityFactory>();
        services.AddSingleton<IBotTurnLeaseStore>(provider =>
            new FirestoreBotTurnLeaseStore(provider.GetRequiredService<FirestoreDb>()));

        services.AddSingleton<BlackjackBotAgent>();
        services.AddSingleton<BlackjackBotPracticeService>();
        services.AddSingleton<ICardBotGameRunner>(provider =>
            provider.GetRequiredService<BlackjackBotPracticeService>());

        services.AddSingleton<SolitaireBotAgent>();
        services.AddSingleton<SolitaireBotPracticeService>();
        services.AddSingleton<ICardBotGameRunner>(provider =>
            provider.GetRequiredService<SolitaireBotPracticeService>());

        services.AddSingleton<TexasHoldemBotAgent>();
        services.AddSingleton<TexasHoldemBotPracticeService>();
        services.AddSingleton<ICardBotGameRunner>(provider =>
            provider.GetRequiredService<TexasHoldemBotPracticeService>());

        services.AddHostedService<CardBotWorker>();
        return services;
    }

    private sealed class CardBotOptionsPostConfigure : IPostConfigureOptions<CardBotPlatformOptions>
    {
        public void PostConfigure(string? name, CardBotPlatformOptions options) =>
            CardBotOptionValidation.Validate(options);
    }
}

internal sealed class CardBotWorker(
    IEnumerable<ICardBotGameRunner> games,
    IOptions<CardBotPlatformOptions> options,
    ILogger<CardBotWorker> logger) : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        using var timer = new PeriodicTimer(
            TimeSpan.FromMilliseconds(options.Value.WorkerIntervalMilliseconds));
        while (await timer.WaitForNextTickAsync(stoppingToken))
        {
            foreach (var game in games)
            {
                try { await game.SweepAsync(DateTime.UtcNow, stoppingToken); }
                catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested) { return; }
                catch (Exception exception)
                {
                    logger.LogError(exception, "Card bot worker sweep failed for {Game}.", game.Game);
                }
            }
        }
    }
}
