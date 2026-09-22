namespace FortuneForge.Server.Matchmaking.QueueBotScheduling;

/// <summary>
/// The human queue state used to decide whether automated seats are needed.
/// It deliberately excludes bot identities, game state, balances, and strategy.
/// </summary>
public sealed record QueueFillSnapshot(
    string StatisticsKey,
    int RequiredPlayers,
    int HumanPlayers,
    int ReservedBotPlayers,
    DateTime CreatedAtUtc);

public sealed record QueueBotFillPolicy(
    int MinimumHumanPlayers,
    int MaximumBots,
    TimeSpan InitialHumanOnlyWait,
    TimeSpan MaximumHumanWait,
    TimeSpan ArrivalSampleWindow)
{
    public void Validate()
    {
        if (MinimumHumanPlayers < 1) throw new InvalidOperationException("At least one human player is required.");
        if (MaximumBots < 0) throw new InvalidOperationException("MaximumBots cannot be negative.");
        if (InitialHumanOnlyWait < TimeSpan.Zero || MaximumHumanWait < InitialHumanOnlyWait)
            throw new InvalidOperationException("Queue wait limits are invalid.");
        if (ArrivalSampleWindow < TimeSpan.FromSeconds(10) || ArrivalSampleWindow > TimeSpan.FromMinutes(30))
            throw new InvalidOperationException("ArrivalSampleWindow must be from 10 seconds through 30 minutes.");
    }
}

public enum QueueBotFillReason
{
    QueueComplete,
    WaitingForFirstHuman,
    HumanOnlyGrace,
    ForecastFillsInTime,
    InsufficientHistory,
    BotCapacityDisabled,
    ForecastMissesWaitTarget,
    MaximumWaitReached
}

public sealed record QueueDemandStatistics(int RecentHumanArrivals, double HumanArrivalsPerMinute);

public sealed record QueueBotFillDecision(
    bool ShouldReserveBots,
    int BotSeatsToReserve,
    QueueBotFillReason Reason,
    QueueDemandStatistics Statistics,
    TimeSpan? ForecastHumanFillWait);

public interface IQueueStatisticsEvaluator
{
    void RecordHumanArrival(string statisticsKey, DateTime occurredAtUtc);
    QueueDemandStatistics Evaluate(string statisticsKey, DateTime nowUtc, TimeSpan sampleWindow);
}

public interface IQueueBotFillScheduler
{
    void RecordHumanArrival(string statisticsKey, DateTime occurredAtUtc);
    QueueBotFillDecision Evaluate(QueueFillSnapshot snapshot, QueueBotFillPolicy policy, DateTime nowUtc);
}

public sealed class InMemoryQueueStatisticsEvaluator : IQueueStatisticsEvaluator
{
    private readonly object gate = new();
    private readonly Dictionary<string, List<DateTime>> arrivals = new(StringComparer.Ordinal);

    public void RecordHumanArrival(string statisticsKey, DateTime occurredAtUtc)
    {
        if (string.IsNullOrWhiteSpace(statisticsKey)) throw new ArgumentException("A statistics key is required.", nameof(statisticsKey));
        lock (gate)
        {
            if (!arrivals.TryGetValue(statisticsKey, out var values))
            {
                values = [];
                arrivals.Add(statisticsKey, values);
            }
            values.Add(occurredAtUtc);
            values.RemoveAll(value => value < occurredAtUtc.AddMinutes(-30));
        }
    }

    public QueueDemandStatistics Evaluate(string statisticsKey, DateTime nowUtc, TimeSpan sampleWindow)
    {
        lock (gate)
        {
            if (!arrivals.TryGetValue(statisticsKey, out var values)) return new(0, 0);
            var cutoff = nowUtc.Subtract(sampleWindow);
            values.RemoveAll(value => value < cutoff);
            var count = values.Count(value => value <= nowUtc);
            return new(count, count / sampleWindow.TotalMinutes);
        }
    }
}

public sealed class QueueBotFillScheduler(IQueueStatisticsEvaluator statistics) : IQueueBotFillScheduler
{
    public void RecordHumanArrival(string statisticsKey, DateTime occurredAtUtc) =>
        statistics.RecordHumanArrival(statisticsKey, occurredAtUtc);

    public QueueBotFillDecision Evaluate(QueueFillSnapshot snapshot, QueueBotFillPolicy policy, DateTime nowUtc)
    {
        ArgumentNullException.ThrowIfNull(snapshot);
        policy.Validate();
        Validate(snapshot);

        var demand = statistics.Evaluate(snapshot.StatisticsKey, nowUtc, policy.ArrivalSampleWindow);
        var missingHumans = snapshot.RequiredPlayers - snapshot.HumanPlayers - snapshot.ReservedBotPlayers;
        if (missingHumans <= 0) return NoFill(QueueBotFillReason.QueueComplete, demand);
        if (snapshot.HumanPlayers < policy.MinimumHumanPlayers)
            return NoFill(QueueBotFillReason.WaitingForFirstHuman, demand);

        var wait = nowUtc - snapshot.CreatedAtUtc;
        if (wait < policy.InitialHumanOnlyWait) return NoFill(QueueBotFillReason.HumanOnlyGrace, demand);
        if (wait >= policy.MaximumHumanWait) return Fill(missingHumans, policy, demand, QueueBotFillReason.MaximumWaitReached, null);
        if (demand.RecentHumanArrivals == 0) return NoFill(QueueBotFillReason.InsufficientHistory, demand);

        var forecast = TimeSpan.FromMinutes(missingHumans / demand.HumanArrivalsPerMinute);
        return wait + forecast <= policy.MaximumHumanWait
            ? NoFill(QueueBotFillReason.ForecastFillsInTime, demand, forecast)
            : Fill(missingHumans, policy, demand, QueueBotFillReason.ForecastMissesWaitTarget, forecast);
    }

    private static QueueBotFillDecision Fill(
        int missing, QueueBotFillPolicy policy, QueueDemandStatistics statistics,
        QueueBotFillReason reason, TimeSpan? forecast)
    {
        var seats = Math.Min(missing, policy.MaximumBots);
        return seats > 0
            ? new(true, seats, reason, statistics, forecast)
            : NoFill(QueueBotFillReason.BotCapacityDisabled, statistics, forecast);
    }

    private static QueueBotFillDecision NoFill(
        QueueBotFillReason reason, QueueDemandStatistics statistics, TimeSpan? forecast = null) =>
        new(false, 0, reason, statistics, forecast);

    private static void Validate(QueueFillSnapshot snapshot)
    {
        if (string.IsNullOrWhiteSpace(snapshot.StatisticsKey)) throw new ArgumentException("A statistics key is required.", nameof(snapshot));
        if (snapshot.RequiredPlayers < 2 || snapshot.HumanPlayers < 0 || snapshot.ReservedBotPlayers < 0 ||
            snapshot.HumanPlayers + snapshot.ReservedBotPlayers > snapshot.RequiredPlayers)
        {
            throw new ArgumentOutOfRangeException(nameof(snapshot), "Queue participant counts are invalid.");
        }
    }
}
