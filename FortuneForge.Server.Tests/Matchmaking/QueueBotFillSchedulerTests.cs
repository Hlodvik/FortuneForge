using FortuneForge.Server.Matchmaking.QueueBotScheduling;
using Xunit;

namespace FortuneForge.Server.Tests.Matchmaking;

public sealed class QueueBotFillSchedulerTests
{
    [Fact]
    public void Waits_during_the_human_only_grace_period()
    {
        var now = DateTime.UnixEpoch.AddMinutes(1);
        var scheduler = Scheduler();
        scheduler.RecordHumanArrival(Key, now);

        var decision = scheduler.Evaluate(Snapshot(now.AddSeconds(-4), humans: 1), Policy(), now);

        Assert.False(decision.ShouldReserveBots);
        Assert.Equal(QueueBotFillReason.HumanOnlyGrace, decision.Reason);
    }

    [Fact]
    public void Waits_when_recent_human_arrivals_should_fill_before_the_limit()
    {
        var now = DateTime.UnixEpoch.AddMinutes(1);
        var scheduler = Scheduler();
        foreach (var secondsAgo in new[] { 3, 8, 15, 22 })
            scheduler.RecordHumanArrival(Key, now.AddSeconds(-secondsAgo));

        var decision = scheduler.Evaluate(Snapshot(now.AddSeconds(-5), humans: 2), Policy(), now);

        Assert.False(decision.ShouldReserveBots);
        Assert.Equal(QueueBotFillReason.ForecastFillsInTime, decision.Reason);
        Assert.Equal(4, decision.Statistics.RecentHumanArrivals);
    }

    [Fact]
    public void Reserves_only_missing_bot_seats_when_the_forecast_misses_the_wait_limit()
    {
        var now = DateTime.UnixEpoch.AddMinutes(1);
        var scheduler = Scheduler();
        scheduler.RecordHumanArrival(Key, now.AddSeconds(-10));

        var decision = scheduler.Evaluate(Snapshot(now.AddSeconds(-5), humans: 1), Policy(), now);

        Assert.True(decision.ShouldReserveBots);
        Assert.Equal(2, decision.BotSeatsToReserve);
        Assert.Equal(QueueBotFillReason.ForecastMissesWaitTarget, decision.Reason);
    }

    [Fact]
    public void Never_reserves_bots_before_a_human_has_joined()
    {
        var now = DateTime.UnixEpoch.AddMinutes(1);

        var decision = Scheduler().Evaluate(Snapshot(now.AddMinutes(-1), humans: 0), Policy(), now);

        Assert.False(decision.ShouldReserveBots);
        Assert.Equal(QueueBotFillReason.WaitingForFirstHuman, decision.Reason);
    }

    [Fact]
    public void Forces_a_fill_at_the_configured_maximum_wait()
    {
        var now = DateTime.UnixEpoch.AddMinutes(1);
        var scheduler = Scheduler();
        scheduler.RecordHumanArrival(Key, now.AddMinutes(-1));

        var decision = scheduler.Evaluate(Snapshot(now.AddSeconds(-25), humans: 1), Policy(), now);

        Assert.True(decision.ShouldReserveBots);
        Assert.Equal(2, decision.BotSeatsToReserve);
        Assert.Equal(QueueBotFillReason.MaximumWaitReached, decision.Reason);
    }

    private const string Key = "craps:3:standard";

    private static IQueueBotFillScheduler Scheduler() =>
        new QueueBotFillScheduler(new InMemoryQueueStatisticsEvaluator());

    private static QueueFillSnapshot Snapshot(DateTime createdAtUtc, int humans) =>
        new(Key, RequiredPlayers: 3, HumanPlayers: humans, ReservedBotPlayers: 0, createdAtUtc);

    private static QueueBotFillPolicy Policy() => new(
        MinimumHumanPlayers: 1,
        MaximumBots: 2,
        InitialHumanOnlyWait: TimeSpan.FromSeconds(5),
        MaximumHumanWait: TimeSpan.FromSeconds(20),
        ArrivalSampleWindow: TimeSpan.FromMinutes(1));
}
