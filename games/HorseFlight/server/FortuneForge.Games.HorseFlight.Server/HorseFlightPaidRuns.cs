using System.Collections;
using System.Security.Cryptography;
using FortuneForge.Games.Abstractions.Auditing;
using FortuneForge.Games.Arcade.Competition;
using FortuneForge.Games.Firestore;
using FortuneForge.Server.Arcade.Competition;
using Google.Cloud.Firestore;

namespace FortuneForge.Games.HorseFlight.Server;

public sealed record HorseFlightPaidRun(string RunId, uint Seed, int RulesVersion, string Status,
    int TotalTicks, int[] JumpTicks, int[] RightClickTicks, int Score, HorseFlightPhase Phase, DateTimeOffset? CompletedAtUtc);

/// <summary>Owns paid Horse Flight run identity, saved rules, replay and terminal commit.</summary>
public sealed class HorseFlightPaidRuns(Func<uint>? createSeed = null) : IArcadeCompetitionRunAdapter<HorseFlightPaidRun>
{
    public const string GameId = HorseFlightModule.GameId;
    public const string CollectionName = "horseFlightRuns";
    private const string RunPrefix = "horse_flight_";

    public static string RunId(ArcadeCompetitionAttemptRecord attempt) => RunPrefix + attempt.DocumentId;
    public static string AttemptDocumentId(string runId)
    {
        if (string.IsNullOrEmpty(runId) || !runId.StartsWith(RunPrefix, StringComparison.Ordinal) ||
            !GameSecurityEventDocuments.IsEventId(runId[RunPrefix.Length..]))
            throw new ArgumentException("Horse Flight run id is invalid.", nameof(runId));
        return runId[RunPrefix.Length..];
    }
    public DocumentReference RunDocument(FirestoreDb database, ArcadeCompetitionAttemptRecord attempt) =>
        database.Collection(CollectionName).Document(attempt.DocumentId);

    public HorseFlightPaidRun PrepareRun(ArcadeCompetitionAttemptRecord attempt)
    {
        if (attempt.Competition.GameId != GameId) throw new ArgumentException("The paid run has a different game.", nameof(attempt));
        var seed = (createSeed ?? CreateSeed)();
        if (seed == 0) throw new InvalidOperationException("The Horse Flight seed source returned zero.");
        return new(RunId(attempt), seed, HorseFlightEngine.CurrentRulesVersion, "started", 0, [], [], 0, HorseFlightPhase.Running, null);
    }

    public HorseFlightPaidRun ReadRun(DocumentSnapshot snapshot, ArcadeCompetitionAttemptRecord attempt)
    {
        ArcadeCompetitionRunDocuments.ValidateIdentity(snapshot, attempt, RunId(attempt));
        if (attempt.Competition.GameId != GameId ||
            !snapshot.TryGetValue<long>("entryFeeCents", out var fee) || fee != attempt.EntryFeeCents ||
            !snapshot.TryGetValue<Timestamp>("enteredAt", out var enteredAt) || enteredAt.ToDateTime() != attempt.EnteredAtUtc.UtcDateTime ||
            !snapshot.TryGetValue<long>("seed", out var seed) || seed is < 1 or > uint.MaxValue ||
            !snapshot.TryGetValue<long>("tickMilliseconds", out var tickMilliseconds) || tickMilliseconds != HorseFlightEngine.TickMilliseconds ||
            !snapshot.TryGetValue<string>("status", out var status) || status is not ("started" or "completed"))
            throw new InvalidOperationException("The stored Horse Flight run does not match its paid attempt.");
        var rules = HorseFlightRunReplay.ResolveRulesVersion(snapshot.TryGetValue<long>("rulesVersion", out var storedRules) ? storedRules : null);
        if (status == "started")
        {
            if (attempt.IsCompleted) throw new InvalidOperationException("The Horse Flight run is missing its completed replay.");
            return new(RunId(attempt), (uint)seed, rules, status, 0, [], [], 0, HorseFlightPhase.Running, null);
        }
        if (!snapshot.TryGetValue<long>("totalTicks", out var totalTicks) || totalTicks is < 1 or > HorseFlightReplayEvaluator.MaximumTicks ||
            !snapshot.TryGetValue<long>("score", out var score) || score is < 0 or > int.MaxValue ||
            !snapshot.TryGetValue<string>("phase", out var phaseName) ||
            !snapshot.TryGetValue<Timestamp>("completedAt", out var completedAt) ||
            !attempt.IsCompleted || attempt.Score != score || attempt.CompletedAtUtc != new DateTimeOffset(completedAt.ToDateTime()))
            throw new InvalidOperationException("The stored Horse Flight outcome does not match its completed attempt.");
        var phase = phaseName switch
        {
            "obstacle-collision" => HorseFlightPhase.ObstacleCollision,
            "fell" => HorseFlightPhase.Fell,
            _ => throw new InvalidOperationException("The Horse Flight terminal phase is invalid."),
        };
        var jumps = ReadTicks(snapshot, "jumpTicks");
        var rights = ReadTicks(snapshot, "rightClickTicks");
        HorseFlightReplayResult result;
        try
        {
            result = HorseFlightReplayEvaluator.Evaluate((uint)seed, new HorseFlightReplay((int)totalTicks, [.. jumps])
            {
                RightClickTicks = [.. rights], RulesVersion = rules,
            });
        }
        catch (ArgumentException error) { throw new InvalidOperationException("The stored Horse Flight replay is invalid.", error); }
        if (result.Snapshot.Score != score || result.Snapshot.Phase != phase)
            throw new InvalidOperationException("The stored Horse Flight outcome does not match its replay.");
        return new(RunId(attempt), (uint)seed, rules, status, (int)totalTicks, jumps, rights, (int)score, phase, attempt.CompletedAtUtc);
    }

    public void WriteRun(Transaction transaction, FirestoreDb database, DocumentReference document,
        ArcadeCompetitionAttemptRecord attempt, HorseFlightPaidRun run, string entryLedgerId)
    {
        if (run.RunId != RunId(attempt) || run.Seed == 0 || run.Status != "started")
            throw new InvalidOperationException("The prepared Horse Flight run is invalid.");
        _ = HorseFlightRunReplay.ResolveRulesVersion(run.RulesVersion);
        var fields = ArcadeCompetitionRunDocuments.IdentityFields(attempt, run.RunId);
        fields["seed"] = (long)run.Seed;
        fields["rulesVersion"] = (long)run.RulesVersion;
        fields["tickMilliseconds"] = HorseFlightEngine.TickMilliseconds;
        transaction.Create(document, fields);
        ArcadeRunSecurityAudit.Started(transaction, database, GameId, run.RunId, attempt.PlayerId,
            attempt.EnteredAtUtc.UtcDateTime, new { runId = run.RunId, run.Seed, run.RulesVersion, attempt.AttemptId,
                competitionId = attempt.Competition.DocumentId, attempt.EntryFeeCents, feeLedgerId = entryLedgerId,
                tickMilliseconds = HorseFlightEngine.TickMilliseconds });
        FirestoreGameAudit.WriteAcceptedParticipation(transaction, database, GameId, attempt.PlayerId, attempt.EnteredAtUtc.UtcDateTime);
    }

    public ArcadeCompetitionRunCompletion<HorseFlightRecordedRunCompletion> Complete(FirestoreDb database,
        ArcadeCompetitionAttemptRecord attempt, DocumentSnapshot snapshot, HorseFlightReplay requestedReplay, DateTimeOffset completedAtUtc)
    {
        var run = ReadRun(snapshot, attempt);
        var replay = requestedReplay with { RulesVersion = run.RulesVersion };
        HorseFlightReplayEvaluator.ValidateReplayInput(replay);
        if (run.Status == "completed")
        {
            if (run.TotalTicks != replay.TotalTicks || !run.JumpTicks.SequenceEqual(replay.JumpTicks) ||
                !run.RightClickTicks.SequenceEqual(replay.RightClickTicks))
                throw new InvalidOperationException("This Horse Flight run was already completed with a different replay.");
            return new(run.Score, _ => Reply(run, true));
        }
        var outcome = HorseFlightReplayEvaluator.Evaluate(run.Seed, replay);
        var phase = HorseFlightRunReplay.PhaseName(outcome.Snapshot.Phase);
        var completedRun = run with { Status = "completed", TotalTicks = replay.TotalTicks, JumpTicks = replay.JumpTicks.ToArray(),
            RightClickTicks = replay.RightClickTicks.ToArray(), Score = outcome.Snapshot.Score, Phase = outcome.Snapshot.Phase,
            CompletedAtUtc = completedAtUtc };
        return new(outcome.Snapshot.Score, _ => Reply(completedRun, false),
            (transaction, document, completed) =>
            {
                var committedAt = completed.CompletedAtUtc!.Value;
                transaction.Set(document, new Dictionary<string, object>
                {
                    ["status"] = "completed", ["rulesVersion"] = (long)run.RulesVersion,
                    ["totalTicks"] = replay.TotalTicks, ["jumpTicks"] = completedRun.JumpTicks,
                    ["rightClickTicks"] = completedRun.RightClickTicks, ["score"] = outcome.Snapshot.Score,
                    ["phase"] = phase, ["completedAt"] = Timestamp.FromDateTime(committedAt.UtcDateTime),
                }, SetOptions.MergeAll);
                ArcadeRunSecurityAudit.Completed(transaction, database, GameId, run.RunId, attempt.PlayerId,
                    attempt.EnteredAtUtc.UtcDateTime, committedAt.UtcDateTime, attempt.EntryFeeCents,
                    CollectionName, document.Id, new { runId = run.RunId, run.Seed, run.RulesVersion, attempt.AttemptId,
                        competitionId = attempt.Competition.DocumentId, feeLedgerId = $"arcade-competition-{attempt.DocumentId}-entry",
                        replay.TotalTicks, jumpTicks = completedRun.JumpTicks, rightClickTicks = completedRun.RightClickTicks,
                        score = outcome.Snapshot.Score, phase }, "horse-flight-paid-replay-v1");
            });
    }

    private static HorseFlightRecordedRunCompletion Reply(HorseFlightPaidRun run, bool wasReplay) =>
        new(run.RunId, run.Score, HorseFlightRunReplay.PhaseName(run.Phase), run.TotalTicks, run.JumpTicks, wasReplay)
        { RightClickTicks = run.RightClickTicks };
    private static int[] ReadTicks(DocumentSnapshot snapshot, string field)
    {
        if (!snapshot.ToDictionary().TryGetValue(field, out var raw)) return [];
        if (raw is not IEnumerable values) throw new InvalidOperationException("The stored Horse Flight input timeline is invalid.");
        return values.Cast<object?>().Select(item => item switch
        {
            long value when value is >= 0 and <= int.MaxValue => (int)value,
            int value when value >= 0 => value,
            _ => throw new InvalidOperationException("The stored Horse Flight input tick is invalid."),
        }).ToArray();
    }
    private static uint CreateSeed()
    {
        uint seed;
        do { seed = BitConverter.ToUInt32(RandomNumberGenerator.GetBytes(sizeof(uint))); } while (seed == 0);
        return seed;
    }
}
