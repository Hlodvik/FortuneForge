using FortuneForge.Games.CasinoWar;
using FortuneForge.Games.Hosting;
using FortuneForge.Server.Cards.CasinoWar;

namespace FortuneForge.Games.PackagedGame.Server;

internal static class GameWorkerModule
{
    internal const string GameId = CasinoWarRouteContract.GameId;

    internal static void Configure(WebApplicationBuilder builder) => builder.Services.AddCasinoWarBackend(builder.Configuration);

    internal static void Map(WebApplication app, GameWorkerAccountResolver resolveAccount)
    {
        app.MapCasinoWar(resolveAccount.Invoke, app.Configuration.GetValue("CasinoWar:AllowNewPaidRounds", false));
        app.MapGameAction(CasinoWarCompute.StartRoute, (CasinoWarComputeStartRequest request) => CasinoWarCompute.Start(request));
        app.MapGameAction(CasinoWarCompute.DecideRoute, (CasinoWarComputeDecisionRequest request) => CasinoWarCompute.Decide(request));
    }
}
