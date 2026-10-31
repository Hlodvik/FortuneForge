using FortuneForge.Games.Abstractions.Analytics;
using FortuneForge.Games.Abstractions.Auditing;
using FortuneForge.Server.Admin.Operations;
using FortuneForge.Server.Risk;
using FortuneForge.Server.RoundAudit;
using FortuneForge.Server.Tests.Blackjack.Table;
using Google.Cloud.Firestore;
using Microsoft.Extensions.Options;
using Xunit;

namespace FortuneForge.Server.Tests.RoundAudit;

[Collection("Blackjack table Firestore emulator")]
public sealed class GameRoundRetentionTests(BlackjackTableFirestoreEmulatorFixture fixture)
{
    private sealed class Clock : TimeProvider
    {
        internal DateTime Now = new(2026, 10, 26, 12, 0, 0, DateTimeKind.Utc);
        public override DateTimeOffset GetUtcNow() => new(Now);
    }
    private FirestoreDb Database() => new FirestoreDbBuilder { ProjectId = $"demo-retention-{Guid.NewGuid():N}", Endpoint = fixture.Endpoint, ChannelCredentials = Grpc.Core.ChannelCredentials.Insecure, EmulatorDetection = Google.Api.Gax.EmulatorDetection.None }.Build();
    private static GameRoundRetentionService Worker(FirestoreDb db, Clock clock) => new(db, Options.Create(new GameRoundRetentionOptions()), clock);
    private static async Task<string> Evidence(FirestoreDb db, Clock clock, string round = "round", IReadOnlyList<RoundAuditFinding>? findings = null)
    {
        var capture = RoundAuditCapture.Create("blackjack", round, "table", 1, clock.Now, clock.Now,
            new { cards = new[] { "AS", "KH" }, action = "stand" }, "test-v1", "test-v1", [new("alice", "Alice", true, 100, 200, 100)], findings ?? []);
        var documents = RoundAuditDocuments.Create(capture);
        // These tests isolate retention after the independently scheduled audit projection completed.
        documents.Summary["securityAuditPending"] = false;
        await db.Collection(RoundAuditDocuments.Summaries).Document(capture.Summary.Id).CreateAsync(documents.Summary);
        await db.Collection(RoundAuditDocuments.Evidence).Document(capture.Summary.Id).CreateAsync(documents.Evidence);
        return capture.Summary.Id;
    }
    private static Task<DocumentSnapshot> Summary(FirestoreDb db, string id) => db.Collection(RoundAuditDocuments.Summaries).Document(id).GetSnapshotAsync();
    private static Task<DocumentSnapshot> Payload(FirestoreDb db, string id) => db.Collection(RoundAuditDocuments.Evidence).Document(id).GetSnapshotAsync();
    private static RiskIncidentDraft Draft(string id) => new("alice", "blackjack", "round", "high", "admin-review", "source-one", "proof", "1", "The player's attributed action was reviewed.", [id], "admin");

    [Fact]
    public async Task Clean_payload_expires_seven_days_after_processing_not_round_time_and_summary_ledger_remain()
    {
        var db = Database(); var clock = new Clock(); var id = await Evidence(db, clock);
        await db.Collection("balanceTransactions").Document("retained").CreateAsync(new { amount = 100 });
        clock.Now = clock.Now.AddDays(2); var worker = Worker(db, clock);
        await worker.SweepAsync(default);
        var summary = await Summary(db, id);
        Assert.Equal("eligible", summary.GetValue<string>("retentionState"));
        Assert.Equal(clock.Now.AddDays(7), summary.GetValue<Timestamp>("evidenceDeleteAfterUtc").ToDateTime());
        clock.Now = clock.Now.AddDays(6); await worker.SweepAsync(default); Assert.True((await Payload(db, id)).Exists);
        clock.Now = clock.Now.AddDays(1); await worker.SweepAsync(default);
        Assert.False((await Payload(db, id)).Exists); Assert.True((await Summary(db, id)).Exists);
        Assert.True((await db.Collection("balanceTransactions").Document("retained").GetSnapshotAsync()).Exists);
        Assert.False((await Summary(db, id)).GetValue<bool>("evidencePresent"));
    }

    [Fact]
    public async Task Incomplete_or_changed_evidence_is_held_and_never_creates_player_strike()
    {
        var db = Database(); var clock = new Clock(); var id = await Evidence(db, clock);
        var incomplete = await Evidence(db, clock, "partial", [new("missing-timeline", "warning", "The full action timeline is missing.")]);
        await db.Collection(RoundAuditDocuments.Evidence).Document(id).UpdateAsync("payloadJson", "changed");
        await Worker(db, clock).SweepAsync(default);
        Assert.Equal("flagged", (await Summary(db, id)).GetValue<string>("auditStatus"));
        Assert.Equal("held", (await Summary(db, incomplete)).GetValue<string>("retentionState"));
        clock.Now = clock.Now.AddDays(400); await Worker(db, clock).SweepAsync(default);
        Assert.True((await Payload(db, id)).Exists); Assert.True((await Payload(db, incomplete)).Exists);
        Assert.Empty((await db.Collection(FirestoreRiskIncidentStore.Incidents).GetSnapshotAsync()).Documents);
    }

    [Fact]
    public async Task Pending_independent_audit_blocks_clean_eligibility_and_expiry()
    {
        var db = Database(); var clock = new Clock(); var id = await Evidence(db, clock);
        await db.Collection(RoundAuditDocuments.Summaries).Document(id).UpdateAsync("securityAuditPending", true);
        clock.Now = clock.Now.AddDays(50);
        await Worker(db, clock).SweepAsync(default);
        var summary = await Summary(db, id);
        Assert.Equal("unreviewed", summary.GetValue<string>("retentionState"));
        Assert.Null(summary.GetValue<Timestamp?>("evidenceDeleteAfterUtc"));
        Assert.True((await Payload(db, id)).Exists);
    }

    [Fact]
    public async Task Open_flag_overrides_due_clean_deadline_cleared_flag_waits_thirty_days()
    {
        var db = Database(); var clock = new Clock(); var id = await Evidence(db, clock); var worker = Worker(db, clock);
        await worker.SweepAsync(default);
        var risks = new FirestoreRiskIncidentStore(db, clock); var incident = await risks.CreateAsync(Draft(id), default);
        clock.Now = clock.Now.AddDays(40); await worker.SweepAsync(default); Assert.True((await Payload(db, id)).Exists);
        await risks.ResolveAsync(incident.Incident.Id, "admin", "cleared", "The reported conduct was legitimate.", default);
        clock.Now = clock.Now.AddDays(29); await worker.SweepAsync(default); Assert.True((await Payload(db, id)).Exists);
        clock.Now = clock.Now.AddDays(1); await worker.SweepAsync(default); Assert.False((await Payload(db, id)).Exists);
        Assert.True((await Summary(db, id)).Exists);
    }

    [Fact]
    public async Task Upheld_evidence_remains_permanently_even_with_accidentally_stale_expiry()
    {
        var db = Database(); var clock = new Clock(); var id = await Evidence(db, clock); var worker = Worker(db, clock);
        var risks = new FirestoreRiskIncidentStore(db, clock); var incident = await risks.CreateAsync(Draft(id), default);
        await risks.ResolveAsync(incident.Incident.Id, "admin", "upheld", "Reviewed evidence confirms the attributed misconduct.", default);
        await db.Collection(RoundAuditDocuments.Summaries).Document(id).UpdateAsync(new Dictionary<string, object> { ["retentionState"] = "eligible", ["evidenceDeleteAfterUtc"] = Timestamp.FromDateTime(clock.Now) });
        clock.Now = clock.Now.AddYears(10); await worker.SweepAsync(default);
        Assert.True((await Payload(db, id)).Exists);
        Assert.Equal("upheld", (await risks.ListAsync("alice", 100, default)).Single().ResolutionOutcome);
    }

    [Fact]
    public async Task Admin_reader_handles_nullable_dates_retained_participants_and_private_payload()
    {
        var db = Database(); var clock = new Clock(); var id = await Evidence(db, clock);
        var store = new FirestoreGameRoundAuditStore(db, Options.Create(new AdminOperationsOptions { CursorSigningKey = "retention-tests-signing-key-with-32-characters" }));
        var detail = await store.ReadAsync(id, default);
        Assert.NotNull(detail); Assert.Null(detail.Summary.EvidenceDeleteAfterUtc);
        Assert.Equal("alice", detail.Summary.Participants.Single().ActorId); Assert.Contains("AS", detail.PayloadJson);
        Assert.Single((await store.ListAsync("blackjack", "passed", 25, null, default)).Items);
    }

    [Fact]
    public void Source_receipt_keeps_gross_return_and_net_distinct_and_stable_under_retry()
    {
        var now = new Clock().Now;
        var a = SinglePlayerRoundRecord.Create("mines", "round", "alice", 500, 1250, now, now, "minesRounds", "round", new { diamonds = 3 }, "mines-v1");
        var b = SinglePlayerRoundRecord.Create("mines", "round", "alice", 500, 1250, now, now, "minesRounds", "round", new { diamonds = 3 }, "mines-v1");
        Assert.Equal(a.Settlement.Id, b.Settlement.Id); Assert.Equal(a.Audit.Summary.Id, b.Audit.Summary.Id);
        var human = a.Audit.Summary.Participants.Single(); Assert.Equal(500, human.WagerCents); Assert.Equal(1250, human.PayoutCents); Assert.Equal(750, human.NetCents);
        Assert.Contains(a.Audit.Summary.Findings, finding => finding.Code == "audit-scope");
    }
}
