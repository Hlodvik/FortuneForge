using FortuneForge.Games.Abstractions;
using FortuneForge.Games.Keno;

namespace FortuneForge.Games.GameCompute.Server;

internal static class GameComputeModule
{
    internal static GameDescriptor Descriptor => KenoModule.Descriptor;

    internal static void Map(WebApplication app) =>
        app.MapComputeAction<KenoComputeRequest, KenoComputeResult>(KenoCompute.ResolveRoute, KenoCompute.Resolve);
}
