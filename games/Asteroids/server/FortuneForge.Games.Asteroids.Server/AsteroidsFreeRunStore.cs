using System.Security.Cryptography;
using System.Text;
using System.Text.RegularExpressions;
using FortuneForge.Games.Firestore;
using Google.Cloud.Firestore;

namespace FortuneForge.Games.Asteroids.Server;

/// <summary>
/// Persists authenticated practice runs without touching competition, balance, jackpot, or payout data.
/// </summary>
public class AsteroidsFreeRunStore
{
    public const string CollectionName = "asteroidsFreeRuns";
    private const string RunIdPrefix = "asteroids_free_";
    private static readonly Regex IdempotencyKeyPattern = new("^[A-Za-z0-9_-]{16,128}$", RegexOptions.CultureInvariant);
    private static readonly Regex DigestPattern = new("^[0-9a-f]{64}$", RegexOptions.CultureInvariant);
    private readonly FirestoreDb database;
    private readonly TimeProvider timeProvider;
    private readonly Func<ulong> createSeed;

    public AsteroidsFreeRunStore(
        FirestoreDb database,
        TimeProvider timeProvider,
        Func<ulong>? createSeed = null)
    {
        this.database = database ?? throw new ArgumentNullException(nameof(database));
        this.timeProvider = timeProvider ?? throw new ArgumentNullException(nameof(timeProvider));
        this.createSeed = createSeed ?? AsteroidsRunIdentity.CreateSeed;
    }

    public Task<AsteroidsFreeRunStartResult> StartAsync(
        string idempotencyKey,
        string authenticatedPlayerId,
        CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(authenticatedPlayerId))
            throw new ArgumentException("An authenticated player is required.", nameof(authenticatedPlayerId));
        if (string.IsNullOrWhiteSpace(idempotencyKey) || !IdempotencyKeyPattern.IsMatch(idempotencyKey))
            throw new ArgumentException("The idempotency key is invalid.", nameof(idempotencyKey));

        var documentId = DocumentId(authenticatedPlayerId, idempotencyKey);
        var runId = $"{RunIdPrefix}{documentId}";
        var startedAtUtc = timeProvider.GetUtcNow();
        var proposedSeed = createSeed();
        if (proposedSeed == 0) throw new InvalidOperationException("The Asteroids seed source returned zero.");
        var reference = database.Collection(CollectionName).Document(documentId);

        return database.RunTransactionAsync(async transaction =>
        {
            var snapshot = await transaction.GetSnapshotAsync(reference, cancellationToken);
            if (snapshot.Exists)
            {
                var existing = ReadStartedRun(snapshot, runId, authenticatedPlayerId, idempotencyKey);
                return new AsteroidsFreeRunStartResult(existing.Run, existing.StartedAtUtc, true);
            }

            var run = new AsteroidsRunIdentity(runId, proposedSeed);
            transaction.Create(reference, new Dictionary<string, object>
            {
                ["runId"] = run.RunId,
                ["seedHex"] = run.Seed.ToString("x16"),
                ["playerId"] = authenticatedPlayerId,
                ["startIdempotencyKey"] = idempotencyKey,
                ["status"] = "started",
                ["startedAt"] = Timestamp.FromDateTime(startedAtUtc.UtcDateTime),
                ["rulesVersion"] = AsteroidsReplayEvaluator.RulesVersion,
                ["schemaVersion"] = 1L,
            });
            ArcadeRunSecurityAudit.Started(transaction, database, AsteroidsModule.GameId, runId, authenticatedPlayerId,
                startedAtUtc.UtcDateTime, new { runId, seed = run.Seed, rulesVersion = AsteroidsReplayEvaluator.RulesVersion });
            return new AsteroidsFreeRunStartResult(run, startedAtUtc, false);
        }, cancellationToken: cancellationToken);
    }

    public Task<AsteroidsFreeRunCompletionResult> CompleteAsync(
        string runId,
        string authenticatedPlayerId,
        AsteroidsReplay replay,
        CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(authenticatedPlayerId))
            throw new ArgumentException("An authenticated player is required.", nameof(authenticatedPlayerId));
        ArgumentNullException.ThrowIfNull(replay);
        AsteroidsReplayEvaluator.ValidateReplayInput(replay);

        var reference = RunDocument(runId);
        var completedAtUtc = timeProvider.GetUtcNow();
        var canonical = AsteroidsReplayEvaluator.CanonicalizeReplay(replay);
        var digest = Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(canonical)));

        return database.RunTransactionAsync(async transaction =>
        {
            var snapshot = await transaction.GetSnapshotAsync(reference, cancellationToken);
            var stored = ReadRunForCompletion(snapshot, runId, authenticatedPlayerId);
            if (stored.Status == "completed")
            {
                if (stored.ReplayCanonical != canonical || stored.ReplayDigest != digest)
                    throw new InvalidOperationException("This Asteroids free run was already completed with a different replay.");
                return new AsteroidsFreeRunCompletionResult(
                    stored.Run.RunId, stored.Score, stored.Terminal, stored.CompletedAtUtc, true);
            }
            if (stored.Status != "started")
                throw new InvalidOperationException("The Asteroids free run is not available for completion.");

            var state = AsteroidsReplayEvaluator.Evaluate(stored.Run, replay);
            if (!state.IsTerminal) throw new ArgumentException("Asteroids replay must reach a terminal state.", nameof(replay));
            transaction.Set(reference, new Dictionary<string, object>
            {
                ["status"] = "completed",
                ["replayCanonical"] = canonical,
                ["replayDigest"] = digest,
                ["score"] = state.Score,
                ["terminal"] = state.Terminal.ToString().ToLowerInvariant(),
                ["completedAt"] = Timestamp.FromDateTime(completedAtUtc.UtcDateTime),
            }, SetOptions.MergeAll);
            ArcadeRunSecurityAudit.Completed(transaction, database, AsteroidsModule.GameId, runId, authenticatedPlayerId,
                stored.StartedAtUtc.UtcDateTime, completedAtUtc.UtcDateTime, 0,
                CollectionName, reference.Id,
                new { runId, seed = stored.Run.Seed, rulesVersion = AsteroidsReplayEvaluator.RulesVersion,
                    replayCanonical = canonical, replaySha256 = digest, score = state.Score,
                    terminal = state.Terminal.ToString().ToLowerInvariant() }, "asteroids-replay-v1");
            FirestoreGameAudit.WriteAcceptedParticipation(transaction, database, AsteroidsModule.GameId,
                authenticatedPlayerId, completedAtUtc.UtcDateTime);
            return new AsteroidsFreeRunCompletionResult(runId, state.Score, state.Terminal, completedAtUtc, false);
        }, cancellationToken: cancellationToken);
    }

    /// <summary>Recovers a committed seed and exact completed replay after an interrupted response.</summary>
    public async Task<AsteroidsFreeRunRecovery> GetAsync(string runId, string authenticatedPlayerId,
        CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(authenticatedPlayerId))
            throw new ArgumentException("An authenticated player is required.", nameof(authenticatedPlayerId));
        var snapshot = await RunDocument(runId).GetSnapshotAsync(cancellationToken);
        var stored = ReadRun(snapshot, runId, authenticatedPlayerId);
        return new(stored.Run.RunId, stored.Run.Seed.ToString("x16"), stored.StartedAtUtc,
            AsteroidsReplayEvaluator.RulesVersion, stored.Status, stored.Score, stored.Terminal.ToString().ToLowerInvariant(), stored.CompletedAtUtc,
            stored.Status == "completed" ? AsteroidsReplayEvaluator.ParseCanonicalReplay(stored.ReplayCanonical) : null);
    }

    private DocumentReference RunDocument(string runId)
    {
        _ = new AsteroidsRunIdentity(runId, 1);
        if (!runId.StartsWith(RunIdPrefix, StringComparison.Ordinal))
            throw new ArgumentException("The Asteroids free run id is invalid.", nameof(runId));
        var documentId = runId[RunIdPrefix.Length..];
        if (documentId.Length != 64 || documentId.Any(character => !Uri.IsHexDigit(character)))
            throw new ArgumentException("The Asteroids free run id is invalid.", nameof(runId));
        return database.Collection(CollectionName).Document(documentId);
    }

    private static StoredFreeRun ReadStartedRun(
        DocumentSnapshot snapshot,
        string runId,
        string playerId,
        string idempotencyKey)
    {
        var stored = ReadRun(snapshot, runId, playerId);
        if (!snapshot.TryGetValue<string>("startIdempotencyKey", out var storedKey) || storedKey != idempotencyKey)
            throw new InvalidOperationException("The Asteroids free run idempotency record is inconsistent.");
        return stored;
    }

    private static StoredFreeRun ReadRunForCompletion(DocumentSnapshot snapshot, string runId, string playerId) =>
        ReadRun(snapshot, runId, playerId);

    private static StoredFreeRun ReadRun(DocumentSnapshot snapshot, string runId, string playerId)
    {
        if (!snapshot.Exists ||
            !snapshot.TryGetValue<string>("runId", out var storedRunId) || storedRunId != runId ||
            !snapshot.TryGetValue<string>("playerId", out var storedPlayerId) || storedPlayerId != playerId ||
            !snapshot.TryGetValue<string>("seedHex", out var seedHex) ||
            !ulong.TryParse(seedHex, System.Globalization.NumberStyles.HexNumber, null, out var seed) || seed == 0 ||
            !snapshot.TryGetValue<string>("status", out var status) || status is not ("started" or "completed") ||
            !snapshot.TryGetValue<Timestamp>("startedAt", out var startedAt) ||
            !snapshot.TryGetValue<long>("rulesVersion", out var rulesVersion) || rulesVersion != AsteroidsReplayEvaluator.RulesVersion ||
            !snapshot.TryGetValue<long>("schemaVersion", out var schemaVersion) || schemaVersion != 1)
        {
            throw new InvalidOperationException("The Asteroids free run is missing, belongs to another player, or is corrupt.");
        }

        if (status == "started")
            return new StoredFreeRun(new AsteroidsRunIdentity(runId, seed), status,
                new DateTimeOffset(startedAt.ToDateTime()), string.Empty, string.Empty, 0,
                AsteroidsTerminalState.Active, null);

        if (!snapshot.TryGetValue<string>("replayCanonical", out var canonical) || string.IsNullOrWhiteSpace(canonical) ||
            !snapshot.TryGetValue<string>("replayDigest", out var digest) || !DigestPattern.IsMatch(digest) ||
            !snapshot.TryGetValue<long>("score", out var score) || score < 0 ||
            !snapshot.TryGetValue<string>("terminal", out var terminalName) ||
            !Enum.TryParse<AsteroidsTerminalState>(terminalName, true, out var terminal) || terminal == AsteroidsTerminalState.Active ||
            !snapshot.TryGetValue<Timestamp>("completedAt", out var completedAt))
        {
            throw new InvalidOperationException("The completed Asteroids free run is corrupt.");
        }
        var identity = new AsteroidsRunIdentity(runId, seed);
        try
        {
            var state = AsteroidsReplayEvaluator.Evaluate(identity, AsteroidsReplayEvaluator.ParseCanonicalReplay(canonical));
            var actualDigest = Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(canonical)));
            if (!state.IsTerminal || state.Score != score || state.Terminal != terminal || digest != actualDigest ||
                completedAt.CompareTo(startedAt) < 0)
                throw new InvalidOperationException("The completed Asteroids free run does not match its replay.");
        }
        catch (ArgumentException exception)
        {
            throw new InvalidOperationException("The completed Asteroids free run replay is corrupt.", exception);
        }
        return new StoredFreeRun(identity, status,
            new DateTimeOffset(startedAt.ToDateTime()), canonical, digest, score, terminal,
            new DateTimeOffset(completedAt.ToDateTime()));
    }

    private static string DocumentId(string playerId, string idempotencyKey) =>
        Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes($"v1\n{playerId}\n{idempotencyKey}")));

    private sealed record StoredFreeRun(
        AsteroidsRunIdentity Run,
        string Status,
        DateTimeOffset StartedAtUtc,
        string ReplayCanonical,
        string ReplayDigest,
        long Score,
        AsteroidsTerminalState Terminal,
        DateTimeOffset? CompletedAtUtc);
}

public sealed record AsteroidsFreeRunStartResult(
    AsteroidsRunIdentity Run,
    DateTimeOffset StartedAtUtc,
    bool WasAlreadyStarted);

public sealed record AsteroidsFreeRunCompletionResult(
    string RunId,
    long Score,
    AsteroidsTerminalState Terminal,
    DateTimeOffset? CompletedAtUtc,
    bool WasAlreadyCompleted);

public sealed record AsteroidsFreeRunRecovery(
    string RunId, string SeedHex, DateTimeOffset StartedAtUtc, int RulesVersion, string Status, long Score,
    string Terminal, DateTimeOffset? CompletedAtUtc, AsteroidsReplay? Replay);
