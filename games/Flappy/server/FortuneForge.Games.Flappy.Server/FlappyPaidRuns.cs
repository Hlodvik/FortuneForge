using System.Security.Cryptography;
using FortuneForge.Games.Abstractions.Auditing;
using FortuneForge.Games.Arcade.Competition;
using FortuneForge.Games.Firestore;
using FortuneForge.Server.Arcade.Competition;
using Google.Cloud.Firestore;

namespace FortuneForge.Games.Flappy.Server;

public sealed record FlappyPaidRun(string RunId, uint Seed, string Status = "started",
    string ReplayCanonical = "", string ReplayDigest = "", long Score = 0,
    string Terminal = "", DateTimeOffset? CompletedAtUtc = null);

/// <summary>Owns paid Flappy run identity, entropy, storage, replay and terminal commit.</summary>
public sealed class FlappyPaidRuns(Func<uint>? createSeed = null) : IArcadeCompetitionRunAdapter<FlappyPaidRun>
{
    public const string GameId = FlappyModule.GameId;
    public const string CollectionName = "flappyRuns";
    private const string RunPrefix = "flappy_";

    public static string RunId(ArcadeCompetitionAttemptRecord attempt) => RunPrefix + attempt.DocumentId;
    public static string AttemptDocumentId(string runId)
    {
        if (string.IsNullOrEmpty(runId) || !runId.StartsWith(RunPrefix, StringComparison.Ordinal) ||
            !GameSecurityEventDocuments.IsEventId(runId[RunPrefix.Length..]))
            throw new ArgumentException("Flappy run id is invalid.", nameof(runId));
        return runId[RunPrefix.Length..];
    }
    public DocumentReference RunDocument(FirestoreDb database, ArcadeCompetitionAttemptRecord attempt) =>
        database.Collection(CollectionName).Document(attempt.DocumentId);

    public FlappyPaidRun PrepareRun(ArcadeCompetitionAttemptRecord attempt)
    {
        if (attempt.Competition.GameId != GameId) throw new ArgumentException("The paid run has a different game.", nameof(attempt));
        var seed = (createSeed ?? CreateSeed)();
        if (seed == 0) throw new InvalidOperationException("The Flappy seed source returned zero.");
        return new(RunId(attempt), seed);
    }

    public FlappyPaidRun ReadRun(DocumentSnapshot snapshot, ArcadeCompetitionAttemptRecord attempt)
    {
        ArcadeCompetitionRunDocuments.ValidateIdentity(snapshot, attempt, RunId(attempt));
        if (attempt.Competition.GameId != GameId ||
            !snapshot.TryGetValue<long>("entryFeeCents", out var fee) || fee != attempt.EntryFeeCents ||
            !snapshot.TryGetValue<Timestamp>("enteredAt", out var enteredAt) || enteredAt.ToDateTime() != attempt.EnteredAtUtc.UtcDateTime ||
            !snapshot.TryGetValue<long>("seed", out var seed) || seed is < 1 or > uint.MaxValue ||
            !snapshot.TryGetValue<string>("status", out var status) || status is not ("started" or "completed"))
            throw new InvalidOperationException("The stored Flappy run does not match its paid attempt.");
        if (status == "started")
        {
            if (attempt.IsCompleted) throw new InvalidOperationException("The Flappy run is missing its completed replay.");
            return new(RunId(attempt), (uint)seed);
        }
        if (!snapshot.TryGetValue<string>("replayCanonical", out var canonical) ||
            !snapshot.TryGetValue<string>("replayDigest", out var digest) || digest != GameSecurityEventDocuments.Hash(canonical) ||
            !snapshot.TryGetValue<long>("score", out var score) || score < 0 ||
            !snapshot.TryGetValue<string>("terminal", out var terminal) ||
            !snapshot.TryGetValue<Timestamp>("completedAt", out var completedAt) ||
            !attempt.IsCompleted || attempt.Score != score || attempt.CompletedAtUtc != new DateTimeOffset(completedAt.ToDateTime()))
            throw new InvalidOperationException("The stored Flappy outcome does not match its completed attempt.");
        FlappyReplayResult result;
        try { result = FlappyReplayEvaluator.Evaluate((uint)seed, FlappyRunReplay.ParseCanonicalReplay(canonical)); }
        catch (ArgumentException error) { throw new InvalidOperationException("The stored Flappy replay is invalid.", error); }
        if (result.Snapshot.Score != score || FlappyRunReplay.TerminalName(result.Snapshot.Phase) != terminal)
            throw new InvalidOperationException("The stored Flappy outcome does not match its replay.");
        return new(RunId(attempt), (uint)seed, status, canonical, digest, score, terminal, attempt.CompletedAtUtc);
    }

    public void WriteRun(Transaction transaction, FirestoreDb database, DocumentReference document,
        ArcadeCompetitionAttemptRecord attempt, FlappyPaidRun run, string entryLedgerId)
    {
        if (run.RunId != RunId(attempt) || run.Seed == 0 || run.Status != "started")
            throw new InvalidOperationException("The prepared Flappy run is invalid.");
        var fields = ArcadeCompetitionRunDocuments.IdentityFields(attempt, run.RunId);
        fields["seed"] = (long)run.Seed;
        transaction.Create(document, fields);
        ArcadeRunSecurityAudit.Started(transaction, database, GameId, run.RunId, attempt.PlayerId,
            attempt.EnteredAtUtc.UtcDateTime, new { runId = run.RunId, run.Seed, attempt.AttemptId,
                competitionId = attempt.Competition.DocumentId, attempt.EntryFeeCents, feeLedgerId = entryLedgerId, replayVersion = 1 });
        FirestoreGameAudit.WriteAcceptedParticipation(transaction, database, GameId, attempt.PlayerId, attempt.EnteredAtUtc.UtcDateTime);
    }

    public ArcadeCompetitionRunCompletion<FlappyRecordedRunCompletion> Complete(FirestoreDb database,
        ArcadeCompetitionAttemptRecord attempt, DocumentSnapshot snapshot, FlappyReplay replay, DateTimeOffset completedAtUtc)
    {
        var run = ReadRun(snapshot, attempt);
        var canonical = FlappyRunReplay.CanonicalizeReplay(replay);
        var digest = GameSecurityEventDocuments.Hash(canonical);
        if (run.Status == "completed")
        {
            if (run.ReplayCanonical != canonical || run.ReplayDigest != digest)
                throw new InvalidOperationException("This Flappy run was already completed with a different replay.");
            return new(run.Score, _ => new(run.RunId, run.Score, run.Terminal, true));
        }
        var outcome = FlappyReplayEvaluator.Evaluate(run.Seed, replay);
        var terminal = FlappyRunReplay.TerminalName(outcome.Snapshot.Phase);
        return new(outcome.Snapshot.Score, _ => new(run.RunId, outcome.Snapshot.Score, terminal, false),
            (transaction, document, completed) =>
            {
                var committedAt = completed.CompletedAtUtc!.Value;
                transaction.Set(document, new Dictionary<string, object>
                {
                    ["status"] = "completed", ["replayCanonical"] = canonical, ["replayDigest"] = digest,
                    ["score"] = (long)outcome.Snapshot.Score, ["terminal"] = terminal,
                    ["completedAt"] = Timestamp.FromDateTime(committedAt.UtcDateTime),
                }, SetOptions.MergeAll);
                ArcadeRunSecurityAudit.Completed(transaction, database, GameId, run.RunId, attempt.PlayerId,
                    attempt.EnteredAtUtc.UtcDateTime, committedAt.UtcDateTime, attempt.EntryFeeCents,
                    CollectionName, document.Id, new { runId = run.RunId, run.Seed, attempt.AttemptId,
                        competitionId = attempt.Competition.DocumentId, feeLedgerId = $"arcade-competition-{attempt.DocumentId}-entry",
                        replayVersion = 1, replayCanonical = canonical, replaySha256 = digest, score = outcome.Snapshot.Score, terminal },
                    "flappy-paid-replay-v1");
            });
    }

    private static uint CreateSeed()
    {
        uint seed;
        do { seed = BitConverter.ToUInt32(RandomNumberGenerator.GetBytes(sizeof(uint))); } while (seed == 0);
        return seed;
    }
}
