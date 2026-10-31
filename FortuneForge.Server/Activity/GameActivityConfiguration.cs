using Microsoft.Extensions.DependencyInjection.Extensions;

namespace FortuneForge.Server.Activity;

public static class GameActivityConfiguration
{
    public static IServiceCollection AddGameActivity(this IServiceCollection services)
    {
        services.TryAddSingleton(TimeProvider.System);
        services.AddSingleton<FirestoreGameAnalyticsStore>()
            .AddSingleton<IGameAnalyticsReader>(provider => provider.GetRequiredService<FirestoreGameAnalyticsStore>())
            .AddSingleton<GameAnalyticsSink>()
            .AddSingleton<IGameAnalyticsSink>(provider => provider.GetRequiredService<GameAnalyticsSink>())
            .AddHostedService<GameAnalyticsWorker>();
        return services.AddSingleton<IGameActivityAccountResolver, GameActivityAccountResolver>();
    }
}
