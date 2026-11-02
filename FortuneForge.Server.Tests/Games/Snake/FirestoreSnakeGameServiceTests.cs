using FortuneForge.Games.Abstractions.Auditing;
using FortuneForge.Server.Games.Snake;
using FortuneForge.Server.RoundAudit;
using FortuneForge.Server.Tests.Blackjack.Table;
using Google.Api.Gax;
using Google.Cloud.Firestore;
using Grpc.Core;
using Xunit;

namespace FortuneForge.Server.Tests.Games.Snake;

[Collection("Blackjack table Firestore emulator")]
public sealed class FirestoreSnakeGameServiceTests(BlackjackTableFirestoreEmulatorFixture fixture)
{
    [Fact]
    public async Task TerminalRunWritesAuthoritativeJournalAndReplayablePrivateEvidence()
    {
        var db = Database();
        var service = new FirestoreSnakeGameService(db, TimeProvider.System, () => 7u);
        var started = await service.StartAsync("alice", new(null, 16, 16), default);
        Assert.Equal("playing", started.Phase);
        Assert.Equal(16, started.Width);
        await Assert.ThrowsAsync<SnakeAccessException>(() => service.GetAsync("bob", started.GameId, default));
        var current = started;
        for (var step = 0; step < 8; step++) current = await service.TickAsync("alice", started.GameId, default);
        Assert.Equal("lost", current.Phase);
        var run = await db.Collection("snakeGames").Document(started.GameId.ToString("N")).GetSnapshotAsync();
        Assert.Equal("snake-practice-16x16-v1", run.GetValue<string>("scoreClassification"));
        Assert.Contains("\"seed\":7", run.GetValue<string>("stateJson"));
        var runId = run.GetValue<string>("runId");
        var auditId = GameSecurityEventDocuments.Hash("snake\n" + runId);
        var summary = await db.Collection(RoundAuditDocuments.Summaries).Document(auditId).GetSnapshotAsync();
        var evidence = await db.Collection(RoundAuditDocuments.Evidence).Document(auditId).GetSnapshotAsync();
        Assert.True(summary.Exists);
        Assert.True(evidence.Exists);
        Assert.Equal("passed", summary.GetValue<string>("auditStatus"));
        Assert.Empty(SnakeReplayAudit.Inspect(FirestoreGameRoundAuditStore.Summary(summary), evidence.GetValue<string>("payloadJson")));
        var events = await db.Collection(GameSecurityEventDocuments.CollectionName).WhereEqualTo("gameId", "snake")
            .WhereEqualTo("instanceId", runId).GetSnapshotAsync();
        Assert.Equal(9, events.Count);
        Assert.Equal(9, events.Documents.Select(item => item.GetValue<long>("version")).Distinct().Count());
        await Assert.ThrowsAsync<InvalidOperationException>(() => service.TickAsync("alice", started.GameId, default));
    }

    [Fact]
    public async Task ResetPreservesUnfinishedRunAsSeparateIncompleteEpisode()
    {
        var db = Database();
        var seeds = new Queue<uint>([31u, 43u]);
        var service = new FirestoreSnakeGameService(db, TimeProvider.System, seeds.Dequeue);
        var started = await service.StartAsync("alice", new(null, 16, 16), default);
        var oldRun = (await db.Collection("snakeGames").Document(started.GameId.ToString("N")).GetSnapshotAsync()).GetValue<string>("runId");
        var reset = await service.ResetAsync("alice", started.GameId, new(null, 20, 20), default);
        Assert.Equal(started.GameId, reset.GameId);
        Assert.Equal("playing", reset.Phase);
        Assert.Equal(20, reset.Width);
        Assert.Equal(0, reset.BestScore);
        var prior = await db.Collection(RoundAuditDocuments.Summaries)
            .Document(GameSecurityEventDocuments.Hash("snake\n" + oldRun)).GetSnapshotAsync();
        Assert.Equal("incomplete", prior.GetValue<string>("auditStatus"));
        var newRun = (await db.Collection("snakeGames").Document(started.GameId.ToString("N")).GetSnapshotAsync()).GetValue<string>("runId");
        Assert.NotEqual(oldRun, newRun);
    }

    [Fact]
    public async Task HostedSnakeRejectsClientSeedAndUnsupportedScoreBoards()
    {
        var service = new FirestoreSnakeGameService(Database(), TimeProvider.System, () => 7u);
        await Assert.ThrowsAsync<SnakeRuleException>(() => service.StartAsync("alice", new(42, 16, 16), default));
        await Assert.ThrowsAsync<SnakeRuleException>(() => service.StartAsync("alice", new(null, 6, 6), default));
        await Assert.ThrowsAsync<SnakeRuleException>(() => service.StartAsync("alice", new(null, 16, 20), default));
    }

    private FirestoreDb Database() => new FirestoreDbBuilder
    {
        ProjectId = "demo-snake-hosted-" + Guid.NewGuid().ToString("N"),
        Endpoint = fixture.Endpoint,
        ChannelCredentials = ChannelCredentials.Insecure,
        EmulatorDetection = EmulatorDetection.None
    }.Build();
}
