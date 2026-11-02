using FortuneForge.Server.Activity;
using FortuneForge.Server.Tests.Blackjack.Table;
using Google.Cloud.Firestore;
using Xunit;

namespace FortuneForge.Server.Tests.Activity;

[Collection("Blackjack table Firestore emulator")]
public sealed class GameActivityFirestoreTests(BlackjackTableFirestoreEmulatorFixture fixture)
{
    [Fact]
    public async Task Activity_is_account_scoped_durable_and_counts_one_participation_per_round()
    {
        var db = Database(); var store = new FirestoreGameActivityStore(db);
        var now = new DateTime(2026, 10, 26, 10, 0, 0, DateTimeKind.Utc);
        var start = new AcceptedGameActivity("mines", "round1", "start");
        await store.RecordAsync("alice", start, now, default);
        await store.RecordAsync("alice", start, now.AddMinutes(10), default);
        Assert.Equal(now, Assert.Single(await store.ReadAsync("alice", default)).PlayedAtUtc);
        Assert.Empty(await store.ReadAsync("bob", default));
        await store.RecordAsync("alice", start with { EventKey = "reveal" }, now.AddMinutes(1), default);
        Assert.Equal(1, (await db.Collection("catalogGamePopularity").Document("mines").GetSnapshotAsync()).GetValue<long>("playCount"));
        await store.RecordAsync("alice", start with { RoundKey = "round2" }, now.AddMinutes(2), default);
        await store.RecordAsync("bob", start, now.AddMinutes(3), default);
        Assert.Equal(3, (await db.Collection("catalogGamePopularity").Document("mines").GetSnapshotAsync()).GetValue<long>("playCount"));
        Assert.Equal(now.AddMinutes(2), Assert.Single(await new FirestoreGameActivityStore(db).ReadAsync("alice", default)).PlayedAtUtc);
        Assert.Equal(now.AddMinutes(3), Assert.Single(await store.ReadAsync("bob", default)).PlayedAtUtc);
        var metadata = await db.Collection("catalogGamePopularityMetadata").Document("tracking").GetSnapshotAsync();
        Assert.Equal(now, metadata.GetValue<Timestamp>("startedAt").ToDateTime());
    }

    [Fact]
    public async Task Concurrent_retries_are_atomic_and_delayed_events_never_move_recency_backwards()
    {
        var db = Database(); var store = new FirestoreGameActivityStore(db); var now = new DateTime(2026, 10, 26, 11, 0, 0, DateTimeKind.Utc);
        var play = new AcceptedGameActivity("texas-holdem", "table:2", "action");
        await Task.WhenAll(Enumerable.Range(0, 4).Select(_ => store.RecordAsync("alice", play, now, default)));
        Assert.Equal(1, (await db.Collection("catalogGamePopularity").Document("texas-holdem").GetSnapshotAsync()).GetValue<long>("playCount"));
        await store.RecordAsync("alice", play with { EventKey = "older-action" }, now.AddMinutes(-1), default);
        Assert.Equal(now, Assert.Single(await store.ReadAsync("alice", default)).PlayedAtUtc);
    }

    private FirestoreDb Database() => new FirestoreDbBuilder
    {
        ProjectId = "demo-activity-" + Guid.NewGuid().ToString("N"), Endpoint = fixture.Endpoint,
        ChannelCredentials = Grpc.Core.ChannelCredentials.Insecure, EmulatorDetection = Google.Api.Gax.EmulatorDetection.None,
    }.Build();
}
