using FortuneForge.Games.Abstractions;
using FortuneForge.Games.TwentyFortyEight;

namespace FortuneForge.Games.GameCompute.Server;

internal static class GameComputeModule
{
    internal static GameDescriptor Descriptor => TwentyFortyEightModule.Descriptor;

    internal static void Map(WebApplication app)
    {
        app.MapComputeAction(TwentyFortyEightSession.StartComputeRoute,
            (TwentyFortyEightComputeStart request) => TwentyFortyEightEngine.Start(request.Seed));
        app.MapComputeAction(TwentyFortyEightSession.MoveComputeRoute,
            (TwentyFortyEightComputeMove request) => request.Apply());
    }
}
