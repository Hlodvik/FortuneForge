using System.Buffers.Binary;
using System.Security.Cryptography;
using FortuneForge.Games.Abstractions;
using FortuneForge.Games.Solitaire;

namespace FortuneForge.Games.GameCompute.Server;

internal static class GameComputeModule
{
    internal static GameDescriptor Descriptor => SolitaireModule.Descriptor;

    internal static void Map(WebApplication app)
    {
        app.MapComputeAction("/internal/v1/boards/start", (StartRequest request) =>
            Snapshot(SolitaireEngine.CreateGame(request.Seed ?? GenerateSeed(), request.DrawCount)));
        app.MapComputeAction("/internal/v1/boards/apply", (ApplyRequest request) =>
        {
            ArgumentNullException.ThrowIfNull(request.State);
            ArgumentNullException.ThrowIfNull(request.Command);
            return Snapshot(SolitaireEngine.Apply(request.State, request.Command));
        }, isRuleViolation: error => error is SolitaireIllegalMoveException);
    }

    private static uint GenerateSeed() => BinaryPrimitives.ReadUInt32LittleEndian(RandomNumberGenerator.GetBytes(sizeof(uint)));

    private static BoardSnapshot Snapshot(SolitaireGameState state) =>
        new(state, SolitaireEngine.ToResponse(state), SolitaireEngine.IsWon(state));

    private sealed record StartRequest(int DrawCount = 3, uint? Seed = null);
    private sealed record ApplyRequest(SolitaireGameState State, SolitaireCommandRequest Command);
    private sealed record BoardSnapshot(SolitaireGameState PrivateState, SolitaireGameResponse PlayerView, bool Won);
}
