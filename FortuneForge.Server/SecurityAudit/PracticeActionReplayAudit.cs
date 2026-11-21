using System.Text.Json;
using FortuneForge.Games.Abstractions;
using FortuneForge.Games.Abstractions.Auditing;
using FortuneForge.Games.DropMerge;
using FortuneForge.Games.TwentyFortyEight;
using static FortuneForge.Games.Abstractions.Auditing.PuzzleReplayEvidence;

namespace FortuneForge.Server.SecurityAudit;

/// <summary>Validates the episode sequence and dispatches the package's puzzle transition replay.</summary>
internal static class PracticeActionReplayAudit
{
    internal static bool Supports(RoundAuditSummary summary) => (summary.GameId, summary.AuditorVersion, summary.EvidenceKind) switch
    {
        ("2048", PracticeOutcomeAudit.TwentyFortyEightVersion, TwentyFortyEightAudit.TerminalEvidenceKind or
            TwentyFortyEightAudit.FirestoreEvidenceKind) => true,
        ("drop-merge", PracticeOutcomeAudit.DropMergeVersion, DropMergeAudit.TerminalEvidenceKind or
            DropMergeAudit.FirestoreEvidenceKind) => true,
        _ => false
    };

    internal static IReadOnlyList<RoundAuditFinding> Inspect(
        RoundAuditSummary summary, string? payloadJson, IReadOnlyList<string> eventPayloads, bool completeHistory = false)
    {
        if (!Supports(summary)) return [];
        if (payloadJson is null)
            return Error("practice-action-terminal-missing", "The completed puzzle has no private terminal state.");
        try
        {
            using var terminal = JsonDocument.Parse(payloadJson);
            var saved = terminal.RootElement;
            if (!Classification(summary, saved))
                return Error("practice-action-classification-mismatch", "The puzzle episode identity or participant summary differs from its terminal evidence.");
            var run = saved.GetProperty("runId").GetGuid();
            var table = saved.GetProperty("id").GetGuid();
            var episode = saved.GetProperty("completionEpisode").GetInt32();
            var observations = new List<PuzzleActionObservation>(eventPayloads.Count);
            foreach (var payload in eventPayloads)
            {
                using var document = JsonDocument.Parse(payload);
                var root = document.RootElement;
                var state = root.GetProperty("state");
                var eventEpisode = state.GetProperty("completionEpisode").GetInt32();
                if (root.GetProperty("gameId").GetString() != summary.GameId ||
                    root.GetProperty("instanceId").GetString() != PuzzleSessionIdentity.EpisodeId(run, eventEpisode) ||
                    state.GetProperty("runId").GetGuid() != run || state.GetProperty("id").GetGuid() != table ||
                    !SameFirestoreTime(state.GetProperty("startedAtUtc").GetDateTime(), summary.StartedAtUtc) ||
                    !root.GetProperty("participants").EnumerateArray().Select(value => value.GetString())
                        .SequenceEqual([summary.Participants[0].ActorId], StringComparer.Ordinal))
                    return SequenceMismatch();
                var request = root.GetProperty("request");
                var requestJson = request.ValueKind == JsonValueKind.Object &&
                    request.TryGetProperty("requestJson", out var json) && json.ValueKind == JsonValueKind.String
                    ? json.GetString() : null;
                observations.Add(new(root.GetProperty("version").GetInt64(),
                    root.GetProperty("eventKind").GetString() ?? "",
                    root.GetProperty("occurredAtUtc").GetDateTime().ToUniversalTime(),
                    state.Clone(), requestJson));
            }
            observations.Sort((a, b) => a.Version.CompareTo(b.Version));
            if (observations.Count == 0 ||
                !JsonElement.DeepEquals(observations[^1].Payload, saved))
                return Error("practice-action-terminal-mismatch", "The terminal puzzle evidence is missing from, or differs from, its last engine observation.");

            var first = observations[0];
            var firstEpisode = ReadEpisode(first);
            if (firstEpisode != (completeHistory ? 1 : episode)) return SequenceMismatch();
            if (firstEpisode == 1 && (first.Version != 0 || first.Kind != "started"))
                return Error("practice-action-start-missing", "The first puzzle episode lacks its initial accepted start observation.");
            if (first.Kind != first.Payload.GetProperty("lastEvent").GetString() || first.Version < 0)
                return SequenceMismatch();
            for (var i = 1; i < observations.Count; i++)
            {
                var prior = observations[i - 1];
                var current = observations[i];
                var oldEpisode = ReadEpisode(prior);
                var newEpisode = ReadEpisode(current);
                if (prior.Version == long.MaxValue || current.Version != prior.Version + 1 ||
                    current.OccurredAtUtc < prior.OccurredAtUtc || current.Kind != current.Payload.GetProperty("lastEvent").GetString() ||
                    current.Kind == "started" || newEpisode < oldEpisode || newEpisode > episode)
                    return SequenceMismatch();
                var priorCompleted = prior.Payload.GetProperty("completedAtUtc").ValueKind != JsonValueKind.Null;
                if (newEpisode == oldEpisode)
                {
                    if (priorCompleted) return SequenceMismatch();
                }
                else if (!completeHistory || newEpisode != oldEpisode + 1 || !priorCompleted || prior.Kind is not ("won" or "lost") ||
                    (current.Kind != "undone" && (summary.GameId != "2048" || current.Kind != "continued")))
                {
                    return SequenceMismatch();
                }
            }
            var findings = (summary.GameId, completeHistory) switch
            {
                ("2048", true) => TwentyFortyEightActionReplay.InspectCompleteRun(observations),
                ("2048", false) => TwentyFortyEightActionReplay.Inspect(observations, episode),
                (_, true) => DropMergeActionReplay.InspectCompleteRun(observations),
                _ => DropMergeActionReplay.Inspect(observations, episode)
            };
            if (findings.Any(value => value.Severity == "error")) return findings;
            var results = findings.ToList();
            if (!completeHistory && episode > 1)
                results.Add(new("practice-resumed-episode-prior-history-not-linked", "warning",
                    "The available evidence verifies only this completion episode. Earlier episodes are required to verify the complete run."));
            if (summary.EvidenceKind is not (TwentyFortyEightAudit.FirestoreEvidenceKind or DropMergeAudit.FirestoreEvidenceKind))
                results.Add(new("practice-in-memory-state-journal-non-atomic", "warning",
                    "The server's in-memory puzzle state and acknowledged Firestore journal cannot commit atomically; a failed write may leave an accepted transition unrecorded."));
            return results;
        }
        catch (Exception exception) when (exception is JsonException or KeyNotFoundException or InvalidOperationException or
            ArgumentException or ArithmeticException or FormatException or OverflowException or IndexOutOfRangeException or NotSupportedException)
        {
            return Error("practice-action-evidence-invalid", "The puzzle terminal state or observation stream cannot be parsed for action replay.");
        }
    }

    internal static bool TryReadRunId(RoundAuditSummary summary, out Guid run)
    {
        run = Guid.Empty;
        return summary.RoundNumber >= 1 && summary.RoundId.Length > 32 &&
            Guid.TryParseExact(summary.RoundId.AsSpan(0, 32), "N", out run) && run != Guid.Empty &&
            summary.RoundId == PuzzleSessionIdentity.EpisodeId(run, summary.RoundNumber);
    }

    private static int ReadEpisode(PuzzleActionObservation observation) => observation.Payload.GetProperty("completionEpisode").GetInt32();

    private static IReadOnlyList<RoundAuditFinding> SequenceMismatch() =>
        Error("practice-action-journal-sequence-mismatch", "The puzzle run has missing versions, reversed times, or an invalid episode transition or identity.");

    private static bool SameFirestoreTime(DateTime left, DateTime right) =>
        left.ToUniversalTime().Ticks / 10 == right.ToUniversalTime().Ticks / 10;

    private static bool Classification(RoundAuditSummary summary, JsonElement saved)
    {
        var run = saved.GetProperty("runId").GetGuid();
        var table = saved.GetProperty("id").GetGuid();
        var episode = saved.GetProperty("completionEpisode").GetInt32();
        var completed = saved.GetProperty("completedAtUtc");
        return TryReadRunId(summary, out var classifiedRun) && classifiedRun == run && summary.TableId == table.ToString("N") &&
            summary.RoundNumber == episode && episode >= 1 && completed.ValueKind != JsonValueKind.Null &&
            SameFirestoreTime(summary.StartedAtUtc, saved.GetProperty("startedAtUtc").GetDateTime()) &&
            SameFirestoreTime(summary.CompletedAtUtc, completed.GetDateTime()) &&
            summary.Participants.Count == 1 && summary.Participants[0].IsAccountBacked &&
            summary.Participants[0].WagerCents == 0 && summary.Participants[0].PayoutCents == 0 &&
            summary.Participants[0].NetCents == 0;
    }
}
