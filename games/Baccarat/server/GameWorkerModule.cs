using FortuneForge.Games.Hosting;
using FortuneForge.Server.Cards.Baccarat;

namespace FortuneForge.Games.PackagedGame.Server;

internal static class GameWorkerModule
{
    internal const string GameId = "baccarat";
    internal static void Configure(WebApplicationBuilder builder)
    {
        builder.Services.AddGameWalletClient(GameId);
        builder.Services.AddBaccaratServer();
    }
    internal static void Map(WebApplication app, GameWorkerAccountResolver resolveUserId) => app.MapBaccarat(context => resolveUserId(context));
}
