using System.Text.Json;
using FortuneForge.Games.Abstractions.Auditing;
using FortuneForge.Server.Admin.Operations;
using FortuneForge.Server.Risk;
using FortuneForge.Server.RoundAudit;
using FortuneForge.Server.SecurityAudit;
using FortuneForge.Server.Tests.Blackjack.Table;
using Google.Cloud.Firestore;
using Microsoft.Extensions.Options;
using Xunit;

namespace FortuneForge.Server.Tests.SecurityAudit;

[Collection("Blackjack table Firestore emulator")]
public sealed class InvestigationFirestoreTests(BlackjackTableFirestoreEmulatorFixture fixture)
{
    private sealed class Clock : TimeProvider
    {
        internal DateTime Now = new(2026, 10, 26, 20, 0, 0, DateTimeKind.Utc);
        public override DateTimeOffset GetUtcNow() => new(Now);
    }

    [Fact]
    public void Copy_identity_separates_audits_and_full_source_paths_without_reusing_legacy_keys()
    {
        const string path = "projects/demo-test/databases/(default)/documents/creditHoldemMatches/shared-table";
        var first = SecurityAuditInvestigationService.EvidenceCopyId("case", "hand-one", "source-document", path);
        Assert.Equal(first, SecurityAuditInvestigationService.EvidenceCopyId("case", "hand-one", "source-document", path));
        Assert.NotEqual(first, SecurityAuditInvestigationService.EvidenceCopyId("case", "hand-two", "source-document", path));
        Assert.NotEqual(first, SecurityAuditInvestigationService.EvidenceCopyId("case", "hand-one", "source-document",
            path.Replace("creditHoldemMatches", "heartsPaidMatches", StringComparison.Ordinal)));
        Assert.NotEqual(first, InvestigationDocuments.Hash("case\nsource-document\nshared-table"));
    }

    [Fact]
    public async Task Two_hands_sharing_source_and_request_have_per_audit_immutable_copies_and_bounded_pages()
    {
        var db = Database(); var clock = new Clock(); var service = Service(db, clock);
        var source = db.Collection(InvestigationDocuments.RequestIntents).Document("shared-request");
        await source.SetAsync(new Dictionary<string, object> { ["marker"] = "first-source-snapshot" });
        var hands = new[] { await SourceRound(db, clock, "shared-table-hand-1", source),
            await SourceRound(db, clock, "shared-table-hand-2", source) };
        await db.Collection("creditHoldemMatches").Document("shared-table")
            .SetAsync(new Dictionary<string, object> { ["marker"] = "shared-authoritative-table" });
        await db.Collection(InvestigationDocuments.RequestIntents).Document("shared-request").SetAsync(new Dictionary<string, object>
        { ["requestId"] = "shared-request", ["gameId"] = "texas-holdem", ["instanceId"] = null!, ["userId"] = "alice" }, SetOptions.MergeAll);
        await db.Collection(InvestigationDocuments.RequestResults).Document("shared-request").SetAsync(new Dictionary<string, object>
        { ["requestId"] = "shared-request", ["outcome"] = "http-response", ["httpStatusCode"] = 200L });
        foreach (var hand in hands)
            await db.Collection(InvestigationDocuments.Events).Document(hand.Summary.RoundId).SetAsync(new Dictionary<string, object>
            {
                ["gameId"] = "texas-holdem", ["instanceId"] = hand.Summary.RoundId,
                ["requestId"] = "shared-request", ["payloadJson"] = "{}"
            });
        var investigation = await service.PreserveAsync("behavioral-review", "shared-source-two-hands",
            hands.Select(hand => hand.Summary.Id).ToArray(), [], [], 1, default);
        await service.ProcessAsync(default);
        var copies = await db.Collection(InvestigationDocuments.Evidence).WhereEqualTo("caseId", investigation.Id).GetSnapshotAsync();
        var sharedSources = copies.Documents.Where(copy => copy.GetValue<string>("kind") == "request-intent").ToArray();
        Assert.Equal(2, sharedSources.Length);
        Assert.Equal(hands.Select(hand => hand.Summary.Id).Order(), sharedSources.Select(copy => copy.GetValue<string>("auditId")).Order());
        Assert.All(sharedSources, copy =>
        {
            Assert.Equal(source.Path, copy.GetValue<string>("sourcePath"));
            Assert.Equal(2L, copy.GetValue<long>("copyKeyVersion"));
            Assert.Equal("first-source-snapshot", copy.GetValue<Dictionary<string, object>>("originalFields")["marker"]);
        });
        Assert.Equal(2, copies.Documents.Count(copy => copy.GetValue<string>("kind") == "request-intent"));
        Assert.Equal(2, copies.Documents.Count(copy => copy.GetValue<string>("kind") == "request-result"));
        Assert.Equal(2, copies.Documents.Count(copy => copy.GetValue<string>("kind") == "table-state"));
        foreach (var hand in hands)
        {
            var page = await service.ReadEvidenceAsync(investigation.Id, hand.Summary.Id, 100, null, default);
            Assert.Contains("Per-audit/full-source-path", page.LinkageCoverage);
            Assert.Contains(page.Items, item => Assert.IsType<Dictionary<string, object?>>(item["data"])["kind"] as string == "request-intent");
            Assert.Contains(page.Items, item => Assert.IsType<Dictionary<string, object?>>(item["data"])["kind"] as string == "table-state");
            var one = await service.ReadEvidenceAsync(investigation.Id, hand.Summary.Id, 1, null, default);
            Assert.Single(one.Items); Assert.NotNull(one.NextCursor);
            var next = await service.ReadEvidenceAsync(investigation.Id, hand.Summary.Id, 1, one.NextCursor, default);
            Assert.Single(next.Items); Assert.NotEqual(one.Items[0]["id"], next.Items[0]["id"]);
        }
        var firstPage = await service.ReadEvidenceAsync(investigation.Id, hands[0].Summary.Id, 1, null, default);
        await Assert.ThrowsAsync<AdminOperationsQueryException>(() => service.ReadEvidenceAsync(investigation.Id, hands[1].Summary.Id, 1, firstPage.NextCursor, default));

        var immutableTimes = sharedSources.ToDictionary(copy => copy.Id, copy => copy.UpdateTime);
        await source.UpdateAsync("marker", "later-live-source-state");
        foreach (var hand in hands)
            await db.Collection(InvestigationDocuments.Instances).Document(InvestigationDocuments.Hash($"{investigation.Id}\n{hand.Summary.Id}"))
                .UpdateAsync(new Dictionary<string, object> { ["capturePending"] = true, ["roundSnapshotCopied"] = false,
                    ["captureNextAttemptAtUtc"] = Timestamp.FromDateTime(clock.Now) });
        await service.ProcessAsync(default);
        var retried = await db.Collection(InvestigationDocuments.Evidence).WhereEqualTo("caseId", investigation.Id).GetSnapshotAsync();
        Assert.Equal(copies.Count, retried.Count);
        foreach (var copy in retried.Documents.Where(copy => copy.GetValue<string>("kind") == "request-intent"))
        {
            Assert.Equal(immutableTimes[copy.Id], copy.UpdateTime);
            Assert.Equal("first-source-snapshot", copy.GetValue<Dictionary<string, object>>("originalFields")["marker"]);
        }
        foreach (var hand in hands)
        {
            var manifest = await db.Collection(InvestigationDocuments.Instances)
                .Document(InvestigationDocuments.Hash($"{investigation.Id}\n{hand.Summary.Id}")).GetSnapshotAsync();
            Assert.Equal("no-source-pointer-in-round-evidence", manifest.GetValue<string>("sourceSnapshotState"));
        }
    }

    [Fact]
    public async Task Source_snapshot_state_distinguishes_new_existing_and_missing_original_without_rewriting_first_copy()
    {
        var db = Database(); var clock = new Clock(); var service = Service(db, clock);
        var source = db.Collection("baccaratRounds").Document("paid-source-round");
        await source.SetAsync(new Dictionary<string, object> { ["marker"] = "first-source-snapshot" });
        var round = await SourceRound(db, clock, source.Id, source, "baccarat", includeSourcePointer: true);
        var investigation = await service.PreserveAsync("system-integrity", "source-outcome-status",
            [round.Summary.Id], [], [], 1, default);
        var manifest = db.Collection(InvestigationDocuments.Instances)
            .Document(InvestigationDocuments.Hash($"{investigation.Id}\n{round.Summary.Id}"));
        await service.ProcessAsync(default);
        Assert.Equal("source-document-copied", (await manifest.GetSnapshotAsync()).GetValue<string>("sourceSnapshotState"));
        var copy = Assert.Single((await db.Collection(InvestigationDocuments.Evidence)
            .WhereEqualTo("caseId", investigation.Id).WhereEqualTo("kind", "source-document").GetSnapshotAsync()).Documents);
        await source.UpdateAsync("marker", "later-source-state");
        await manifest.UpdateAsync(new Dictionary<string, object> { ["capturePending"] = true, ["roundSnapshotCopied"] = false,
            ["captureNextAttemptAtUtc"] = Timestamp.FromDateTime(clock.Now) });
        await service.ProcessAsync(default);
        Assert.Equal("source-document-already-captured", (await manifest.GetSnapshotAsync()).GetValue<string>("sourceSnapshotState"));
        Assert.Equal(copy.UpdateTime, (await copy.Reference.GetSnapshotAsync()).UpdateTime);
        Assert.Equal("first-source-snapshot", (await copy.Reference.GetSnapshotAsync()).GetValue<Dictionary<string, object>>("originalFields")["marker"]);
        await source.DeleteAsync(); // Isolated emulator source; retained immutable evidence remains.
        await manifest.UpdateAsync(new Dictionary<string, object> { ["capturePending"] = true, ["roundSnapshotCopied"] = false,
            ["captureNextAttemptAtUtc"] = Timestamp.FromDateTime(clock.Now) });
        await service.ProcessAsync(default);
        Assert.Equal("source-document-missing", (await manifest.GetSnapshotAsync()).GetValue<string>("sourceSnapshotState"));
        Assert.Equal(copy.UpdateTime, (await copy.Reference.GetSnapshotAsync()).UpdateTime);
    }

    [Fact]
    public async Task Legacy_shared_copies_and_completed_manifests_are_preserved_and_missing_links_are_disclosed()
    {
        var db = Database(); var clock = new Clock(); var service = Service(db, clock);
        var source = db.Collection(InvestigationDocuments.RequestIntents).Document("shared-request");
        await source.SetAsync(new Dictionary<string, object> { ["marker"] = "current-live-state" });
        var hands = new[] { await SourceRound(db, clock, "legacy-table-hand-1", source),
            await SourceRound(db, clock, "legacy-table-hand-2", source) };
        var investigation = await service.PreserveAsync("behavioral-review", "legacy-shared-source",
            hands.Select(hand => hand.Summary.Id).ToArray(), [], [], 1, default);
        var manifests = new List<DocumentSnapshot>();
        foreach (var hand in hands)
        {
            var reference = db.Collection(InvestigationDocuments.Instances).Document(InvestigationDocuments.Hash($"{investigation.Id}\n{hand.Summary.Id}"));
            await reference.UpdateAsync(new Dictionary<string, object> { ["evidenceCopyKeyVersion"] = FieldValue.Delete,
                ["capturePending"] = false, ["roundSnapshotCopied"] = true, ["captureState"] = "available-originals-captured" });
            manifests.Add(await reference.GetSnapshotAsync());
        }
        var legacy = db.Collection(InvestigationDocuments.Evidence)
            .Document(InvestigationDocuments.Hash($"{investigation.Id}\nrequest-intent\n{source.Id}"));
        await legacy.CreateAsync(new Dictionary<string, object> { ["caseId"] = investigation.Id, ["auditId"] = hands[0].Summary.Id,
            ["kind"] = "request-intent", ["sourcePath"] = source.Path, ["sourceDocumentId"] = source.Id,
            ["originalFields"] = new Dictionary<string, object> { ["marker"] = "original-legacy-bytes" },
            ["capturedAtUtc"] = Timestamp.FromDateTime(clock.Now.AddDays(-1)) });
        var before = await legacy.GetSnapshotAsync();
        var first = await service.ReadEvidenceAsync(investigation.Id, hands[0].Summary.Id, 100, null, default);
        Assert.Single(first.Items); Assert.Contains("Legacy shared copy keys", first.LinkageCoverage);
        var second = await service.ReadEvidenceAsync(investigation.Id, hands[1].Summary.Id, 100, null, default);
        Assert.Empty(second.Items); Assert.Contains("historical per-audit linkage may be incomplete", second.LinkageCoverage);
        var instancePage = await service.ReadInstancesAsync(investigation.Id, 100, null, default);
        Assert.All(instancePage.Items, item => Assert.Contains("Legacy shared copy keys",
            (string)Assert.IsType<Dictionary<string, object?>>(item["data"])["evidenceLinkageCoverage"]!));
        await service.ProcessAsync(default);
        var after = await legacy.GetSnapshotAsync();
        Assert.Equal(before.UpdateTime, after.UpdateTime);
        Assert.Equal("original-legacy-bytes", after.GetValue<Dictionary<string, object>>("originalFields")["marker"]);
        Assert.False(after.ContainsField("copyKeyVersion"));
        Assert.Single((await db.Collection(InvestigationDocuments.Evidence).WhereEqualTo("caseId", investigation.Id).GetSnapshotAsync()).Documents);
        foreach (var manifest in manifests)
            Assert.Equal(manifest.UpdateTime, (await manifest.Reference.GetSnapshotAsync()).UpdateTime);
        var missing = await service.ReadEvidenceAsync(investigation.Id, new string('f', 64), 100, null, default);
        Assert.Contains("manifest is missing", missing.LinkageCoverage);
    }

    [Fact]
    public async Task ConcurrentCandidateRetriesPinTheEntireOriginalInstanceAndCopyAllParticipantsAndLinkedStartRequest()
    {
        var db = Database(); var clock = new Clock(); var service = Service(db, clock);
        var capture = await Round(db, clock, "round-one");
        await db.Collection(RoundAuditDocuments.Summaries).Document(capture.Summary.Id)
            .UpdateAsync(new Dictionary<string, object> { ["securityAuditPending"] = false });
        await db.Collection(InvestigationDocuments.RequestIntents).Document("request-one").SetAsync(new Dictionary<string, object>
        {
            ["requestId"] = "request-one", ["gameId"] = "texas-holdem", ["instanceId"] = null!, ["userId"] = "bob",
            ["ipAddress"] = "192.0.2.1", ["receivedAtUtc"] = Timestamp.FromDateTime(clock.Now), ["requestBodyState"] = "captured"
        });
        await db.Collection(InvestigationDocuments.RequestResults).Document("request-one").SetAsync(new Dictionary<string, object>
            { ["requestId"] = "request-one", ["outcome"] = "http-response", ["httpStatusCode"] = 200L });
        await db.Collection(InvestigationDocuments.Events).Document("event-one").SetAsync(new Dictionary<string, object>
        {
            ["gameId"] = "texas-holdem", ["instanceId"] = "round-one", ["requestId"] = "request-one", ["payloadJson"] = "{\"allCards\":[\"As\",\"Kh\"]}"
        });
        var attempts = await Task.WhenAll(Enumerable.Range(0, 3).Select(_ => service.PreserveAsync("behavioral-review", "one-episode",
            [capture.Summary.Id], ["alice"], [new("test", "behavior", "Review candidate only", "uncalibrated")], 1, default)));
        Assert.Single(attempts.Select(value => value.Id).Distinct());
        Assert.All(attempts, value => Assert.Equal(1, value.InstanceCount));
        var held = await db.Collection(RoundAuditDocuments.Summaries).Document(capture.Summary.Id).GetSnapshotAsync();
        Assert.Equal([attempts[0].Id], held.GetValue<List<string>>("investigationHoldIds"));
        Assert.Null(held.GetValue<object>("evidenceDeleteAfterUtc"));
        Assert.True((await db.Collection(InvestigationDocuments.InstanceHolds).Document(InvestigationDocuments.InstanceKey("texas-holdem", "round-one")).GetSnapshotAsync()).GetValue<bool>("held"));
        await service.ProcessAsync(default);
        var copies = await db.Collection(InvestigationDocuments.Evidence).WhereEqualTo("caseId", attempts[0].Id).GetSnapshotAsync();
        Assert.Equal(5, copies.Count);
        var original = copies.Documents.Single(value => value.GetValue<string>("kind") == "round-evidence");
        var fields = original.GetValue<Dictionary<string, object>>("originalFields");
        Assert.Equal(capture.PayloadJson, fields["payloadJson"]);
        using var payload = JsonDocument.Parse((string)fields["payloadJson"]);
        Assert.Equal(3, payload.RootElement.GetProperty("players").GetArrayLength()); // Includes unflagged human and managed seat.
        Assert.Single((await db.Collection(InvestigationDocuments.Cases).GetSnapshotAsync()).Documents);
        Assert.Empty((await db.Collection(FirestoreRiskIncidentStore.Incidents).GetSnapshotAsync()).Documents);
        Assert.Empty((await db.Collection(FirestoreRiskIncidentStore.States).GetSnapshotAsync()).Documents);
    }

    [Theory]
    [InlineData("blackjack", "table-one-round-1", "blackjack-table-payout-table-one-round-1-alice", true)]
    [InlineData("baccarat", "paid-round-1", "baccarat-paid-round-1-wager", false)]
    public async Task Incident_capture_copies_all_available_round_wallet_ledgers_even_without_a_roundId_field(
        string game, string round, string ledgerId, bool tableLedger)
    {
        var db = Database(); var clock = new Clock(); var service = Service(db, clock);
        var capture = await Round(db, clock, round, game: game);
        await db.Collection(RoundAuditDocuments.Summaries).Document(capture.Summary.Id)
            .UpdateAsync("securityAuditPending", false);
        await db.Collection("balanceTransactions").Document(ledgerId).SetAsync(new Dictionary<string, object>
        {
            ["transactionId"] = ledgerId, ["userId"] = "alice", ["amount"] = 2d,
            ["idempotencyKey"] = tableLedger ? round : "command-key"
        });
        var incident = await service.PreserveAsync("system-integrity", "ledger-" + round,
            [capture.Summary.Id], [], [new("test-ledger", "system-integrity", "Review full instance.", "unattributed")], 1, default);

        await service.ProcessAsync(default);
        var copies = await db.Collection(InvestigationDocuments.Evidence).WhereEqualTo("caseId", incident.Id).GetSnapshotAsync();
        Assert.Contains(copies.Documents, document => document.GetValue<string>("kind") == "round-ledger" &&
            document.GetValue<string>("sourceDocumentId") == ledgerId);
    }

    [Fact]
    public async Task Linked_request_without_http_result_is_rechecked_after_two_minute_grace()
    {
        var db = Database(); var clock = new Clock(); var service = Service(db, clock);
        var round = "pending-result-round";
        var capture = await Round(db, clock, round, game: "mines");
        var request = new GameRequestObservation(Guid.NewGuid().ToString("N"), "alice", "session", "203.0.113.3",
            clock.Now.AddSeconds(-1), "POST", "/api/games/mines", "mines", round, null, null,
            "{}", "sanitized", "connection");
        await new FirestoreGameSecurityRequestStore(db).CreateIntentAsync(request, default);
        using (GameSecurityContext.Push(request))
            await db.RunTransactionAsync(tx =>
            {
                GameSecurityJournal.Write(tx, db, "mines", round, 1, clock.Now, new { state = "settled" }, ["alice"]);
                return Task.FromResult(true);
            });

        clock.Now = clock.Now.AddMinutes(1);
        await service.ProcessAsync(default);
        var summaryRef = db.Collection(RoundAuditDocuments.Summaries).Document(capture.Summary.Id);
        var deferred = await summaryRef.GetSnapshotAsync();
        Assert.True(deferred.GetValue<bool>("securityAuditPending"));
        Assert.Empty((await db.Collection(InvestigationDocuments.Cases).GetSnapshotAsync()).Documents);
        clock.Now = clock.Now.AddMinutes(2);
        await service.ProcessAsync(default);
        var completed = await summaryRef.GetSnapshotAsync();
        Assert.False(completed.GetValue<bool>("securityAuditPending"));
        var incident = Assert.Single((await service.ListAsync(25, null, default)).Items);
        Assert.Contains(incident.Signals, signal => signal.Code == "engine-request-result-missing");
    }

    [Fact]
    public async Task Recheck_replaces_prior_second_pass_findings_instead_of_preserving_stale_warnings()
    {
        var db = Database(); var clock = new Clock(); var service = Service(db, clock);
        var capture = await Round(db, clock, "recheck-stale-round",
            [new("engine-request-result-pending", "warning", "Prior result write was pending")], "mines");
        var summaryRef = db.Collection(RoundAuditDocuments.Summaries).Document(capture.Summary.Id);
        await summaryRef.UpdateAsync("securityAuditFindingCodes", new[] { "engine-request-result-pending" });

        await service.ProcessAsync(default);

        var observed = FirestoreGameRoundAuditStore.Summary(await summaryRef.GetSnapshotAsync());
        Assert.DoesNotContain(observed.Findings, finding => finding.Code == "engine-request-result-pending");
        Assert.Contains(observed.Findings, finding => finding.Code == "engine-journal-missing");
    }

    [Theory]
    [InlineData("hi-lo", "hiLoEvents", true)]
    [InlineData("house-holdem", "houseHoldemRoundEvents", false)]
    public async Task Incident_capture_includes_game_operation_events_and_linked_hi_lo_operations(
        string game, string eventCollection, bool hasOperation)
    {
        var db = Database(); var clock = new Clock(); var service = Service(db, clock);
        var roundId = "operation-round-1";
        var capture = await Round(db, clock, roundId, game: game);
        await db.Collection(RoundAuditDocuments.Summaries).Document(capture.Summary.Id)
            .UpdateAsync("securityAuditPending", false);
        var eventId = Guid.NewGuid().ToString("N");
        await db.Collection(eventCollection).Document(eventId).SetAsync(new Dictionary<string, object>
        { ["roundId"] = roundId, ["userId"] = "alice", ["command"] = "accepted" });
        if (hasOperation)
            await db.Collection("hiLoOperations").Document(eventId).SetAsync(new Dictionary<string, object>
            { ["operationId"] = "accepted-command", ["walletDeltaCents"] = -100L });
        var incident = await service.PreserveAsync("system-integrity", game + "-operations",
            [capture.Summary.Id], [], [new("test-operations", "system-integrity", "Review full instance.", "unattributed")], 1, default);

        await service.ProcessAsync(default);
        var copies = await db.Collection(InvestigationDocuments.Evidence).WhereEqualTo("caseId", incident.Id).GetSnapshotAsync();
        Assert.Contains(copies.Documents, item => item.GetValue<string>("kind") == "game-operation-event" &&
            item.GetValue<string>("sourceDocumentId") == eventId);
        Assert.Equal(hasOperation, copies.Documents.Any(item => item.GetValue<string>("kind") == "game-operation" &&
            item.GetValue<string>("sourceDocumentId") == eventId));
    }

    [Fact]
    public async Task ErrorCreatesSystemCaseButKnownIncompleteCoverageDoesNotAccusePlayers()
    {
        var db = Database(); var clock = new Clock(); var service = Service(db, clock);
        await Round(db, clock, "incomplete", [new("action-timeline-unavailable", "warning", "Earlier decisions unavailable")], "blackjack");
        await Round(db, clock, "error", [new("award-mismatch", "error", "Payout does not reconcile")], "blackjack");
        await service.ProcessAsync(default);
        var cases = await service.ListAsync(25, null, default);
        Assert.Equal(2, cases.Items.Count); // Both synthetic rounds also lack their required engine journal.
        Assert.All(cases.Items, item =>
        {
            Assert.Equal("system-integrity", item.Kind);
            Assert.Empty(item.TargetUserIds);
            Assert.Equal("unattributed", item.AttributionStatus);
        });
        Assert.Equal(2, (await db.Collection(InvestigationDocuments.Coverage).GetSnapshotAsync()).Count);
        Assert.Empty((await db.Collection(FirestoreRiskIncidentStore.Incidents).GetSnapshotAsync()).Documents);
    }

    [Fact]
    public async Task StaleSlotAdmissionCreatesOperationalCaseWithEntireAvailableAdmissionContext()
    {
        var db = Database(); var clock = new Clock(); var service = Service(db, clock);
        var admissionId = Guid.NewGuid().ToString("N");
        await db.Collection("slotSpinAdmissions").Document(admissionId).SetAsync(new Dictionary<string, object>
        {
            ["status"] = "admitted", ["auditObserved"] = false,
            ["startedAtUtc"] = Timestamp.FromDateTime(clock.Now.AddMinutes(-3)),
            ["auditNextAttemptAtUtc"] = Timestamp.FromDateTime(clock.Now.AddMinutes(-1)),
            ["gameId"] = "wukong-journey-to-the-west", ["userId"] = "alice",
            ["isFreeSpin"] = true, ["freeSpinsBefore"] = 2L,
            ["requestId"] = "slot-request-1", ["freeGameUseTransactionId"] = "free-spin-ledger-1"
        });
        await db.Collection(InvestigationDocuments.RequestIntents).Document("slot-request-1").SetAsync(new Dictionary<string, object>
            { ["requestId"] = "slot-request-1", ["userId"] = "alice", ["ipAddress"] = "203.0.113.8" });
        await db.Collection("balanceTransactions").Document("free-spin-ledger-1").SetAsync(new Dictionary<string, object>
            { ["userId"] = "alice", ["type"] = "free-game-use", ["amount"] = -1L });

        await service.ProcessAsync(default);
        var investigation = Assert.Single((await service.ListAsync(25, null, default)).Items);
        Assert.Equal("system-integrity", investigation.Kind);
        Assert.Empty(investigation.TargetUserIds);
        var copy = await db.Collection(InvestigationDocuments.Evidence).WhereEqualTo("caseId", investigation.Id).GetSnapshotAsync();
        Assert.Equal(["admission-ledger", "request-intent", "slot-admission"],
            copy.Documents.Select(document => document.GetValue<string>("kind")).Order().ToArray());
        Assert.True((await db.Collection("slotSpinAdmissions").Document(admissionId).GetSnapshotAsync()).GetValue<bool>("auditObserved"));
        await service.ProcessAsync(default);
        Assert.Single((await service.ListAsync(25, null, default)).Items);
        Assert.Empty((await db.Collection(FirestoreRiskIncidentStore.Incidents).GetSnapshotAsync()).Documents);
    }

    [Fact]
    public async Task CleanCompletedAuditReleasesRawJournalAndLinkedPrivateRequestOnlyAfterItsHoldCheck()
    {
        var db = Database(); var clock = new Clock();
        var capture = await Round(db, clock, "raw-retention", game: "mines");
        var summaryRef = db.Collection(RoundAuditDocuments.Summaries).Document(capture.Summary.Id);
        await summaryRef.UpdateAsync(new Dictionary<string, object>
        {
            ["retentionState"] = "deleted", ["securityAuditPending"] = false,
            ["rawEvidencePurgePending"] = true, ["rawEvidenceNextAttemptAtUtc"] = Timestamp.FromDateTime(clock.Now),
            ["rawRequestCursor"] = "", ["evidencePresent"] = false
        });
        await db.Collection(RoundAuditDocuments.Evidence).Document(capture.Summary.Id).DeleteAsync();
        await db.Collection(InvestigationDocuments.Events).Document("raw-event").SetAsync(new Dictionary<string, object>
            { ["gameId"] = "mines", ["instanceId"] = "raw-retention", ["requestId"] = "raw-request" });
        await db.Collection(InvestigationDocuments.RequestIntents).Document("raw-request").SetAsync(new Dictionary<string, object>
            { ["gameId"] = "mines", ["instanceId"] = "raw-retention", ["requestId"] = "raw-request", ["ipAddress"] = "203.0.113.8" });
        await db.Collection(InvestigationDocuments.RequestResults).Document("raw-request").SetAsync(new Dictionary<string, object>
            { ["requestId"] = "raw-request", ["httpStatusCode"] = 200L });
        var holdRef = db.Collection(InvestigationDocuments.InstanceHolds).Document(InvestigationDocuments.InstanceKey("mines", "raw-retention"));
        await holdRef.SetAsync(new Dictionary<string, object> { ["held"] = true });
        var retention = new SecurityAuditRawRetention(db, Options.Create(new GameRoundRetentionOptions()), clock);
        await retention.SweepAsync(default);
        Assert.True((await db.Collection(InvestigationDocuments.Events).Document("raw-event").GetSnapshotAsync()).Exists);

        await holdRef.UpdateAsync(new Dictionary<string, object> { ["held"] = false });
        clock.Now = clock.Now.AddHours(2);
        for (var pass = 0; pass < 4; pass++) await retention.SweepAsync(default);
        Assert.False((await db.Collection(InvestigationDocuments.Events).Document("raw-event").GetSnapshotAsync()).Exists);
        Assert.False((await db.Collection(InvestigationDocuments.RequestIntents).Document("raw-request").GetSnapshotAsync()).Exists);
        Assert.False((await db.Collection(InvestigationDocuments.RequestResults).Document("raw-request").GetSnapshotAsync()).Exists);
        Assert.False((await summaryRef.GetSnapshotAsync()).GetValue<bool>("rawEvidencePurgePending"));
    }

    [Fact]
    public async Task HoldemFinancialAuditReconcilesRevenueAndHandLinkedWalletEntries()
    {
        var db = Database(); var clock = new Clock();
        var round = await Round(db, clock, "table-hand-1");
        await db.Collection("creditHoldemMatchRevenue").Document("table-hand-1").SetAsync(new Dictionary<string, object>
        {
            ["humanWagerCents"] = 200L, ["humanPayoutCents"] = 200L,
            ["financialClassification"] = "real-human-wager-v2"
        });
        await db.Collection("balanceTransactions").Document("table-hand-1-blind-alice-1").SetAsync(new Dictionary<string, object>
            { ["userId"] = "alice", ["type"] = "texas-holdem-blind", ["amount"] = -1d });
        await db.Collection("balanceTransactions").Document("table-hand-1-blind-bob-1").SetAsync(new Dictionary<string, object>
            { ["userId"] = "bob", ["type"] = "texas-holdem-blind", ["amount"] = -1d });
        var payout = db.Collection("balanceTransactions").Document("table-hand-1-payout-bob");
        await payout.SetAsync(new Dictionary<string, object>
            { ["userId"] = "bob", ["type"] = "texas-holdem-hand-payout", ["amount"] = 2d });
        Assert.Empty(await HoldemFinancialAudit.InspectAsync(db, round.Summary, default));
        await payout.UpdateAsync(new Dictionary<string, object> { ["amount"] = 1.99d });
        Assert.Contains(await HoldemFinancialAudit.InspectAsync(db, round.Summary, default),
            finding => finding.Code == "holdem-ledger-settlement-mismatch" && finding.Severity == "error");
    }

    [Fact]
    public async Task EscrowHandRequiresItsBuyInWalletDebitAndValidatesLaterCashOut()
    {
        var db = Database(); var clock = new Clock();
        var capture = RoundAuditCapture.Create("texas-holdem", "escrow-hand-1", "table-escrow", 1,
            clock.Now.AddMinutes(-1), clock.Now,
            new { usesBuyInEscrow = true, matchId = "table-escrow",
                players = new[] { new { isAccountBacked = true, actorId = "alice", escrowTicketId = "ticket-a", stack = 500L } },
                cashedOutReservations = Array.Empty<string>() }, "holdem-hand-v1", "test",
            [new("alice", "Alice", true, 100, 100, 0)], []);
        await db.Collection("creditHoldemMatchRevenue").Document(capture.Summary.RoundId).SetAsync(new Dictionary<string, object>
        {
            ["matchId"] = "table-escrow", ["humanWagerCents"] = 100L, ["humanPayoutCents"] = 100L,
            ["financialClassification"] = "escrow-house-backed-v1"
        });
        await db.Collection("creditHoldemTickets").Document("ticket-a").SetAsync(new Dictionary<string, object>
        { ["ticketJson"] = JsonSerializer.Serialize(new { userId = "alice", buyInCents = 500L }), ["status"] = "matched" });
        Assert.Contains(await HoldemFinancialAudit.InspectAsync(db, capture.Summary, default, capture.PayloadJson),
            finding => finding.Code == "holdem-escrow-buy-in-mismatch" && finding.Severity == "error");
        await db.Collection("balanceTransactions").Document("ticket-a-buy-in").SetAsync(new Dictionary<string, object>
        { ["userId"] = "alice", ["type"] = "texas-holdem-buy-in", ["idempotencyKey"] = "ticket-a", ["amount"] = -5d });
        Assert.DoesNotContain(await HoldemFinancialAudit.InspectAsync(db, capture.Summary, default, capture.PayloadJson),
            finding => finding.Severity == "error");
        await db.Collection("balanceTransactions").Document("ticket-a-cash-out").SetAsync(new Dictionary<string, object>
        { ["userId"] = "bob", ["type"] = "texas-holdem-cash-out", ["idempotencyKey"] = "ticket-a", ["amount"] = 5d });
        Assert.Contains(await HoldemFinancialAudit.InspectAsync(db, capture.Summary, default, capture.PayloadJson),
            finding => finding.Code == "holdem-escrow-cash-out-invalid" && finding.Severity == "error");
    }

    [Fact]
    public async Task NetworkReviewIncludesJoinRequestsLinkedByEngineJournalWithoutPathInstance()
    {
        var db = Database(); var clock = new Clock(); var service = Service(db, clock);
        foreach (var user in new[] { "alice", "bob" })
        {
            await db.Collection(InvestigationDocuments.RequestIntents).Document("join-" + user).SetAsync(new Dictionary<string, object>
            {
                ["gameId"] = "texas-holdem", ["instanceId"] = null!, ["userId"] = user,
                ["receivedAtUtc"] = Timestamp.FromDateTime(clock.Now),
                ["observationJson"] = JsonSerializer.Serialize(new { userId = user, ipAddress = "203.0.113.8", ipSource = "trusted-proxy-chain", receivedAtUtc = clock.Now })
            });
            await db.Collection(InvestigationDocuments.Events).Document("event-" + user).SetAsync(new Dictionary<string, object>
            { ["gameId"] = "texas-holdem", ["instanceId"] = "table-hand-1", ["requestId"] = "join-" + user });
        }
        var network = await service.ReadNetworkAsync("texas-holdem", "table-hand-1", default);
        Assert.False(network.Truncated);
        Assert.Equal(2, network.Observations.Count);
        Assert.All(network.Observations, value => Assert.Equal("203.0.113.8", value.IpAddress));
        Assert.All(network.Observations, value => Assert.Equal("trusted-proxy-chain", value.IpSource));
    }

    [Theory]
    [InlineData("hearts")]
    [InlineData("solitaire")]
    public async Task CompetitiveHumanMatchesProjectPairBaselineWithoutAPlayerIncident(string game)
    {
        var db = Database(); var clock = new Clock(); var service = Service(db, clock);
        var capture = await Round(db, clock, "competitive-pair", game: game);
        await service.ProjectPairsAsync(capture.Summary, default);
        var pairId = InvestigationDocuments.PairId(game, "alice", "bob");
        var pair = await db.Collection(InvestigationDocuments.PairFeatures).Document(pairId).GetSnapshotAsync();
        Assert.Equal(1L, pair.GetValue<long>("sharedRounds"));
        Assert.Empty((await db.Collection(InvestigationDocuments.PairAnalysisQueue).GetSnapshotAsync()).Documents);
        Assert.Empty((await db.Collection(FirestoreRiskIncidentStore.Incidents).GetSnapshotAsync()).Documents);
    }

    [Fact]
    public async Task CorruptNullEnginePayloadBecomesAnIntegrityFinding()
    {
        var db = Database(); var clock = new Clock();
        var capture = await Round(db, clock, "null-event", game: "mines");
        await db.Collection(InvestigationDocuments.Events).Document("bad-event").SetAsync(new Dictionary<string, object>
        { ["gameId"] = "mines", ["instanceId"] = "null-event", ["payloadJson"] = null! });
        var assessment = await GameSecurityIntegrityAuditor.InspectAsync(db, capture.Summary, default, clock.Now);
        Assert.Contains(assessment.Findings, finding => finding.Code == "engine-journal-changed" && finding.Severity == "error");
    }

    [Fact]
    public async Task ClearedReviewKeepsCopiedDetailsThirtyDaysThenReleasesOnlyItsOwnInstanceHold()
    {
        var db = Database(); var clock = new Clock(); var service = Service(db, clock);
        var capture = await Round(db, clock, "round-one");
        await db.Collection(RoundAuditDocuments.Summaries).Document(capture.Summary.Id)
            .UpdateAsync(new Dictionary<string, object> { ["securityAuditPending"] = false });
        var first = await service.PreserveAsync("behavioral-review", "first", [capture.Summary.Id], [], [], 0, default);
        var second = await service.PreserveAsync("system-integrity", "second", [capture.Summary.Id], [], [], 0, default);
        await service.ResolveAsync(first.Id, "cleared", "Reviewed context does not support the candidate.", "admin", default);
        clock.Now = clock.Now.AddDays(29);
        await service.ProcessAsync(default);
        Assert.Equal(2, (await db.Collection(InvestigationDocuments.Evidence).WhereEqualTo("caseId", first.Id).GetSnapshotAsync()).Count);
        clock.Now = clock.Now.AddDays(2);
        for (var pass = 0; pass < 4; pass++) await service.ProcessAsync(default);
        Assert.Empty((await db.Collection(InvestigationDocuments.Evidence).WhereEqualTo("caseId", first.Id).GetSnapshotAsync()).Documents);
        var held = await db.Collection(RoundAuditDocuments.Summaries).Document(capture.Summary.Id).GetSnapshotAsync();
        Assert.Equal([second.Id], held.GetValue<List<string>>("investigationHoldIds"));
        Assert.True((await db.Collection(RoundAuditDocuments.Evidence).Document(capture.Summary.Id).GetSnapshotAsync()).Exists);
    }

    private static async Task<RoundAuditCapture> SourceRound(FirestoreDb db, Clock clock, string round, DocumentReference source,
        string game = "texas-holdem", bool includeSourcePointer = false)
    {
        RoundAuditCapture capture;
        if (game == "texas-holdem" && !includeSourcePointer)
        {
            var suffix = round.LastIndexOf("-hand-", StringComparison.Ordinal);
            var matchId = round[..suffix];
            var match = CreditHoldemEngine.Deal(matchId, new[]
            {
                new CreditHoldemSeatAssignment("alice", "seat-alice", "Alice", true, 0, 5000),
                new CreditHoldemSeatAssignment("bob", "seat-bob", "Bob", true, 1, 5000),
                new CreditHoldemSeatAssignment("carol", "seat-carol", "Carol", true, 2, 5000),
            }, "test-partition", 42, clock.Now.AddMinutes(-1));
            match.HandNumber = int.Parse(round[(suffix + "-hand-".Length)..], System.Globalization.CultureInfo.InvariantCulture);
            for (var action = 0; match.Status == "active" && action < match.Players.Count; action++)
                CreditHoldemEngine.ApplyAction(match, match.Players[match.ActiveSeat].ActorId,
                    CreditHoldemActions.Fold, null, clock.Now);
            Assert.Equal("completed", match.Status);
            _ = CreditHoldemEngine.ApplyFinancialSettlement(match, clock.Now);
            capture = HoldemRoundAudit.Capture(match, clock.Now);
        }
        else
        {
            capture = RoundAuditCapture.Create(game, round, "table-one", 1, clock.Now.AddMinutes(-1), clock.Now,
                new { sourceCollection = source.Parent.Id, sourceDocumentId = source.Id, data = new { round } },
                "test-full-instance", "test-auditor", [new("alice", "Alice", true, 100, 0, -100)], []);
        }
        var documents = RoundAuditDocuments.Create(capture);
        documents.Summary["securityAuditPending"] = false;
        await db.Collection(RoundAuditDocuments.Summaries).Document(capture.Summary.Id).SetAsync(documents.Summary);
        await db.Collection(RoundAuditDocuments.Evidence).Document(capture.Summary.Id).SetAsync(documents.Evidence);
        return capture;
    }

    private async Task<RoundAuditCapture> Round(FirestoreDb db, Clock clock, string round,
        IReadOnlyList<RoundAuditFinding>? findings = null, string game = "texas-holdem")
    {
        var capture = RoundAuditCapture.Create(game, round, "table-one", 1, clock.Now.AddMinutes(-1), clock.Now,
            new { engineVersion = "test-v1", players = new[] { new { actorId = "alice", cards = new[] { "As", "Kh" } }, new { actorId = "bob", cards = new[] { "Qh", "Qs" } }, new { actorId = "managed", cards = new[] { "2h", "3s" } } } },
            "test-full-instance", "test-auditor", [new("alice", "Alice", true, 100, 0, -100), new("bob", "Bob", true, 100, 200, 100), new("managed", "Managed", false, 0, 0, 0)], findings ?? []);
        var documents = RoundAuditDocuments.Create(capture);
        documents.Summary["securityAuditPending"] = true;
        await db.Collection(RoundAuditDocuments.Summaries).Document(capture.Summary.Id).SetAsync(documents.Summary);
        await db.Collection(RoundAuditDocuments.Evidence).Document(capture.Summary.Id).SetAsync(documents.Evidence);
        return capture;
    }
    private static SecurityAuditInvestigationService Service(FirestoreDb db, Clock clock) => new(db, clock,
        Options.Create(new SecurityAuditInvestigationOptions()), Options.Create(new AdminOperationsOptions { CursorSigningKey = "test-cursor-signing-key" }));
    private FirestoreDb Database() => new FirestoreDbBuilder
    {
        ProjectId = "demo-investigations-" + Guid.NewGuid().ToString("N"), Endpoint = fixture.Endpoint,
        ChannelCredentials = Grpc.Core.ChannelCredentials.Insecure, EmulatorDetection = Google.Api.Gax.EmulatorDetection.None
    }.Build();
}
