using FortuneForge.Games.Abstractions.Analytics;
using FortuneForge.Server.Activity;
using Xunit;

namespace FortuneForge.Server.Tests.Activity;

public sealed class GameAnalyticsTests
{
    internal static readonly DateTime Now = new(2026, 10, 26, 12, 0, 0, DateTimeKind.Utc);
    internal static GameAnalyticsSettlement Settlement(string user = "alice", string round = "round-1", string game = "blackjack", string mode = "paid", long? wager = 100, long? payout = 300) =>
        new(game, round, user, mode, true, Now, Now.AddMinutes(-1), wager, payout, "realResults", round);

    [Fact]
    public void Outbox_identity_is_round_participant_based_not_delivery_or_source_based()
    {
        var value = Settlement(); var original = GameAnalyticsOutbox.Create(value)!;
        Assert.Equal(original.Id, GameAnalyticsOutbox.Create(value with { SourceCollection = "anotherEvidence", SourceDocumentId = "different" })!.Id);
        Assert.NotEqual(original.Id, GameAnalyticsOutbox.Create(value with { UserId = "bob" })!.Id);
        Assert.NotEqual(original.Id, GameAnalyticsOutbox.Create(value with { Mode = "practice" })!.Id);
        Assert.Equal(original.Fields["roundKey"], GameAnalyticsOutbox.Create(value with { UserId = "bob" })!.Fields["roundKey"]);
        Assert.Null(GameAnalyticsOutbox.Create(value with { IsHuman = false }));
        Assert.Equal(41, GameAnalyticsOutbox.GameIds.Count);
        foreach (var game in GameAnalyticsOutbox.GameIds) Assert.NotNull(GameAnalyticsOutbox.Create(value with { GameId = game }));
    }

    [Fact]
    public void Outbox_requires_explicit_mode_actual_utc_order_and_known_or_unknown_gross_money()
    {
        var value = Settlement();
        foreach (var invalid in new[] { value with { GameId = "fake" }, value with { Mode = "unknown" }, value with { RoundId = "" },
                     value with { WagerCents = -1 }, value with { PayoutCents = null }, value with { CompletedAtUtc = DateTime.SpecifyKind(Now, DateTimeKind.Local) },
                     value with { StartedAtUtc = Now.AddSeconds(1) }, value with { SourceDocumentId = "" } })
            Assert.Throws<ArgumentException>(() => GameAnalyticsOutbox.Create(invalid));
        Assert.NotNull(GameAnalyticsOutbox.Create(value with { WagerCents = null, PayoutCents = null, StartedAtUtc = null }));
    }

    [Theory]
    [InlineData("day", "2026-10-26", "day:20261026", "2026-10-27")]
    [InlineData("week", "2026-11-01", "week:20261026", "2026-11-02")]
    [InlineData("week", "2027-01-01", "week:20261228", "2027-01-04")]
    [InlineData("month", "2026-10", "month:20261001", "2026-11-01")]
    public void Periods_are_explicit_utc_calendar_buckets(string kind, string date, string key, string end)
    {
        var period = GameAnalyticsPeriods.Resolve(kind, date, Now);
        Assert.Equal(key, period.Key); Assert.Equal(end, period.EndUtc!.Value.ToString("yyyy-MM-dd"));
        Assert.Equal(DateTimeKind.Utc, period.StartUtc!.Value.Kind);
    }

    [Fact]
    public void Invalid_periods_and_dates_are_rejected_instead_of_silently_reinterpreted()
    {
        Assert.Throws<GameAnalyticsQueryException>(() => GameAnalyticsPeriods.Resolve("year", null, Now));
        Assert.Throws<GameAnalyticsQueryException>(() => GameAnalyticsPeriods.Resolve("day", "10/26/2026", Now));
        Assert.Equal("all", GameAnalyticsPeriods.Resolve("alltime", null, Now).Key);
    }
}
