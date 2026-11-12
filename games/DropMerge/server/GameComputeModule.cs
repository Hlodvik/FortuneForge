using FortuneForge.Games.Abstractions;
using FortuneForge.Games.DropMerge;

namespace FortuneForge.Games.GameCompute.Server;

internal static class GameComputeModule
{
    internal static GameDescriptor Descriptor => DropMergeModule.Descriptor;

    internal static void Map(WebApplication app)
    {
        app.MapComputeAction("/internal/v1/games/start", (StartRequest request) =>
            DropMergeEngine.Start(request.Seed));
        app.MapComputeAction("/internal/v1/games/drop", (DropRequest request) =>
        {
            ArgumentNullException.ThrowIfNull(request.State);
            if (request.State.Tiles.IsDefault)
                throw new ArgumentException("A Drop Merge board is required.");
            return DropMergeEngine.Drop(request.State, request.Column);
        });
    }

    private sealed record StartRequest(uint Seed);
    private sealed record DropRequest(DropMergeState State, int Column);
}
