using FortuneForge.Games.Abstractions;
using FortuneForge.Games.Solitaire;

namespace FortuneForge.Games.GameCompute.Server;

internal static class GameComputeModule
{
    internal static GameDescriptor Descriptor { get; } = SolitaireModule.Descriptor with
    {
        Id = SolitaireBoardCompute.FreeGameId,
        DisplayName = "Solitaire",
        ClientRoute = "/cards/free-solitaire",
        Capabilities = GameCapability.FreePlay | GameCapability.History,
    };

    internal static void Map(WebApplication app)
    {
        SolitaireComputeEndpoints.Map(app);
        app.MapComputeAction<SolitaireBoardReplayRequest, SolitaireBoardReplayResult>(
            SolitaireBoardCompute.ReplayRoute, SolitaireBoardCompute.Replay);
    }
}
