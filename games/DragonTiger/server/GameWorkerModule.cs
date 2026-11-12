using Google.Cloud.Firestore;
using FortuneForge.Games.DragonTiger.Server;

namespace FortuneForge.Games.PackagedGame.Server;

internal static class GameWorkerModule
{
    internal const string GameId = "dragon-tiger";

    internal static void Configure(WebApplicationBuilder builder) => builder.Services.AddDragonTigerServer();

    internal static void Map(WebApplication app, GameWorkerAccountResolver resolveUserId) =>
        app.MapDragonTiger(async (context, cancellationToken) =>
        {
            if (resolveUserId(context) is not { } userId) return null;
            var database = context.RequestServices.GetRequiredService<FirestoreDb>();
            var balance = await ReadWalletBalanceAsync(database, userId, cancellationToken);
            return new DragonTigerAccount(userId, balance);
        });

    private static async Task<decimal> ReadWalletBalanceAsync(
        FirestoreDb database, string userId, CancellationToken cancellationToken)
    {
        var wallet = await database.Collection("userBalances").Document(userId + "_slotsCredits")
            .GetSnapshotAsync(cancellationToken);
        if (!wallet.Exists) return 0;

        var whole = wallet.GetValue<long>("available");
        var cents = wallet.TryGetValue<long>("availableFractionalCents", out var fractional) ? fractional : 0;
        if (whole < 0 || cents is < 0 or > 99)
            throw new InvalidOperationException("The wallet balance is invalid.");
        return whole + cents / 100m;
    }
}
