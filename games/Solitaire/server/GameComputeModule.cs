using FortuneForge.Games.Abstractions;
using FortuneForge.Games.Solitaire;

namespace FortuneForge.Games.GameCompute.Server;

internal static class GameComputeModule
{
    internal static GameDescriptor Descriptor => SolitaireModule.Descriptor;

    internal static void Map(WebApplication app)
    {
        SolitaireComputeEndpoints.Map(app);
        app.MapComputeAction<SolitaireBoardBatchRequest, SolitaireBoardBatchResult>(
            SolitaireBoardCompute.ApplyBatchesRoute, SolitaireBoardCompute.ApplyBatches);
    }
}
