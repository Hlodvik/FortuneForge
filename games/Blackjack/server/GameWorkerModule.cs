using FortuneForge.Server.Cards.Blackjack;
using FortuneForge.Server.Cards.Blackjack.Table;
using FortuneForge.Games.Hosting;

namespace FortuneForge.Games.PackagedGame.Server;

internal static class GameWorkerModule
{
    internal const string GameId = "blackjack";
    internal static void Configure(WebApplicationBuilder builder)
    {
        builder.Services.AddBlackjackSolo();
        builder.Services.AddBlackjackTableBackend();
    }
    internal static void Map(WebApplication app, GameWorkerAccountResolver resolveUserId)
    {
        app.MapBlackjackSolo(resolveUserId.Invoke);
        app.MapBlackjackTable(resolveUserId.Invoke);
        FortuneForge.Games.GameCompute.Server.GameComputeModule.Map(app);
    }
}
