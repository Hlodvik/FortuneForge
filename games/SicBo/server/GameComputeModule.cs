using FortuneForge.Games.Abstractions;
using FortuneForge.Games.SicBo;

namespace FortuneForge.Games.GameCompute.Server;

internal static class GameComputeModule
{
    internal static GameDescriptor Descriptor => SicBoModule.Descriptor;

    internal static void Map(WebApplication app) =>
        app.MapComputeAction<SicBoComputeRequest, SicBoComputeResult>(SicBoCompute.ResolveRoute, SicBoCompute.Resolve);
}
