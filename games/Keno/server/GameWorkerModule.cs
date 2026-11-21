using FortuneForge.Games.Keno.Server;
using FortuneForge.Games.Keno;
using FortuneForge.Games.Hosting;

namespace FortuneForge.Games.PackagedGame.Server;

internal static class GameWorkerModule
{
    internal const string GameId = "keno";
    internal static void Configure(WebApplicationBuilder builder)
    {
        builder.Services.AddGameWalletClient(GameId);
        builder.Services.AddKenoWorker(builder.Configuration);
    }
    internal static void Map(WebApplication app, GameWorkerAccountResolver accountResolver)
    {
        app.MapKenoWorker(context => accountResolver(context));
        app.MapGameAction<KenoComputeRequest, KenoComputeResult>(KenoCompute.ResolveRoute, KenoCompute.Resolve);
    }
}
