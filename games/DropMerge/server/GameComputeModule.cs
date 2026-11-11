using System.Security.Cryptography;
using FortuneForge.Games.Abstractions;
using FortuneForge.Games.DropMerge;

namespace FortuneForge.Games.GameCompute.Server;

internal static class GameComputeModule
{
    internal static GameDescriptor Descriptor => DropMergeModule.Descriptor;

    internal static void Map(WebApplication app)
    {
        app.MapPost("/internal/v1/games/start", () => Results.Ok(
            DropMergeEngine.Start((uint)RandomNumberGenerator.GetInt32(1, int.MaxValue))));
        app.MapComputeAction("/internal/v1/games/drop", (DropRequest request) =>
        {
            ArgumentNullException.ThrowIfNull(request.State);
            if (request.State.Tiles.IsDefault || request.State.Phase != DropMergePhase.Playing)
                throw new ArgumentException("A playable Drop Merge state is required.");
            return DropMergeEngine.Drop(request.State, request.Column);
        });
    }

    private sealed record DropRequest(DropMergeState State, int Column);
}
