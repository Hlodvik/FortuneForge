using FortuneForge.Games.Abstractions;
using FortuneForge.Games.Keno;

namespace FortuneForge.Games.GameCompute.Server;

internal static class GameComputeModule
{
    internal static GameDescriptor Descriptor => KenoModule.Descriptor;

    internal static void Map(WebApplication app) =>
        app.MapComputeAction("/internal/v1/rounds/resolve", (KenoComputeRequest request) =>
            KenoRoundEngine.Play(new KenoTicket(request.Ticket), new KenoDraw(request.Draw), StandardKenoPaytable.Instance));

    private sealed record KenoComputeRequest(IReadOnlyList<int> Ticket, IReadOnlyList<int> Draw);
}
