using FortuneForge.Games.Abstractions.Auditing;
using FortuneForge.Server.RoundAudit;
using Google.Cloud.Firestore;

namespace FortuneForge.Server.SecurityAudit;

internal static class GameSecurityJournalManifestAudit
{
    internal static async Task<IReadOnlyList<RoundAuditFinding>> InspectAsync(
        DocumentSnapshot manifest, RoundAuditSummary summary, bool expected,
        IReadOnlySet<string> observedIds, IReadOnlyDictionary<string, long> observedVersions, CancellationToken ct)
    {
        if (!manifest.Exists)
            return expected
                ? [new("engine-journal-manifest-missing", "error",
                    "The committed engine observation manifest is missing for this completed instance.")]
                : [];

        try
        {
            if (FirestoreGameRoundAuditStore.Text(manifest, "gameId") != summary.GameId ||
                FirestoreGameRoundAuditStore.Text(manifest, "instanceId") != summary.RoundId)
                return [InvalidManifest()];
            if (!manifest.TryGetValue<long>("schemaVersion", out var version) || version == 1)
                return manifest.TryGetValue<List<string>>("eventIds", out var legacyIds) && legacyIds is not null &&
                    legacyIds.Count == legacyIds.Distinct(StringComparer.Ordinal).Count()
                    ? CompareIds(legacyIds.ToHashSet(StringComparer.Ordinal), observedIds)
                    : [InvalidManifest()];
            if (version != GameSecurityEventDocuments.PagedManifestVersion)
                return [InvalidManifest()];

            return await InspectPagesAsync(manifest, observedIds, observedVersions, ct);
        }
        catch (Exception exception) when (exception is ArgumentException or InvalidOperationException or
            InvalidCastException or OverflowException)
        {
            return [InvalidManifest()];
        }
    }

    private static async Task<IReadOnlyList<RoundAuditFinding>> InspectPagesAsync(DocumentSnapshot manifest,
        IReadOnlySet<string> observedIds, IReadOnlyDictionary<string, long> observedVersions, CancellationToken ct)
    {
        if (!manifest.TryGetValue<long>("eventCount", out var count) || count < 1 ||
            !manifest.TryGetValue<long>("firstVersion", out var firstVersion) || firstVersion < 0 ||
            !manifest.TryGetValue<long>("pageSize", out var pageSize) || pageSize != GameSecurityEventDocuments.ManifestPageSize ||
            firstVersion > long.MaxValue - (count - 1))
            return [InvalidManifest()];

        // If the actual journal is bounded but the manifest commits more entries, a discrepancy
        // is already proven. Never issue a page query sized by an unchecked manifest count.
        if (count > observedIds.Count) return [MissingEvent()];
        if (count < observedIds.Count) return [UnmanifestedEvent()];

        var expectedPageCount = checked((int)((count - 1) / pageSize + 1));
        var pages = await manifest.Reference.Collection(GameSecurityEventDocuments.ManifestPageCollectionName)
            .OrderBy(FieldPath.DocumentId).Limit(expectedPageCount + 1).GetSnapshotAsync(ct);
        if (pages.Count != expectedPageCount)
            return [InvalidManifest()];

        var expectedIds = new HashSet<string>(StringComparer.Ordinal);
        var orderMismatch = false;
        for (var index = 0; index < expectedPageCount; index++)
        {
            var page = pages.Documents[index];
            var start = index * pageSize;
            var length = Math.Min(pageSize, count - start);
            if (page.Id != GameSecurityEventDocuments.ManifestPageId(index) ||
                !page.TryGetValue<long>("pageIndex", out var pageIndex) || pageIndex != index ||
                !page.TryGetValue<string[]>("eventIds", out var ids) || ids is null || ids.LongLength != length)
                return [InvalidManifest()];

            for (var offset = 0; offset < ids.Length; offset++)
            {
                var id = ids[offset];
                if (!GameSecurityEventDocuments.IsEventId(id) || !expectedIds.Add(id))
                    return [InvalidManifest()];
                if (observedIds.Contains(id) &&
                    (!observedVersions.TryGetValue(id, out var eventVersion) || eventVersion != firstVersion + start + offset))
                    orderMismatch = true;
            }
        }

        var findings = CompareIds(expectedIds, observedIds);
        if (orderMismatch)
            findings.Add(new("engine-journal-manifest-order", "error", "The committed engine observation order differs from the journal versions."));
        return findings;
    }

    private static List<RoundAuditFinding> CompareIds(IReadOnlySet<string> expected, IReadOnlySet<string> observed)
    {
        var findings = new List<RoundAuditFinding>();
        if (expected.Except(observed).Any()) findings.Add(MissingEvent());
        if (observed.Except(expected).Any()) findings.Add(UnmanifestedEvent());
        return findings;
    }

    private static RoundAuditFinding InvalidManifest() =>
        new("engine-journal-manifest-invalid", "error", "The engine observation manifest has invalid identity or duplicate/missing event identifiers.");

    private static RoundAuditFinding MissingEvent() =>
        new("engine-journal-event-missing", "error", "A committed engine observation is absent from this instance's journal.");

    private static RoundAuditFinding UnmanifestedEvent() =>
        new("engine-journal-unmanifested-event", "error", "An engine observation is absent from this instance's committed manifest.");
}
