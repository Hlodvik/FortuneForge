using FortuneForge.Games.Craps.Server;
using FortuneForge.Games.Hosting;

namespace FortuneForge.Games.PackagedGame.Server;

internal static class GameWorkerModule
{
    internal const string GameId = "craps";

    internal static void Configure(WebApplicationBuilder builder)
    {
        builder.Services.AddGameWalletClient(GameId);
        builder.Services.AddCrapsWorker(builder.Configuration);
    }

    internal static void Map(WebApplication app, GameWorkerAccountResolver resolveUserId) =>
        app.MapCrapsWorker(resolveUserId.Invoke);
}
