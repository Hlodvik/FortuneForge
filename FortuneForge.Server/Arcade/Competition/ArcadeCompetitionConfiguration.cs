using FortuneForge.Games.Arcade.Competition;
using FortuneForge.Games.FreeSolitaire.Server;
using Google.Cloud.Firestore;

namespace FortuneForge.Server.Arcade.Competition;

public sealed record ArcadeCompetitionApiOptions
{
    public IReadOnlyList<string> AllowedGameIds { get; init; } = ["asteroids", "flappy", "horse-flight"];
    public int AllTimeMaximumPlaces { get; init; } = 100;
}

public static class ArcadeCompetitionConfiguration
{
    public static IServiceCollection AddArcadeCompetitions(this IServiceCollection services, IConfiguration configuration)
    {
        services.Configure<ArcadeCompetitionApiOptions>(configuration.GetSection("ArcadeCompetition"));
        services.AddArcadeCompetitionStandings();
        services.AddSingleton<IArcadeCompetitionPayoutCreditor, FirestoreArcadeCompetitionPayoutCreditor>();
        services.AddSingleton(provider => new FirestoreSolitaireFreeRunService(
            provider.GetRequiredService<FirestoreDb>(), provider.GetRequiredService<TimeProvider>()));
        return services;
    }
}
