using FortuneForge.Games.Abstractions.Analytics;
using FortuneForge.Server.Activity;
using FortuneForge.Server.Tests.Blackjack.Table;
using Google.Cloud.Firestore;
using Xunit;

namespace FortuneForge.Server.Tests.Activity;

[Collection("Blackjack table Firestore emulator")]
public sealed class GameAnalyticsFirestoreTests(BlackjackTableFirestoreEmulatorFixture fixture)
{
    private sealed class Clock(DateTime now) : TimeProvider
    {
        internal DateTime Now = now;
        public override DateTimeOffset GetUtcNow() => new(Now);
    }

    [Fact]
    public async Task Projection_counts_human_participations_and_unique_rounds_separately_and_never_double_counts_retries()
    {
        var db = Database(); var clock = new Clock(GameAnalyticsTests.Now); var store = new FirestoreGameAnalyticsStore(db, clock);
        var alice = GameAnalyticsTests.Settlement(); var bob = GameAnalyticsTests.Settlement("bob", wager: 300, payout: 100);
        await store.WriteOutboxAsync(alice, default); await store.WriteOutboxAsync(bob, default);
        Assert.Equal(2, await store.ProcessPendingAsync(100, default));
        foreach (var period in new[] { "day", "week", "month", "alltime" })
        {
            var global = await store.ReadAsync(period, null, null, default);
            Assert.Equal(new GameAnalyticsMetrics(2, 1, 2, 0, 0, 2, 0, 400, 400, 200, 200), global.Totals);
            Assert.Equal(41, global.Games.Count);
            Assert.Equal(global.Totals, global.Games.Single(game => game.GameId == "blackjack").Metrics);
            var player = await store.ReadAsync(period, null, "alice", default);
            Assert.Equal(new GameAnalyticsMetrics(1, 1, 1, 0, 0, 1, 0, 100, 300, 200, 0), player.Totals);
        }
        // Simulate a replayed source delivery after the first projection committed.
        await store.WriteOutboxAsync(alice, default); await store.ProcessPendingAsync(100, default);
        Assert.Equal(2, (await store.ReadAsync("alltime", null, null, default)).Totals.HumanRounds);
        Assert.Equal(2, (await db.Collection("gameAnalyticsParticipations").GetSnapshotAsync()).Count);
        Assert.Single((await db.Collection("gameAnalyticsRounds").GetSnapshotAsync()).Documents);
        Assert.NotNull((await store.ReadAsync("alltime", null, "alice", default)).TrackingStartedAtUtc);
    }

    [Fact]
    public async Task Free_and_practice_scores_never_become_real_money_unknown_paid_amounts_remain_unknown()
    {
        var db = Database(); var store = new FirestoreGameAnalyticsStore(db, new Clock(GameAnalyticsTests.Now));
        await store.WriteOutboxAsync(GameAnalyticsTests.Settlement(game: "craps", mode: "practice", wager: 10000, payout: 90000), default);
        await store.WriteOutboxAsync(GameAnalyticsTests.Settlement(round: "free", game: "2048", mode: "free", wager: null, payout: null), default);
        await store.WriteOutboxAsync(GameAnalyticsTests.Settlement(round: "unknown", game: "hearts", wager: null, payout: null), default);
        await store.ProcessPendingAsync(100, default);
        var report = await store.ReadAsync("alltime", null, "alice", default);
        Assert.Equal(new GameAnalyticsMetrics(3, 3, 1, 1, 1, 0, 1), report.Totals);
        Assert.Equal(1, report.Games.Single(game => game.GameId == "craps").Metrics.PracticeRounds);
        Assert.Equal(1, report.Games.Single(game => game.GameId == "2048").Metrics.FreeRounds);
        Assert.Equal(1, report.Games.Single(game => game.GameId == "hearts").Metrics.UnknownMoneyRounds);
    }

    [Fact]
    public async Task Concurrent_projection_is_atomic_and_bad_sources_are_quarantined_without_blocking_good_sources()
    {
        var db = Database(); var store = new FirestoreGameAnalyticsStore(db, new Clock(GameAnalyticsTests.Now));
        var value = GameAnalyticsTests.Settlement(); var source = GameAnalyticsOutbox.Create(value)!;
        var reference = db.Collection(GameAnalyticsOutbox.CollectionName).Document(source.Id);
        await reference.SetAsync(source.Fields);
        await Task.WhenAll(Enumerable.Range(0, 3).Select(_ => store.ProjectAsync(reference, default)));
        Assert.Equal(1, (await store.ReadAsync("alltime", null, "alice", default)).Totals.HumanRounds);
        var invalid = GameAnalyticsOutbox.Create(value with { RoundId = "bad" })!; invalid.Fields["mode"] = "not-a-mode";
        var invalidReference = db.Collection(GameAnalyticsOutbox.CollectionName).Document(invalid.Id);
        await invalidReference.SetAsync(invalid.Fields);
        await store.WriteOutboxAsync(value with { RoundId = "second" }, default);
        Assert.Equal(2, await store.ProcessPendingAsync(100, default));
        var quarantined = await invalidReference.GetSnapshotAsync();
        Assert.True(quarantined.GetValue<bool>("reviewRequired")); Assert.False(quarantined.GetValue<bool>("pending"));
        Assert.Equal(2, (await store.ReadAsync("alltime", null, "alice", default)).Totals.HumanRounds);
    }

    [Fact]
    public async Task Presence_counts_distinct_humans_per_game_expires_and_old_tabs_cannot_clear_new_tabs()
    {
        var db = Database(); var clock = new Clock(GameAnalyticsTests.Now); var store = new FirestoreGameAnalyticsStore(db, clock);
        await Account(db, "alice"); await Account(db, "bob"); await Account(db, "bot", true);
        await store.TouchPresenceAsync("alice", "blackjack", "tab-one", true, default);
        clock.Now = clock.Now.AddSeconds(25);
        await store.TouchPresenceAsync("alice", "blackjack", "tab-two", true, default);
        await store.TouchPresenceAsync("alice", "blackjack", "tab-one", false, default);
        await store.TouchPresenceAsync("bob", "mines", "tab-three", true, default);
        await store.TouchPresenceAsync("bot", "mines", "tab-bot", true, default);
        await store.TouchPresenceAsync("missing", "mines", "tab-missing", true, default);
        var global = await store.ReadAsync("day", null, null, default);
        Assert.Equal(2, global.CurrentPlayers); Assert.Equal(1, global.Games.Single(game => game.GameId == "blackjack").CurrentPlayers);
        Assert.Equal(1, global.Games.Single(game => game.GameId == "mines").CurrentPlayers);
        Assert.Equal(0, global.Totals.ObservedActiveMilliseconds); // time is durably pending until a batch flush
        Assert.Equal(1, (await store.ReadAsync("day", null, "alice", default)).CurrentPlayers);
        Assert.Equal(0, (await store.ReadAsync("day", null, "missing", default)).CurrentPlayers);
        await store.TouchPresenceAsync("alice", "blackjack", "tab-two", false, default);
        Assert.Equal(0, (await store.ReadAsync("day", null, "alice", default)).CurrentPlayers);
        Assert.Equal(25000, (await store.ReadAsync("day", null, "alice", default)).Totals.ObservedActiveMilliseconds);
        // Global snapshots deliberately cache for ten seconds and expose their as-of time.
        Assert.Equal(global.PresenceAsOfUtc, (await store.ReadAsync("day", null, null, default)).PresenceAsOfUtc);
        clock.Now = clock.Now.AddSeconds(91);
        Assert.Equal(0, (await store.ReadAsync("day", null, null, default)).CurrentPlayers);
    }

    [Fact]
    public async Task Observed_time_splits_utc_day_month_and_week_boundaries_and_never_counts_idle_lease_tail()
    {
        var db = Database(); var clock = new Clock(new DateTime(2026, 10, 31, 23, 59, 50, DateTimeKind.Utc));
        var store = new FirestoreGameAnalyticsStore(db, clock); await Account(db, "alice");
        await store.TouchPresenceAsync("alice", "mines", "tab-one", true, default);
        clock.Now = clock.Now.AddSeconds(25); await store.TouchPresenceAsync("alice", "mines", "tab-one", true, default);
        await store.TouchPresenceAsync("alice", "mines", "tab-one", false, default);
        Assert.Equal(10000, (await store.ReadAsync("day", "2026-10-31", "alice", default)).Totals.ObservedActiveMilliseconds);
        Assert.Equal(15000, (await store.ReadAsync("day", "2026-11-01", "alice", default)).Totals.ObservedActiveMilliseconds);
        Assert.Equal(10000, (await store.ReadAsync("month", "2026-10", "alice", default)).Totals.ObservedActiveMilliseconds);
        Assert.Equal(15000, (await store.ReadAsync("month", "2026-11", "alice", default)).Totals.ObservedActiveMilliseconds);
        Assert.Equal(25000, (await store.ReadAsync("alltime", null, "alice", default)).Totals.ObservedActiveMilliseconds);
        clock.Now = clock.Now.AddSeconds(70); await store.TouchPresenceAsync("alice", "mines", "tab-one", true, default);
        Assert.Equal(25000, (await store.ReadAsync("alltime", null, "alice", default)).Totals.ObservedActiveMilliseconds);
        await store.WriteOutboxAsync(GameAnalyticsTests.Settlement(game: "mines"), default); await store.ProcessPendingAsync(100, default);
        var combined = await store.ReadAsync("alltime", null, "alice", default);
        Assert.Equal(25000, combined.Totals.ObservedActiveMilliseconds); Assert.Equal(1, combined.Totals.PaidRounds);
    }

    [Fact]
    public async Task Presence_batches_are_durable_flush_after_restart_and_switch_and_do_not_write_aggregates_every_heartbeat()
    {
        var db = Database(); var clock = new Clock(GameAnalyticsTests.Now); var store = new FirestoreGameAnalyticsStore(db, clock);
        await Account(db, "alice");
        await store.TouchPresenceAsync("alice", "mines", "tab-one", true, default);
        for (var tick = 0; tick < 4; tick++)
        {
            clock.Now = clock.Now.AddSeconds(25);
            await store.TouchPresenceAsync("alice", "mines", "tab-one", true, default);
        }
        Assert.Empty((await db.Collection(FirestoreGameAnalyticsStore.Buckets).GetSnapshotAsync()).Documents);
        var persisted = await db.Collection(FirestoreGameAnalyticsStore.Presence).Document(GameAnalyticsOutbox.Hash("alice")).GetSnapshotAsync();
        Assert.Equal(100000, persisted.GetValue<Dictionary<string, long>>("pendingTime").Values.Sum());
        Assert.Equal(0, await store.FlushPendingPresenceAsync(100, default));
        // A browser/server disappearance loses no already-observed interval.
        clock.Now = clock.Now.AddMinutes(4); store = new FirestoreGameAnalyticsStore(db, clock);
        Assert.Equal(1, await store.FlushPendingPresenceAsync(100, default));
        Assert.Equal(0, await store.FlushPendingPresenceAsync(100, default));
        Assert.Equal(100000, (await store.ReadAsync("alltime", null, "alice", default)).Totals.ObservedActiveMilliseconds);
        await store.TouchPresenceAsync("alice", "mines", "tab-two", true, default);
        clock.Now = clock.Now.AddSeconds(25); await store.TouchPresenceAsync("alice", "mines", "tab-two", true, default);
        await store.TouchPresenceAsync("alice", "blackjack", "tab-three", true, default);
        Assert.Equal(125000, (await store.ReadAsync("alltime", null, "alice", default)).Totals.ObservedActiveMilliseconds);
        Assert.Equal(125000, (await store.ReadAsync("alltime", null, "alice", default)).Games.Single(game => game.GameId == "mines").Metrics.ObservedActiveMilliseconds);
    }

    private static Task Account(FirestoreDb db, string user, bool bot = false) => db.Collection("users").Document(user)
        .SetAsync(new Dictionary<string, object> { ["profileTags"] = bot ? new[] { "bot" } : new[] { "player" } });
    private FirestoreDb Database() => new FirestoreDbBuilder
    {
        ProjectId = "demo-analytics-" + Guid.NewGuid().ToString("N"), Endpoint = fixture.Endpoint,
        ChannelCredentials = Grpc.Core.ChannelCredentials.Insecure, EmulatorDetection = Google.Api.Gax.EmulatorDetection.None,
    }.Build();
}
