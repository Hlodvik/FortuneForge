using FortuneForge.Games.Abstractions;
using FortuneForge.Games.CasinoWar;

namespace FortuneForge.Games.GameCompute.Server;

internal static class GameComputeModule
{
    internal static GameDescriptor Descriptor => CasinoWarModule.Descriptor;

    internal static void Map(WebApplication app)
    {
        app.MapComputeAction(CasinoWarCompute.StartRoute, (CasinoWarComputeStartRequest request) => CasinoWarCompute.Start(request));
        app.MapComputeAction(CasinoWarCompute.DecideRoute, (CasinoWarComputeDecisionRequest request) => CasinoWarCompute.Decide(request));
    }
}
