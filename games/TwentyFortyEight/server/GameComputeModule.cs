using System.Security.Cryptography;
using FortuneForge.Games.Abstractions;
using FortuneForge.Games.TwentyFortyEight;

namespace FortuneForge.Games.GameCompute.Server;

internal static class GameComputeModule
{
    internal static GameDescriptor Descriptor => TwentyFortyEightModule.Descriptor;

    internal static void Map(WebApplication app)
    {
        app.MapPost("/internal/v1/games/start", () => Results.Ok(
            TwentyFortyEightEngine.Start((uint)RandomNumberGenerator.GetInt32(1, int.MaxValue))));
        app.MapComputeAction("/internal/v1/games/move", (MoveRequest request) =>
        {
            ArgumentNullException.ThrowIfNull(request.State);
            if (request.State.Tiles.IsDefault || request.State.Phase != TwentyFortyEightPhase.Playing)
                throw new ArgumentException("A playable 2048 state is required.");
            return TwentyFortyEightEngine.Move(request.State, request.Direction);
        });
    }

    private sealed record MoveRequest(TwentyFortyEightState State, TwentyFortyEightDirection Direction);
}
