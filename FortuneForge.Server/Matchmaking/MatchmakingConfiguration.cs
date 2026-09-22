using FortuneForge.Server.Matchmaking.QueueBotScheduling;

namespace FortuneForge.Server.Matchmaking;

public static class MatchmakingConfiguration
{
    public static IServiceCollection AddMatchmaking(this IServiceCollection services)
    {
        services.AddSingleton<IQueueStatisticsEvaluator, InMemoryQueueStatisticsEvaluator>();
        services.AddSingleton<IQueueBotFillScheduler, QueueBotFillScheduler>();
        return services;
    }
}
