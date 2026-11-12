using FortuneForge.Games.Abstractions;
using FortuneForge.Games.TwentyFortyEight;

namespace FortuneForge.Games.GameCompute.Server;

internal static class GameComputeModule
{
    internal static GameDescriptor Descriptor => TwentyFortyEightModule.Descriptor;

    internal static void Map(WebApplication app)
    {
        app.MapComputeAction("/internal/v1/games/start", (StartRequest request) =>
            TwentyFortyEightEngine.Start(request.Seed));
        app.MapComputeAction("/internal/v1/games/move", (MoveRequest request) =>
        {
            ArgumentNullException.ThrowIfNull(request.State);
            if (request.State.Tiles.IsDefault)
                throw new ArgumentException("A 2048 board is required.");
            return TwentyFortyEightEngine.Move(request.State, request.Direction);
        });
    }

    private sealed record StartRequest(uint Seed);
    private sealed record MoveRequest(TwentyFortyEightState State, TwentyFortyEightDirection Direction);
}
