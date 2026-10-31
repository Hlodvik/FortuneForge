using FortuneForge.Games.Abstractions.Auditing;
using FortuneForge.Server.Risk;
using FortuneForge.Server.Tests.Blackjack.Table;
using Google.Cloud.Firestore;
using Xunit;

namespace FortuneForge.Server.Tests.Risk;

[Collection("Blackjack table Firestore emulator")]
public sealed class RiskIncidentFirestoreTests(BlackjackTableFirestoreEmulatorFixture fixture)
{
    private sealed class Clock : TimeProvider
    {
        internal DateTime Now = new(2026, 10, 26, 12, 0, 0, DateTimeKind.Utc);
        public override DateTimeOffset GetUtcNow() => new(Now);
    }

    [Fact]
    public async Task Concurrent_retry_is_one_incident_and_one_restriction_with_atomic_evidence_hold()
    {
        var db = Database(); var clock = new Clock(); var store = new FirestoreRiskIncidentStore(db, clock);
        var evidence = await Evidence(db, clock, "blackjack", "round-one");
        var draft = Draft("one", evidence);
        var attempts = await Task.WhenAll(Enumerable.Range(0, 4).Select(_ => store.CreateAsync(draft, default)));
        Assert.Single(attempts, value => value.Changed);
        var state = await store.GetStateAsync("alice", default);
        Assert.Equal(1, state.Version);
        Assert.True(state.WithdrawalsBlocked); Assert.False(state.AccountRestricted);
        Assert.Equal(new RiskLevelCounts(0, 1, 0), state.CountsByGame["blackjack"]);
        Assert.Single((await db.Collection(FirestoreRiskIncidentStore.Incidents).GetSnapshotAsync()).Documents);
        var summary = await Summary(db, evidence);
        Assert.Equal("held-risk", summary.GetValue<string>("retentionState"));
        Assert.Equal([attempts[0].Incident.Id], summary.GetValue<List<string>>("riskHoldIncidentIds"));
        Assert.Null(summary.GetValue<object>("evidenceDeleteAfterUtc"));
        await Assert.ThrowsAsync<RiskIncidentConflictException>(() => store.CreateAsync(draft with { RuleVersion = "2" }, default));
        await Assert.ThrowsAsync<RiskIncidentConflictException>(() => store.CreateAsync(draft with { Severity = "critical" }, default));
        Assert.Equal(1, (await store.GetStateAsync("alice", default)).Version);
    }

    [Fact]
    public async Task Unattributed_integrity_errors_foreign_actor_or_missing_evidence_cannot_create_player_strikes()
    {
        var db = Database(); var clock = new Clock(); var store = new FirestoreRiskIncidentStore(db, clock);
        var evidence = await Evidence(db, clock, "blackjack", "round-one");
        await Assert.ThrowsAsync<ArgumentException>(() => store.CreateAsync(Draft("raw", evidence) with { SourceKind = "round-integrity" }, default));
        await Assert.ThrowsAsync<ArgumentException>(() => store.CreateAsync(Draft("blank", evidence) with { AttributionBasis = " " }, default));
        await Assert.ThrowsAsync<RiskIncidentConflictException>(() => store.CreateAsync(Draft("wrong-actor", evidence) with { UserId = "bob" }, default));
        await Assert.ThrowsAsync<RiskIncidentConflictException>(() => store.CreateAsync(Draft("wrong-round", evidence) with { RoundId = "other-round" }, default));
        await Assert.ThrowsAsync<RiskIncidentConflictException>(() => store.CreateAsync(Draft("missing", "missing"), default));
        var botEvidence = await Evidence(db, clock, "blackjack", "bot-round", false);
        await Assert.ThrowsAsync<RiskIncidentConflictException>(() => store.CreateAsync(Draft("bot", botEvidence) with { RoundId = "bot-round" }, default));
        Assert.Empty((await db.Collection(FirestoreRiskIncidentStore.Incidents).GetSnapshotAsync()).Documents);
        Assert.False((await store.GetStateAsync("alice", default)).WithdrawalsBlocked);
    }

    [Fact]
    public async Task Separate_game_thresholds_are_durable_and_clearing_one_high_lifts_cross_game_restriction()
    {
        var db = Database(); var clock = new Clock(); var store = new FirestoreRiskIncidentStore(db, clock);
        RiskIncident? last = null;
        foreach (var game in new[] { "blackjack", "mines" })
            for (var index = 0; index < 2; index++)
            {
                var round = game + index; var evidence = await Evidence(db, clock, game, round);
                last = (await store.CreateAsync(Draft(round, evidence) with { GameId = game, RoundId = round }, default)).Incident;
            }
        var state = await store.GetStateAsync("alice", default);
        Assert.True(state.AccountRestricted); Assert.True(state.WithdrawalsBlocked);
        Assert.Equal(["blackjack", "mines"], state.BlockedGameIds);
        var cleared = await store.ResolveAsync(last!.Id, "admin", "cleared", "Review found a client defect, not attributed abuse.", default);
        Assert.False(cleared.State.AccountRestricted); Assert.True(cleared.State.WithdrawalsBlocked);
        Assert.Equal(["blackjack"], cleared.State.BlockedGameIds);
        Assert.Equal(5, cleared.State.Version);
    }

    [Fact]
    public async Task Shared_evidence_stays_held_until_all_cases_clear_and_latest_resolution_extends_thirty_days()
    {
        var db = Database(); var clock = new Clock(); var store = new FirestoreRiskIncidentStore(db, clock);
        var evidence = await Evidence(db, clock, "blackjack", "round-one");
        var one = await store.CreateAsync(Draft("one", evidence), default);
        var two = await store.CreateAsync(Draft("two", evidence), default);
        var firstDate = clock.Now;
        await store.ResolveAsync(one.Incident.Id, "admin", "cleared", "First independent case cleared.", default);
        var held = await Summary(db, evidence);
        Assert.Equal("held-risk", held.GetValue<string>("retentionState"));
        Assert.Null(held.GetValue<object>("evidenceDeleteAfterUtc"));
        Assert.Equal(firstDate.AddDays(30), held.GetValue<Timestamp>("riskRetainUntilUtc").ToDateTime());
        clock.Now = clock.Now.AddDays(4);
        var last = await store.ResolveAsync(two.Incident.Id, "admin", "cleared", "Second independent case cleared.", default);
        Assert.False(last.State.WithdrawalsBlocked);
        var released = await Summary(db, evidence);
        Assert.Empty(released.GetValue<List<string>>("riskHoldIncidentIds"));
        Assert.Equal("eligible-resolved", released.GetValue<string>("retentionState"));
        Assert.Equal(clock.Now.AddDays(30), released.GetValue<Timestamp>("evidenceDeleteAfterUtc").ToDateTime());
        clock.Now = clock.Now.AddDays(3);
        Assert.False((await store.ResolveAsync(two.Incident.Id, "admin", "cleared", "Repeated request.", default)).Changed);
        Assert.Equal(released.GetValue<Timestamp>("evidenceDeleteAfterUtc"), (await Summary(db, evidence)).GetValue<Timestamp>("evidenceDeleteAfterUtc"));
    }

    [Fact]
    public async Task Upheld_evidence_is_permanent_and_admin_correction_lifts_only_its_own_hold_with_review_trail()
    {
        var db = Database(); var clock = new Clock(); var store = new FirestoreRiskIncidentStore(db, clock);
        var evidence = await Evidence(db, clock, "blackjack", "round-one");
        var one = await store.CreateAsync(Draft("one", evidence) with { Severity = "critical" }, default);
        var two = await store.CreateAsync(Draft("two", evidence), default);
        var upheld = await store.ResolveAsync(one.Incident.Id, "reviewer-one", "upheld", "Evidence confirmed this attributed action.", default);
        Assert.True(upheld.State.AccountRestricted);
        await store.ResolveAsync(two.Incident.Id, "reviewer-two", "cleared", "Other case cleared.", default);
        var preserved = await Summary(db, evidence);
        Assert.Equal("preserved", preserved.GetValue<string>("retentionState"));
        Assert.Equal([one.Incident.Id], preserved.GetValue<List<string>>("riskPreservedIncidentIds"));
        Assert.Null(preserved.GetValue<object>("evidenceDeleteAfterUtc"));
        clock.Now = clock.Now.AddDays(5);
        var corrected = await store.ResolveAsync(one.Incident.Id, "senior-reviewer", "cleared", "Appeal identified a mistaken attribution.", default);
        Assert.False(corrected.State.AccountRestricted); Assert.False(corrected.State.WithdrawalsBlocked);
        Assert.Equal(2, corrected.Incident.Reviews!.Count);
        Assert.Equal("upheld", corrected.Incident.Reviews[0].Outcome);
        Assert.Equal("upheld", corrected.Incident.Reviews[1].PreviousOutcome);
        Assert.Equal("senior-reviewer", corrected.Incident.Reviews[1].ReviewedBy);
        var released = await Summary(db, evidence);
        Assert.Empty(released.GetValue<List<string>>("riskPreservedIncidentIds"));
        Assert.Equal(clock.Now.AddDays(30), released.GetValue<Timestamp>("evidenceDeleteAfterUtc").ToDateTime());
        var restored = await store.ResolveAsync(one.Incident.Id, "senior-reviewer", "upheld", "New retained evidence changed the decision.", default);
        Assert.True(restored.State.AccountRestricted);
        Assert.Equal(3, restored.Incident.Reviews!.Count);
        Assert.Equal("preserved", (await Summary(db, evidence)).GetValue<string>("retentionState"));
    }

    [Fact]
    public async Task Cleared_case_cannot_be_upheld_after_its_detailed_evidence_was_deleted()
    {
        var db = Database(); var clock = new Clock(); var store = new FirestoreRiskIncidentStore(db, clock);
        var evidence = await Evidence(db, clock, "blackjack", "round-one");
        var created = await store.CreateAsync(Draft("one", evidence), default);
        await store.ResolveAsync(created.Incident.Id, "admin", "cleared", "Case cleared.", default);
        await db.Collection(RoundAuditDocuments.Evidence).Document(evidence).DeleteAsync();
        await db.Collection(RoundAuditDocuments.Summaries).Document(evidence).UpdateAsync("evidencePresent", false);
        await Assert.ThrowsAsync<RiskIncidentConflictException>(() => store.ResolveAsync(created.Incident.Id,
            "admin", "upheld", "Attempt to reverse without the retained proof.", default));
        Assert.False((await store.GetStateAsync("alice", default)).WithdrawalsBlocked);
        Assert.Equal("cleared", (await store.GetIncidentAsync(created.Incident.Id, default))!.ResolutionOutcome);
    }

    [Fact]
    public async Task Explicit_review_can_change_severity_on_same_incident_without_duplicate_strikes_or_automatic_escalation()
    {
        var db = Database(); var clock = new Clock(); var store = new FirestoreRiskIncidentStore(db, clock);
        var evidence = await Evidence(db, clock, "blackjack", "round-one");
        var created = await store.CreateAsync(Draft("one", evidence), default);
        var confirmed = await store.ResolveAsync(created.Incident.Id, "admin", "upheld", "Confirmed at its original level.", default);
        Assert.False(confirmed.State.AccountRestricted); Assert.Equal("high", confirmed.Incident.Severity);
        var critical = await store.ResolveAsync(created.Incident.Id, "senior-admin", "upheld", "Explicit review requires account restriction.", default, "critical");
        Assert.True(critical.State.AccountRestricted);
        Assert.Equal(new RiskLevelCounts(0, 0, 1), critical.State.CountsByGame["blackjack"]);
        Assert.Equal("high", critical.Incident.Reviews![1].PreviousSeverity);
        Assert.Equal("critical", critical.Incident.Reviews[1].Severity);
        Assert.False((await store.ResolveAsync(created.Incident.Id, "senior-admin", "upheld", "Retry.", default, "critical")).Changed);
        var reduced = await store.ResolveAsync(created.Incident.Id, "senior-admin", "upheld", "Corrected classification.", default, "possible");
        Assert.False(reduced.State.AccountRestricted); Assert.False(reduced.State.WithdrawalsBlocked);
        Assert.Equal(new RiskLevelCounts(1, 0, 0), reduced.State.CountsByGame["blackjack"]);
        var cleared = await store.ResolveAsync(created.Incident.Id, "admin", "cleared", "Attribution cleared after further review.", default);
        Assert.Empty(cleared.State.CountsByGame);
        Assert.Single((await store.ListAsync("alice", 25, default)));
        Assert.Equal(5, cleared.State.Version);
        Assert.Empty(await store.ListContributingAsync("alice", 100, default));
        Assert.Single(await store.ListAsync("alice", 100, default));
        await Assert.ThrowsAsync<ArgumentException>(() => store.ResolveAsync(created.Incident.Id, "admin", "upheld", "Invalid severity.", default, "banned"));
    }

    private static RiskIncidentDraft Draft(string source, string evidence) => new("alice", "blackjack", "round-one", "high",
        "admin-review", source, "explicit-attribution", "1", "Reviewed action and retained source evidence identify this actor.", [evidence], "admin");

    private static async Task<string> Evidence(FirestoreDb db, Clock clock, string game, string round, bool human = true)
    {
        var capture = RoundAuditCapture.Create(game, round, "table", 1, clock.Now.AddMinutes(-1), clock.Now,
            new { cards = new[] { "AS", "KD" } }, "test-evidence", "test-v1",
            [new RoundAuditParticipant("alice", "Alice", human, 100, 0, -100)],
            [new RoundAuditFinding("unattributed-integrity-error", "error", "An error is evidence for review, not actor culpability.")]);
        var docs = RoundAuditDocuments.Create(capture);
        await db.Collection(RoundAuditDocuments.Summaries).Document(capture.Summary.Id).SetAsync(docs.Summary);
        await db.Collection(RoundAuditDocuments.Evidence).Document(capture.Summary.Id).SetAsync(docs.Evidence);
        return capture.Summary.Id;
    }

    private static Task<DocumentSnapshot> Summary(FirestoreDb db, string id) => db.Collection(RoundAuditDocuments.Summaries).Document(id).GetSnapshotAsync();
    private FirestoreDb Database() => new FirestoreDbBuilder
    {
        ProjectId = "demo-risk-" + Guid.NewGuid().ToString("N"), Endpoint = fixture.Endpoint,
        ChannelCredentials = Grpc.Core.ChannelCredentials.Insecure, EmulatorDetection = Google.Api.Gax.EmulatorDetection.None,
    }.Build();
}
