using System.Threading.Channels;
using FortuneForge.Games.Abstractions.Analytics;

namespace FortuneForge.Server.Activity;

internal sealed class GameAnalyticsSink(FirestoreGameAnalyticsStore store, ILogger<GameAnalyticsSink> logger) : IGameAnalyticsSink
{
    private readonly Channel<GameAnalyticsSettlement> queue = Channel.CreateBounded<GameAnalyticsSettlement>(new BoundedChannelOptions(4096)
    { SingleReader = true, FullMode = BoundedChannelFullMode.Wait });

    public Task RecordAsync(GameAnalyticsSettlement settlement, CancellationToken ct)
    {
        EnsurePractice(settlement);
        return store.WriteOutboxAsync(settlement, ct);
    }
    public bool TryEnqueuePractice(GameAnalyticsSettlement settlement)
    {
        EnsurePractice(settlement);
        if (!settlement.IsHuman) return true;
        if (queue.Writer.TryWrite(settlement)) return true;
        logger.LogWarning("The free/practice analytics queue is full; settlement {GameId}/{RoundId} was not queued", settlement.GameId, settlement.RoundId);
        return false;
    }
    internal bool TryRead(out GameAnalyticsSettlement? settlement) => queue.Reader.TryRead(out settlement);
    private static void EnsurePractice(GameAnalyticsSettlement settlement)
    {
        if (settlement.Mode == "paid") throw new ArgumentException("Paid analytics must be written in the financial settlement transaction.");
        _ = GameAnalyticsOutbox.Create(settlement);
    }
}

internal sealed class GameAnalyticsWorker(GameAnalyticsSink sink, FirestoreGameAnalyticsStore store, ILogger<GameAnalyticsWorker> logger) : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        GameAnalyticsSettlement? inFlight = null;
        var initialized = false;
        while (!stoppingToken.IsCancellationRequested)
        {
            var delay = TimeSpan.FromSeconds(5);
            try
            {
                if (!initialized) { await store.InitializeAsync(stoppingToken); initialized = true; }
                for (var count = 0; count < 100; count++)
                {
                    if (inFlight is null && !sink.TryRead(out inFlight)) break;
                    await store.WriteOutboxAsync(inFlight!, stoppingToken);
                    inFlight = null;
                }
                var countProcessed = await store.ProcessPendingAsync(100, stoppingToken);
                var presenceFlushed = await store.FlushPendingPresenceAsync(100, stoppingToken);
                if (countProcessed == 100 || presenceFlushed == 100) continue;
                if (countProcessed == 0 && presenceFlushed == 0) delay = TimeSpan.FromSeconds(30);
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested) { break; }
            catch (Exception error) { logger.LogWarning(error, "Game analytics projection will retry; paid settlement outbox remains durable"); }
            try { await Task.Delay(delay, stoppingToken); }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested) { break; }
        }
    }
}
