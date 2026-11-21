using FortuneForge.Games.Abstractions.Wallet;
using FortuneForge.Games.DragonTiger.Server;
using FortuneForge.Games.Hosting;

namespace FortuneForge.Games.PackagedGame.Server;

internal static class GameWorkerModule
{
    internal const string GameId = "dragon-tiger";
    internal static void Configure(WebApplicationBuilder builder)
    {
        builder.Services.AddDragonTigerServer();
        builder.Services.AddGameWalletClient(GameId);
    }
    internal static void Map(WebApplication app, GameWorkerAccountResolver resolveUserId) =>
        app.MapDragonTiger(async (context, cancellationToken) =>
        {
            if (resolveUserId(context) is not { } userId) return null;
            var state = await context.RequestServices.GetRequiredService<IGameWalletAccountReader>().GetStateAsync(userId, cancellationToken);
            return new DragonTigerAccount(userId, (state.SlotsCreditsBalanceCents ?? throw new GameWalletUnavailableException()) / 100m);
        });
}
