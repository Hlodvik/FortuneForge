using FortuneForge.Games.Abstractions.Auditing;
using FortuneForge.Server.RoundAudit;
using FortuneForge.Server.SecurityAudit;
using FortuneForge.Server.Tests.Blackjack.Table;
using Google.Cloud.Firestore;
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

    private FirestoreDb Database() => new FirestoreDbBuilder
    {
        ProjectId = "demo-journal-manifest-" + Guid.NewGuid().ToString("N"), Endpoint = fixture.Endpoint,
        ChannelCredentials = Grpc.Core.ChannelCredentials.Insecure,
        EmulatorDetection = Google.Api.Gax.EmulatorDetection.None
    }.Build();
}
