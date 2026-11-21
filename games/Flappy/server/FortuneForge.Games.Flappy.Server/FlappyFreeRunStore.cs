using System.Security.Cryptography;
using System.Text;
using System.Text.RegularExpressions;
using FortuneForge.Games.Flappy;
using Google.Cloud.Firestore;
using FortuneForge.Games.Firestore;

namespace FortuneForge.Games.Flappy.Server;

/// <summary>
/// Persists authenticated Flappy practice runs. The client submits only flap timing;
/// score and terminal state are replayed from the server-issued seed.
/// </summary>
public class FlappyFreeRunStore
{
    public const string CollectionName = "flappyFreeRuns";
    private const string RunIdPrefix = "flappy_free_";
    private static readonly Regex IdempotencyKeyPattern = new("^[A-Za-z0-9_-]{16,128}$", RegexOptions.CultureInvariant);
    private static readonly Regex DigestPattern = new("^[0-9a-f]{64}$", RegexOptions.CultureInvariant);
    private readonly FirestoreDb database;
    private readonly TimeProvider timeProvider;
    private readonly Func<uint> createSeed;

    public FlappyFreeRunStore(
        FirestoreDb database,
        TimeProvider timeProvider,
        Func<uint>? createSeed = null)
    {
        this.database = database ?? throw new ArgumentNullException(nameof(database));
        this.timeProvider = timeProvider ?? throw new ArgumentNullException(nameof(timeProvider));
        this.createSeed = createSeed ?? CreateCryptographicNonZeroSeed;
    }

    public Task<FlappyFreeRunStartResult> StartAsync(
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
        var startedAtUtc = DurableNow();
        uint? proposedSeed = null;
        var reference = database.Collection(CollectionName).Document(documentId);

        return database.RunTransactionAsync(async transaction =>
        {
            var snapshot = await transaction.GetSnapshotAsync(reference, cancellationToken);
            if (snapshot.Exists)
            {
                var existing = ReadStartedRun(snapshot, runId, authenticatedPlayerId, idempotencyKey);
                return new FlappyFreeRunStartResult(existing.Run, existing.StartedAtUtc, true);
            }

            proposedSeed ??= createSeed();
            if (proposedSeed == 0) throw new InvalidOperationException("The Flappy seed source returned zero.");
            var run = new FlappyFreeRunIdentity(runId, proposedSeed.Value);
            transaction.Create(reference, new Dictionary<string, object>
            {
                ["runId"] = run.RunId,
                ["seed"] = (long)run.Seed,
                ["playerId"] = authenticatedPlayerId,
                ["startIdempotencyKey"] = idempotencyKey,
                ["status"] = "started",
                ["startedAt"] = Timestamp.FromDateTime(startedAtUtc.UtcDateTime),
                ["schemaVersion"] = 1L,
            });
            ArcadeRunSecurityAudit.Started(transaction, database, "flappy", runId, authenticatedPlayerId,
                startedAtUtc.UtcDateTime, new { runId, seed = run.Seed, replayVersion = 1 });
            return new FlappyFreeRunStartResult(run, startedAtUtc, false);
        }, cancellationToken: cancellationToken);
    }

    public Task<FlappyFreeRunCompletionResult> CompleteAsync(
        string runId,
        string authenticatedPlayerId,
        FlappyReplay replay,
        CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(authenticatedPlayerId))
            throw new ArgumentException("An authenticated player is required.", nameof(authenticatedPlayerId));
        ArgumentNullException.ThrowIfNull(replay);
        ValidateReplayInput(replay);

        var reference = RunDocument(runId);
        var completedAtUtc = DurableNow();
        var canonical = CanonicalizeReplay(replay);
        var digest = Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(canonical)));

        return database.RunTransactionAsync(async transaction =>
        {
            var snapshot = await transaction.GetSnapshotAsync(reference, cancellationToken);
            var stored = ReadRun(snapshot, runId, authenticatedPlayerId);
            if (stored.Status == "completed")
            {
                if (stored.ReplayCanonical != canonical || stored.ReplayDigest != digest)
                    throw new InvalidOperationException("This Flappy free run was already completed with a different replay.");
                return new FlappyFreeRunCompletionResult(
                    stored.Run.RunId, stored.Score, stored.Terminal, stored.CompletedAtUtc, true);
            }

            var result = FlappyReplayEvaluator.Evaluate(stored.Run.Seed, replay);
            var terminal = TerminalName(result.Snapshot.Phase);
            transaction.Set(reference, new Dictionary<string, object>
            {
                ["status"] = "completed",
                ["replayCanonical"] = canonical,
                ["replayDigest"] = digest,
                ["score"] = (long)result.Snapshot.Score,
                ["terminal"] = terminal,
                ["completedAt"] = Timestamp.FromDateTime(completedAtUtc.UtcDateTime),
            }, SetOptions.MergeAll);
            ArcadeRunSecurityAudit.Completed(transaction, database, "flappy", runId, authenticatedPlayerId,
                stored.StartedAtUtc.UtcDateTime, completedAtUtc.UtcDateTime, 0,
                CollectionName, reference.Id,
                new { runId, seed = stored.Run.Seed, replayVersion = 1, replayCanonical = canonical,
                    replaySha256 = digest, score = result.Snapshot.Score, terminal }, "flappy-replay-v1");
            return new FlappyFreeRunCompletionResult(runId, result.Snapshot.Score, terminal, completedAtUtc, false);
        }, cancellationToken: cancellationToken);
    }

    public static void ValidateReplayInput(FlappyReplay replay) => FlappyReplayEvaluator.ValidateReplayInput(replay);
    public static string CanonicalizeReplay(FlappyReplay replay) => FlappyRunReplay.CanonicalizeReplay(replay);
    public static FlappyReplay ParseCanonicalReplay(string canonical) => FlappyRunReplay.ParseCanonicalReplay(canonical);

    private DateTimeOffset DurableNow()
    {
        var ticks = timeProvider.GetUtcNow().UtcTicks;
        // Firestore persists microseconds. Return the same instant on the first response and every retry.
        return new DateTimeOffset(ticks - ticks % 10, TimeSpan.Zero);
    }

    private DocumentReference RunDocument(string runId)
    {
        if (!runId.StartsWith(RunIdPrefix, StringComparison.Ordinal))
            throw new ArgumentException("The Flappy free run id is invalid.", nameof(runId));
        var documentId = runId[RunIdPrefix.Length..];
        if (documentId.Length != 64 || documentId.Any(character => !Uri.IsHexDigit(character)))
            throw new ArgumentException("The Flappy free run id is invalid.", nameof(runId));
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
            throw new InvalidOperationException("The Flappy free run idempotency record is inconsistent.");
        return stored;
    }

    private static StoredFreeRun ReadRun(DocumentSnapshot snapshot, string runId, string playerId)
    {
        if (!snapshot.Exists ||
            !snapshot.TryGetValue<string>("runId", out var storedRunId) || storedRunId != runId ||
            !snapshot.TryGetValue<string>("playerId", out var storedPlayerId) || storedPlayerId != playerId ||
            !snapshot.TryGetValue<long>("seed", out var storedSeed) || storedSeed is < 1 or > uint.MaxValue ||
            !snapshot.TryGetValue<string>("status", out var status) || status is not ("started" or "completed") ||
            !snapshot.TryGetValue<Timestamp>("startedAt", out var startedAt) ||
            !snapshot.TryGetValue<long>("schemaVersion", out var schemaVersion) || schemaVersion != 1)
        {
            throw new InvalidOperationException("The Flappy free run is missing, belongs to another player, or is corrupt.");
        }

        var run = new FlappyFreeRunIdentity(runId, (uint)storedSeed);
        if (status == "started")
            return new StoredFreeRun(run, status, new DateTimeOffset(startedAt.ToDateTime()), string.Empty, string.Empty, 0, string.Empty, null);

        if (!snapshot.TryGetValue<string>("replayCanonical", out var canonical) || string.IsNullOrWhiteSpace(canonical) ||
            !snapshot.TryGetValue<string>("replayDigest", out var digest) || !DigestPattern.IsMatch(digest) ||
            !snapshot.TryGetValue<long>("score", out var score) || score < 0 ||
            !snapshot.TryGetValue<string>("terminal", out var terminal) || !IsTerminalName(terminal) ||
            !snapshot.TryGetValue<Timestamp>("completedAt", out var completedAt))
        {
            throw new InvalidOperationException("The completed Flappy free run is corrupt.");
        }
        if (Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(canonical))) != digest)
            throw new InvalidOperationException("The completed Flappy replay digest is corrupt.");
        var replay = ParseCanonicalReplay(canonical);
        var evaluated = FlappyReplayEvaluator.Evaluate(run.Seed, replay);
        if (evaluated.Snapshot.Score != score || TerminalName(evaluated.Snapshot.Phase) != terminal)
            throw new InvalidOperationException("The completed Flappy outcome disagrees with its replay.");
        return new StoredFreeRun(run, status, new DateTimeOffset(startedAt.ToDateTime()), canonical, digest, score, terminal, new DateTimeOffset(completedAt.ToDateTime()));
    }

    private static string DocumentId(string playerId, string idempotencyKey) =>
        Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes($"v1\\n{playerId}\\n{idempotencyKey}")));

    private static uint CreateCryptographicNonZeroSeed()
    {
        Span<byte> bytes = stackalloc byte[sizeof(uint)];
        uint seed;
        do { RandomNumberGenerator.Fill(bytes); seed = BitConverter.ToUInt32(bytes); } while (seed == 0);
        return seed;
    }

    public static string TerminalName(FlappyPhase phase) => FlappyRunReplay.TerminalName(phase);

    private static bool IsTerminalName(string value) => value is "obstacle-collision" or "ground-collision" or "ceiling-collision";

    private sealed record StoredFreeRun(
        FlappyFreeRunIdentity Run,
        string Status,
        DateTimeOffset StartedAtUtc,
        string ReplayCanonical,
        string ReplayDigest,
        long Score,
        string Terminal,
        DateTimeOffset? CompletedAtUtc);
}

public sealed record FlappyFreeRunIdentity(string RunId, uint Seed);
public sealed record FlappyFreeRunStartResult(FlappyFreeRunIdentity Run, DateTimeOffset StartedAtUtc, bool WasAlreadyStarted);
public sealed record FlappyFreeRunCompletionResult(string RunId, long Score, string Terminal, DateTimeOffset? CompletedAtUtc, bool WasAlreadyCompleted);
