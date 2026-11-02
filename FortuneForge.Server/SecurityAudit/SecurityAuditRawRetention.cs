using FortuneForge.Games.Abstractions.Auditing;
using FortuneForge.Server.RoundAudit;
using Google.Cloud.Firestore;
using Microsoft.Extensions.Options;
using System.Text.Json;

namespace FortuneForge.Server.SecurityAudit;

// Runs only after the complete round audit and its normal/cleared evidence deadline.
// Every delete rechecks the same hold document written by investigation preservation.
internal sealed class SecurityAuditRawRetention(FirestoreDb database, IOptions<GameRoundRetentionOptions> policy,
    TimeProvider clock)
{
    private const string RequestQueue = "gameSecurityRequestRetentionQueue";

    internal async Task<int> SweepAsync(CancellationToken ct)
    {
        var size = Math.Min(policy.Value.BatchSize, 50);
        var now = Timestamp.FromDateTime(clock.GetUtcNow().UtcDateTime);
        var failures = new List<Exception>();
        var processed = 0;
        if (policy.Value.DeleteCleanEvidence)
        {
            var due = await database.Collection(RoundAuditDocuments.Summaries).WhereEqualTo("retentionState", "deleted")
                .WhereEqualTo("rawEvidencePurgePending", true).WhereLessThanOrEqualTo("rawEvidenceNextAttemptAtUtc", now)
                .Limit(size).GetSnapshotAsync(ct);
            processed += due.Count;
            foreach (var round in due.Documents)
                try { await PurgeInstancePageAsync(round.Reference, ct); }
                catch (Exception exception) when (exception is not OperationCanceledException)
                {
                    failures.Add(new InvalidOperationException($"Round raw retention {round.Id} failed.", exception));
                    try { await DeferSummaryAsync(round.Reference, ct); }
                    catch (Exception deferred) when (deferred is not OperationCanceledException) { failures.Add(deferred); }
                }
            var requests = await database.Collection(RequestQueue).WhereEqualTo("pending", true)
                .WhereLessThanOrEqualTo("nextAttemptAtUtc", now).Limit(size).GetSnapshotAsync(ct);
            processed += requests.Count;
            foreach (var request in requests.Documents)
                try { await PurgeRequestIfEligibleAsync(request.Reference, ct); }
                catch (Exception exception) when (exception is not OperationCanceledException)
                {
                    failures.Add(new InvalidOperationException($"Request raw retention {request.Id} failed.", exception));
                    try { await DeferRequestAsync(request.Reference, ct); }
                    catch (Exception deferred) when (deferred is not OperationCanceledException) { failures.Add(deferred); }
                }
        }
        // Rejected commands can have neither a round summary nor an engine observation.
        // Review every old receipt separately so its IP/session data cannot live forever.
        var orphans = await database.Collection(InvestigationDocuments.RequestIntents)
            .WhereEqualTo("orphanRetentionPending", true)
            .WhereLessThanOrEqualTo("orphanRetentionNextAttemptAtUtc", now)
            .Limit(size).GetSnapshotAsync(ct);
        foreach (var orphan in orphans.Documents)
            try { await PurgeOrphanIfEligibleAsync(orphan.Reference, ct); }
            catch (Exception exception) when (exception is not OperationCanceledException)
            {
                failures.Add(new InvalidOperationException($"Orphan request review {orphan.Id} failed.", exception));
                try { await DeferOrphanAsync(orphan.Reference, ct); }
                catch (Exception deferred) when (deferred is not OperationCanceledException) { failures.Add(deferred); }
            }
        if (failures.Count > 0) throw new AggregateException("Security evidence retention failed for one or more items; later items were still processed.", failures);
        return processed + orphans.Count;
    }

    private Task DeferSummaryAsync(DocumentReference summaryRef, CancellationToken ct) => database.RunTransactionAsync(async tx =>
    {
        var current = await tx.GetSnapshotAsync(summaryRef, ct);
        if (!current.Exists || !current.TryGetValue<bool>("rawEvidencePurgePending", out var pending) || !pending) return false;
        tx.Update(summaryRef, new Dictionary<string, object>
        { ["rawEvidenceNextAttemptAtUtc"] = Timestamp.FromDateTime(clock.GetUtcNow().UtcDateTime.AddHours(1)) });
        return true;
    }, cancellationToken: ct);

    private Task DeferOrphanAsync(DocumentReference intentRef, CancellationToken ct) => database.RunTransactionAsync(async tx =>
    {
        var current = await tx.GetSnapshotAsync(intentRef, ct);
        if (!current.Exists || !current.TryGetValue<bool>("orphanRetentionPending", out var pending) || !pending) return false;
        tx.Update(intentRef, new Dictionary<string, object>
        { ["orphanRetentionNextAttemptAtUtc"] = Timestamp.FromDateTime(clock.GetUtcNow().UtcDateTime.AddHours(1)) });
        return true;
    }, cancellationToken: ct);

    private async Task PurgeInstancePageAsync(DocumentReference summaryRef, CancellationToken ct)
    {
        var summary = await summaryRef.GetSnapshotAsync(ct);
        if (!summary.Exists || !Eligible(summary))
        {
            if (summary.Exists && summary.TryGetValue<bool>("rawEvidencePurgePending", out var pending) && pending)
                await summaryRef.UpdateAsync(new Dictionary<string, object>
                { ["rawEvidenceNextAttemptAtUtc"] = Timestamp.FromDateTime(clock.GetUtcNow().UtcDateTime.AddHours(1)) }, cancellationToken: ct);
            return;
        }
        var game = FirestoreGameRoundAuditStore.Text(summary, "gameId");
        var instance = FirestoreGameRoundAuditStore.Text(summary, "roundId");
        var holdRef = database.Collection(InvestigationDocuments.InstanceHolds)
            .Document(InvestigationDocuments.InstanceKey(game, instance));
        if (Held(await holdRef.GetSnapshotAsync(ct)))
        {
            await summaryRef.UpdateAsync(new Dictionary<string, object>
            { ["rawEvidenceNextAttemptAtUtc"] = Timestamp.FromDateTime(clock.GetUtcNow().UtcDateTime.AddHours(1)) }, cancellationToken: ct);
            return;
        }
        var events = await database.Collection(GameSecurityEventDocuments.CollectionName).WhereEqualTo("gameId", game)
            .WhereEqualTo("instanceId", instance).Limit(25).GetSnapshotAsync(ct);
        if (events.Count > 0)
        {
            foreach (var item in events.Documents)
                await database.RunTransactionAsync(async tx =>
                {
                    var reads = await tx.GetAllSnapshotsAsync([summaryRef, holdRef, item.Reference], ct);
                    if (!Eligible(reads[0]) || Held(reads[1]) || !reads[2].Exists) return false;
                    var requestId = FirestoreGameRoundAuditStore.Text(reads[2], "requestId");
                    if (requestId.Length > 0) StageRequest(tx, summaryRef.Id, game, instance, requestId);
                    tx.Delete(item.Reference);
                    return true;
                }, cancellationToken: ct);
            return;
        }

        // Some accepted commands do not yield a separate engine event. Stage their
        // request receipts before marking the instance fully purged.
        var cursor = FirestoreGameRoundAuditStore.Text(summary, "rawRequestCursor");
        Query query = database.Collection(InvestigationDocuments.RequestIntents).WhereEqualTo("gameId", game)
            .WhereEqualTo("instanceId", instance).OrderBy(FieldPath.DocumentId);
        if (cursor.Length > 0) query = query.StartAfter(cursor);
        var page = await query.Limit(25).GetSnapshotAsync(ct);
        if (page.Count > 0)
        {
            await database.RunTransactionAsync(async tx =>
            {
                var reads = await tx.GetAllSnapshotsAsync([summaryRef, holdRef], ct);
                if (!Eligible(reads[0]) || Held(reads[1])) return false;
                foreach (var item in page.Documents)
                    StageRequest(tx, summaryRef.Id, game, instance, item.Id);
                tx.Update(summaryRef, new Dictionary<string, object> { ["rawRequestCursor"] = page.Documents[^1].Id });
                return true;
            }, cancellationToken: ct);
            if (page.Count == 25) return;
        }

        // A settled slot admission contains private IP, session and bonus state too.
        if (game.StartsWith("slot-", StringComparison.Ordinal) || game == "slots" ||
            FortuneForge.Server.Activity.AcceptedGameActivityReader.SlotIds.Values.Contains(game))
        {
            var admissions = await database.Collection("slotSpinAdmissions").WhereEqualTo("spinId", instance)
                .Limit(5).GetSnapshotAsync(ct);
            foreach (var admission in admissions.Documents)
                await database.RunTransactionAsync(async tx =>
                {
                    var reads = await tx.GetAllSnapshotsAsync([summaryRef, holdRef, admission.Reference], ct);
                    if (!Eligible(reads[0]) || Held(reads[1]) || !reads[2].Exists ||
                        FirestoreGameRoundAuditStore.Text(reads[2], "status") != "settled") return false;
                    var requestId = FirestoreGameRoundAuditStore.Text(reads[2], "requestId");
                    if (requestId.Length > 0) StageRequest(tx, summaryRef.Id, game, instance, requestId);
                    tx.Delete(admission.Reference);
                    return true;
                }, cancellationToken: ct);
            if (admissions.Count > 0) return;
        }

        await database.RunTransactionAsync(async tx =>
        {
            var reads = await tx.GetAllSnapshotsAsync([summaryRef, holdRef], ct);
            if (!Eligible(reads[0]) || Held(reads[1])) return false;
            tx.Delete(database.Collection(GameSecurityEventDocuments.ManifestCollectionName)
                .Document(GameSecurityEventDocuments.ManifestId(game, instance)));
            tx.Update(summaryRef, new Dictionary<string, object>
            {
                ["rawEvidencePurgePending"] = false,
                ["rawEvidencePurgedAtUtc"] = Timestamp.FromDateTime(clock.GetUtcNow().UtcDateTime)
            });
            return true;
        }, cancellationToken: ct);
    }

    private void StageRequest(Transaction tx, string auditId, string gameId, string instanceId, string requestId)
    {
        var reference = database.Collection(RequestQueue).Document(requestId);
        tx.Set(reference, new Dictionary<string, object>
        {
            ["requestId"] = requestId, ["pending"] = true,
            ["auditIds"] = FieldValue.ArrayUnion(auditId),
            ["gameId"] = gameId, ["instanceId"] = instanceId,
            ["queuedAtUtc"] = Timestamp.FromDateTime(clock.GetUtcNow().UtcDateTime),
            ["nextAttemptAtUtc"] = Timestamp.FromDateTime(clock.GetUtcNow().UtcDateTime)
        }, SetOptions.MergeAll);
    }

    private async Task PurgeRequestIfEligibleAsync(DocumentReference queueRef, CancellationToken ct)
    {
        var queue = await queueRef.GetSnapshotAsync(ct);
        if (!queue.Exists || !queue.GetValue<bool>("pending")) return;
        var requestId = FirestoreGameRoundAuditStore.Text(queue, "requestId");
        if (requestId.Length == 0) { await DeferRequestAsync(queueRef, ct); return; }
        var auditIds = queue.GetValue<List<string>>("auditIds");
        var auditRefs = auditIds.Select(id => database.Collection(RoundAuditDocuments.Summaries).Document(id)).ToArray();
        var audits = await Task.WhenAll(auditRefs.Select(reference => reference.GetSnapshotAsync(ct)));
        if (audits.Any(value => !RequestEligible(value))) { await DeferRequestAsync(queueRef, ct); return; }
        var holdRefs = audits.Select(value => database.Collection(InvestigationDocuments.InstanceHolds).Document(
            InvestigationDocuments.InstanceKey(FirestoreGameRoundAuditStore.Text(value, "gameId"),
                FirestoreGameRoundAuditStore.Text(value, "roundId")))).ToArray();
        var intentRef = database.Collection(InvestigationDocuments.RequestIntents).Document(requestId);
        var resultRef = database.Collection(InvestigationDocuments.RequestResults).Document(requestId);
        var purged = await database.RunTransactionAsync(async tx =>
        {
            var sources = await tx.GetAllSnapshotsAsync(auditRefs.Concat(holdRefs).Concat([queueRef, intentRef, resultRef]), ct);
            var linked = await tx.GetSnapshotAsync(database.Collection(GameSecurityEventDocuments.CollectionName)
                .WhereEqualTo("requestId", requestId).Limit(1), ct);
            var currentQueue = sources[auditRefs.Length + holdRefs.Length];
            if (sources.Take(auditRefs.Length).Any(value => !RequestEligible(value)) ||
                sources.Skip(auditRefs.Length).Take(holdRefs.Length).Any(Held) ||
                linked.Count > 0 ||
                !currentQueue.Exists || !currentQueue.GetValue<bool>("pending") ||
                !currentQueue.GetValue<List<string>>("auditIds").SequenceEqual(auditIds)) return false;
            if (sources[^2].Exists) tx.Delete(intentRef);
            if (sources[^1].Exists) tx.Delete(resultRef);
            tx.Update(queueRef, new Dictionary<string, object>
            {
                ["pending"] = false, ["purgedAtUtc"] = Timestamp.FromDateTime(clock.GetUtcNow().UtcDateTime)
            });
            return true;
        }, cancellationToken: ct);
        if (!purged) await DeferRequestAsync(queueRef, ct);
    }

    private Task DeferRequestAsync(DocumentReference queueRef, CancellationToken ct) => database.RunTransactionAsync(async tx =>
    {
        var current = await tx.GetSnapshotAsync(queueRef, ct);
        if (!current.Exists || !current.TryGetValue<bool>("pending", out var pending) || !pending) return false;
        tx.Update(queueRef, new Dictionary<string, object>
        { ["nextAttemptAtUtc"] = Timestamp.FromDateTime(clock.GetUtcNow().UtcDateTime.AddHours(1)) });
        return true;
    }, cancellationToken: ct);

    private Task PurgeOrphanIfEligibleAsync(DocumentReference intentRef, CancellationToken ct) => database.RunTransactionAsync(async tx =>
    {
        var now = clock.GetUtcNow().UtcDateTime;
        var intent = await tx.GetSnapshotAsync(intentRef, ct);
        if (!intent.Exists || !intent.TryGetValue<bool>("orphanRetentionPending", out var pending) || !pending) return false;
        var received = FirestoreGameRoundAuditStore.Time(intent, "receivedAtUtc");
        var minimumDays = Math.Max(FirestoreGameSecurityRequestStore.MinimumOrphanRetentionDays, policy.Value.CleanEvidenceDays);
        var eligibleAt = received?.AddDays(minimumDays);
        var resultRef = database.Collection(InvestigationDocuments.RequestResults).Document(intentRef.Id);
        var queueRef = database.Collection(RequestQueue).Document(intentRef.Id);
        var result = await tx.GetSnapshotAsync(resultRef, ct);
        var queue = await tx.GetSnapshotAsync(queueRef, ct);
        void Defer(DateTime until) => tx.Update(intentRef, new Dictionary<string, object>
        { ["orphanRetentionNextAttemptAtUtc"] = Timestamp.FromDateTime(until) });
        if (received is null)
        {
            Defer(now.AddDays(1));
            return false;
        }
        if (received.Value.AddMinutes(2) > now)
        {
            Defer(received.Value.AddMinutes(2));
            return false;
        }
        if (queue.Exists && queue.TryGetValue<bool>("pending", out var staged) && staged)
        {
            // The instance retention queue has its own summary and hold checks.
            tx.Update(intentRef, new Dictionary<string, object> { ["orphanRetentionPending"] = false });
            return false;
        }
        var linkedEvents = await tx.GetSnapshotAsync(database.Collection(GameSecurityEventDocuments.CollectionName)
            .WhereEqualTo("requestId", intentRef.Id).Limit(1), ct);
        var linkedRoundEvidence = await tx.GetSnapshotAsync(database.Collection(RoundAuditDocuments.Evidence)
            .WhereEqualTo("requestId", intentRef.Id).Limit(1), ct);
        var admission = await tx.GetSnapshotAsync(database.Collection("slotSpinAdmissions")
            .WhereEqualTo("requestId", intentRef.Id).Limit(1), ct);
        if (linkedEvents.Count > 0 || linkedRoundEvidence.Count > 0 || admission.Count > 0)
        {
            Defer(now.AddDays(1));
            return false;
        }
        var gameId = FirestoreGameRoundAuditStore.Text(intent, "gameId") ?? "";
        var instanceId = FirestoreGameRoundAuditStore.Text(intent, "instanceId") ?? "";
        if (gameId.Length > 0 && instanceId.Length > 0)
        {
            var audit = await tx.GetSnapshotAsync(database.Collection(RoundAuditDocuments.Summaries)
                .Document(InvestigationDocuments.InstanceKey(gameId, instanceId)), ct);
            var hold = await tx.GetSnapshotAsync(database.Collection(InvestigationDocuments.InstanceHolds)
                .Document(InvestigationDocuments.InstanceKey(gameId, instanceId)), ct);
            if (Held(hold) || audit.Exists && !RequestEligible(audit))
            {
                Defer(now.AddDays(1));
                return false;
            }
        }
        long status = 0;
        var statusKnown = result.Exists && result.TryGetValue<long>("httpStatusCode", out status);
        var cleanRejection = statusKnown && status is >= 400 and < 500;
        var episode = $"unlinked-request:{intentRef.Id}";
        var caseId = InvestigationDocuments.CaseId("system-integrity", episode);
        var auditId = InvestigationDocuments.Hash($"unlinked-request\n{intentRef.Id}");
        var caseRef = database.Collection(InvestigationDocuments.Cases).Document(caseId);
        var instanceRef = database.Collection(InvestigationDocuments.Instances)
            .Document(InvestigationDocuments.Hash($"{caseId}\n{auditId}"));
        var intentCopyRef = database.Collection(InvestigationDocuments.Evidence)
            .Document(InvestigationDocuments.Hash($"{caseId}\nrequest-intent\n{intentRef.Id}"));
        var resultCopyRef = database.Collection(InvestigationDocuments.Evidence)
            .Document(InvestigationDocuments.Hash($"{caseId}\nrequest-result\n{intentRef.Id}"));
        var copies = await tx.GetAllSnapshotsAsync([caseRef, instanceRef, intentCopyRef, resultCopyRef], ct);
        if (cleanRejection && !copies[0].Exists)
        {
            if (!policy.Value.DeleteCleanEvidence) { Defer(now.AddDays(1)); return false; }
            if (eligibleAt > now) { Defer(eligibleAt.Value); return false; }
            tx.Delete(intentRef);
            tx.Delete(resultRef);
            return true;
        }
        if (!copies[0].Exists)
        {
            var code = !result.Exists ? "unlinked-request-result-missing" :
                statusKnown && status is >= 500 ? "unlinked-request-server-failure" : "unlinked-accepted-command";
            var message = !result.Exists
                ? "An authenticated game request has no durable HTTP result after two minutes, and no engine, round or admission link was found. Reconcile whether state changed."
                : statusKnown && status is >= 500
                    ? "A game request returned a server error and has no engine, round or admission link. Reconcile whether state changed."
                    : "A game request returned without a rejected-command status and has no engine, round or admission link. Reconcile the accepted action.";
            var investigation = new InvestigationCase(caseId, "system-integrity", episode, "open", 2, [],
                [new InvestigationSignal(code, "system-integrity", message, "unattributed-operational-gap")], now, now, 1);
            tx.Create(caseRef, new Dictionary<string, object>
            {
                ["kind"] = investigation.Kind, ["status"] = investigation.Status,
                ["reviewPriority"] = investigation.ReviewPriority,
                ["createdAtUtc"] = Timestamp.FromDateTime(now), ["updatedAtUtc"] = Timestamp.FromDateTime(now),
                ["caseJson"] = JsonSerializer.Serialize(investigation, InvestigationDocuments.Json),
                ["noAutomaticPlayerStrikes"] = true, ["deleteAfterUtc"] = null!, ["purgePending"] = false
            });
        }
        if (!copies[1].Exists)
            tx.Create(instanceRef, new Dictionary<string, object>
            {
                ["caseId"] = caseId, ["auditId"] = auditId, ["gameId"] = gameId,
                ["instanceId"] = instanceId, ["requestId"] = intentRef.Id,
                ["capturePending"] = false, ["captureState"] = "request-only-incomplete",
                ["coverage"] = "The original request and available HTTP result are preserved. No completed instance, engine event or admission was found; game state and participant intent cannot be inferred.",
                ["originalEvidencePresent"] = false, ["capturedAtUtc"] = Timestamp.FromDateTime(now)
            });
        if (!copies[2].Exists)
            tx.Create(intentCopyRef, OrphanCopyFields(caseId, auditId, "request-intent", intent, now));
        if (result.Exists && !copies[3].Exists)
            tx.Create(resultCopyRef, OrphanCopyFields(caseId, auditId, "request-result", result, now));
        if (eligibleAt > now || !policy.Value.DeleteCleanEvidence)
        {
            tx.Update(intentRef, new Dictionary<string, object>
            {
                ["orphanRetentionCaseId"] = caseId,
                ["orphanRetentionNextAttemptAtUtc"] = Timestamp.FromDateTime(
                    policy.Value.DeleteCleanEvidence ? eligibleAt!.Value : now.AddDays(1))
            });
            return false;
        }
        tx.Delete(intentRef);
        if (result.Exists) tx.Delete(resultRef);
        return true;
    }, cancellationToken: ct);

    private static Dictionary<string, object> OrphanCopyFields(string caseId, string auditId, string kind,
        DocumentSnapshot original, DateTime now) => new()
    {
        ["caseId"] = caseId, ["auditId"] = auditId, ["kind"] = kind,
        ["sourcePath"] = original.Reference.Path, ["sourceDocumentId"] = original.Id,
        ["originalFields"] = original.ToDictionary(), ["capturedAtUtc"] = Timestamp.FromDateTime(now)
    };

    private static bool Eligible(DocumentSnapshot summary) => summary.Exists &&
        summary.TryGetValue<bool>("rawEvidencePurgePending", out var pending) && pending &&
        FirestoreGameRoundAuditStore.Text(summary, "retentionState") == "deleted" &&
        !GameRoundRetentionService.HasRiskHold(summary);

    private static bool RequestEligible(DocumentSnapshot summary) => summary.Exists &&
        summary.TryGetValue<bool>("rawEvidencePurgePending", out var pending) && !pending &&
        FirestoreGameRoundAuditStore.Text(summary, "retentionState") == "deleted" &&
        !GameRoundRetentionService.HasRiskHold(summary);

    private static bool Held(DocumentSnapshot hold) => hold.Exists &&
        hold.TryGetValue<bool>("held", out var held) && held;
}

internal sealed class SecurityAuditRawRetentionWorker(SecurityAuditRawRetention retention,
    IOptions<GameRoundRetentionOptions> options, ILogger<SecurityAuditRawRetentionWorker> logger) : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        while (!stoppingToken.IsCancellationRequested)
        {
            try { await retention.SweepAsync(stoppingToken); }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested) { break; }
            catch (Exception exception) { logger.LogError(exception, "Private game event retention will retry; evidence was retained."); }
            try { await Task.Delay(TimeSpan.FromSeconds(options.Value.PollSeconds), stoppingToken); }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested) { break; }
        }
    }
}
