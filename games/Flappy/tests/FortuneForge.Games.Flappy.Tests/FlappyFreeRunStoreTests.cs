using System.Collections.Immutable;
using System.Security.Cryptography;
using System.Text;
using FortuneForge.Games.Flappy.Server;
using Google.Api.Gax;
using Google.Cloud.Firestore;
using Xunit;

namespace FortuneForge.Games.Flappy.Tests;

public sealed class FlappyFreeRunStoreTests
{
    [Fact]
    public void HistoricalCanonicalBytesRoundTripWithoutChangingTheDigest()
    {
        var replay = new FlappyReplay(36, []);
        const string expected = "v1\\n36\\n";
        Assert.Equal(expected, FlappyFreeRunStore.CanonicalizeReplay(replay));
        var restored = FlappyFreeRunStore.ParseCanonicalReplay(expected);
        Assert.Equal(36, restored.TotalTicks);
        Assert.Empty(restored.FlapTicks);
        Assert.Throws<ArgumentException>(() => FlappyFreeRunStore.ParseCanonicalReplay("v1\\n036\\n"));
        Assert.Throws<ArgumentException>(() => FlappyFreeRunStore.ParseCanonicalReplay("v1\\n36\\n1,1"));
        Assert.Throws<ArgumentException>(() => FlappyFreeRunStore.ParseCanonicalReplay("v1\n36\n"));
    }

    [FirestoreEmulatorFact]
    public async Task LostResponsesRecoverFromAnotherReplicaWithoutAdditionalRunOrAudit()
    {
        var db = Connect();
        var worker = Store(db, 17);
        var replica = new FlappyFreeRunStore(db, TimeProvider.System, () => throw new InvalidOperationException("An existing run must not request a replacement seed."));
        var opened = await worker.StartAsync("flappy-start-0001", "alice", default);
        var recovered = await replica.StartAsync("flappy-start-0001", "alice", default);
        Assert.Equal(opened.Run, recovered.Run);
        Assert.True(recovered.WasAlreadyStarted);
        var replay = new FlappyReplay(36, ImmutableArray<int>.Empty);
        var first = await worker.CompleteAsync(opened.Run.RunId, "alice", replay, default);
        var retry = await replica.CompleteAsync(opened.Run.RunId, "alice", replay, default);
        Assert.False(first.WasAlreadyCompleted);
        Assert.True(retry.WasAlreadyCompleted);
        Assert.Equal(first with { WasAlreadyCompleted = true }, retry);
        Assert.Equal("ground-collision", first.Terminal);
        Assert.Single((await db.Collection(FlappyFreeRunStore.CollectionName).GetSnapshotAsync()).Documents);
        Assert.Equal(2, (await db.Collection("gameSecurityEvents").GetSnapshotAsync()).Count);
        Assert.Single((await db.Collection("gameRoundAudits").GetSnapshotAsync()).Documents);
        Assert.Empty((await db.Collection("userBalances").GetSnapshotAsync()).Documents);
        Assert.Empty((await db.Collection("balanceTransactions").GetSnapshotAsync()).Documents);
        await Assert.ThrowsAsync<InvalidOperationException>(() => replica.CompleteAsync(opened.Run.RunId, "bob", replay, default));
        await Assert.ThrowsAsync<InvalidOperationException>(() => replica.CompleteAsync(opened.Run.RunId, "alice", new FlappyReplay(37, []), default));
    }

    [FirestoreEmulatorFact]
    public async Task StoredLegacyRunResumesAndCompletedOutcomeCorruptionFailsClosed()
    {
        var db = Connect();
        var key = "flappy-legacy-key-0001";
        var id = Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes($"v1\\nalice\\n{key}")));
        var runId = "flappy_free_" + id;
        var doc = db.Collection("flappyFreeRuns").Document(id);
        await doc.SetAsync(new Dictionary<string, object>
        {
            ["runId"] = runId, ["seed"] = 17L, ["playerId"] = "alice",
            ["startIdempotencyKey"] = key, ["status"] = "started", ["schemaVersion"] = 1L,
            ["startedAt"] = Timestamp.FromDateTime(new DateTime(2026, 9, 6, 12, 0, 0, DateTimeKind.Utc)),
        });
        var service = Store(db, 99);
        Assert.Equal(17U, (await service.StartAsync(key, "alice", default)).Run.Seed);
        var replay = new FlappyReplay(36, []);
        await service.CompleteAsync(runId, "alice", replay, default);
        await doc.UpdateAsync("score", 999L);
        await Assert.ThrowsAsync<InvalidOperationException>(() => service.CompleteAsync(runId, "alice", replay, default));
        await Assert.ThrowsAsync<InvalidOperationException>(() => service.StartAsync(key, "alice", default));
    }

    private static FlappyFreeRunStore Store(FirestoreDb db, uint seed) => new(db, TimeProvider.System, () => seed);
    private static FirestoreDb Connect() => new FirestoreDbBuilder
    {
        ProjectId = $"demo-flappy-{Guid.NewGuid():N}"[..29], EmulatorDetection = EmulatorDetection.EmulatorOnly,
    }.Build();
}

public sealed class FirestoreEmulatorFactAttribute : FactAttribute
{
    public FirestoreEmulatorFactAttribute()
    {
        if (string.IsNullOrWhiteSpace(Environment.GetEnvironmentVariable("FIRESTORE_EMULATOR_HOST")))
            Skip = "Run with a local Firestore emulator to verify Flappy persistence.";
    }
}
