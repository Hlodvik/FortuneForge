using System.Collections;
using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using FortuneForge.Games.Abstractions.Auditing;
using FortuneForge.Games.Flappy;
using FortuneForge.Games.HorseFlight;
using FortuneForge.Server.Arcade.Asteroids;
using FortuneForge.Server.Arcade.Flappy;
using Google.Cloud.Firestore;

namespace FortuneForge.Server.Arcade;

/// <summary>Replays completed arcade runs from persisted inputs, then reconciles their source and entry debit.</summary>
internal static class ArcadeReplayAudit
{
    internal static bool Supports(RoundAuditSummary summary) =>
        summary.AuditorVersion == "arcade-server-replay-v1" &&
        summary.GameId is "asteroids" or "flappy" or "horse-flight";

    internal static IReadOnlyList<RoundAuditFinding> Inspect(RoundAuditSummary summary, string payloadJson)
    {
        if (!Supports(summary)) return [];
        try
        {
            using var document = JsonDocument.Parse(payloadJson);
            var root = document.RootElement;
            var data = root.GetProperty("data");
            var sourceCollection = root.GetProperty("sourceCollection").GetString();
            var sourceId = root.GetProperty("sourceDocumentId").GetString();
            var entryFee = root.GetProperty("entryFeeCents").GetInt64();
            var paid = entryFee > 0;
            var expectedCollection = summary.GameId switch
            {
                "asteroids" => paid ? "asteroidsRuns" : "asteroidsFreeRuns",
                "flappy" => paid ? "flappyRuns" : "flappyFreeRuns",
                _ => "horseFlightRuns"
            };
            var expectedKind = summary.GameId switch
            {
                "asteroids" => paid ? "asteroids-paid-replay-v1" : "asteroids-replay-v1",
                "flappy" => paid ? "flappy-paid-replay-v1" : "flappy-replay-v1",
                _ => paid ? "horse-flight-paid-replay-v1" : "horse-flight-replay-v1"
            };
            var expectedRunId = summary.GameId switch
            {
                "asteroids" => (paid ? "asteroids_" : "asteroids_free_") + sourceId,
                "flappy" => (paid ? "flappy_" : "flappy_free_") + sourceId,
                _ => paid ? "horse_flight_" + sourceId : sourceId
            };
            var validSourceId = sourceId is not null &&
                (summary.GameId == "horse-flight" && !paid
                    ? sourceId.StartsWith("horse_flight_free_", StringComparison.Ordinal) &&
                      Hex64(sourceId["horse_flight_free_".Length..])
                    : Hex64(sourceId));
            if (entryFee < 0 || sourceCollection != expectedCollection || summary.EvidenceKind != expectedKind ||
                !validSourceId ||
                summary.RoundId != expectedRunId || data.GetProperty("runId").GetString() != summary.RoundId ||
                summary.Participants.Count != 1 || !summary.Participants[0].IsAccountBacked ||
                summary.Participants[0].WagerCents != entryFee || summary.Participants[0].PayoutCents != 0 ||
                summary.Participants[0].NetCents != -entryFee)
                return Error("arcade-replay-classification-mismatch", "The run identity, mode, source, or entry amount differs from the audit summary.");
            if (paid && (data.GetProperty("feeLedgerId").GetString() != $"arcade-competition-{sourceId}-entry" ||
                string.IsNullOrWhiteSpace(data.GetProperty("attemptId").GetString()) ||
                string.IsNullOrWhiteSpace(data.GetProperty("competitionId").GetString())))
                return Error("arcade-replay-classification-mismatch", "The paid run lacks its competition attempt or entry debit reference.");
            return summary.GameId switch
            {
                "asteroids" => Asteroids(summary, data),
                "flappy" => Flappy(data),
                _ => HorseFlight(data)
            };
        }
        catch (Exception exception) when (exception is JsonException or KeyNotFoundException or InvalidOperationException or
            ArgumentException or FormatException or OverflowException or IndexOutOfRangeException or NotSupportedException)
        {
            return Error("arcade-replay-evidence-invalid", "The private arcade replay input cannot be parsed or accepted by the saved engine rules.");
        }
    }

    internal static async Task<IReadOnlyList<RoundAuditFinding>> InspectAsync(
        FirestoreDb database, RoundAuditSummary summary, string payloadJson, CancellationToken ct)
    {
        var findings = Inspect(summary, payloadJson);
        if (!Supports(summary) || findings.Count > 0) return findings;
        using var document = JsonDocument.Parse(payloadJson);
        var root = document.RootElement;
        var data = root.GetProperty("data");
        var sourceCollection = root.GetProperty("sourceCollection").GetString()!;
        var sourceId = root.GetProperty("sourceDocumentId").GetString()!;
        var entryFee = root.GetProperty("entryFeeCents").GetInt64();
        var paid = entryFee > 0;
        var source = await database.Collection(sourceCollection).Document(sourceId).GetSnapshotAsync(ct);
        try
        {
            if (!SourceMatches(source, summary, data, paid, entryFee))
                return Error("arcade-run-source-mismatch", "The authoritative run document differs from the independently replayed private evidence.");
            if (!paid) return [];

            var attemptId = data.GetProperty("attemptId").GetString()!;
            var competitionId = data.GetProperty("competitionId").GetString()!;
            var ledgerId = data.GetProperty("feeLedgerId").GetString()!;
            var attemptTask = database.Collection("arcadeCompetitionAttempts").Document(sourceId).GetSnapshotAsync(ct);
            var ledgerTask = database.Collection("balanceTransactions").Document(ledgerId).GetSnapshotAsync(ct);
            await Task.WhenAll(attemptTask, ledgerTask);
            var attempt = await attemptTask;
            var ledger = await ledgerTask;
            var userId = summary.Participants[0].ActorId;
            var score = data.GetProperty("score").GetInt64();
            if (!attempt.Exists || Text(attempt, "attemptId") != attemptId ||
                Text(attempt, "competitionId") != competitionId || Text(attempt, "gameId") != summary.GameId ||
                Text(attempt, "playerId") != userId || Text(attempt, "status") != "completed" ||
                Long(attempt, "entryFeeCents") != entryFee || Long(attempt, "score") != score)
                return Error("arcade-paid-attempt-mismatch", "The paid competition attempt differs from the replayed run or account.");
            if (!ledger.Exists || Text(ledger, "transactionId") != ledgerId ||
                Text(ledger, "userId") != userId || Text(ledger, "type") != "arcade-competition-entry" ||
                Text(ledger, "idempotencyKey") != attemptId ||
                !ledger.TryGetValue<double>("amount", out var amount) || amount != (double)(-entryFee / 100m))
                return Error("arcade-paid-entry-mismatch", "The paid run has no matching wallet entry debit.");
            // Competition prizes and sole-player refunds are posted later per account and competition,
            // rather than per run. The completion receipt intentionally leaves that link incomplete.
            return [];
        }
        catch (Exception exception) when (exception is ArgumentException or InvalidCastException or
            InvalidOperationException or FormatException or OverflowException or KeyNotFoundException)
        {
            return Error("arcade-source-data-invalid", "A saved arcade run, competition attempt, or entry debit has an invalid schema.");
        }
    }

    private static IReadOnlyList<RoundAuditFinding> Asteroids(RoundAuditSummary summary, JsonElement data)
    {
        if (data.GetProperty("rulesVersion").GetInt32() != AsteroidsReplayEvaluator.RulesVersion)
            return Error("arcade-rules-version-mismatch", "The saved Asteroids rules version is unsupported.");
        var canonical = data.GetProperty("replayCanonical").GetString() ?? "";
        var parts = canonical.Split('|');
        if (parts.Length != 3 || parts[0] != $"v{AsteroidsReplayEvaluator.RulesVersion}" ||
            !int.TryParse(parts[1], NumberStyles.None, CultureInfo.InvariantCulture, out var steps))
            return Error("arcade-replay-evidence-invalid", "The saved Asteroids input timeline has an invalid canonical form.");
        var commands = new List<AsteroidsInputCommand>();
        if (parts[2].Length > 0)
            foreach (var item in parts[2].Split(','))
            {
                var pair = item.Split(':');
                if (pair.Length != 2 || !int.TryParse(pair[0], NumberStyles.None, CultureInfo.InvariantCulture, out var step) ||
                    !int.TryParse(pair[1], NumberStyles.None, CultureInfo.InvariantCulture, out var input))
                    return Error("arcade-replay-evidence-invalid", "The saved Asteroids input transition is invalid.");
                commands.Add(new(step, (AsteroidsInput)input));
            }
        var replay = new AsteroidsReplay(steps, commands);
        if (AsteroidsReplayEvaluator.CanonicalizeReplay(replay) != canonical ||
            data.GetProperty("replaySha256").GetString() != Hash(canonical))
            return Error("arcade-replay-digest-mismatch", "The saved Asteroids input differs from its canonical fingerprint.");
        var seed = data.GetProperty("seed").GetUInt64();
        var state = AsteroidsReplayEvaluator.Evaluate(new AsteroidsRunIdentity(summary.RoundId, seed), replay);
        return !state.IsTerminal || state.Score != data.GetProperty("score").GetInt64() ||
            state.Terminal.ToString().ToLowerInvariant() != data.GetProperty("terminal").GetString()
            ? Error("arcade-replay-outcome-mismatch", "The saved Asteroids score or terminal state differs from deterministic replay.") : [];
    }

    private static IReadOnlyList<RoundAuditFinding> Flappy(JsonElement data)
    {
        if (data.GetProperty("replayVersion").GetInt32() != 1)
            return Error("arcade-rules-version-mismatch", "The saved Flappy replay version is unsupported.");
        var canonical = data.GetProperty("replayCanonical").GetString() ?? "";
        var parts = canonical.Split(@"\n", StringSplitOptions.None);
        if (parts.Length != 3 || parts[0] != "v1" ||
            !int.TryParse(parts[1], NumberStyles.None, CultureInfo.InvariantCulture, out var ticks))
            return Error("arcade-replay-evidence-invalid", "The saved Flappy input timeline has an invalid canonical form.");
        var flaps = new List<int>();
        if (parts[2].Length > 0)
            foreach (var item in parts[2].Split(','))
            {
                if (!int.TryParse(item, NumberStyles.None, CultureInfo.InvariantCulture, out var tick))
                    return Error("arcade-replay-evidence-invalid", "The saved Flappy flap timeline is invalid.");
                flaps.Add(tick);
            }
        var replay = new FlappyReplay(ticks, [.. flaps]);
        if (FirestoreFlappyFreeRunService.CanonicalizeReplay(replay) != canonical ||
            data.GetProperty("replaySha256").GetString() != Hash(canonical))
            return Error("arcade-replay-digest-mismatch", "The saved Flappy input differs from its canonical fingerprint.");
        var result = FlappyReplayEvaluator.Evaluate(data.GetProperty("seed").GetUInt32(), replay);
        return result.Snapshot.Score != data.GetProperty("score").GetInt64() ||
            FirestoreFlappyFreeRunService.TerminalName(result.Snapshot.Phase) != data.GetProperty("terminal").GetString()
            ? Error("arcade-replay-outcome-mismatch", "The saved Flappy score or terminal state differs from deterministic replay.") : [];
    }

    private static IReadOnlyList<RoundAuditFinding> HorseFlight(JsonElement data)
    {
        var rules = data.GetProperty("rulesVersion").GetInt32();
        if (rules is not (HorseFlightEngine.LegacyRulesVersion or HorseFlightEngine.DraftMultiBiomeRulesVersion or
            HorseFlightEngine.PreviousRulesVersion or HorseFlightEngine.CurrentRulesVersion))
            return Error("arcade-rules-version-mismatch", "The saved Horse Flight rules version is unsupported.");
        var jumps = Ints(data.GetProperty("jumpTicks"));
        var rightClicks = Ints(data.GetProperty("rightClickTicks"));
        var replay = new HorseFlightReplay(data.GetProperty("totalTicks").GetInt32(), [.. jumps])
        {
            RightClickTicks = [.. rightClicks], RulesVersion = rules
        };
        var result = HorseFlightReplayEvaluator.Evaluate(data.GetProperty("seed").GetUInt32(), replay);
        var phase = result.Snapshot.Phase switch
        {
            HorseFlightPhase.ObstacleCollision => "obstacle-collision",
            HorseFlightPhase.Fell => "fell",
            _ => "running"
        };
        return result.Snapshot.Score != data.GetProperty("score").GetInt64() ||
            phase != data.GetProperty("phase").GetString()
            ? Error("arcade-replay-outcome-mismatch", "The saved Horse Flight score or terminal state differs from deterministic replay.") : [];
    }

    private static bool SourceMatches(DocumentSnapshot source, RoundAuditSummary summary, JsonElement data, bool paid, long fee)
    {
        if (!source.Exists || Text(source, "runId") != summary.RoundId ||
            Long(source, "score") != data.GetProperty("score").GetInt64()) return false;
        if (!(summary.GameId == "horse-flight" && !paid) && Text(source, "status") != "completed")
            return false;
        var userId = summary.Participants[0].ActorId;
        if (Text(source, summary.GameId == "horse-flight" && !paid ? "userId" : "playerId") != userId)
            return false;
        var startField = paid ? "enteredAt" : summary.GameId == "horse-flight" ? "createdAt" : "startedAt";
        if (!SameTime(source, startField, summary.StartedAtUtc) ||
            !SameTime(source, "completedAt", summary.CompletedAtUtc)) return false;
        if (summary.GameId == "asteroids")
        {
            if (Text(source, "seedHex") != data.GetProperty("seed").GetUInt64().ToString("x16") ||
                Long(source, "rulesVersion") != AsteroidsReplayEvaluator.RulesVersion ||
                Text(source, "replayCanonical") != data.GetProperty("replayCanonical").GetString() ||
                Text(source, "replayDigest") != data.GetProperty("replaySha256").GetString() ||
                Text(source, "terminal") != data.GetProperty("terminal").GetString()) return false;
        }
        else if (summary.GameId == "flappy")
        {
            if (Long(source, "seed") != data.GetProperty("seed").GetUInt32() ||
                Text(source, "replayCanonical") != data.GetProperty("replayCanonical").GetString() ||
                Text(source, "replayDigest") != data.GetProperty("replaySha256").GetString() ||
                Text(source, "terminal") != data.GetProperty("terminal").GetString()) return false;
        }
        else if (Long(source, "seed") != data.GetProperty("seed").GetUInt32() ||
            Long(source, "rulesVersion") != data.GetProperty("rulesVersion").GetInt32() ||
            Long(source, "totalTicks") != data.GetProperty("totalTicks").GetInt32() ||
            !SourceInts(source, "jumpTicks").SequenceEqual(Ints(data.GetProperty("jumpTicks"))) ||
            !SourceInts(source, "rightClickTicks").SequenceEqual(Ints(data.GetProperty("rightClickTicks"))) ||
            Text(source, "phase") != data.GetProperty("phase").GetString()) return false;
        return !paid || Long(source, "entryFeeCents") == fee &&
            Text(source, "attemptDocumentId") == source.Id &&
            Text(source, "attemptId") == data.GetProperty("attemptId").GetString() &&
            Text(source, "competitionId") == data.GetProperty("competitionId").GetString() &&
            Text(source, "gameId") == summary.GameId;
    }

    private static string Text(DocumentSnapshot document, string field) =>
        document.Exists && document.TryGetValue<string>(field, out var value) ? value : "";
    private static long? Long(DocumentSnapshot document, string field) =>
        document.Exists && document.TryGetValue<long>(field, out var value) ? value : null;
    private static bool SameTime(DocumentSnapshot document, string field, DateTime expected) =>
        document.TryGetValue<Timestamp>(field, out var value) &&
        Math.Abs((value.ToDateTime() - expected).TotalMilliseconds) <= 1;
    private static int[] Ints(JsonElement values) => values.EnumerateArray().Select(value => value.GetInt32()).ToArray();
    private static int[] SourceInts(DocumentSnapshot document, string field) =>
        document.ToDictionary().TryGetValue(field, out var raw) && raw is IEnumerable values
            ? values.Cast<object?>().Select(value => value switch
            {
                long number when number is >= int.MinValue and <= int.MaxValue => (int)number,
                int number => number,
                _ => int.MinValue
            }).ToArray()
            : [];
    private static string Hash(string value) => Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(value)));
    private static bool Hex64(string value) => value.Length == 64 &&
        value.All(character => character is >= '0' and <= '9' or >= 'a' and <= 'f');
    private static IReadOnlyList<RoundAuditFinding> Error(string code, string message) => [new(code, "error", message)];
}
