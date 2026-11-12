using System.Buffers.Binary;
using System.Security.Cryptography;
using FortuneForge.Games.Solitaire;

namespace FortuneForge.Games.GameCompute.Server;

internal static class SolitaireComputeEndpoints
{
    internal static void Map(WebApplication app)
    {
        app.MapComputeAction(SolitaireBoardCompute.StartRoute, (SolitaireBoardStartRequest request) =>
            SolitaireBoardCompute.Snapshot(SolitaireEngine.CreateGame(request.Seed ?? GenerateSeed(), request.DrawCount)));
        app.MapComputeAction(SolitaireBoardCompute.ApplyRoute, (SolitaireBoardApplyRequest request) =>
        {
            ArgumentNullException.ThrowIfNull(request.State);
            ArgumentNullException.ThrowIfNull(request.Command);
            return SolitaireBoardCompute.Snapshot(SolitaireEngine.Apply(request.State, request.Command));
        }, isRuleViolation: error => error is SolitaireIllegalMoveException);
    }

    private static uint GenerateSeed() => BinaryPrimitives.ReadUInt32LittleEndian(RandomNumberGenerator.GetBytes(sizeof(uint)));
}
