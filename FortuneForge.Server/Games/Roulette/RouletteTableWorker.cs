namespace FortuneForge.Server.Games.Roulette;

internal sealed class RouletteTableWorker(
    RouletteGameService games,
    ILogger<RouletteTableWorker> logger) : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        using var timer = new PeriodicTimer(TimeSpan.FromSeconds(1));
        while (await timer.WaitForNextTickAsync(stoppingToken))
        {
            try
            {
                games.Sweep(DateTime.UtcNow);
            }
            catch (Exception exception)
            {
                logger.LogError(exception, "Roulette table maintenance failed.");
            }
        }
    }
}
