namespace FortuneForge.Games.Abstractions.Analytics;

public interface IGameAnalyticsSink
{
    /// <summary>Records free or practice play. Paid play writes its outbox with the settlement.</summary>
    Task RecordAsync(GameAnalyticsSettlement settlement, CancellationToken cancellationToken);

    /// <summary>Queues practice play from an in-memory engine. Returns false when the queue is full.</summary>
    bool TryEnqueuePractice(GameAnalyticsSettlement settlement);
}
