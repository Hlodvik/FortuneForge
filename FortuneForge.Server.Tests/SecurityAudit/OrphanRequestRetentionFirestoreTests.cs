using FortuneForge.Games.Abstractions.Auditing;
using FortuneForge.Server.RoundAudit;
using FortuneForge.Server.SecurityAudit;
using FortuneForge.Server.Tests.Blackjack.Table;
using Google.Cloud.Firestore;
using Microsoft.Extensions.Options;
using Xunit;

namespace FortuneForge.Server.Tests.SecurityAudit;

[Collection("Blackjack table Firestore emulator")]
public sealed class OrphanRequestRetentionFirestoreTests(BlackjackTableFirestoreEmulatorFixture fixture)
{
    private sealed class Clock : TimeProvider
    {
        internal DateTime Now = new(2026, 10, 26, 20, 0, 0, DateTimeKind.Utc);
        public override DateTimeOffset GetUtcNow() => new(Now);
    }

    [Fact]
    public async Task OldRejectedCommandWithoutAnInstanceReleasesItsPrivateReceipts()
    {
        var db = Database(); var clock = new Clock();
        var request = await RequestAsync(db, clock.Now.AddDays(-8), null);
        await new SecurityAuditRawRetention(db, Options.Create(new GameRoundRetentionOptions()), clock).SweepAsync(default);

        Assert.False((await db.Collection(FirestoreGameSecurityRequestStore.Intents).Document(request).GetSnapshotAsync()).Exists);
        Assert.False((await db.Collection(FirestoreGameSecurityRequestStore.Results).Document(request).GetSnapshotAsync()).Exists);
    }

    [Fact]
    public async Task LinkedJournalAndInvestigationHoldPreventOrphanCleanupUntilReleased()
    {
        var db = Database(); var clock = new Clock();
        var request = await RequestAsync(db, clock.Now.AddDays(-8), "held-round");
        var eventRef = db.Collection(GameSecurityEventDocuments.CollectionName).Document("linked-event");
        await eventRef.SetAsync(new Dictionary<string, object>
            { ["gameId"] = "mines", ["instanceId"] = "held-round", ["requestId"] = request });
        var retention = new SecurityAuditRawRetention(db, Options.Create(new GameRoundRetentionOptions()), clock);
        await retention.SweepAsync(default);
        Assert.True((await db.Collection(FirestoreGameSecurityRequestStore.Intents).Document(request).GetSnapshotAsync()).Exists);

        await eventRef.DeleteAsync();
        var holdRef = db.Collection(InvestigationDocuments.InstanceHolds)
            .Document(InvestigationDocuments.InstanceKey("mines", "held-round"));
        await holdRef.SetAsync(new Dictionary<string, object> { ["held"] = true });
        clock.Now = clock.Now.AddDays(1);
        await retention.SweepAsync(default);
        Assert.True((await db.Collection(FirestoreGameSecurityRequestStore.Intents).Document(request).GetSnapshotAsync()).Exists);

        await holdRef.UpdateAsync(new Dictionary<string, object> { ["held"] = false });
        clock.Now = clock.Now.AddDays(1);
        await retention.SweepAsync(default);
        Assert.False((await db.Collection(FirestoreGameSecurityRequestStore.Intents).Document(request).GetSnapshotAsync()).Exists);
        Assert.False((await db.Collection(FirestoreGameSecurityRequestStore.Results).Document(request).GetSnapshotAsync()).Exists);
    }

    [Fact]
    public async Task MissingHttpResultCreatesAnUnattributedCaseAndCopiesTheIntent()
    {
        var db = Database(); var clock = new Clock();
        var request = await RequestAsync(db, clock.Now.AddMinutes(-3), null, writeResult: false);
        var retention = new SecurityAuditRawRetention(db, Options.Create(new GameRoundRetentionOptions()), clock);
        await retention.SweepAsync(default);

        var intent = await db.Collection(FirestoreGameSecurityRequestStore.Intents).Document(request).GetSnapshotAsync();
        Assert.True(intent.Exists);
        var caseId = InvestigationDocuments.CaseId("system-integrity", $"unlinked-request:{request}");
        var investigation = await db.Collection(InvestigationDocuments.Cases).Document(caseId).GetSnapshotAsync();
        Assert.Equal("system-integrity", investigation.GetValue<string>("kind"));
        Assert.True(investigation.GetValue<bool>("noAutomaticPlayerStrikes"));
        var copies = await db.Collection(InvestigationDocuments.Evidence).WhereEqualTo("caseId", caseId).GetSnapshotAsync();
        var copy = Assert.Single(copies.Documents);
        Assert.Equal("request-intent", copy.GetValue<string>("kind"));
        Assert.Equal("203.0.113.8", copy.GetValue<Dictionary<string, object>>("originalFields")["ipAddress"]);

        clock.Now = clock.Now.AddDays(8);
        await retention.SweepAsync(default);
        Assert.False((await db.Collection(FirestoreGameSecurityRequestStore.Intents).Document(request).GetSnapshotAsync()).Exists);
        Assert.True((await db.Collection(InvestigationDocuments.Evidence).Document(copy.Id).GetSnapshotAsync()).Exists);
    }

    [Fact]
    public async Task ACompletedRoundSnapshotRetainsItsStartRequestWithoutAPathInstance()
    {
        var db = Database(); var clock = new Clock();
        var request = await RequestAsync(db, clock.Now.AddDays(-8), null);
        var evidenceRef = db.Collection(RoundAuditDocuments.Evidence).Document("held-round-evidence");
        await evidenceRef.SetAsync(new Dictionary<string, object> { ["requestId"] = request });
        var retention = new SecurityAuditRawRetention(db, Options.Create(new GameRoundRetentionOptions()), clock);
        await retention.SweepAsync(default);
        Assert.True((await db.Collection(FirestoreGameSecurityRequestStore.Intents).Document(request).GetSnapshotAsync()).Exists);

        await evidenceRef.DeleteAsync();
        clock.Now = clock.Now.AddDays(1);
        await retention.SweepAsync(default);
        Assert.False((await db.Collection(FirestoreGameSecurityRequestStore.Intents).Document(request).GetSnapshotAsync()).Exists);
    }

    [Theory]
    [InlineData(200)]
    [InlineData(500)]
    public async Task NonRejectedCommandWithoutEngineLinkCreatesAnOperationalCase(int statusCode)
    {
        var db = Database(); var clock = new Clock();
        var request = await RequestAsync(db, clock.Now.AddMinutes(-3), null, statusCode: statusCode);
        var retention = new SecurityAuditRawRetention(db, Options.Create(new GameRoundRetentionOptions()), clock);
        await retention.SweepAsync(default);

        var intent = await db.Collection(FirestoreGameSecurityRequestStore.Intents).Document(request).GetSnapshotAsync();
        Assert.True(intent.Exists);
        var caseId = InvestigationDocuments.CaseId("system-integrity", $"unlinked-request:{request}");
        var caseDoc = await db.Collection(InvestigationDocuments.Cases).Document(caseId).GetSnapshotAsync();
        Assert.True(caseDoc.Exists);
        Assert.True(caseDoc.GetValue<bool>("noAutomaticPlayerStrikes"));
        var copies = await db.Collection(InvestigationDocuments.Evidence).WhereEqualTo("caseId", caseId).GetSnapshotAsync();
        Assert.Equal(["request-intent", "request-result"],
            copies.Documents.Select(value => value.GetValue<string>("kind")).Order().ToArray());

        clock.Now = clock.Now.AddDays(8);
        await retention.SweepAsync(default);
        Assert.False((await db.Collection(FirestoreGameSecurityRequestStore.Intents).Document(request).GetSnapshotAsync()).Exists);
        Assert.False((await db.Collection(FirestoreGameSecurityRequestStore.Results).Document(request).GetSnapshotAsync()).Exists);
        Assert.Equal(2, (await db.Collection(InvestigationDocuments.Evidence).WhereEqualTo("caseId", caseId).GetSnapshotAsync()).Count);
    }

    [Fact]
    public async Task CorruptDueQueueItemIsDeferredWhileLaterOrphanIsProcessed()
    {
        var db = Database(); var clock = new Clock();
        var bad = db.Collection("gameSecurityRequestRetentionQueue").Document("corrupt-queue");
        await bad.SetAsync(new Dictionary<string, object>
        {
            ["requestId"] = "corrupt-queue", ["pending"] = true,
            ["nextAttemptAtUtc"] = Timestamp.FromDateTime(clock.Now.AddMinutes(-1))
        });
        var request = await RequestAsync(db, clock.Now.AddDays(-8), null);
        var retention = new SecurityAuditRawRetention(db, Options.Create(new GameRoundRetentionOptions()), clock);

        await Assert.ThrowsAsync<AggregateException>(() => retention.SweepAsync(default));
        Assert.False((await db.Collection(FirestoreGameSecurityRequestStore.Intents).Document(request).GetSnapshotAsync()).Exists);
        Assert.True((await bad.GetSnapshotAsync()).GetValue<Timestamp>("nextAttemptAtUtc").ToDateTime() > clock.Now);
    }

    [Fact]
    public async Task OperationalCaseStillOpensWhenCleanDeletionIsDisabled()
    {
        var db = Database(); var clock = new Clock();
        var request = await RequestAsync(db, clock.Now.AddMinutes(-3), null, statusCode: 200);
        var retention = new SecurityAuditRawRetention(db,
            Options.Create(new GameRoundRetentionOptions { DeleteCleanEvidence = false }), clock);
        await retention.SweepAsync(default);

        var caseId = InvestigationDocuments.CaseId("system-integrity", $"unlinked-request:{request}");
        Assert.True((await db.Collection(InvestigationDocuments.Cases).Document(caseId).GetSnapshotAsync()).Exists);
        clock.Now = clock.Now.AddDays(8);
        await retention.SweepAsync(default);
        Assert.True((await db.Collection(FirestoreGameSecurityRequestStore.Intents).Document(request).GetSnapshotAsync()).Exists);
        Assert.True((await db.Collection(FirestoreGameSecurityRequestStore.Results).Document(request).GetSnapshotAsync()).Exists);
    }

    private static async Task<string> RequestAsync(FirestoreDb db, DateTime received, string? instanceId,
        bool writeResult = true, int statusCode = 409)
    {
        var id = Guid.NewGuid().ToString("N");
        var store = new FirestoreGameSecurityRequestStore(db);
        await store.CreateIntentAsync(new GameRequestObservation(id, "alice", "session-hash", "203.0.113.8",
            received, "POST", "/api/games/mines/rounds", "mines", instanceId, null, null,
            "{}", "sanitized", "connection"), default);
        if (writeResult)
            await store.CreateResultAsync(new GameRequestResult(id, received.AddSeconds(1), statusCode, "http-response", 1000), default);
        return id;
    }

    private FirestoreDb Database() => new FirestoreDbBuilder
    {
        ProjectId = "demo-orphan-retention-" + Guid.NewGuid().ToString("N"), Endpoint = fixture.Endpoint,
        ChannelCredentials = Grpc.Core.ChannelCredentials.Insecure,
        EmulatorDetection = Google.Api.Gax.EmulatorDetection.None
    }.Build();
}
