using FortuneForge.Games.Abstractions;
using FortuneForge.Games.Baccarat;

namespace FortuneForge.Games.GameCompute.Server;

internal static class GameComputeModule
{
    internal static GameDescriptor Descriptor => BaccaratModule.Descriptor;

    internal static void Map(WebApplication app) =>
        app.MapComputeAction(BaccaratCompute.ResolveRoute, (BaccaratComputeRequest request) => BaccaratCompute.Resolve(request));
}
