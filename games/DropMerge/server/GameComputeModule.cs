using FortuneForge.Games.Abstractions;
using FortuneForge.Games.DropMerge;

namespace FortuneForge.Games.GameCompute.Server;

internal static class GameComputeModule
{
    internal static GameDescriptor Descriptor => DropMergeModule.Descriptor;

    internal static void Map(WebApplication app)
    {
        app.MapComputeAction(DropMergeSession.StartComputeRoute, (DropMergeComputeStart request) => DropMergeEngine.Start(request.Seed));
        app.MapComputeAction(DropMergeSession.DropComputeRoute, (DropMergeComputeDrop request) => request.Apply());
    }
}
