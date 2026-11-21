using FortuneForge.Games.LiarsDice;
using FortuneForge.Games.LiarsDice.Server;
using FortuneForge.Games.Hosting;

namespace FortuneForge.Games.PackagedGame.Server;

internal static class GameWorkerModule
{
    internal const string GameId = "liars-dice";
    internal static void Configure(WebApplicationBuilder builder)
    {
        builder.Services.AddSingleton(TimeProvider.System);
        builder.Services.AddManagedPlayerRosterClient(GameId);
        builder.Services.AddSingleton<ILiarsDiceManagedPlayerRoster, LiarsDiceWorkerRoster>();
        builder.Services.AddSingleton<LiarsDiceGameService>();
        builder.Services.AddHostedService<LiarsDiceTurnWorker>();
    }
    internal static void Map(WebApplication app, GameWorkerAccountResolver resolveUserId) =>
        app.MapLiarsDice(resolveUserId.Invoke, GameWorkerAuthentication.GetVerifiedBodySha256);
}

internal sealed class LiarsDiceTurnWorker(LiarsDiceGameService games, ILogger<LiarsDiceTurnWorker> logger) : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken token)
    {
        using var timer = new PeriodicTimer(TimeSpan.FromMilliseconds(250));
        while (await timer.WaitForNextTickAsync(token))
        {
            try { await games.AdvanceDueManagedTurnsAsync(token); }
            catch (OperationCanceledException) when (token.IsCancellationRequested) { return; }
            catch (Exception exception) { logger.LogWarning(exception, "A durable Liar's Dice turn could not advance."); }
        }
    }
}
