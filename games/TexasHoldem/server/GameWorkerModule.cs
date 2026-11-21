using FortuneForge.Games.Abstractions.ManagedPlayers;
using FortuneForge.Games.Hosting;
using FortuneForge.Server.Cards.TexasHoldem.Credit;
using Google.Cloud.Firestore;

namespace FortuneForge.Games.PackagedGame.Server;

internal static class GameWorkerModule
{
    internal const string GameId = CreditHoldemBackend.GameId;
    internal static void Configure(WebApplicationBuilder builder)
    {
        builder.Services.AddGameWalletClient(GameId);
        builder.Services.AddGameWalletBatchClient(GameId);
        builder.Services.AddManagedPlayerRosterClient(GameId);
        builder.Services.AddSingleton<IManagedTablePopulationDirector, ManagedTablePopulationDirector>();
        if (builder.Services.Any(service => service.ServiceType == typeof(FirestoreDb)))
            builder.Services.AddHostedService<CreditHoldemDeadlineWorker>();
    }

    internal static void Map(WebApplication app, GameWorkerAccountResolver resolveAccount)
    {
        app.MapCreditHoldem(resolveAccount.Invoke);
        app.MapCreditHoldemCompute();
    }
}
