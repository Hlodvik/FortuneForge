using FortuneForge.Games.DragonTiger.Server;

namespace FortuneForge.Games.PackagedGame.Server;

internal static class GameWorkerModule
{
    internal const string GameId = "dragon-tiger";

    internal static void Configure(WebApplicationBuilder builder) => builder.Services.AddDragonTigerServer();

    internal static void Map(WebApplication app, DemoGameAccount? account) =>
        app.MapDragonTiger(async (_, cancellationToken) => account is null ? null :
            new DragonTigerAccount(account.UserId, await account.ReadWalletBalanceAsync(cancellationToken)));
}
