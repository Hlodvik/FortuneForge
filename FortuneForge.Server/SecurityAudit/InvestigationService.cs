using System.Globalization;
using System.Text.Json;
using FortuneForge.Games.Abstractions.Auditing;
using FortuneForge.Server.Admin.Operations;
using FortuneForge.Server.Arcade;
using FortuneForge.Server.RoundAudit;
using FortuneForge.Server.Risk;
using Google.Cloud.Firestore;
using Grpc.Core;
using Microsoft.Extensions.Options;

namespace FortuneForge.Server.SecurityAudit;

internal sealed class SecurityAuditInvestigationService(FirestoreDb database, TimeProvider clock,
    IOptions<SecurityAuditInvestigationOptions> options, IOptions<AdminOperationsOptions> adminOptions)
{
    private readonly AdminOperationsCursor cursors = new(adminOptions.Value.CursorSigningKey);

    internal async Task<InvestigationCase> CreateAttributedIncidentAsync(string incidentId, IReadOnlyList<string> evidenceIds, CancellationToken ct)
    {
        var incident = await database.Collection(FirestoreRiskIncidentStore.Incidents).Document(incidentId).GetSnapshotAsync(ct);
        if (!incident.Exists) throw new KeyNotFoundException("The attributed risk incident was not found.");
        var risk = FirestoreRiskIncidentStore.ReadIncident(incident);
        return await PreserveAsync("attributed-player-incident", $"risk:{incidentId}", evidenceIds, [risk.UserId],
            [new("admin-attributed-incident", "attribution", "Evidence retained for an existing attributed player incident. This preservation service does not add strikes or alter restrictions.", "existing-incident")], 3, ct);
    }

    internal async Task<InvestigationCase> PreserveAsync(string kind, string sourceEpisodeId, IReadOnlyList<string> auditIds,
        IReadOnlyList<string> targetUserIds, IReadOnlyList<InvestigationSignal> signals, int priority, CancellationToken ct)
    {
        if (kind is not ("system-integrity" or "behavioral-review" or "attributed-player-incident")) throw new ArgumentException("Choose a supported investigation kind.");
        if (string.IsNullOrWhiteSpace(sourceEpisodeId) || sourceEpisodeId.Length > 1024) throw new ArgumentException("A bounded stable episode identifier is required.");
        var ids = auditIds.Distinct(StringComparer.Ordinal).Order(StringComparer.Ordinal).ToArray();
        if (ids.Length is < 1 or > 20) throw new ArgumentException("Preserve one to twenty complete instances per call.");
        foreach (var id in ids) FirestoreGameRoundAuditStore.ValidateId(id);
        var caseId = InvestigationDocuments.CaseId(kind, sourceEpisodeId);
        var caseRef = database.Collection(InvestigationDocuments.Cases).Document(caseId);
        var now = clock.GetUtcNow().UtcDateTime;
        return await RetryAbortedAsync(() => database.RunTransactionAsync(async tx =>
        {
            var previous = await tx.GetSnapshotAsync(caseRef, ct);
            var prior = previous.Exists ? ReadCase(previous) : null;
            // Cumulative co-play counts cannot manufacture a fresh episode after an administrator reviewed it.
            if (kind == "behavioral-review" && prior is { Status: not "open" }) return prior;
            var summaries = await tx.GetAllSnapshotsAsync(ids.Select(id => database.Collection(RoundAuditDocuments.Summaries).Document(id)), ct);
            if (summaries.Any(value => !value.Exists)) throw new KeyNotFoundException("An original audit summary is missing; a complete instance cannot be reconstructed.");
            var parsed = summaries.Select(FirestoreGameRoundAuditStore.Summary).ToArray();
            var instanceRefs = ids.Select(id => database.Collection(InvestigationDocuments.Instances).Document(InvestigationDocuments.Hash($"{caseId}\n{id}"))).ToArray();
            var instances = await tx.GetAllSnapshotsAsync(instanceRefs, ct);
            var holdRefs = parsed.Select(value => database.Collection(InvestigationDocuments.InstanceHolds)
                .Document(InvestigationDocuments.InstanceKey(value.GameId, value.RoundId))).ToArray();
            // Read hold documents too: a retention transaction reading these guards conflicts with pinning.
            _ = await tx.GetAllSnapshotsAsync(holdRefs, ct);
            var mergedSignals = (prior?.Signals ?? []).Concat(signals).DistinctBy(value => value.Code).ToArray();
            var value = new InvestigationCase(caseId, kind, sourceEpisodeId, prior?.Status ?? "open",
                Math.Max(prior?.ReviewPriority ?? 0, Math.Clamp(priority, 0, 9)),
                (prior?.TargetUserIds ?? []).Concat(targetUserIds).Distinct(StringComparer.Ordinal).Order(StringComparer.Ordinal).ToArray(),
                mergedSignals, prior?.CreatedAtUtc ?? now, now, checked((prior?.InstanceCount ?? 0) + instances.Count(item => !item.Exists)),
                kind == "attributed-player-incident" ? "linked-existing-risk-incident" : "unattributed", DeleteAfterUtc: prior?.DeleteAfterUtc, ResolutionReason: prior?.ResolutionReason);
            tx.Set(caseRef, CaseFields(value));
            for (var index = 0; index < ids.Length; index++)
            {
                var summary = parsed[index];
                tx.Set(holdRefs[index], new Dictionary<string, object>
                {
                    ["gameId"] = summary.GameId, ["instanceId"] = summary.RoundId, ["held"] = true,
                    ["investigationIds"] = FieldValue.ArrayUnion(caseId), ["updatedAtUtc"] = Timestamp.FromDateTime(now)
                }, SetOptions.MergeAll);
                tx.Update(summaries[index].Reference, new Dictionary<string, object>
                {
                    ["investigationHoldIds"] = FieldValue.ArrayUnion(caseId), ["retentionState"] = "held-investigation", ["evidenceDeleteAfterUtc"] = null!
                });
                if (!instances[index].Exists)
                {
                    tx.Create(instanceRefs[index], new Dictionary<string, object>
                    {
                        ["caseId"] = caseId, ["auditId"] = ids[index], ["gameId"] = summary.GameId, ["instanceId"] = summary.RoundId,
                        ["tableId"] = summary.TableId, ["startedAtUtc"] = Timestamp.FromDateTime(summary.StartedAtUtc),
                        ["completedAtUtc"] = Timestamp.FromDateTime(summary.CompletedAtUtc), ["participants"] = summaries[index].GetValue<object>("participants"),
                        ["evidenceSha256"] = summary.EvidenceSha256, ["versions"] = new Dictionary<string, object> { ["auditorVersion"] = summary.AuditorVersion, ["evidenceKind"] = summary.EvidenceKind },
                        ["originalEvidenceExpected"] = summary.EvidencePresent, ["roundSnapshotCopied"] = false, ["capturePending"] = true,
                        ["captureNextAttemptAtUtc"] = Timestamp.FromDateTime(now),
                        ["eventCursor"] = "", ["requestCursor"] = "", ["ledgerCursor"] = "",
                        ["gameEventCursor"] = "", ["gameEventsDone"] = false,
                        ["eventsDone"] = false, ["requestsDone"] = false, ["ledgersDone"] = false,
                        ["missingResultIds"] = Array.Empty<string>(), ["captureState"] = "pinned-capture-pending",
                        ["coverage"] = "originals-pinned; bounded snapshot/journal/request copying pending",
                        ["capturedAtUtc"] = Timestamp.FromDateTime(now)
                    });
                }
            }
            return value;
        }, cancellationToken: ct), ct);
    }

    private static async Task<T> RetryAbortedAsync<T>(Func<Task<T>> action, CancellationToken ct)
    {
        for (var attempt = 0; ; attempt++)
        {
            try { return await action(); }
            catch (RpcException exception) when (exception.StatusCode == StatusCode.Aborted && attempt < 4)
            {
                await Task.Delay(TimeSpan.FromMilliseconds(50 * (1 << attempt)), ct);
            }
        }
    }

    internal async Task<int> ProcessAsync(CancellationToken ct)
    {
        if (!options.Value.Enabled) return 0;
        var failures = new List<Exception>();
        var now = Timestamp.FromDateTime(clock.GetUtcNow().UtcDateTime);
        var pending = await database.Collection(RoundAuditDocuments.Summaries).WhereEqualTo("securityAuditPending", true)
            .WhereLessThanOrEqualTo("securityAuditNextAttemptAtUtc", now)
            .Limit(options.Value.BatchSize).GetSnapshotAsync(ct);
        foreach (var audit in pending.Documents)
            try { await ObserveRoundAsync(audit, ct); }
            catch (Exception exception) when (exception is not OperationCanceledException)
            {
                await DeferPendingAsync(audit.Reference, "securityAuditPending", "securityAuditNextAttemptAtUtc", ct);
                failures.Add(new InvalidOperationException($"Round audit {audit.Id} failed.", exception));
            }
        var captures = await database.Collection(InvestigationDocuments.Instances).WhereEqualTo("capturePending", true)
            .WhereLessThanOrEqualTo("captureNextAttemptAtUtc", now)
            .Limit(options.Value.BatchSize).GetSnapshotAsync(ct);
        foreach (var instance in captures.Documents)
            try { await CapturePageAsync(instance, ct); }
            catch (Exception exception) when (exception is not OperationCanceledException)
            {
                await DeferPendingAsync(instance.Reference, "capturePending", "captureNextAttemptAtUtc", ct);
                failures.Add(new InvalidOperationException($"Investigation capture {instance.Id} failed.", exception));
            }
        var riskPending = await database.Collection(FirestoreRiskIncidentStore.Incidents).WhereEqualTo("investigationPending", true)
            .WhereLessThanOrEqualTo("investigationNextAttemptAtUtc", now)
            .Limit(options.Value.BatchSize).GetSnapshotAsync(ct);
        foreach (var incident in riskPending.Documents)
            try { await SynchronizeAttributedCaseAsync(incident, ct); }
            catch (Exception exception) when (exception is not OperationCanceledException)
            {
                await DeferPendingAsync(incident.Reference, "investigationPending", "investigationNextAttemptAtUtc", ct);
                failures.Add(new InvalidOperationException($"Incident projection {incident.Id} failed.", exception));
            }
        // A slot admission can consume a free spin or boost before an outcome exists.
        // This is an operational reconciliation case, never a player cheating strike.
        var staleAdmissions = await database.Collection("slotSpinAdmissions").WhereEqualTo("status", "admitted")
            .WhereEqualTo("auditObserved", false)
            .WhereLessThanOrEqualTo("auditNextAttemptAtUtc", now)
            .Limit(options.Value.BatchSize).GetSnapshotAsync(ct);
        foreach (var admission in staleAdmissions.Documents)
            try { await ObserveStaleSlotAdmissionAsync(admission, ct); }
            catch (Exception exception) when (exception is not OperationCanceledException)
            {
                await DeferStaleAdmissionAsync(admission.Reference, ct);
                failures.Add(new InvalidOperationException($"Slot admission {admission.Id} failed.", exception));
            }
        var expired = await database.Collection(InvestigationDocuments.Cases).WhereEqualTo("purgePending", true)
            .WhereLessThanOrEqualTo("deleteAfterUtc", Timestamp.FromDateTime(clock.GetUtcNow().UtcDateTime)).Limit(options.Value.BatchSize).GetSnapshotAsync(ct);
        foreach (var investigation in expired.Documents)
            try { await PurgePageAsync(investigation, ct); }
            catch (Exception exception) when (exception is not OperationCanceledException) { failures.Add(new InvalidOperationException($"Investigation release {investigation.Id} failed.", exception)); }
        var queuedReleases = await database.Collection(InvestigationDocuments.PairAnalysisQueue).WhereEqualTo("retentionPending", true)
            .WhereLessThanOrEqualTo("releaseAfterUtc", Timestamp.FromDateTime(clock.GetUtcNow().UtcDateTime)).Limit(options.Value.BatchSize).GetSnapshotAsync(ct);
        foreach (var queue in queuedReleases.Documents)
            try { await ReleasePairQueueAsync(queue, ct); }
            catch (Exception exception) when (exception is not OperationCanceledException) { failures.Add(new InvalidOperationException($"Pair review release {queue.Id} failed.", exception)); }
        if (failures.Count > 0) throw new AggregateException("One or more security audit items failed; the remaining items were processed and failed items will retry.", failures);
        return pending.Count + captures.Count + riskPending.Count + staleAdmissions.Count + expired.Count;
    }

    private async Task DeferPendingAsync(DocumentReference reference, string pendingField, string nextField, CancellationToken ct) =>
        await database.RunTransactionAsync(async tx =>
        {
            var current = await tx.GetSnapshotAsync(reference, ct);
            if (!Bool(current, pendingField)) return false;
            tx.Update(reference, new Dictionary<string, object>
            { [nextField] = Timestamp.FromDateTime(clock.GetUtcNow().UtcDateTime.AddMinutes(5)) });
            return true;
        }, cancellationToken: ct);

    private async Task DeferStaleAdmissionAsync(DocumentReference reference, CancellationToken ct) =>
        await database.RunTransactionAsync(async tx =>
        {
            var current = await tx.GetSnapshotAsync(reference, ct);
            if (!current.Exists || Text(current, "status") != "admitted" || Bool(current, "auditObserved")) return false;
            tx.Update(reference, new Dictionary<string, object>
            { ["auditNextAttemptAtUtc"] = Timestamp.FromDateTime(clock.GetUtcNow().UtcDateTime.AddMinutes(5)) });
            return true;
        }, cancellationToken: ct);

    private async Task ObserveStaleSlotAdmissionAsync(DocumentSnapshot admission, CancellationToken ct)
    {
        var caseId = InvestigationDocuments.CaseId("system-integrity", $"slot-admission:{admission.Id}");
        var auditId = InvestigationDocuments.Hash($"slot-admission\n{admission.Id}");
        var caseRef = database.Collection(InvestigationDocuments.Cases).Document(caseId);
        var instanceRef = database.Collection(InvestigationDocuments.Instances).Document(InvestigationDocuments.Hash($"{caseId}\n{auditId}"));
        var copiedRef = CopyReference(caseId, "slot-admission", admission.Id);
        var opened = await database.RunTransactionAsync(async tx =>
        {
            var reads = await tx.GetAllSnapshotsAsync([admission.Reference, caseRef, instanceRef, copiedRef], ct);
            if (!reads[0].Exists || Text(reads[0], "status") != "admitted" ||
                FirestoreGameRoundAuditStore.Time(reads[0], "startedAtUtc") is not { } started ||
                started > clock.GetUtcNow().UtcDateTime.AddMinutes(-2)) return false;
            if (!reads[1].Exists)
            {
                var now = clock.GetUtcNow().UtcDateTime;
                var investigation = new InvestigationCase(caseId, "system-integrity", $"slot-admission:{admission.Id}", "open", 2,
                    [], [new("slot-admission-unsettled", "system-integrity", "The accepted slot admission has no recorded settlement after two minutes; reconcile any consumed free spin or boost and determine whether an outcome was generated.", "unattributed-operational-gap")],
                    now, now, 1);
                tx.Create(caseRef, CaseFields(investigation));
            }
            if (!reads[2].Exists)
                tx.Create(instanceRef, new Dictionary<string, object>
                {
                    ["caseId"] = caseId, ["auditId"] = auditId, ["gameId"] = Text(reads[0], "gameId"),
                    ["instanceId"] = admission.Id, ["capturePending"] = false, ["captureState"] = "admission-only",
                    ["coverage"] = "Admission and available linked receipts retained; no settled game round exists, so an engine outcome cannot be inferred.",
                    ["originalEvidencePresent"] = false, ["capturedAtUtc"] = Timestamp.FromDateTime(clock.GetUtcNow().UtcDateTime)
                });
            if (!reads[3].Exists)
                tx.Create(copiedRef, CopyFields(caseId, auditId, "slot-admission", reads[0], clock.GetUtcNow().UtcDateTime));
            return true;
        }, cancellationToken: ct);
        if (!opened) return;
        var current = await admission.Reference.GetSnapshotAsync(ct);
        var requestId = Text(current, "requestId");
        if (requestId.Length > 0)
        {
            var originals = await Task.WhenAll(database.Collection(InvestigationDocuments.RequestIntents).Document(requestId).GetSnapshotAsync(ct),
                database.Collection(InvestigationDocuments.RequestResults).Document(requestId).GetSnapshotAsync(ct));
            if (originals[0].Exists) await CopyAsync(caseId, auditId, "request-intent", originals[0], ct);
            if (originals[1].Exists) await CopyAsync(caseId, auditId, "request-result", originals[1], ct);
        }
        foreach (var key in new[] { "freeGameUseTransactionId", "specialPointUseTransactionId" })
        {
            var ledgerId = Text(current, key);
            if (GameRequestPayloadObservation.SafeId(ledgerId) != ledgerId || ledgerId.Length == 0) continue;
            var ledger = await database.Collection("balanceTransactions").Document(ledgerId).GetSnapshotAsync(ct);
            if (ledger.Exists) await CopyAsync(caseId, auditId, "admission-ledger", ledger, ct);
        }
        await database.RunTransactionAsync(async tx =>
        {
            var latest = await tx.GetSnapshotAsync(admission.Reference, ct);
            if (!latest.Exists || Bool(latest, "auditObserved")) return false;
            tx.Update(admission.Reference, new Dictionary<string, object>
            {
                ["auditObserved"] = true, ["auditIncidentId"] = caseId,
                ["auditObservedAtUtc"] = Timestamp.FromDateTime(clock.GetUtcNow().UtcDateTime)
            });
            return true;
        }, cancellationToken: ct);
    }

    private async Task ObserveRoundAsync(DocumentSnapshot audit, CancellationToken ct)
    {
        var summary = FirestoreGameRoundAuditStore.Summary(audit);
        var observedGeneration = Long(audit, "securityAuditGeneration");
        var previousAuditCodes = Strings(audit, "securityAuditFindingCodes").ToHashSet(StringComparer.Ordinal);
        var baseFindings = summary.Findings.Where(value => !previousAuditCodes.Contains(value.Code)).ToArray();
        var integrity = await GameSecurityIntegrityAuditor.InspectAsync(database, summary, ct, clock.GetUtcNow().UtcDateTime);
        if (integrity.Findings.Any(value => value.Code == "engine-request-result-pending"))
        {
            await audit.Reference.UpdateAsync(new Dictionary<string, object>
            { ["securityAuditNextAttemptAtUtc"] = Timestamp.FromDateTime(summary.CompletedAtUtc.AddMinutes(2)) },
                cancellationToken: ct);
            return;
        }
        var allFindings = baseFindings.Concat(integrity.Findings).Where(value => value.Severity == "error")
            .DistinctBy(value => value.Code).ToArray();
        if (allFindings.Length > 0)
            await PreserveAsync("system-integrity", $"{summary.GameId}:{summary.RoundId}:{GameSecurityIntegrityAuditor.Version}:" +
                InvestigationDocuments.Hash(string.Join('\n', allFindings.Select(value => value.Code).Order(StringComparer.Ordinal))), [summary.Id], [],
                allFindings.Select(value => new InvestigationSignal(value.Code, "system-integrity", value.Message, "unattributed-integrity-finding")).ToArray(), 2, ct);
        if (summary.GameId is "texas-holdem" or "hearts" or "solitaire" && allFindings.Length == 0)
            await ProjectPairsAsync(summary, ct);
        // Coverage is explicit. A projection completion does not certify that cheating was absent.
        await database.RunTransactionAsync(async tx =>
        {
            var current = await tx.GetSnapshotAsync(audit.Reference, ct);
            if (!current.Exists || !Bool(current, "securityAuditPending")) return false;
            if (Long(current, "securityAuditGeneration") != observedGeneration) return false;
            var currentSummary = FirestoreGameRoundAuditStore.Summary(current);
            var oldCodes = Strings(current, "securityAuditFindingCodes").ToHashSet(StringComparer.Ordinal);
            var captureSummary = currentSummary with
            { Findings = currentSummary.Findings.Where(value => !oldCodes.Contains(value.Code)).ToArray() };
            var (findings, auditStatus) = ReconcileRoundFindings(captureSummary, integrity.Findings);
            tx.Set(database.Collection(InvestigationDocuments.Coverage).Document(audit.Id), new Dictionary<string, object>
            {
                ["auditId"] = audit.Id, ["gameId"] = summary.GameId, ["instanceId"] = summary.RoundId,
                ["observedAtUtc"] = Timestamp.FromDateTime(clock.GetUtcNow().UtcDateTime),
                ["coverage"] = allFindings.Length > 0 ? "system-integrity-review" : summary.GameId is "texas-holdem" or "hearts" or "solitaire" ? "pair-summary-projection; behavioral-score-uncalibrated" : "integrity-findings-only; behavior-not-implemented",
                ["journalEventCount"] = integrity.JournalEventCount, ["journalCoverage"] = integrity.JournalCoverage,
                ["roundCoverage"] = auditStatus, ["noPlayerStrikes"] = true,
                ["version"] = GameSecurityIntegrityAuditor.Version
            });
            var changes = new Dictionary<string, object> { ["securityAuditPending"] = false,
                ["securityAuditProjectedAtUtc"] = Timestamp.FromDateTime(clock.GetUtcNow().UtcDateTime),
                ["auditStatus"] = auditStatus,
                ["securityAuditFindingCodes"] = integrity.Findings.Select(value => value.Code)
                    .Distinct(StringComparer.Ordinal).ToArray(),
                ["findings"] = findings.Select(value => new Dictionary<string, object>
                { ["code"] = value.Code, ["severity"] = value.Severity, ["message"] = value.Message }).ToArray() };
            tx.Update(audit.Reference, changes);
            return true;
        }, cancellationToken: ct);
    }

    internal static (IReadOnlyList<RoundAuditFinding> Findings, string AuditStatus) ReconcileRoundFindings(
        RoundAuditSummary summary, IReadOnlyList<RoundAuditFinding> integrityFindings)
    {
        var cleanReplay = integrityFindings.All(value => value.Severity != "error");
        var practiceReplayed = cleanReplay && PracticeRoundReplayAudit.Supported(summary);
        var arcadeReplayed = cleanReplay && ArcadeReplayAudit.Supports(summary);
        var heartsReplayed = cleanReplay && HeartsEvidenceReplayAudit.Supports(summary);
        var paidCardReplayed = cleanReplay && PaidCardEvidenceReplayAudit.Supports(summary);
        var liarsDiceReplayed = cleanReplay && LiarsDiceEvidenceReplayAudit.Supports(summary);
        var findings = summary.Findings
            .Where(value => !((practiceReplayed || heartsReplayed || liarsDiceReplayed) && value.Code == "independent-replay-pending") &&
                            !(arcadeReplayed && value.Code == "arcade-replay-second-pass-pending") &&
                            !(paidCardReplayed && value.Code == "outcome-replay-pending"))
            .Concat(integrityFindings)
            .DistinctBy(value => (value.Code, value.Severity, value.Message))
            .ToArray();
        var status = findings.Any(value => value.Severity == "error") ? "flagged" :
            findings.Any(value => value.Severity == "warning") ? "incomplete" : "passed";
        return (findings, status);
    }

    internal async Task ProjectPairsAsync(RoundAuditSummary summary, CancellationToken ct)
    {
        var original = await database.Collection(RoundAuditDocuments.Evidence).Document(summary.Id).GetSnapshotAsync(ct);
        if (original.Exists && summary.GameId == "texas-holdem")
            await ProjectStrategyFeaturesAsync(summary, Text(original, "payloadJson"), ct);
        var players = summary.Participants.Where(value => value.IsAccountBacked).OrderBy(value => value.ActorId, StringComparer.Ordinal).ToArray();
        var (network, networkTruncated) = await ReadNetworkAsync(summary.GameId, summary.RoundId, ct);
        for (var first = 0; first < players.Length; first++)
        for (var second = first + 1; second < players.Length; second++)
        {
            var a = players[first]; var b = players[second];
            var pairId = InvestigationDocuments.PairId(summary.GameId, a.ActorId, b.ActorId);
            var pairRef = database.Collection(InvestigationDocuments.PairFeatures).Document(pairId);
            var receiptRef = database.Collection(InvestigationDocuments.PairReceipts).Document(InvestigationDocuments.Hash($"{pairId}\n{summary.Id}"));
            var pair = await database.RunTransactionAsync(async tx =>
            {
                var reads = await tx.GetAllSnapshotsAsync([pairRef, receiptRef], ct);
                var rounds = Long(reads[0], "sharedRounds"); var opposed = Long(reads[0], "opposedOutcomeRounds");
                var recent = Strings(reads[0], "recentAuditIds").ToList();
                if (!reads[1].Exists)
                {
                    rounds++; if (InvestigationAssessment.OpposedNet(a, b)) opposed++;
                    recent.Add(summary.Id); recent = recent.Distinct(StringComparer.Ordinal).TakeLast(20).ToList();
                    tx.Create(receiptRef, new Dictionary<string, object> { ["pairId"] = pairId, ["auditId"] = summary.Id, ["completedAtUtc"] = Timestamp.FromDateTime(summary.CompletedAtUtc) });
                    tx.Set(pairRef, new Dictionary<string, object>
                    {
                        ["gameId"] = summary.GameId, ["firstUserId"] = a.ActorId, ["secondUserId"] = b.ActorId,
                        ["sharedRounds"] = rounds, ["opposedOutcomeRounds"] = opposed, ["recentAuditIds"] = recent.ToArray(),
                        ["updatedAtUtc"] = Timestamp.FromDateTime(clock.GetUtcNow().UtcDateTime),
                        ["coverage"] = "summary-only-uncalibrated; opposed-net-is-not-direct-transfer", ["noPlayerStrikes"] = true
                    });
                }
                return (Rounds: checked((int)rounds), Opposed: checked((int)opposed), Recent: recent.ToArray());
            }, cancellationToken: ct);
            var assessment = InvestigationAssessment.Assess(pair.Rounds, pair.Opposed, network, a.ActorId, b.ActorId, options.Value);
            if (networkTruncated)
                assessment = assessment with
                {
                    NeedsDeeperAnalysis = true,
                    ReviewPriority = Math.Max(assessment.ReviewPriority, 1),
                    Coverage = "network-receipt-scan-truncated; needs-manual-review",
                    Signals = assessment.Signals.Concat([new InvestigationSignal("network-receipt-scan-truncated", "coverage",
                        "More than 2,000 request intents were linked to this instance. Automatic network comparison was bounded; inspect the remaining receipts before excluding shared context.", "incomplete")]).ToArray()
                };
            if (assessment.NeedsDeeperAnalysis)
                await QueuePairAnalysisAsync(pairId, summary, a.ActorId, b.ActorId, pair.Recent, assessment, network, ct);
        }
    }

    private async Task CapturePageAsync(DocumentSnapshot instance, CancellationToken ct)
    {
        var gameId = Text(instance, "gameId"); var instanceId = Text(instance, "instanceId");
        var caseId = Text(instance, "caseId"); var auditId = Text(instance, "auditId");
        var investigation = await database.Collection(InvestigationDocuments.Cases).Document(caseId).GetSnapshotAsync(ct);
        if (IsExpiredCleared(investigation))
        {
            await instance.Reference.UpdateAsync(new Dictionary<string, object>
            { ["capturePending"] = false, ["captureState"] = "case-expired" }, cancellationToken: ct);
            return;
        }
        var updates = new Dictionary<string, object>();
        if (!Bool(instance, "roundSnapshotCopied"))
        {
            var snapshots = await Task.WhenAll(database.Collection(RoundAuditDocuments.Summaries).Document(auditId).GetSnapshotAsync(ct),
                database.Collection(RoundAuditDocuments.Evidence).Document(auditId).GetSnapshotAsync(ct));
            if (snapshots[0].Exists) await CopyAsync(caseId, auditId, "summary", snapshots[0], ct);
            if (gameId == "texas-holdem")
            {
                var revenue = await database.Collection("creditHoldemMatchRevenue").Document(instanceId).GetSnapshotAsync(ct);
                if (revenue.Exists) await CopyAsync(caseId, auditId, "hand-financial-revenue", revenue, ct);
                updates["handRevenuePresent"] = revenue.Exists;
            }
            if (snapshots[1].Exists)
            {
                await CopyAsync(caseId, auditId, "round-evidence", snapshots[1], ct);
                try
                {
                    var originalSummary = FirestoreGameRoundAuditStore.Summary(snapshots[0]);
                    var payload = Text(snapshots[1], "payloadJson");
                    updates["versions"] = InvestigationAssessment.Versions(payload, originalSummary.EvidenceKind, originalSummary.AuditorVersion).ToDictionary(pair => pair.Key, pair => (object)pair.Value);
                    var check = GameRoundEvidenceIntegrity.Check(originalSummary, payload, Text(snapshots[1], "sha256"));
                    updates["snapshotIntegrity"] = check.Valid ? "digest-and-summary-consistent" : check.Code;
                    updates["sourceSnapshotState"] = await CopySourceSnapshotAsync(caseId, auditId, payload, ct);
                    await CopyExplicitFinancialLinksAsync(caseId, auditId, gameId, instanceId, payload, ct);
                }
                catch (JsonException) { updates["snapshotIntegrity"] = "invalid-json"; }
            }
            updates["originalEvidencePresent"] = snapshots[1].Exists;
            updates["roundSnapshotCopied"] = true;
        }
        var missingResults = Strings(instance, "missingResultIds").ToHashSet(StringComparer.Ordinal);
        foreach (var requestId in missingResults.Take(options.Value.CapturePageSize).ToArray())
        {
            var result = await database.Collection(InvestigationDocuments.RequestResults).Document(requestId).GetSnapshotAsync(ct);
            if (result.Exists) { await CopyAsync(caseId, auditId, "request-result", result, ct); missingResults.Remove(requestId); }
        }
        foreach (var stream in new[] { (Collection: InvestigationDocuments.Events, Kind: "journal-event", Cursor: "eventCursor", Done: "eventsDone"),
            (Collection: InvestigationDocuments.RequestIntents, Kind: "request-intent", Cursor: "requestCursor", Done: "requestsDone") })
        {
            if (Bool(instance, stream.Done)) continue;
            Query query = database.Collection(stream.Collection).WhereEqualTo("gameId", gameId).WhereEqualTo("instanceId", instanceId).OrderBy(FieldPath.DocumentId);
            var cursor = Text(instance, stream.Cursor);
            if (cursor.Length > 0) query = query.StartAfter(cursor);
            var page = await query.Limit(options.Value.CapturePageSize).GetSnapshotAsync(ct);
            foreach (var original in page.Documents)
            {
                await CopyAsync(caseId, auditId, stream.Kind, original, ct);
                var linkedRequest = stream.Kind == "request-intent" ? original.Id : Text(original, "requestId");
                if (linkedRequest.Length > 0)
                {
                    // Start/admission requests may not know the instance until the committed journal links them.
                    if (stream.Kind == "journal-event")
                    {
                        var intent = await database.Collection(InvestigationDocuments.RequestIntents).Document(linkedRequest).GetSnapshotAsync(ct);
                        if (intent.Exists) await CopyAsync(caseId, auditId, "request-intent", intent, ct);
                    }
                    var result = await database.Collection(InvestigationDocuments.RequestResults).Document(linkedRequest).GetSnapshotAsync(ct);
                    if (result.Exists) await CopyAsync(caseId, auditId, "request-result", result, ct);
                    else missingResults.Add(linkedRequest);
                }
            }
            updates[stream.Cursor] = page.Count == 0 ? cursor : page.Documents[^1].Id;
            updates[stream.Done] = page.Count < options.Value.CapturePageSize;
        }
        if (!Bool(instance, "gameEventsDone"))
        {
            var collection = gameId switch
            {
                "hi-lo" => "hiLoEvents",
                "house-holdem" => "houseHoldemRoundEvents",
                _ => null
            };
            if (collection is null) updates["gameEventsDone"] = true;
            else
            {
                Query query = database.Collection(collection).WhereEqualTo("roundId", instanceId)
                    .OrderBy(FieldPath.DocumentId);
                var cursor = Text(instance, "gameEventCursor");
                if (cursor.Length > 0) query = query.StartAfter(cursor);
                var page = await query.Limit(options.Value.CapturePageSize).GetSnapshotAsync(ct);
                foreach (var original in page.Documents)
                {
                    await CopyAsync(caseId, auditId, "game-operation-event", original, ct);
                    if (gameId == "hi-lo")
                    {
                        var operation = await database.Collection("hiLoOperations").Document(original.Id).GetSnapshotAsync(ct);
                        if (operation.Exists) await CopyAsync(caseId, auditId, "game-operation", operation, ct);
                    }
                }
                updates["gameEventCursor"] = page.Count == 0 ? cursor : page.Documents[^1].Id;
                updates["gameEventsDone"] = page.Count < options.Value.CapturePageSize;
            }
        }
        if (!Bool(instance, "ledgersDone"))
        {
            var cursor = Text(instance, "ledgerCursor");
            Query query;
            if ((gameId == "blackjack" && instanceId.Contains("-round-", StringComparison.Ordinal)) ||
                gameId == "three-card-poker")
            {
                // Table wager/payout IDs are hashed from commands, but their ledger reference is the round ID.
                query = database.Collection("balanceTransactions").WhereEqualTo("idempotencyKey", instanceId)
                    .OrderBy(FieldPath.DocumentId);
                if (cursor.Length > 0) query = query.StartAfter(cursor);
            }
            else if (LedgerPrefix(gameId, instanceId) is { } prefix)
            {
                // Several paid paths use deterministic round-prefixed ledger IDs without a roundId field.
                query = database.Collection("balanceTransactions").OrderBy(FieldPath.DocumentId)
                    .EndAt(prefix + "\uf8ff");
                query = cursor.Length > 0 ? query.StartAfter(cursor) : query.StartAt(prefix);
            }
            else
            {
                query = database.Collection("balanceTransactions").WhereEqualTo("roundId", instanceId).OrderBy(FieldPath.DocumentId);
                if (cursor.Length > 0) query = query.StartAfter(cursor);
            }
            var page = await query.Limit(options.Value.CapturePageSize).GetSnapshotAsync(ct);
            foreach (var original in page.Documents)
                await CopyAsync(caseId, auditId, "round-ledger", original, ct);
            updates["ledgerCursor"] = page.Count == 0 ? cursor : page.Documents[^1].Id;
            updates["ledgersDone"] = page.Count < options.Value.CapturePageSize;
        }
        var eventsDone = updates.GetValueOrDefault("eventsDone") as bool? ?? Bool(instance, "eventsDone");
        var requestsDone = updates.GetValueOrDefault("requestsDone") as bool? ?? Bool(instance, "requestsDone");
        var gameEventsDone = updates.GetValueOrDefault("gameEventsDone") as bool? ?? Bool(instance, "gameEventsDone");
        var ledgersDone = updates.GetValueOrDefault("ledgersDone") as bool? ?? Bool(instance, "ledgersDone");
        var missingResultDeadline = FirestoreGameRoundAuditStore.Time(instance, "capturedAtUtc")?.AddMinutes(10);
        var missingResultsTimedOut = missingResults.Count > 0 && missingResultDeadline <= clock.GetUtcNow().UtcDateTime;
        var done = eventsDone && requestsDone && gameEventsDone && ledgersDone &&
            (missingResults.Count == 0 || missingResultsTimedOut);
        updates["missingResultIds"] = missingResults.ToArray();
        updates["capturePending"] = !done;
        updates["captureNextAttemptAtUtc"] = Timestamp.FromDateTime(clock.GetUtcNow().UtcDateTime.Add(
            missingResults.Count > 0 && !missingResultsTimedOut ? TimeSpan.FromMinutes(1) : TimeSpan.Zero));
        updates["captureState"] = missingResultsTimedOut ? "partial-missing-request-results" : done ? "available-originals-captured" : "capture-pending";
        var evidenceAvailable = updates.GetValueOrDefault("originalEvidencePresent") as bool? ?? Bool(instance, "originalEvidencePresent");
        updates["coverage"] = missingResultsTimedOut ? "Available originals copied; one or more linked request results were still absent after ten minutes. Missing IDs are retained for manual reconciliation."
            : done ? (evidenceAvailable ? "All available original documents copied; absent instrumentation and unknown-instance requests cannot be reconstructed. No certification of human play or absence of collusion." : "Original round detail was already missing; available journals/requests copied. Full original round evidence cannot be certified.")
            : "Pinned originals; bounded journal/request capture or missing request result still pending.";
        updates["lastCaptureAtUtc"] = Timestamp.FromDateTime(clock.GetUtcNow().UtcDateTime);
        await instance.Reference.UpdateAsync(updates, cancellationToken: ct);
    }

    private static string? LedgerPrefix(string gameId, string instanceId) => gameId switch
    {
        "texas-holdem" or "blackjack" or "hearts" => instanceId + "-",
        "keno" or "sic-bo" or "baccarat" or "video-poker" or "casino-war" or
            "dragon-tiger" or "andar-bahar" or "house-holdem" => gameId + "-" + instanceId + "-",
        "solitaire" => instanceId + "-claim-",
        _ => null
    };

    private static readonly HashSet<string> SourceCollections = new(StringComparer.Ordinal)
    {
        "slotSpinResults", "blackjackGames", "baccaratRounds", "houseHoldemRounds", "videoPokerRounds",
        "casinoWarRounds", "kenoRounds", "sicBoRounds", "andarBaharRounds", "dragonTigerRounds",
        "minesRounds", "cardGameResults", "asteroidsFreeRuns", "flappyFreeRuns", "horseFlightRuns",
        "asteroidsRuns", "flappyRuns", "solitaireMatches", "heartsPaidMatches"
    };

    private async Task<string> CopySourceSnapshotAsync(string caseId, string auditId, string payload, CancellationToken ct)
    {
        using var json = JsonDocument.Parse(payload);
        var root = json.RootElement;
        if (root.ValueKind != JsonValueKind.Object) return "source-pointer-not-object";
        if (!root.TryGetProperty("sourceCollection", out var collectionField) ||
            !root.TryGetProperty("sourceDocumentId", out var idField) ||
            collectionField.ValueKind != JsonValueKind.String || idField.ValueKind != JsonValueKind.String)
            return "no-source-pointer-in-round-evidence";
        var collection = collectionField.GetString()!;
        var id = idField.GetString()!;
        if (!SourceCollections.Contains(collection) || GameRequestPayloadObservation.SafeId(id) != id)
            return "source-pointer-outside-allowlist";
        var source = await database.Collection(collection).Document(id).GetSnapshotAsync(ct);
        if (source.Exists) await CopyAsync(caseId, auditId, "source-document", source, ct);
        if (root.TryGetProperty("data", out var detail) && detail.ValueKind == JsonValueKind.Object &&
            detail.TryGetProperty("feeLedgerId", out var feeField) && feeField.ValueKind == JsonValueKind.String &&
            feeField.GetString() is { } feeLedgerId)
            await CopyKnownLedgerAsync(caseId, auditId, feeLedgerId, ct);
        if (collection == "slotSpinResults")
        {
            foreach (var suffix in new[] { "wager", "payout", "free-games-award", "special-points-award", "energy-award", "energy-multiplier-use" })
                await CopyKnownLedgerAsync(caseId, auditId, $"{id}-{suffix}", ct);
            if (root.TryGetProperty("data", out var data) && data.ValueKind == JsonValueKind.Object &&
                data.TryGetProperty("admissionId", out var admissionField) && admissionField.ValueKind == JsonValueKind.String &&
                GameRequestPayloadObservation.SafeId(admissionField.GetString()) is { } admissionId)
            {
                var admission = await database.Collection("slotSpinAdmissions").Document(admissionId).GetSnapshotAsync(ct);
                if (!admission.Exists) return "source-copied-slot-admission-missing";
                await CopyAsync(caseId, auditId, "slot-admission", admission, ct);
                foreach (var key in new[] { "freeGameUseTransactionId", "specialPointUseTransactionId" })
                {
                    var ledgerId = Text(admission, key);
                    if (ledgerId.Length > 0) await CopyKnownLedgerAsync(caseId, auditId, ledgerId, ct);
                }
            }
        }
        return source.Exists ? "source-document-copied" : "source-document-missing";
    }

    private async Task CopyKnownLedgerAsync(string caseId, string auditId, string id, CancellationToken ct)
    {
        if (GameRequestPayloadObservation.SafeId(id) != id) return;
        var document = await database.Collection("balanceTransactions").Document(id).GetSnapshotAsync(ct);
        if (document.Exists) await CopyAsync(caseId, auditId, "linked-ledger", document, ct);
    }

    private async Task CopyExplicitFinancialLinksAsync(string caseId, string auditId, string gameId,
        string instanceId, string payload, CancellationToken ct)
    {
        if (gameId == "texas-holdem")
        {
            using var handJson = JsonDocument.Parse(payload);
            var root = handJson.RootElement;
            if (root.TryGetProperty("matchId", out var matchIdField) && matchIdField.ValueKind == JsonValueKind.String &&
                GameRequestPayloadObservation.SafeId(matchIdField.GetString()) is { } matchId)
            {
                var match = await database.Collection("creditHoldemMatches").Document(matchId).GetSnapshotAsync(ct);
                if (match.Exists) await CopyAsync(caseId, auditId, "table-state", match, ct);
            }
            if (!root.TryGetProperty("players", out var players) || players.ValueKind != JsonValueKind.Array) return;
            foreach (var player in players.EnumerateArray().Take(5))
            {
                if (!player.TryGetProperty("escrowTicketId", out var field) || field.ValueKind != JsonValueKind.String ||
                    GameRequestPayloadObservation.SafeId(field.GetString()) is not { } ticketId) continue;
                var ticket = await database.Collection("creditHoldemTickets").Document(ticketId).GetSnapshotAsync(ct);
                if (ticket.Exists) await CopyAsync(caseId, auditId, "escrow-ticket", ticket, ct);
                foreach (var suffix in new[] { "-buy-in", "-cash-out", "-buy-in-refund" })
                    await CopyKnownLedgerAsync(caseId, auditId, ticketId + suffix, ct);
            }
            return;
        }
        if (gameId == "solitaire")
        {
            foreach (var collection in new[] { "solitaireMatchRevenue", "solitaireHouseFunding" })
            {
                var linked = await database.Collection(collection).Document(instanceId).GetSnapshotAsync(ct);
                if (linked.Exists) await CopyAsync(caseId, auditId, collection, linked, ct);
            }
            foreach (var collection in new[] { "solitaireTickets", "solitaireMatchPlayers" })
            {
                var linked = await database.Collection(collection).WhereEqualTo("matchId", instanceId).Limit(20).GetSnapshotAsync(ct);
                foreach (var document in linked.Documents)
                {
                    await CopyAsync(caseId, auditId, collection, document, ct);
                    if (collection == "solitaireTickets")
                        foreach (var suffix in new[] { "-buyin", "-refund" })
                            await CopyKnownLedgerAsync(caseId, auditId, document.Id + suffix, ct);
                }
            }
            return;
        }
        if (gameId != "hearts") return;
        var revenue = await database.Collection("heartsPaidRevenue").Document(instanceId).GetSnapshotAsync(ct);
        if (revenue.Exists) await CopyAsync(caseId, auditId, "match-financial-revenue", revenue, ct);
        var funding = await database.Collection("heartsPaidFunding").Document(instanceId).GetSnapshotAsync(ct);
        if (funding.Exists) await CopyAsync(caseId, auditId, "match-financial-funding", funding, ct);
        var transfers = await database.Collection("heartsPaidFundingTransfers").WhereEqualTo("matchId", instanceId).Limit(20).GetSnapshotAsync(ct);
        foreach (var transfer in transfers.Documents)
            await CopyAsync(caseId, auditId, "match-funding-transfer", transfer, ct);
        using var json = JsonDocument.Parse(payload);
        if (json.RootElement.ValueKind != JsonValueKind.Object ||
            !json.RootElement.TryGetProperty("payoutLedgerIds", out var ids) || ids.ValueKind != JsonValueKind.Array) return;
        foreach (var id in ids.EnumerateArray().Take(20))
            if (id.ValueKind == JsonValueKind.String && id.GetString() is { } value)
                await CopyKnownLedgerAsync(caseId, auditId, value, ct);
    }

    internal async Task<(IReadOnlyList<InvestigationNetworkObservation> Observations, bool Truncated)> ReadNetworkAsync(string gameId, string instanceId, CancellationToken ct)
    {
        var values = new List<InvestigationNetworkObservation>();
        var seenReceipts = new HashSet<string>(StringComparer.Ordinal);
        const int pageSize = 100;
        const int maximumRequests = 2_000;
        var truncated = false;

        void Add(DocumentSnapshot document)
        {
            if (!document.Exists || !seenReceipts.Add(document.Id)) return;
            var fields = document.ToDictionary();
            var json = fields.GetValueOrDefault("observationJson") as string ?? fields.GetValueOrDefault("requestJson") as string;
            try
            {
                using var payload = JsonDocument.Parse(json ?? JsonSerializer.Serialize(Normalize(fields), InvestigationDocuments.Json));
                var root = payload.RootElement;
                var user = Property(root, "userId");
                if (string.IsNullOrWhiteSpace(user)) return;
                var received = DateTime.TryParse(Property(root, "receivedAtUtc"), CultureInfo.InvariantCulture, DateTimeStyles.AdjustToUniversal, out var time)
                    ? DateTime.SpecifyKind(time, DateTimeKind.Utc) : FirestoreGameRoundAuditStore.Time(document, "receivedAtUtc") ?? DateTime.UnixEpoch;
                root.TryGetProperty("geo", out var geo);
                var accuracy = double.TryParse(Property(geo, "accuracyRadiusKm"), NumberStyles.Float, CultureInfo.InvariantCulture, out var km) ? km : (double?)null;
                var geoTime = DateTime.TryParse(Property(geo, "observedAtUtc"), CultureInfo.InvariantCulture, DateTimeStyles.AdjustToUniversal, out var observed) ? observed : (DateTime?)null;
                values.Add(new(user, Property(root, "ipAddress"), received, Property(geo, "city"), Property(geo, "provider"), geoTime, accuracy, Property(geo, "uncertainty"),
                    Property(geo, "countryCode") ?? Property(geo, "country"), Property(geo, "regionCode") ?? Property(geo, "region"), Property(root, "ipSource")));
            }
            catch (JsonException) { /* Malformed context lowers coverage; it cannot exclude a pair. */ }
        }

        // Admission and join commands often have no path instance. Engine observations
        // link them by request ID even when the request's own instance field is null.
        var linkedIds = new HashSet<string>(StringComparer.Ordinal);
        var eventCursor = "";
        var eventCount = 0;
        while (true)
        {
            Query query = database.Collection(InvestigationDocuments.Events).WhereEqualTo("gameId", gameId)
                .WhereEqualTo("instanceId", instanceId).OrderBy(FieldPath.DocumentId);
            if (eventCursor.Length > 0) query = query.StartAfter(eventCursor);
            var page = await query.Limit(pageSize).GetSnapshotAsync(ct);
            eventCount += page.Count;
            foreach (var item in page.Documents)
            {
                var requestId = Text(item, "requestId");
                if (requestId.Length > 0 && GameRequestPayloadObservation.SafeId(requestId) == requestId)
                    linkedIds.Add(requestId);
            }
            if (page.Count == 0 || page.Count < pageSize) break;
            eventCursor = page.Documents[^1].Id;
            if (eventCount >= maximumRequests)
            {
                truncated = true;
                break;
            }
        }
        foreach (var batch in linkedIds.Chunk(50))
        {
            var linked = await Task.WhenAll(batch.Select(id => database.Collection(InvestigationDocuments.RequestIntents)
                .Document(id).GetSnapshotAsync(ct)));
            foreach (var receipt in linked) Add(receipt);
        }

        var cursor = "";
        var inspected = 0;
        while (true)
        {
            Query query = database.Collection(InvestigationDocuments.RequestIntents).WhereEqualTo("gameId", gameId)
                .WhereEqualTo("instanceId", instanceId).OrderBy(FieldPath.DocumentId);
            if (cursor.Length > 0) query = query.StartAfter(cursor);
            var page = await query.Limit(pageSize).GetSnapshotAsync(ct);
            inspected += page.Count;
            foreach (var document in page.Documents) Add(document);
            if (page.Count == 0 || page.Count < pageSize) break;
            cursor = page.Documents[^1].Id;
            if (inspected >= maximumRequests)
            {
                truncated = true;
                break;
            }
        }
        return (values, truncated);
    }

    private Task CopyAsync(string caseId, string auditId, string kind, DocumentSnapshot original, CancellationToken ct) => database.RunTransactionAsync(async tx =>
    {
        var destination = CopyReference(caseId, kind, original.Id);
        var reads = await tx.GetAllSnapshotsAsync([destination, database.Collection(InvestigationDocuments.Cases).Document(caseId)], ct);
        if (reads[0].Exists || IsExpiredCleared(reads[1])) return false;
        tx.Create(destination, CopyFields(caseId, auditId, kind, original, clock.GetUtcNow().UtcDateTime));
        return true;
    }, cancellationToken: ct);
    private DocumentReference CopyReference(string caseId, string kind, string originalId) => database.Collection(InvestigationDocuments.Evidence)
        .Document(InvestigationDocuments.Hash($"{caseId}\n{kind}\n{originalId}"));
    private static Dictionary<string, object> CopyFields(string caseId, string auditId, string kind, DocumentSnapshot original, DateTime now) => new()
    {
        ["caseId"] = caseId, ["auditId"] = auditId, ["kind"] = kind, ["sourcePath"] = original.Reference.Path,
        ["sourceDocumentId"] = original.Id, ["originalFields"] = original.ToDictionary(), ["capturedAtUtc"] = Timestamp.FromDateTime(now)
    };
    private static Dictionary<string, object> CaseFields(InvestigationCase value) => new()
    {
        ["kind"] = value.Kind, ["status"] = value.Status, ["reviewPriority"] = value.ReviewPriority,
        ["createdAtUtc"] = Timestamp.FromDateTime(value.CreatedAtUtc), ["updatedAtUtc"] = Timestamp.FromDateTime(value.UpdatedAtUtc),
        ["caseJson"] = JsonSerializer.Serialize(value, InvestigationDocuments.Json), ["noAutomaticPlayerStrikes"] = true,
        ["deleteAfterUtc"] = value.DeleteAfterUtc is { } delete ? Timestamp.FromDateTime(delete) : null!,
        ["purgePending"] = value.DeleteAfterUtc is not null
    };
    private static InvestigationCase ReadCase(DocumentSnapshot value) => JsonSerializer.Deserialize<InvestigationCase>(value.GetValue<string>("caseJson"), InvestigationDocuments.Json)!;
    private static string Text(DocumentSnapshot value, string key) => value.Exists && value.TryGetValue<string>(key, out var text) ? text : "";
    private static bool Bool(DocumentSnapshot value, string key) => value.Exists && value.TryGetValue<bool>(key, out var result) && result;
    private static long Long(DocumentSnapshot value, string key) => value.Exists && value.TryGetValue<long>(key, out var result) ? result : 0;
    private static IReadOnlyList<string> Strings(DocumentSnapshot value, string key) => value.Exists && value.TryGetValue<List<string>>(key, out var values) ? values : [];
    private static string? Property(JsonElement element, string key) => element.ValueKind == JsonValueKind.Object && element.TryGetProperty(key, out var value) && value.ValueKind is JsonValueKind.String or JsonValueKind.Number ? value.ToString() : null;
    private static object? Normalize(object? value) => value switch
    {
        Timestamp time => time.ToDateTime(), DocumentReference reference => reference.Path,
        IDictionary<string, object> map => map.ToDictionary(pair => pair.Key, pair => Normalize(pair.Value)),
        IEnumerable<object> items => items.Select(Normalize).ToArray(), _ => value
    };

    internal async Task<InvestigationPage> ListAsync(int limit, string? cursor, CancellationToken ct)
    {
        ValidatePageSize(limit);
        Query query = database.Collection(InvestigationDocuments.Cases).OrderByDescending("createdAtUtc").OrderByDescending(FieldPath.DocumentId);
        if (cursor is not null)
        {
            var after = cursors.Decode("security-investigations", cursor);
            query = query.StartAfter(Timestamp.FromDateTime(after.OccurredAtUtc), after.Id);
        }
        var page = await query.Limit(limit + 1).GetSnapshotAsync(ct);
        var items = page.Documents.Take(limit).Select(ReadCase).ToArray();
        return new(items, page.Count > limit ? cursors.Encode("security-investigations", items[^1].CreatedAtUtc, items[^1].Id) : null);
    }
    internal async Task<InvestigationCase?> ReadAsync(string id, CancellationToken ct)
    {
        FirestoreGameRoundAuditStore.ValidateId(id);
        var snapshot = await database.Collection(InvestigationDocuments.Cases).Document(id).GetSnapshotAsync(ct);
        return snapshot.Exists ? ReadCase(snapshot) : null;
    }

    internal Task RequeueAuditAsync(string auditId, string reviewer, CancellationToken ct)
    {
        FirestoreGameRoundAuditStore.ValidateId(auditId);
        var summaryRef = database.Collection(RoundAuditDocuments.Summaries).Document(auditId);
        var evidenceRef = database.Collection(RoundAuditDocuments.Evidence).Document(auditId);
        return database.RunTransactionAsync(async tx =>
        {
            var reads = await tx.GetAllSnapshotsAsync([summaryRef, evidenceRef], ct);
            if (!reads[0].Exists) throw new KeyNotFoundException("The round audit was not found.");
            if (!reads[1].Exists) throw new ArgumentException("The private round evidence is no longer available for a fresh audit.");
            tx.Update(summaryRef, new Dictionary<string, object>
            {
                ["securityAuditPending"] = true,
                ["securityAuditNextAttemptAtUtc"] = Timestamp.FromDateTime(clock.GetUtcNow().UtcDateTime),
                ["securityAuditRequestedVersion"] = GameSecurityIntegrityAuditor.Version,
                ["securityAuditRequestedBy"] = reviewer,
                ["securityAuditRequestedAtUtc"] = Timestamp.FromDateTime(clock.GetUtcNow().UtcDateTime)
            });
            return true;
        }, cancellationToken: ct);
    }
    internal async Task<InvestigationEvidencePage> ReadInstancesAsync(string id, int limit, string? cursor, CancellationToken ct) =>
        await ReadPageAsync(InvestigationDocuments.Instances, "caseId", id, $"security-instances:{id}", limit, cursor, ct);
    internal async Task<InvestigationEvidencePage> ReadEvidenceAsync(string caseId, string auditId, int limit, string? cursor, CancellationToken ct)
    {
        FirestoreGameRoundAuditStore.ValidateId(caseId); FirestoreGameRoundAuditStore.ValidateId(auditId); ValidatePageSize(limit);
        Query query = database.Collection(InvestigationDocuments.Evidence).WhereEqualTo("caseId", caseId).WhereEqualTo("auditId", auditId).OrderBy(FieldPath.DocumentId);
        var scope = $"security-evidence:{caseId}:{auditId}";
        if (cursor is not null) query = query.StartAfter(cursors.Decode(scope, cursor).Id);
        var page = await query.Limit(limit + 1).GetSnapshotAsync(ct);
        return Page(page, scope, limit);
    }
    private async Task<InvestigationEvidencePage> ReadPageAsync(string collection, string field, string id, string scope, int limit, string? cursor, CancellationToken ct, bool validateId = true)
    {
        if (validateId) FirestoreGameRoundAuditStore.ValidateId(id); ValidatePageSize(limit);
        Query query = database.Collection(collection).WhereEqualTo(field, id).OrderBy(FieldPath.DocumentId);
        if (cursor is not null) query = query.StartAfter(cursors.Decode(scope, cursor).Id);
        return Page(await query.Limit(limit + 1).GetSnapshotAsync(ct), scope, limit);
    }
    private InvestigationEvidencePage Page(QuerySnapshot page, string scope, int limit)
    {
        var documents = page.Documents.Take(limit).ToArray();
        return new(documents.Select(document => new Dictionary<string, object> { ["id"] = document.Id, ["data"] = Normalize(document.ToDictionary())! }).ToArray(),
            page.Count > limit ? cursors.Encode(scope, DateTime.UnixEpoch, documents[^1].Id) : null);
    }
    private static void ValidatePageSize(int limit) { if (limit is < 1 or > 100) throw new ArgumentException("Choose a page size from 1 to 100."); }

    private async Task SynchronizeAttributedCaseAsync(DocumentSnapshot source, CancellationToken ct)
    {
        var incident = FirestoreRiskIncidentStore.ReadIncident(source);
        await CreateAttributedIncidentAsync(incident.Id, incident.EvidenceIds, ct);
        var caseRef = database.Collection(InvestigationDocuments.Cases).Document(InvestigationDocuments.CaseId("attributed-player-incident", $"risk:{incident.Id}"));
        await database.RunTransactionAsync(async tx =>
        {
            var reads = await tx.GetAllSnapshotsAsync([source.Reference, caseRef], ct);
            if (!reads[0].Exists || !reads[1].Exists) return false;
            // A concurrent review leaves the marker for another pass; never acknowledge a stale decision.
            if (reads[0].GetValue<string>("incidentJson") != source.GetValue<string>("incidentJson")) return false;
            var current = ReadCase(reads[1]);
            var cleared = incident.Status == "resolved" && incident.ResolutionOutcome == "cleared";
            var resolved = incident.Status == "resolved";
            var updated = current with
            {
                Status = cleared ? "cleared" : resolved ? "preserved" : "open",
                UpdatedAtUtc = clock.GetUtcNow().UtcDateTime,
                DeleteAfterUtc = cleared ? (incident.ResolvedAtUtc ?? clock.GetUtcNow().UtcDateTime).AddDays(30) : null,
                ResolutionReason = incident.ResolutionReason
            };
            tx.Set(caseRef, CaseFields(updated));
            tx.Update(source.Reference, new Dictionary<string, object> { ["investigationPending"] = false, ["investigationId"] = current.Id });
            return true;
        }, cancellationToken: ct);
    }

    internal Task<InvestigationCase> ResolveAsync(string id, string outcome, string reason, string reviewer, CancellationToken ct)
    {
        FirestoreGameRoundAuditStore.ValidateId(id);
        if (outcome is not ("cleared" or "preserved") || string.IsNullOrWhiteSpace(reason) || reason.Length > 4000) throw new ArgumentException("Choose cleared or preserved and provide a bounded review reason.");
        var reference = database.Collection(InvestigationDocuments.Cases).Document(id);
        return database.RunTransactionAsync(async tx =>
        {
            var snapshot = await tx.GetSnapshotAsync(reference, ct);
            if (!snapshot.Exists) throw new KeyNotFoundException("The investigation was not found.");
            var previous = ReadCase(snapshot);
            if (previous.Kind == "attributed-player-incident") throw new ArgumentException("Resolve the linked player risk incident so restrictions and evidence share one decision.");
            var now = clock.GetUtcNow().UtcDateTime;
            var updated = previous with { Status = outcome, UpdatedAtUtc = now, DeleteAfterUtc = outcome == "cleared" ? now.AddDays(30) : null, ResolutionReason = reason };
            tx.Set(reference, CaseFields(updated));
            tx.Set(reference.Collection("reviews").Document(Guid.NewGuid().ToString("N")), new Dictionary<string, object>
            {
                ["reviewedAtUtc"] = Timestamp.FromDateTime(now), ["reviewedBy"] = reviewer, ["outcome"] = outcome, ["reason"] = reason, ["previousStatus"] = previous.Status
            });
            return updated;
        }, cancellationToken: ct);
    }

    private async Task PurgePageAsync(DocumentSnapshot source, CancellationToken ct)
    {
        var caseId = source.Id;
        var copies = await database.Collection(InvestigationDocuments.Evidence).WhereEqualTo("caseId", caseId).Limit(options.Value.CapturePageSize).GetSnapshotAsync(ct);
        foreach (var copy in copies.Documents)
            await database.RunTransactionAsync(async tx =>
            {
                var current = await tx.GetSnapshotAsync(source.Reference, ct);
                if (!CanPurge(current)) return false;
                tx.Delete(copy.Reference); return true;
            }, cancellationToken: ct);
        if (copies.Count > 0) return;
        var instances = await database.Collection(InvestigationDocuments.Instances).WhereEqualTo("caseId", caseId).Limit(options.Value.CapturePageSize).GetSnapshotAsync(ct);
        foreach (var instance in instances.Documents)
            await database.RunTransactionAsync(async tx =>
            {
                var auditRef = database.Collection(RoundAuditDocuments.Summaries).Document(Text(instance, "auditId"));
                var holdRef = database.Collection(InvestigationDocuments.InstanceHolds).Document(InvestigationDocuments.InstanceKey(Text(instance, "gameId"), Text(instance, "instanceId")));
                var reads = await tx.GetAllSnapshotsAsync([source.Reference, auditRef, holdRef], ct);
                if (!CanPurge(reads[0])) return false;
                var remaining = Strings(reads[2], "investigationIds").Where(value => value != caseId).ToArray();
                tx.Set(holdRef, new Dictionary<string, object> { ["investigationIds"] = remaining, ["held"] = remaining.Length > 0 }, SetOptions.MergeAll);
                if (reads[1].Exists)
                {
                    var auditHolds = Strings(reads[1], "investigationHoldIds").Where(value => value != caseId).ToArray();
                    var changes = new Dictionary<string, object> { ["investigationHoldIds"] = auditHolds };
                    if (auditHolds.Length == 0 && !Bool(reads[1], "preserveIndefinitely") && Strings(reads[1], "riskHoldIncidentIds").Count == 0 && Strings(reads[1], "riskPreservedIncidentIds").Count == 0)
                    {
                        if (!Bool(reads[1], "evidencePresent"))
                        { changes["retentionState"] = "deleted"; changes["rawEvidencePurgePending"] = true;
                          changes["rawEvidenceNextAttemptAtUtc"] = Timestamp.FromDateTime(clock.GetUtcNow().UtcDateTime); }
                        else
                        { changes["retentionState"] = "eligible-resolved"; changes["evidenceDeleteAfterUtc"] = Timestamp.FromDateTime(clock.GetUtcNow().UtcDateTime); }
                    }
                    tx.Update(auditRef, changes);
                }
                tx.Delete(instance.Reference); return true;
            }, cancellationToken: ct);
        if (instances.Count == 0)
            await database.RunTransactionAsync(async tx =>
            {
                var current = await tx.GetSnapshotAsync(source.Reference, ct);
                if (!CanPurge(current)) return false;
                tx.Update(source.Reference, new Dictionary<string, object> { ["purgePending"] = false, ["evidencePurgedAtUtc"] = Timestamp.FromDateTime(clock.GetUtcNow().UtcDateTime) });
                return true;
            }, cancellationToken: ct);
    }
    private bool CanPurge(DocumentSnapshot value) => value.Exists && Bool(value, "purgePending") &&
        IsExpiredCleared(value);
    private bool IsExpiredCleared(DocumentSnapshot value) => value.Exists &&
        ReadCase(value) is { Status: "cleared", DeleteAfterUtc: { } deadline } && deadline <= clock.GetUtcNow().UtcDateTime;

    private async Task QueuePairAnalysisAsync(string pairId, RoundAuditSummary summary, string first, string second, string[] auditIds,
        InvestigationPairAssessment assessment, IReadOnlyList<InvestigationNetworkObservation> network, CancellationToken ct)
    {
        var comparison = await ReadPairStrategyComparisonAsync(summary.GameId, first, second, ct);
        await database.RunTransactionAsync(async tx =>
        {
        var reference = database.Collection(InvestigationDocuments.PairAnalysisQueue).Document(pairId);
        var existing = await tx.GetSnapshotAsync(reference, ct);
        if (existing.Exists)
        {
            // Keep the original bounded, pinned sample intact. Cumulative counts do not reopen an adjudicated episode.
            if (Text(existing, "status") == "needs-strategy-analysis")
                tx.Update(reference, new Dictionary<string, object>
                {
                    ["sharedRounds"] = assessment.SharedRounds, ["opposedOutcomeRounds"] = assessment.OpposedOutcomeRounds,
                    ["reviewPriority"] = Math.Max(Long(existing, "reviewPriority"), assessment.ReviewPriority),
                    ["strategyComparisonJson"] = comparison, ["updatedAtUtc"] = Timestamp.FromDateTime(clock.GetUtcNow().UtcDateTime)
                });
            return true;
        }
        var auditRefs = auditIds.Select(id => database.Collection(RoundAuditDocuments.Summaries).Document(id)).ToArray();
        var audits = await tx.GetAllSnapshotsAsync(auditRefs, ct);
        var available = audits.Where(value => value.Exists).ToArray();
        var holds = available.Select(value => database.Collection(InvestigationDocuments.InstanceHolds).Document(
            InvestigationDocuments.InstanceKey(Text(value, "gameId"), Text(value, "roundId")))).ToArray();
        _ = await tx.GetAllSnapshotsAsync(holds, ct);
        var holdId = $"pair-analysis:{pairId}";
        tx.Set(reference, new Dictionary<string, object>
        {
            ["pairId"] = pairId, ["gameId"] = summary.GameId, ["firstUserId"] = first, ["secondUserId"] = second,
            ["status"] = "needs-strategy-analysis", ["reviewPriority"] = assessment.ReviewPriority,
            ["sharedRounds"] = assessment.SharedRounds, ["opposedOutcomeRounds"] = assessment.OpposedOutcomeRounds,
            ["auditIds"] = auditIds, ["pinnedAuditIds"] = FieldValue.ArrayUnion(available.Select(value => (object)value.Id).ToArray()),
            ["analysisCoverage"] = assessment.Coverage, ["caseCreated"] = false, ["noPlayerStrikes"] = true,
            ["signalsJson"] = JsonSerializer.Serialize(assessment.Signals, InvestigationDocuments.Json),
            ["networkContextJson"] = JsonSerializer.Serialize(network, InvestigationDocuments.Json),
            ["strategyComparisonJson"] = comparison, ["strategyVersion"] = InvestigationStrategyExtractor.Version,
            ["pinnedInstanceCount"] = available.Length, ["maximumPinnedInstances"] = 20,
            ["evidenceFor"] = assessment.Signals.Where(value => value.Code == "shared-ip-overlap").Select(value => value.Message).ToArray(),
            ["evidenceAgainstOrLimitations"] = new[] { "Co-play and opposing net results alone do not establish coordinated play.", "Shared addresses can be households, venues or carrier NAT.", "Game-specific decisions have not been compared with comparable non-partner play.", "Recent source references cannot reconstruct details deleted before this queue was created." },
            ["createdAtUtc"] = existing.Exists ? existing.GetValue<Timestamp>("createdAtUtc") : Timestamp.FromDateTime(clock.GetUtcNow().UtcDateTime),
            ["updatedAtUtc"] = Timestamp.FromDateTime(clock.GetUtcNow().UtcDateTime)
        }, SetOptions.MergeAll);
        for (var index = 0; index < available.Length; index++)
        {
            tx.Update(available[index].Reference, new Dictionary<string, object>
            { ["investigationHoldIds"] = FieldValue.ArrayUnion(holdId), ["retentionState"] = "held-pair-analysis", ["evidenceDeleteAfterUtc"] = null! });
            tx.Set(holds[index], new Dictionary<string, object>
            {
                ["gameId"] = Text(available[index], "gameId"), ["instanceId"] = Text(available[index], "roundId"), ["held"] = true,
                ["investigationIds"] = FieldValue.ArrayUnion(holdId), ["updatedAtUtc"] = Timestamp.FromDateTime(clock.GetUtcNow().UtcDateTime)
            }, SetOptions.MergeAll);
        }
        return true;
        }, cancellationToken: ct);
    }

    internal Task<InvestigationEvidencePage> ReadPairQueueAsync(int limit, string? cursor, CancellationToken ct) =>
        ReadPageAsync(InvestigationDocuments.PairAnalysisQueue, "status", "needs-strategy-analysis", "security-pair-analysis", limit, cursor, ct, validateId: false);

    internal async Task<object> ResolvePairQueueAsync(string id, InvestigationPairResolutionRequest request, string reviewer, CancellationToken ct)
    {
        FirestoreGameRoundAuditStore.ValidateId(id);
        if (request.Outcome is not ("cleared" or "linked-risk-incident") || string.IsNullOrWhiteSpace(request.Reason) || request.Reason.Length > 4000)
            throw new ArgumentException("Choose cleared or linked-risk-incident and provide a bounded reason.");
        var reference = database.Collection(InvestigationDocuments.PairAnalysisQueue).Document(id);
        var original = await reference.GetSnapshotAsync(ct);
        if (!original.Exists) throw new KeyNotFoundException("The pair-analysis queue item was not found.");
        if (request.Outcome == "linked-risk-incident")
        {
            if (string.IsNullOrWhiteSpace(request.RiskIncidentId) || request.RiskIncidentId.Contains('/')) throw new ArgumentException("An existing attributed risk incident is required.");
            var risk = await database.Collection(FirestoreRiskIncidentStore.Incidents).Document(request.RiskIncidentId).GetSnapshotAsync(ct);
            if (!risk.Exists) throw new KeyNotFoundException("The attributed risk incident was not found.");
            var incident = FirestoreRiskIncidentStore.ReadIncident(risk);
            if (incident.UserId != Text(original, "firstUserId") && incident.UserId != Text(original, "secondUserId")) throw new ArgumentException("The attributed incident must identify a participant in this pair.");
            // Attach the entire bounded original context, including every unflagged participant, to the existing incident.
            await CreateAttributedIncidentAsync(incident.Id, Strings(original, "pinnedAuditIds"), ct);
        }
        return await database.RunTransactionAsync(async tx =>
        {
            var current = await tx.GetSnapshotAsync(reference, ct);
            if (!current.Exists) throw new KeyNotFoundException("The pair-analysis queue item was not found.");
            var now = clock.GetUtcNow().UtcDateTime;
            if (Text(current, "status") == request.Outcome) return new { id, status = request.Outcome, noNewPlayerStrikes = true };
            tx.Update(reference, new Dictionary<string, object>
            {
                ["status"] = request.Outcome, ["resolvedBy"] = reviewer, ["resolvedAtUtc"] = Timestamp.FromDateTime(now),
                ["resolutionReason"] = request.Reason, ["riskIncidentId"] = request.RiskIncidentId!,
                ["releaseAfterUtc"] = Timestamp.FromDateTime(now.AddDays(30)), ["retentionPending"] = true
            });
            tx.Create(reference.Collection("reviews").Document(Guid.NewGuid().ToString("N")), new Dictionary<string, object>
            { ["outcome"] = request.Outcome, ["reason"] = request.Reason, ["reviewedBy"] = reviewer, ["reviewedAtUtc"] = Timestamp.FromDateTime(now) });
            return new { id, status = request.Outcome, noNewPlayerStrikes = true };
        }, cancellationToken: ct);
    }

    private async Task ReleasePairQueueAsync(DocumentSnapshot queue, CancellationToken ct)
    {
        await database.RunTransactionAsync(async tx =>
        {
            var current = await tx.GetSnapshotAsync(queue.Reference, ct);
            if (!current.Exists || !Bool(current, "retentionPending") ||
                FirestoreGameRoundAuditStore.Time(current, "releaseAfterUtc") is not { } deadline || deadline > clock.GetUtcNow().UtcDateTime) return false;
            var auditIds = Strings(current, "pinnedAuditIds");
            var auditRefs = auditIds.Select(id => database.Collection(RoundAuditDocuments.Summaries).Document(id)).ToArray();
            var audits = await tx.GetAllSnapshotsAsync(auditRefs, ct);
            var existing = audits.Where(value => value.Exists).ToArray();
            var holdRefs = existing.Select(value => database.Collection(InvestigationDocuments.InstanceHolds).Document(
                InvestigationDocuments.InstanceKey(Text(value, "gameId"), Text(value, "roundId")))).ToArray();
            var holds = await tx.GetAllSnapshotsAsync(holdRefs, ct);
            var holdId = $"pair-analysis:{queue.Id}";
            for (var index = 0; index < existing.Length; index++)
            {
                var remaining = Strings(holds[index], "investigationIds").Where(value => value != holdId).ToArray();
                tx.Set(holdRefs[index], new Dictionary<string, object> { ["investigationIds"] = remaining, ["held"] = remaining.Length > 0 }, SetOptions.MergeAll);
                var other = Strings(existing[index], "investigationHoldIds").Where(value => value != holdId).ToArray();
                var changes = new Dictionary<string, object> { ["investigationHoldIds"] = other };
                if (other.Length == 0 && !Bool(existing[index], "preserveIndefinitely") && Strings(existing[index], "riskHoldIncidentIds").Count == 0 && Strings(existing[index], "riskPreservedIncidentIds").Count == 0)
                {
                    if (!Bool(existing[index], "evidencePresent"))
                    { changes["retentionState"] = "deleted"; changes["rawEvidencePurgePending"] = true;
                      changes["rawEvidenceNextAttemptAtUtc"] = Timestamp.FromDateTime(clock.GetUtcNow().UtcDateTime); }
                    else
                    { changes["retentionState"] = "eligible-resolved"; changes["evidenceDeleteAfterUtc"] = Timestamp.FromDateTime(clock.GetUtcNow().UtcDateTime); }
                }
                tx.Update(existing[index].Reference, changes);
            }
            tx.Update(queue.Reference, new Dictionary<string, object> { ["retentionPending"] = false, ["pinnedAuditIds"] = Array.Empty<string>(),
                ["networkContextJson"] = FieldValue.Delete, ["privateContextReleasedAtUtc"] = Timestamp.FromDateTime(clock.GetUtcNow().UtcDateTime) });
            return true;
        }, cancellationToken: ct);
    }

    private async Task ProjectStrategyFeaturesAsync(RoundAuditSummary summary, string payload, CancellationToken ct)
    {
        InvestigationStrategyFeatures extracted;
        try { extracted = InvestigationStrategyExtractor.Extract(payload); }
        catch (JsonException) { return; }
        var decisions = extracted.Decisions.Where(value => value.HeadsUp && value.Source == "account-action").ToArray();
        var groups = decisions.GroupBy(value => value.ActorId).Select(group => (Actor: group.Key, Opponent: "all-human-heads-up", Items: group.ToArray()))
            .Concat(decisions.GroupBy(value => (value.ActorId, value.OpponentId)).Select(group => (Actor: group.Key.ActorId, Opponent: group.Key.OpponentId!, Items: group.ToArray())));
        foreach (var group in groups)
        {
            var featureRef = StrategyReference(summary.GameId, group.Actor, group.Opponent);
            var receiptRef = database.Collection(InvestigationDocuments.StrategyReceipts).Document(InvestigationDocuments.Hash($"{featureRef.Id}\n{summary.Id}"));
            await database.RunTransactionAsync(async tx =>
            {
                var reads = await tx.GetAllSnapshotsAsync([featureRef, receiptRef], ct);
                if (reads[1].Exists) return false;
                var fields = new Dictionary<string, object>
                {
                    ["gameId"] = summary.GameId, ["actorId"] = group.Actor, ["opponentScope"] = group.Opponent,
                    ["decisions"] = Long(reads[0], "decisions") + group.Items.Length,
                    ["facedWagerCents"] = Long(reads[0], "facedWagerCents") + group.Items.Sum(value => value.FacedWagerCents),
                    ["committedCents"] = Long(reads[0], "committedCents") + group.Items.Sum(value => value.AmountCents),
                    ["timestampedDecisions"] = Long(reads[0], "timestampedDecisions") + group.Items.Count(value => value.Timestamped),
                    ["version"] = InvestigationStrategyExtractor.Version, ["coverage"] = "descriptive-account-actions-human-heads-up-only",
                    ["updatedAtUtc"] = Timestamp.FromDateTime(clock.GetUtcNow().UtcDateTime)
                };
                foreach (var action in new[] { "call", "raise", "fold", "check" })
                    fields[$"{action}Count"] = Long(reads[0], $"{action}Count") + group.Items.Count(value => value.Action == action);
                foreach (var street in new[] { "preflop", "flop", "turn", "river" })
                    fields[$"{street}Decisions"] = Long(reads[0], $"{street}Decisions") + group.Items.Count(value => value.Street == street);
                tx.Set(featureRef, fields);
                tx.Create(receiptRef, new Dictionary<string, object> { ["auditId"] = summary.Id, ["featureId"] = featureRef.Id });
                return true;
            }, cancellationToken: ct);
        }
    }
    private DocumentReference StrategyReference(string game, string actor, string opponent) => database.Collection(InvestigationDocuments.StrategyFeatures)
        .Document(InvestigationDocuments.Hash($"{game}\n{actor}\n{opponent}"));
    private async Task<string> ReadPairStrategyComparisonAsync(string game, string first, string second, CancellationToken ct)
    {
        if (game != "texas-holdem")
            return JsonSerializer.Serialize(new
            {
                gameId = game, firstUserId = first, secondUserId = second,
                coverage = "game-specific-decision-comparison-unavailable",
                limitation = "Co-play, outcomes and network context can select review, but this title has no calibrated decision or coordinated-play detector.",
                noBehavioralCaseOrStrikes = true
            }, InvestigationDocuments.Json);
        var users = new List<object>();
        foreach (var (actor, partner) in new[] { (first, second), (second, first) })
        {
            var documents = await Task.WhenAll(StrategyReference(game, actor, "all-human-heads-up").GetSnapshotAsync(ct), StrategyReference(game, actor, partner).GetSnapshotAsync(ct));
            var partnerCount = Long(documents[1], "decisions");
            var otherCount = Math.Max(0, Long(documents[0], "decisions") - partnerCount);
            var rates = new Dictionary<string, object?>();
            foreach (var action in new[] { "call", "raise", "fold", "check" })
            {
                var partnerActions = Long(documents[1], $"{action}Count");
                var otherActions = Math.Max(0, Long(documents[0], $"{action}Count") - partnerActions);
                rates[action] = new { partnerCount = partnerActions, partnerRate = partnerCount > 0 ? partnerActions / (double)partnerCount : (double?)null,
                    otherCount = otherActions, otherRate = otherCount > 0 ? otherActions / (double)otherCount : (double?)null };
            }
            users.Add(new { actorId = actor, partnerId = partner, partnerDecisionCount = partnerCount, otherOpponentDecisionCount = otherCount,
                coverage = partnerCount < 20 || otherCount < 20 ? "insufficient-comparable-decisions" : "descriptive-unmatched-comparisons-only", rates,
                partnerFacedWagerCents = Long(documents[1], "facedWagerCents"), partnerCommittedCents = Long(documents[1], "committedCents"),
                limitation = "Unmatched action-rate differences can reflect cards, position, stacks, stakes and opponent skill. Similar rates are counterevidence to a simple soft-play hypothesis but cannot exclude collusion. Twenty decisions is a display coverage threshold, not a validated statistical sample guarantee." });
        }
        return JsonSerializer.Serialize(new { version = InvestigationStrategyExtractor.Version, users, noBehavioralCaseOrStrikes = true }, InvestigationDocuments.Json);
    }
}
