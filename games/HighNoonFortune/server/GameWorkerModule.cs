using FortuneForge.Games.FrontierFortune;
using FortuneForge.Games.FrontierFortune.Server;
using FortuneForge.Games.Hosting;
using FortuneForge.Games.Slots;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace FortuneForge.Games.PackagedGame.Server;

internal static class GameWorkerModule
{
    internal const string GameId = FrontierFortuneEngine.GameId;

    internal static void Configure(WebApplicationBuilder builder)
    {
        builder.Services.TryAddSingleton(TimeProvider.System);
        builder.Services.TryAddSingleton<IGameRandomSource, FrontierCryptographicRandom>();
        builder.Services.AddSingleton<IFrontierRoundStore, FrontierRoundStore>();
        builder.Services.AddSingleton<FrontierRoundService>();
        builder.Services.AddGameWalletClient(GameId);
    }

    internal static void Map(WebApplication app, GameWorkerAccountResolver resolveUserId)
    {
        app.MapFrontierPaidRounds(resolveUserId.Invoke);
    }
}
