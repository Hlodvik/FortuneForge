using FortuneForge.Games.HorseFlight;
using FortuneForge.Games.HorseFlight.Server;
using FortuneForge.Games.Arcade.Competition;
using FortuneForge.Games.Hosting;
using FortuneForge.Server.Arcade.Competition;
using Google.Cloud.Firestore;

namespace FortuneForge.Games.PackagedGame.Server;

internal static class GameWorkerModule
{
    internal const string GameId = HorseFlightModule.GameId;
    internal static void Configure(WebApplicationBuilder builder)
    {
        builder.WebHost.ConfigureKestrel(options => options.Limits.MaxRequestBodySize = CompleteHorseFlightRunRequest.MaximumBodyBytes);
        builder.Services.AddArcadeCompetitionWallet(GameId);
        builder.Services.AddArcadeCompetitionStandings();
        builder.Services.AddGameWalletClient(GameId);
        builder.Services.AddSingleton<HorseFlightFreeRunStore>();
        builder.Services.AddSingleton<HorseFlightPaidRuns>();
        builder.Services.AddSingleton<IArcadeCompetitionRunAdapter<HorseFlightPaidRun>>(provider => provider.GetRequiredService<HorseFlightPaidRuns>());
        builder.Services.AddSingleton(provider => new ArcadeCompetitionPaidEntries<HorseFlightPaidRun>(
            provider.GetRequiredService<FirestoreDb>(), GameId, provider.GetRequiredService<ArcadeCompetitionRulesOptions>(),
            provider.GetRequiredService<IArcadeCompetitionEntryWallet>(), provider.GetRequiredService<HorseFlightPaidRuns>()));
        builder.Services.AddSingleton<HorseFlightPaidRunService>();
        builder.Services.AddArcadeCompetitionSettlementScheduler<HorseFlightPaidRun>(GameId);
    }
    internal static void Map(WebApplication app, GameWorkerAccountResolver resolveUserId)
    {
        var routes = app.MapGroup("").WithMetadata(new GameWorkerPublicRoute(GameId, HorseFlightPaidRunEndpoints.RoutePrefix, GameId));
        bool Enabled() => app.Configuration.GetValue("Features:HorseFlightEnabled", false);
        routes.MapHorseFlightFreeRuns(resolveUserId.Invoke, Enabled);
        routes.MapHorseFlightPaidRuns(resolveUserId.Invoke, Enabled, GameWorkerAuthentication.GetCommandBodySha256);
        routes.MapArcadeCompetitionStandings(GameId);
    }
}
