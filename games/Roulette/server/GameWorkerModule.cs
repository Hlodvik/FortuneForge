using FortuneForge.Games.Hosting;
using FortuneForge.Server.Games.Roulette;
namespace FortuneForge.Games.PackagedGame.Server;
internal static class GameWorkerModule
{
    internal const string GameId = "roulette";
    internal static void Configure(WebApplicationBuilder builder)
    {
        builder.Services.AddGameWalletClient(GameId);
        builder.Services.AddManagedPlayerRosterClient(GameId);
        builder.Services.AddRouletteServer();
    }
    internal static void Map(WebApplication app, GameWorkerAccountResolver resolveUserId) => app.MapRoulette(context => resolveUserId(context));
}
