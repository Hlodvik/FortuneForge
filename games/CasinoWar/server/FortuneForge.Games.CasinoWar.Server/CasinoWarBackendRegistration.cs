using FortuneForge.Games.Abstractions.Wallet;
using FortuneForge.Games.Hosting;
using Google.Cloud.Firestore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace FortuneForge.Server.Cards.CasinoWar;

public static class CasinoWarBackendRegistration
{
    public static IServiceCollection AddCasinoWarBackend(this IServiceCollection services, IConfiguration configuration)
    {
        services.TryAddSingleton(TimeProvider.System);
        services.AddGameWalletClient(CasinoWarRouteContract.GameId);
        services.AddSingleton(provider => new CasinoWarFirestoreStore(provider.GetRequiredService<FirestoreDb>(),
            provider.GetRequiredService<IGameWalletClient>(), provider.GetRequiredService<IGameWalletAccountReader>(),
            configuration.GetValue("CasinoWar:AllowNewPaidRounds", false)));
        services.AddSingleton<ICasinoWarStore>(provider => provider.GetRequiredService<CasinoWarFirestoreStore>());
        services.AddSingleton<CasinoWarService>();
        return services;
    }
}
