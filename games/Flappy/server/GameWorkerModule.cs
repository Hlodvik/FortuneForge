using FortuneForge.Games.Flappy;
using FortuneForge.Games.Flappy.Server;
using FortuneForge.Games.Arcade.Competition;
using FortuneForge.Games.Hosting;
using FortuneForge.Server.Arcade.Competition;
using Google.Cloud.Firestore;

namespace FortuneForge.Games.PackagedGame.Server;

internal static class GameWorkerModule
{
    internal const string GameId = FlappyModule.GameId;
    internal static void Configure(WebApplicationBuilder builder)
    {
        builder.Services.AddArcadeCompetitionWallet(GameId);
        builder.Services.AddArcadeCompetitionStandings();
        builder.Services.AddSingleton<FlappyFreeRunStore>();
        builder.Services.AddSingleton<FlappyPaidRuns>();
        builder.Services.AddSingleton<IArcadeCompetitionRunAdapter<FlappyPaidRun>>(provider => provider.GetRequiredService<FlappyPaidRuns>());
        builder.Services.AddSingleton(provider => new ArcadeCompetitionPaidEntries<FlappyPaidRun>(
            provider.GetRequiredService<FirestoreDb>(), GameId, provider.GetRequiredService<ArcadeCompetitionRulesOptions>(),
            provider.GetRequiredService<IArcadeCompetitionEntryWallet>(), provider.GetRequiredService<FlappyPaidRuns>()));
        builder.Services.AddSingleton<FlappyPaidRunService>();
        builder.Services.AddArcadeCompetitionSettlementScheduler<FlappyPaidRun>(GameId);
    }
    internal static void Map(WebApplication app, GameWorkerAccountResolver resolveUserId)
    {
        var routes = app.MapGroup("").WithMetadata(new GameWorkerPublicRoute(GameId, FlappyPaidRunEndpoints.RoutePrefix, GameId));
        routes.MapFlappyFreeRuns(resolveUserId.Invoke);
        routes.MapFlappyPaidRuns(resolveUserId.Invoke, GameWorkerAuthentication.GetCommandBodySha256);
        routes.MapArcadeCompetitionStandings(GameId);
    }
}
