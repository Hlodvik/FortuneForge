using FortuneForge.Games.CrookedRoad;
using FortuneForge.Games.CrookedRoad.Server;
using FortuneForge.Games.Hosting;

namespace FortuneForge.Games.PackagedGame.Server;

internal static class GameWorkerModule
{
    internal const string GameId = CrookedRoadRules.GameId;
    internal static void Configure(WebApplicationBuilder builder)
    {
        builder.Services.AddCrookedRoadPaid();
        builder.Services.AddGameWalletClient(GameId);
    }
    internal static void Map(WebApplication app, GameWorkerAccountResolver resolveAccount) =>
        app.MapCrookedRoadPaidRounds(resolveAccount.Invoke);
}
