using FortuneForge.Games.Abstractions.Auditing;
using FortuneForge.Games.ArcaneArchives;
using FortuneForge.Games.ArcaneArchives.Server;
using FortuneForge.Server.RoundAudit;
using FortuneForge.Server.SecurityAudit;
using FortuneForge.Server.Tests.Blackjack.Table;
using Google.Cloud.Firestore;
using Microsoft.Extensions.Options;
using Xunit;

namespace FortuneForge.Server.Tests.SecurityAudit;

[Collection("Blackjack table Firestore emulator")]
public sealed class JournalManifestFirestoreTests(BlackjackTableFirestoreEmulatorFixture fixture)
{
    private static readonly DateTime Now = new(2026, 10, 26, 20, 0, 0, DateTimeKind.Utc);

    [Fact]
    public async Task MissingIntermediateEventIsDetectedAgainstTheCommittedManifest()
    {
        var db = Database();
        var instanceId = Guid.NewGuid().ToString("N");
        var capture = RoundAuditCapture.Create("mines", instanceId, instanceId, 1, Now, Now.AddSeconds(2),
            new { state = "completed", score = 42 }, "test-round-v1", "test-auditor-v1", [], []);
        await db.RunTransactionAsync(tx =>
        {
            var records = RoundAuditDocuments.Create(capture);
            tx.Create(db.Collection(RoundAuditDocuments.Summaries).Document(capture.Summary.Id), records.Summary);
            tx.Create(db.Collection(RoundAuditDocuments.Evidence).Document(capture.Summary.Id), records.Evidence);
            GameSecurityJournal.Write(tx, db, "mines", instanceId, 1, Now, new { state = "active" }, [], "started");
            GameSecurityJournal.Write(tx, db, "mines", instanceId, 2, Now.AddSeconds(1), new { state = "completed" }, [], "settled");
            return Task.FromResult(true);
        });

        var manifestRef = db.Collection(GameSecurityEventDocuments.ManifestCollectionName)
            .Document(GameSecurityEventDocuments.ManifestId("mines", instanceId));
        var manifest = await manifestRef.GetSnapshotAsync();
        var eventIds = manifest.GetValue<List<string>>("eventIds");
        Assert.Equal(2, eventIds.Count);
        var complete = await GameSecurityIntegrityAuditor.InspectAsync(db, capture.Summary, default, Now.AddMinutes(3));
        Assert.DoesNotContain(complete.Findings, finding => finding.Severity == "error");

        await db.Collection(GameSecurityEventDocuments.CollectionName).Document(eventIds[0]).DeleteAsync();
        var incomplete = await GameSecurityIntegrityAuditor.InspectAsync(db, capture.Summary, default, Now.AddMinutes(3));
        Assert.Contains(incomplete.Findings, finding => finding.Code == "engine-journal-event-missing" && finding.Severity == "error");

        await manifestRef.DeleteAsync();
        var missingManifest = await GameSecurityIntegrityAuditor.InspectAsync(db, capture.Summary, default, Now.AddMinutes(3));
        Assert.Contains(missingManifest.Findings, finding => finding.Code == "engine-journal-manifest-missing" && finding.Severity == "error");
    }

    [Fact]
    public async Task AcknowledgedSinkWriteCommitsEventAndManifestTogether()
    {
        var db = Database(); var instanceId = Guid.NewGuid().ToString("N");
        var observation = GameSecurityEventDocuments.Capture("mines", instanceId, 1, Now,
            new { state = "active" }, ["alice"]);
        await new FirestoreGameSecurityEventSink(db).WriteAsync(observation);

        var stored = await db.Collection(GameSecurityEventDocuments.CollectionName).Document(observation.Id).GetSnapshotAsync();
        var manifest = await db.Collection(GameSecurityEventDocuments.ManifestCollectionName)
            .Document(GameSecurityEventDocuments.ManifestId("mines", instanceId)).GetSnapshotAsync();
        Assert.True(stored.Exists);
        Assert.Equal([observation.Id], manifest.GetValue<List<string>>("eventIds"));
    }

    [Fact]
    public async Task PagedManifestVerifiesOrderAndDetectsAMissingPage()
    {
        var db = Database();
        const string gameId = "2048";
        var instance = Guid.NewGuid().ToString("N");
        var capture = RoundAuditCapture.Create(gameId, instance, instance, 1, Now, Now.AddSeconds(2),
            new { score = 42 }, "manifest-test-v1", "manifest-test-v1", [], []);
        var count = GameSecurityEventDocuments.ManifestPageSize + 2;
        var events = Enumerable.Range(0, count).Select(version => GameSecurityEventDocuments.Capture(gameId, instance,
            version, Now, new { version }, [], "moved")).ToArray();
        var root = db.Collection(GameSecurityEventDocuments.ManifestCollectionName)
            .Document(GameSecurityEventDocuments.ManifestId(gameId, instance));
        var pages = root.Collection(GameSecurityEventDocuments.ManifestPageCollectionName);
        var batch = db.StartBatch();
        var records = RoundAuditDocuments.Create(capture);
        batch.Create(db.Collection(RoundAuditDocuments.Summaries).Document(capture.Summary.Id), records.Summary);
        batch.Create(db.Collection(RoundAuditDocuments.Evidence).Document(capture.Summary.Id), records.Evidence);
        batch.Create(root, new Dictionary<string, object>
        {
            ["gameId"] = gameId, ["instanceId"] = instance,
            ["schemaVersion"] = GameSecurityEventDocuments.PagedManifestVersion,
            ["eventCount"] = count, ["firstVersion"] = 0L, ["pageSize"] = GameSecurityEventDocuments.ManifestPageSize,
        });
        foreach (var observation in events)
            batch.Create(db.Collection(GameSecurityEventDocuments.CollectionName).Document(observation.Id), observation.Fields);
        var chunks = events.Select(observation => observation.Id).Chunk(GameSecurityEventDocuments.ManifestPageSize).ToArray();
        for (var index = 0; index < chunks.Length; index++)
            batch.Create(pages.Document(GameSecurityEventDocuments.ManifestPageId(index)),
                new Dictionary<string, object> { ["pageIndex"] = index, ["eventIds"] = chunks[index] });
        await batch.CommitAsync();

        var correct = await GameSecurityIntegrityAuditor.InspectAsync(db, capture.Summary, default);
        Assert.Equal(count, correct.JournalEventCount);
        Assert.DoesNotContain(correct.Findings, finding => finding.Severity == "error");

        var lastPage = pages.Document(GameSecurityEventDocuments.ManifestPageId(1));
        await lastPage.UpdateAsync(new Dictionary<string, object> { ["eventIds"] = chunks[1].Reverse().ToArray() });
        var reversed = await GameSecurityIntegrityAuditor.InspectAsync(db, capture.Summary, default);
        Assert.Contains(reversed.Findings, finding => finding.Code == "engine-journal-manifest-order");

        await lastPage.DeleteAsync();
        var partial = await GameSecurityIntegrityAuditor.InspectAsync(db, capture.Summary, default);
        Assert.Contains(partial.Findings, finding => finding.Code == "engine-journal-manifest-invalid");
    }

    [Fact]
    public async Task RetentionRemovesPagesBeforeTheirParentAndRespectsAHold()
    {
        var db = Database();
        const string gameId = "2048";
        var instance = Guid.NewGuid().ToString("N");
        var capture = RoundAuditCapture.Create(gameId, instance, instance, 1, Now, Now.AddSeconds(1),
            new { score = 42 }, "manifest-test-v1", "manifest-test-v1", [], []);
        var fields = RoundAuditDocuments.Create(capture).Summary;
        fields["retentionState"] = "deleted";
        fields["securityAuditPending"] = false;
        fields["evidencePresent"] = false;
        fields["rawEvidencePurgePending"] = true;
        fields["rawEvidenceNextAttemptAtUtc"] = Now;
        var summary = db.Collection(RoundAuditDocuments.Summaries).Document(capture.Summary.Id);
        var hold = db.Collection(InvestigationDocuments.InstanceHolds).Document(InvestigationDocuments.InstanceKey(gameId, instance));
        var root = db.Collection(GameSecurityEventDocuments.ManifestCollectionName)
            .Document(GameSecurityEventDocuments.ManifestId(gameId, instance));
        var pages = root.Collection(GameSecurityEventDocuments.ManifestPageCollectionName);
        var batch = db.StartBatch();
        batch.Create(summary, fields);
        batch.Create(hold, new Dictionary<string, object> { ["held"] = true });
        batch.Create(root, new Dictionary<string, object>
        { ["gameId"] = gameId, ["instanceId"] = instance, ["schemaVersion"] = GameSecurityEventDocuments.PagedManifestVersion });
        for (var index = 0; index < 26; index++)
            batch.Create(pages.Document(GameSecurityEventDocuments.ManifestPageId(index)),
                new Dictionary<string, object> { ["pageIndex"] = index });
        await batch.CommitAsync();

        var clock = new RetentionClock { Now = Now.AddMinutes(1) };
        var retention = new SecurityAuditRawRetention(db, Options.Create(new GameRoundRetentionOptions()), clock);
        await retention.SweepAsync(default);
        Assert.Equal(26, (await pages.GetSnapshotAsync()).Count);

        await hold.UpdateAsync(new Dictionary<string, object> { ["held"] = false });
        clock.Now = clock.Now.AddHours(2);
        await retention.SweepAsync(default);
        Assert.True((await root.GetSnapshotAsync()).Exists);
        Assert.Single((await pages.GetSnapshotAsync()).Documents);
        await retention.SweepAsync(default);
        Assert.Empty((await pages.GetSnapshotAsync()).Documents);
        Assert.True((await root.GetSnapshotAsync()).Exists);
        await retention.SweepAsync(default);
        Assert.False((await root.GetSnapshotAsync()).Exists);
        Assert.False((await summary.GetSnapshotAsync()).GetValue<bool>("rawEvidencePurgePending"));
    }

    private sealed class RetentionClock : TimeProvider
    {
        public DateTime Now { get; set; }
        public override DateTimeOffset GetUtcNow() => new(Now);
    }

    [Fact]
    public async Task ArcaneRawRoundRetentionRespectsHoldsAndPreservesTheOperationKey()
    {
        var db = Database();
        const string game = ArcaneFeatureEndpoints.AuditGameId;
        var spin = Guid.NewGuid().ToString("N");
        var admission = Guid.NewGuid().ToString("N");
        var captured = RoundAuditCapture.Create(game, spin, spin, 1, Now, Now.AddSeconds(1), new { },
            "arcane-audit-test", "arcane-audit-test", [], []);
        var fields = RoundAuditDocuments.Create(captured).Summary;
        fields["retentionState"] = "deleted";
        fields["securityAuditPending"] = false;
        fields["evidencePresent"] = false;
        fields["rawEvidencePurgePending"] = true;
        fields["rawEvidenceNextAttemptAtUtc"] = Now;
        var summary = db.Collection(RoundAuditDocuments.Summaries).Document(captured.Summary.Id);
        var hold = db.Collection(InvestigationDocuments.InstanceHolds).Document(InvestigationDocuments.InstanceKey(game, spin));
        var raw = db.Collection(ArcaneRoundStore.RawCollectionName).Document(admission);
        var operation = db.Collection("slotPreparedOperations").Document("arcane-held-operation");
        var batch = db.StartBatch();
        batch.Create(summary, fields);
        batch.Create(hold, new Dictionary<string, object> { ["held"] = true });
        batch.Create(raw, new Dictionary<string, object>
        {
            ["schemaVersion"] = 2L, ["owner"] = "arcane-game-v1", ["status"] = "settled",
            ["spinId"] = spin, ["admissionId"] = admission, ["userId"] = "account-1",
            ["gameId"] = ArcaneArchivesEngine.GameId, ["operationId"] = operation.Id, ["requestId"] = "arcane-original-request"
        });
        batch.Create(operation, new Dictionary<string, object> { ["admissionId"] = admission });
        await batch.CommitAsync();

        var clock = new RetentionClock { Now = Now.AddMinutes(1) };
        var retention = new SecurityAuditRawRetention(db, Options.Create(new GameRoundRetentionOptions()), clock);
        await retention.SweepAsync(default);
        Assert.True((await raw.GetSnapshotAsync()).Exists);

        await hold.UpdateAsync("held", false);
        clock.Now = clock.Now.AddHours(2);
        await retention.SweepAsync(default);
        Assert.False((await raw.GetSnapshotAsync()).Exists);
        Assert.True((await operation.GetSnapshotAsync()).Exists);
        Assert.True((await db.Collection("gameSecurityRequestRetentionQueue").Document("arcane-original-request")
            .GetSnapshotAsync()).Exists);
    }

    private FirestoreDb Database() => new FirestoreDbBuilder
    {
        ProjectId = "demo-journal-manifest-" + Guid.NewGuid().ToString("N"), Endpoint = fixture.Endpoint,
        ChannelCredentials = Grpc.Core.ChannelCredentials.Insecure,
        EmulatorDetection = Google.Api.Gax.EmulatorDetection.None
    }.Build();
}
