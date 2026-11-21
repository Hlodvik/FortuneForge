using FortuneForge.Games.Abstractions.Auditing;
using FortuneForge.Games.Arcade.Competition;
using FortuneForge.Games.Firestore;
using FortuneForge.Server.Arcade.Competition;
using Google.Cloud.Firestore;

namespace FortuneForge.Games.Asteroids.Server;

public sealed record AsteroidsPaidRun(AsteroidsRunIdentity Run, string Status = "started",
    string ReplayCanonical = "", string ReplayDigest = "", long Score = 0,
    AsteroidsTerminalState Terminal = AsteroidsTerminalState.Active, DateTimeOffset? CompletedAtUtc = null);

public sealed record AsteroidsPaidReplayCompletion(string RunId, long Score, AsteroidsTerminalState Terminal,
    DateTimeOffset CompletedAtUtc, bool WasAlreadyCompleted);

/// <summary>The Asteroids-owned paid run schema, entropy, replay rules, and terminal commit.</summary>
public sealed class AsteroidsPaidRuns(Func<ulong>? createSeed = null) : IArcadeCompetitionRunAdapter<AsteroidsPaidRun>
{
    public const string CollectionName = "asteroidsRuns";
    private const string RunPrefix = "asteroids_";

    public static string RunId(ArcadeCompetitionAttemptRecord attempt) => RunPrefix + attempt.DocumentId;

    public static string AttemptDocumentId(string runId)
    {
        if (string.IsNullOrEmpty(runId) || !runId.StartsWith(RunPrefix, StringComparison.Ordinal) ||
            !GameSecurityEventDocuments.IsEventId(runId[RunPrefix.Length..]))
            throw new ArgumentException("Asteroids run id is invalid.", nameof(runId));
        return runId[RunPrefix.Length..];
    }

    public DocumentReference RunDocument(FirestoreDb database, ArcadeCompetitionAttemptRecord attempt) =>
        database.Collection(CollectionName).Document(attempt.DocumentId);

    public AsteroidsPaidRun PrepareRun(ArcadeCompetitionAttemptRecord attempt) =>
        new(new AsteroidsRunIdentity(RunId(attempt), (createSeed ?? AsteroidsRunIdentity.CreateSeed)()));

    public AsteroidsPaidRun ReadRun(DocumentSnapshot snapshot, ArcadeCompetitionAttemptRecord attempt)
    {
        ArcadeCompetitionRunDocuments.ValidateIdentity(snapshot, attempt, RunId(attempt));
        if (attempt.Competition.GameId != AsteroidsModule.GameId ||
            !Number(snapshot, "rulesVersion", AsteroidsReplayEvaluator.RulesVersion) ||
            !snapshot.TryGetValue<string>("seedHex", out var seedHex) || seedHex.Length != 16 ||
            seedHex.Any(character => character is not (>= '0' and <= '9' or >= 'a' and <= 'f')) ||
            !ulong.TryParse(seedHex, System.Globalization.NumberStyles.HexNumber, null, out var seed) || seed == 0 ||
            !snapshot.TryGetValue<string>("status", out var status) || status is not ("started" or "completed"))
            throw new InvalidOperationException("The stored Asteroids run does not match its paid attempt.");
        var identity = new AsteroidsRunIdentity(RunId(attempt), seed);
        if (status == "started")
            return new(identity);

        if (!snapshot.TryGetValue<string>("replayCanonical", out var canonical) ||
            !snapshot.TryGetValue<string>("replayDigest", out var digest) || digest != GameSecurityEventDocuments.Hash(canonical) ||
            !snapshot.TryGetValue<long>("score", out var score) || score < 0 ||
            !snapshot.TryGetValue<string>("terminal", out var terminalName) ||
            !Enum.TryParse<AsteroidsTerminalState>(terminalName, true, out var terminal))
            throw new InvalidOperationException("The stored Asteroids outcome does not match its completed attempt.");
        AsteroidsGameState replayed;
        try { replayed = AsteroidsReplayEvaluator.Evaluate(identity, AsteroidsReplayEvaluator.ParseCanonicalReplay(canonical)); }
        catch (ArgumentException error) { throw new InvalidOperationException("The stored Asteroids replay is invalid.", error); }
        if (!replayed.IsTerminal || replayed.Score != score || replayed.Terminal != terminal)
            throw new InvalidOperationException("The stored Asteroids outcome does not match its replay.");
        return new(identity, status, canonical, digest, score, terminal, attempt.CompletedAtUtc);
    }

    public void WriteRun(Transaction transaction, FirestoreDb database, DocumentReference document,
        ArcadeCompetitionAttemptRecord attempt, AsteroidsPaidRun run, string entryLedgerId)
    {
        if (run.Run.RunId != RunId(attempt) || run.Status != "started")
            throw new InvalidOperationException("The prepared Asteroids run does not match its admitted attempt.");
        var fields = ArcadeCompetitionRunDocuments.IdentityFields(attempt, run.Run.RunId);
        fields["seedHex"] = run.Run.Seed.ToString("x16");
        fields["rulesVersion"] = AsteroidsReplayEvaluator.RulesVersion;
        transaction.Create(document, fields);
        ArcadeRunSecurityAudit.Started(transaction, database, AsteroidsModule.GameId, run.Run.RunId,
            attempt.PlayerId, attempt.EnteredAtUtc.UtcDateTime, new { runId = run.Run.RunId, seed = run.Run.Seed,
                attempt.AttemptId, competitionId = attempt.Competition.DocumentId, attempt.EntryFeeCents,
                feeLedgerId = entryLedgerId, rulesVersion = AsteroidsReplayEvaluator.RulesVersion });
        FirestoreGameAudit.WriteAcceptedParticipation(transaction, database, AsteroidsModule.GameId,
            attempt.PlayerId, attempt.EnteredAtUtc.UtcDateTime);
    }

    public ArcadeCompetitionRunCompletion<AsteroidsPaidReplayCompletion> Complete(FirestoreDb database,
        ArcadeCompetitionAttemptRecord attempt, DocumentSnapshot snapshot, AsteroidsReplay replay, DateTimeOffset completedAtUtc)
    {
        var run = ReadRun(snapshot, attempt);
        var canonical = AsteroidsReplayEvaluator.CanonicalizeReplay(replay);
        var digest = GameSecurityEventDocuments.Hash(canonical);
        if (run.Status == "completed")
        {
            if (run.ReplayCanonical != canonical || run.ReplayDigest != digest)
                throw new InvalidOperationException("This Asteroids run was already completed with a different replay.");
            return new(run.Score, _ => new(run.Run.RunId, run.Score, run.Terminal, run.CompletedAtUtc!.Value, true));
        }
        var outcome = AsteroidsReplayEvaluator.Evaluate(run.Run, replay);
        if (!outcome.IsTerminal) throw new ArgumentException("Asteroids replay must reach a terminal state.", nameof(replay));
        return new(outcome.Score, _ => new(run.Run.RunId, outcome.Score, outcome.Terminal, completedAtUtc, false),
            (transaction, document, completed) =>
            {
                transaction.Set(document, new Dictionary<string, object>
                {
                    ["status"] = "completed", ["replayCanonical"] = canonical, ["replayDigest"] = digest,
                    ["score"] = outcome.Score, ["terminal"] = outcome.Terminal.ToString().ToLowerInvariant(),
                    ["completedAt"] = Timestamp.FromDateTime(completedAtUtc.UtcDateTime),
                }, SetOptions.MergeAll);
                ArcadeRunSecurityAudit.Completed(transaction, database, AsteroidsModule.GameId, run.Run.RunId,
                    attempt.PlayerId, attempt.EnteredAtUtc.UtcDateTime, completedAtUtc.UtcDateTime, attempt.EntryFeeCents,
                    CollectionName, document.Id, new { runId = run.Run.RunId, seed = run.Run.Seed, attempt.AttemptId,
                        competitionId = attempt.Competition.DocumentId, feeLedgerId = $"arcade-competition-{attempt.DocumentId}-entry",
                        rulesVersion = AsteroidsReplayEvaluator.RulesVersion, replayCanonical = canonical,
                        replaySha256 = digest, score = outcome.Score, terminal = outcome.Terminal.ToString().ToLowerInvariant() },
                    "asteroids-paid-replay-v1");
            });
    }

    private static bool Number(DocumentSnapshot snapshot, string field, long expected) =>
        snapshot.TryGetValue<long>(field, out var value) && value == expected;
}
