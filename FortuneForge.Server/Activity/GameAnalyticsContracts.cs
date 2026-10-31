using System.Globalization;
using FortuneForge.Games.Abstractions.Analytics;

namespace FortuneForge.Server.Activity;

public sealed record GameAnalyticsMetrics(long HumanRounds = 0, long UniqueRounds = 0,
    long PaidRounds = 0, long PracticeRounds = 0, long FreeRounds = 0,
    long KnownMoneyRounds = 0, long UnknownMoneyRounds = 0,
    long WagerCents = 0, long PayoutCents = 0, long NetWinCents = 0, long NetLossCents = 0,
    long ObservedActiveMilliseconds = 0);
public sealed record GameAnalyticsGame(string GameId, GameAnalyticsMetrics Metrics, long CurrentPlayers = 0);
public sealed record GameAnalyticsPeriod(string Kind, string Key, DateTime? StartUtc, DateTime? EndUtc);
public sealed record GameAnalyticsReport(GameAnalyticsPeriod Period, DateTime AsOfUtc, DateTime? TrackingStartedAtUtc,
    GameAnalyticsMetrics Totals, IReadOnlyList<GameAnalyticsGame> Games, long CurrentPlayers, IReadOnlyList<string> Limitations,
    DateTime? PresenceAsOfUtc = null);

public interface IGameAnalyticsReader
{
    Task<GameAnalyticsReport> ReadAsync(string period, string? date, string? userId, CancellationToken ct);
}
public interface IGameAnalyticsSink
{
    /// <summary>For async free/practice engines. Paid engines MUST use the shared outbox helper in their settlement transaction.</summary>
    Task RecordAsync(GameAnalyticsSettlement settlement, CancellationToken ct);
    /// <summary>For synchronous in-memory engines. Returns false if the bounded owned worker queue is full.</summary>
    bool TryEnqueuePractice(GameAnalyticsSettlement settlement);
}
public sealed class GameAnalyticsQueryException(string message) : ArgumentException(message);

internal static class GameAnalyticsPeriods
{
    internal static GameAnalyticsPeriod Resolve(string period, string? date, DateTime nowUtc)
    {
        period = period.ToLowerInvariant();
        if (period is "alltime" or "all-time") return new("alltime", "all", null, null);
        if (period is not ("day" or "week" or "month")) throw new GameAnalyticsQueryException("Choose day, week, month, or alltime.");
        var selected = nowUtc.Date;
        if (!string.IsNullOrWhiteSpace(date))
        {
            var formats = period == "month" ? new[] { "yyyy-MM", "yyyy-MM-dd" } : ["yyyy-MM-dd"];
            if (!DateTime.TryParseExact(date, formats, CultureInfo.InvariantCulture, DateTimeStyles.AssumeUniversal | DateTimeStyles.AdjustToUniversal, out selected))
                throw new GameAnalyticsQueryException("Use a UTC date in YYYY-MM-DD format, or YYYY-MM for a month.");
        }
        selected = DateTime.SpecifyKind(selected.Date, DateTimeKind.Utc);
        if (period == "week") selected = selected.AddDays(-(((int)selected.DayOfWeek + 6) % 7));
        if (period == "month") selected = new(selected.Year, selected.Month, 1, 0, 0, 0, DateTimeKind.Utc);
        return new(period, period + ":" + selected.ToString("yyyyMMdd", CultureInfo.InvariantCulture), selected,
            period == "day" ? selected.AddDays(1) : period == "week" ? selected.AddDays(7) : selected.AddMonths(1));
    }
    internal static IReadOnlyList<GameAnalyticsPeriod> At(DateTime utc) =>
        [Resolve("day", null, utc), Resolve("week", null, utc), Resolve("month", null, utc), Resolve("alltime", null, utc)];
}
