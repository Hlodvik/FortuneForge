using FortuneForge.Games.SicBo.Server;
using FortuneForge.Games.SicBo;
using FortuneForge.Games.Hosting;

namespace FortuneForge.Games.PackagedGame.Server;

internal static class GameWorkerModule
{
    internal const string GameId = "sic-bo";
    internal static void Configure(WebApplicationBuilder builder)
    {
        builder.Services.AddGameWalletClient(GameId);
        builder.Services.AddSicBoWorker(builder.Configuration);
    }
    internal static void Map(WebApplication app, GameWorkerAccountResolver accountResolver)
    {
        app.MapSicBoWorker(context => accountResolver(context));
        app.MapGameAction<SicBoComputeRequest, SicBoComputeResult>(SicBoCompute.ResolveRoute, SicBoCompute.Resolve);
    }
}
