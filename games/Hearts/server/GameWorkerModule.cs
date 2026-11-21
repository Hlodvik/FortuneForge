using FortuneForge.Games.Hosting;
using FortuneForge.Server.Games.Hearts;

namespace FortuneForge.Games.PackagedGame.Server;

internal static class GameWorkerModule
{
    internal static string GameId => HeartsBackend.GameId;
    internal static void Configure(WebApplicationBuilder builder)
    {
        builder.Services.AddHeartsFreeBackend();
        builder.Services.AddHeartsPaidBackend();
    }
    internal static void Map(WebApplication app, GameWorkerAccountResolver resolveUserId)
    {
        app.MapHeartsFree(resolveUserId.Invoke);
        app.MapHeartsPaid(resolveUserId.Invoke);
        FortuneForge.Games.GameCompute.Server.GameComputeModule.Map(app);
    }
}
