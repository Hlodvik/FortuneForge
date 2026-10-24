using FortuneForge.Server.GameReports;
using FortuneForge.Server.Tests.Blackjack.Table;
using Google.Cloud.Firestore;
using Xunit;

namespace FortuneForge.Server.Tests.GameReports;

[Collection("Blackjack table Firestore emulator")]
public sealed class GameReportsFirestoreTests(BlackjackTableFirestoreEmulatorFixture fixture)
{
    private static readonly DateTime Now = new(2026, 10, 24, 18, 0, 0, DateTimeKind.Utc);
    [Fact]
    public async Task Reports_persist_across_store_instances_and_identical_retries_create_one_record()
    {
        var db = Database(); var store = new FirestoreGameReportsStore(db); var draft = Draft(new string('a', 64));
        var first = await store.CreateAsync(draft, default); var retry = await store.CreateAsync(draft, default);
        Assert.Equal(first, retry); Assert.Equal(first, await new FirestoreGameReportsStore(db).GetAsync(first.Id, default));
        Assert.Single((await db.Collection(FirestoreGameReportsStore.CollectionName).GetSnapshotAsync()).Documents);
        await Assert.ThrowsAsync<GameReportConflictException>(() => store.CreateAsync(draft with { Fingerprint = "changed-message" }, default));
    }
    [Fact]
    public async Task Status_and_private_note_update_atomically_with_revision_guard_and_audit_history()
    {
        var db = Database(); var store = new FirestoreGameReportsStore(db); var report = await store.CreateAsync(Draft(new string('a', 64)), default);
        var next = await store.UpdateAsync(new(report.Id, "in-review", 0, "Reproduced on phone", "opaque-admin", Now.AddMinutes(1)), default);
        Assert.NotNull(next); Assert.Equal(1, next.Version); Assert.Equal("in-review", next.Status); Assert.Equal("Reproduced on phone", next.AdminNote);
        var persisted = await new FirestoreGameReportsStore(db).GetAsync(report.Id, default); Assert.Equal(next, persisted);
        var retriedAfterReview = await store.CreateAsync(Draft(report.Id), default);
        Assert.Equal(next, retriedAfterReview);
        Assert.Single((await db.Collection(FirestoreGameReportsStore.CollectionName).GetSnapshotAsync()).Documents);
        await Assert.ThrowsAsync<GameReportConflictException>(() => store.UpdateAsync(new(report.Id, "closed", 0, "", "another-admin", Now), default));
        var audit = await db.Collection(FirestoreGameReportsStore.CollectionName).Document(report.Id).Collection("statusHistory").GetSnapshotAsync();
        var row = Assert.Single(audit.Documents); Assert.Equal("open", row.GetValue<string>("before")); Assert.Equal("in-review", row.GetValue<string>("after"));
        Assert.Equal("opaque-admin", row.GetValue<string>("actorId"));
    }
    [Fact]
    public async Task Timestamp_ties_page_without_duplicates_or_missing_rows()
    {
        var store = new FirestoreGameReportsStore(Database());
        foreach (var id in new[] { new string('a', 64), new string('b', 64), new string('c', 64) }) await store.CreateAsync(Draft(id), default);
        var first = await store.ListAsync(2, null, null, default); Assert.Equal([new string('c', 64), new string('b', 64)], first.Select(item => item.Id));
        var next = await store.ListAsync(2, first[^1].CreatedAtUtc, first[^1].Id, default); Assert.Equal(new string('a', 64), Assert.Single(next).Id);
    }
    private FirestoreDb Database() => new FirestoreDbBuilder { ProjectId = "demo-reports-" + Guid.NewGuid().ToString("N"), Endpoint = fixture.Endpoint,
        ChannelCredentials = Grpc.Core.ChannelCredentials.Insecure, EmulatorDetection = Google.Api.Gax.EmulatorDetection.None }.Build();
    private static GameReportDraft Draft(string id) => new(id, "reporting-player", "andar-bahar", "Andar Bahar", "/games/andar-bahar", "bug", "The button stopped responding.", "same-payload", Now);
}
