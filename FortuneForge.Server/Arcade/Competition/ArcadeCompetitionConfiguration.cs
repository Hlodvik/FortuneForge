using Microsoft.Extensions.Options;
using FortuneForge.Server.Arcade.Asteroids;
using FortuneForge.Server.Arcade.Flappy;
using FortuneForge.Server.Cards.Solitaire;

namespace FortuneForge.Server.Arcade.Competition;

public sealed record ArcadeCompetitionApiOptions
{
    public IReadOnlyList<string> AllowedGameIds { get; init; } = ["asteroids"];
    public int AllTimeMaximumPlaces { get; init; } = 100;
}

public static class ArcadeCompetitionConfiguration
{
    public static IServiceCollection AddArcadeCompetitions(this IServiceCollection services, IConfiguration configuration)
    {
        services.Configure<ArcadeCompetitionApiOptions>(configuration.GetSection("ArcadeCompetition"));
        services.Configure<ArcadeCompetitionSettlementWorkerOptions>(
            configuration.GetSection(ArcadeCompetitionSettlementWorkerOptions.SectionName));
        services.AddSingleton<IArcadeCompetitionStore, FirestoreArcadeCompetitionStore>();
        services.AddSingleton<ArcadeCompetitionRulesOptions>();
        services.AddSingleton<IArcadeCompetitionPaidEntryCoordinator>(provider =>
            new FirestoreArcadeCompetitionPaidEntryCoordinator(
                provider.GetRequiredService<Google.Cloud.Firestore.FirestoreDb>(),
                provider.GetRequiredService<ArcadeCompetitionRulesOptions>()));
        services.AddSingleton<ArcadeCompetitionPaidEntryService>(provider => new ArcadeCompetitionPaidEntryService(
            provider.GetRequiredService<IArcadeCompetitionPaidEntryCoordinator>(),
            provider.GetRequiredService<TimeProvider>(),
            provider.GetRequiredService<ArcadeCompetitionRulesOptions>()));
        services.AddSingleton<ArcadeCompetitionAsteroidsPaidEntryService>(provider => new ArcadeCompetitionAsteroidsPaidEntryService(
            provider.GetRequiredService<IArcadeCompetitionPaidEntryCoordinator>(),
            provider.GetRequiredService<TimeProvider>(),
            provider.GetRequiredService<ArcadeCompetitionRulesOptions>()));
        services.AddSingleton<TimeProvider>(TimeProvider.System);
        services.AddSingleton(provider => new FirestoreAsteroidsFreeRunService(
            provider.GetRequiredService<Google.Cloud.Firestore.FirestoreDb>(),
            provider.GetRequiredService<TimeProvider>()));
        services.AddSingleton(provider => new FirestoreFlappyFreeRunService(
            provider.GetRequiredService<Google.Cloud.Firestore.FirestoreDb>(),
            provider.GetRequiredService<TimeProvider>()));
        services.AddSingleton(provider => new FirestoreSolitaireFreeRunService(
            provider.GetRequiredService<Google.Cloud.Firestore.FirestoreDb>(),
            provider.GetRequiredService<TimeProvider>()));
        services.AddSingleton(provider => new ArcadeCompetitionService(
            provider.GetRequiredService<IArcadeCompetitionStore>(),
            provider.GetRequiredService<TimeProvider>(),
            provider.GetRequiredService<ArcadeCompetitionRulesOptions>()));
        services.AddSingleton(provider => new ArcadeCompetitionSettlementPlanningService(
            provider.GetRequiredService<IArcadeCompetitionStore>(),
            provider.GetRequiredService<TimeProvider>(),
            provider.GetRequiredService<ArcadeCompetitionRulesOptions>()));
        services.AddSingleton<IArcadeCompetitionPayoutCreditor>(provider =>
            new FirestoreArcadeCompetitionPayoutCreditor(
                provider.GetRequiredService<Google.Cloud.Firestore.FirestoreDb>()));
        services.AddSingleton<ArcadeCompetitionSettlementExecutor>();
        services.AddSingleton<IArcadeCompetitionSettlementExecutor>(provider =>
            provider.GetRequiredService<ArcadeCompetitionSettlementExecutor>());
        services.AddHostedService<ArcadeCompetitionSettlementWorker>();
        return services;
    }
}
