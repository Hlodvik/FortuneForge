using FortuneForge.Games.Asteroids.Server;
using FortuneForge.Games.Asteroids;
using FortuneForge.Games.Arcade.Competition;
using FortuneForge.Games.Hosting;
using FortuneForge.Server.Arcade.Competition;
using Google.Cloud.Firestore;

namespace FortuneForge.Games.PackagedGame.Server;

internal static class GameWorkerModule
{
    internal const string GameId = AsteroidsModule.GameId;

    internal static void Configure(WebApplicationBuilder builder)
    {
        builder.Services.AddArcadeCompetitionWallet(GameId);
        builder.Services.AddArcadeCompetitionStandings();
        builder.Services.AddSingleton(provider =>
        {
            var database = provider.GetRequiredService<FirestoreDb>();
            return new AsteroidsFreeRunStore(database, provider.GetRequiredService<TimeProvider>());
        });
        builder.Services.AddSingleton<AsteroidsPaidRuns>();
        builder.Services.AddSingleton<IArcadeCompetitionRunAdapter<AsteroidsPaidRun>>(provider => provider.GetRequiredService<AsteroidsPaidRuns>());
        builder.Services.AddSingleton(provider => new ArcadeCompetitionPaidEntries<AsteroidsPaidRun>(
            provider.GetRequiredService<FirestoreDb>(), GameId, provider.GetRequiredService<ArcadeCompetitionRulesOptions>(),
            provider.GetRequiredService<IArcadeCompetitionEntryWallet>(), provider.GetRequiredService<AsteroidsPaidRuns>()));
        builder.Services.AddSingleton<AsteroidsPaidRunService>();
        builder.Services.AddArcadeCompetitionSettlementScheduler<AsteroidsPaidRun>(GameId);
    }

    internal static void Map(WebApplication app, GameWorkerAccountResolver resolveUserId)
    {
        var routes = app.MapGroup("").WithMetadata(new GameWorkerPublicRoute(
            GameId, AsteroidsPaidRunEndpoints.RoutePrefix, GameId));
        routes.MapAsteroidsFreeRuns(resolveUserId.Invoke);
        routes.MapAsteroidsPaidRuns(resolveUserId.Invoke, GameWorkerAuthentication.GetCommandBodySha256);
        routes.MapArcadeCompetitionStandings(GameId);
    }
}
