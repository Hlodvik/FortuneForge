using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using Google.Cloud.Firestore;

namespace FortuneForge.Server.Cards.Solitaire;

/// <summary>
/// Persists authenticated free-play runs without touching competitive or financial data.
/// </summary>
public sealed class FirestoreSolitaireFreeRunService
{
    internal const string CollectionName = "solitaireFreeRuns";
    private const string RunIdPrefix = "solitaire_free_";
    // A fully populated move command is roughly 120 bytes in the JSON envelope.
    // Keep an accepted replay beneath the application's 32 KB request-body limit.
    private const int MaximumCommands = 256;
    private static readonly Regex IdempotencyKeyPattern = new("^[A-Za-z0-9_-]{16,128}$", RegexOptions.CultureInvariant);
    private static readonly Regex DigestPattern = new("^[0-9a-f]{64}$", RegexOptions.CultureInvariant);
    private static readonly JsonSerializerOptions CanonicalJson = new(JsonSerializerDefaults.Web);
    private readonly FirestoreDb database;
    private readonly TimeProvider timeProvider;
    private readonly Func<uint> createSeed;

    internal FirestoreSolitaireFreeRunService(
        FirestoreDb database,
        TimeProvider timeProvider,
        Func<uint>? createSeed = null)
    {
        this.database = database ?? throw new ArgumentNullException(nameof(database));
        this.timeProvider = timeProvider ?? throw new ArgumentNullException(nameof(timeProvider));
        this.createSeed = createSeed ?? CreateCryptographicNonZeroSeed;
    }

    internal Task<SolitaireFreeRunStartResult> StartAsync(
        string idempotencyKey,
        string authenticatedUserId,
        int drawCount,
        CancellationToken cancellationToken)
    {
        ValidateUser(authenticatedUserId);
        SolitaireRules.ValidateDrawCount(drawCount);
        if (string.IsNullOrWhiteSpace(idempotencyKey) || !IdempotencyKeyPattern.IsMatch(idempotencyKey))
            throw new ArgumentException("The idempotency key is invalid.", nameof(idempotencyKey));

        var documentId = DocumentId(authenticatedUserId, idempotencyKey);
        var runId = $"{RunIdPrefix}{documentId}";
        var startedAtUtc = timeProvider.GetUtcNow();
        var proposedSeed = createSeed();
        if (proposedSeed == 0) throw new InvalidOperationException("The Solitaire seed source returned zero.");
        var reference = database.Collection(CollectionName).Document(documentId);

        return database.RunTransactionAsync(async transaction =>
        {
            var snapshot = await transaction.GetSnapshotAsync(reference, cancellationToken);
            if (snapshot.Exists)
            {
                var existing = ReadRun(snapshot, runId, authenticatedUserId);
                if (!snapshot.TryGetValue<string>("startIdempotencyKey", out var storedKey) || storedKey != idempotencyKey ||
                    existing.DrawCount != drawCount)
                    throw new InvalidOperationException("The Solitaire free-run idempotency record conflicts with this request.");
                return new SolitaireFreeRunStartResult(
                    existing.RunId, existing.Seed, existing.DrawCount, existing.StartedAtUtc, true);
            }

            transaction.Create(reference, new Dictionary<string, object>
            {
                ["runId"] = runId,
                ["userId"] = authenticatedUserId,
                ["seed"] = (long)proposedSeed,
                ["drawCount"] = (long)drawCount,
                ["startIdempotencyKey"] = idempotencyKey,
                ["status"] = "started",
                ["startedAt"] = Timestamp.FromDateTime(startedAtUtc.UtcDateTime),
                ["schemaVersion"] = 1L,
            });
            return new SolitaireFreeRunStartResult(runId, proposedSeed, drawCount, startedAtUtc, false);
        }, cancellationToken: cancellationToken);
    }

    internal Task<SolitaireFreeRunCompletionResult> CompleteAsync(
        string runId,
        string authenticatedUserId,
        IReadOnlyList<SolitaireFreeReplayCommand> commands,
        CancellationToken cancellationToken)
    {
        ValidateUser(authenticatedUserId);
        ArgumentNullException.ThrowIfNull(commands);
        if (commands.Count > MaximumCommands)
            throw new ArgumentException($"A Solitaire replay may contain at most {MaximumCommands} commands.", nameof(commands));

        var normalized = commands.Select(Normalize).ToArray();
        var canonical = JsonSerializer.Serialize(normalized, CanonicalJson);
        var digest = Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(canonical)));
        var completedAtUtc = timeProvider.GetUtcNow();
        var reference = RunDocument(runId);

        return database.RunTransactionAsync(async transaction =>
        {
            var snapshot = await transaction.GetSnapshotAsync(reference, cancellationToken);
            var stored = ReadRun(snapshot, runId, authenticatedUserId);
            if (stored.Status == "completed")
            {
                if (stored.ReplayCanonical != canonical || stored.ReplayDigest != digest)
                    throw new InvalidOperationException("This Solitaire free run was already completed with a different replay.");
                return stored.ToCompletionResult(true);
            }
            if (stored.Status != "started")
                throw new InvalidOperationException("The Solitaire free run is not available for completion.");

            var game = SolitaireEngine.CreateGame(stored.Seed, stored.DrawCount);
            for (var index = 0; index < normalized.Length; index++)
            {
                var command = normalized[index];
                try
                {
                    game = SolitaireEngine.Apply(game, new SolitaireCommandRequest(
                        command.Type, index + 1, command.From, command.StartIndex, command.To, command.Column));
                }
                catch (SolitaireIllegalMoveException exception)
                {
                    throw new ArgumentException($"Replay command {index + 1} is illegal: {exception.Message}", nameof(commands), exception);
                }
            }

            var terminal = SolitaireEngine.IsWon(game) ? "won" : "submitted";
            var elapsedMilliseconds = Math.Max(0, (long)(completedAtUtc - stored.StartedAtUtc).TotalMilliseconds);
            transaction.Set(reference, new Dictionary<string, object>
            {
                ["status"] = "completed",
                ["replayCanonical"] = canonical,
                ["replayDigest"] = digest,
                ["score"] = (long)game.Score,
                ["moves"] = (long)game.Moves,
                ["elapsedMilliseconds"] = elapsedMilliseconds,
                ["terminal"] = terminal,
                ["completedAt"] = Timestamp.FromDateTime(completedAtUtc.UtcDateTime),
            }, SetOptions.MergeAll);
            return new SolitaireFreeRunCompletionResult(
                runId, stored.Seed, stored.DrawCount, game.Score, game.Moves,
                elapsedMilliseconds, terminal, completedAtUtc, false);
        }, cancellationToken: cancellationToken);
    }

    private static SolitaireFreeReplayCommand Normalize(SolitaireFreeReplayCommand command)
    {
        ArgumentNullException.ThrowIfNull(command);
        var type = command.Type?.Trim().ToLowerInvariant();
        if (type is not (SolitaireCommandTypes.Draw or SolitaireCommandTypes.Flip or SolitaireCommandTypes.Move))
            throw new ArgumentException("Free-run replays accept only draw, flip, and move commands.", nameof(command));
        return command with
        {
            Type = type,
            From = NormalizePile(command.From),
            To = NormalizePile(command.To),
        };
    }

    private static SolitairePileReference? NormalizePile(SolitairePileReference? pile) => pile is null
        ? null
        : pile with { Zone = pile.Zone?.Trim().ToLowerInvariant() ?? string.Empty };

    private DocumentReference RunDocument(string runId)
    {
        if (string.IsNullOrWhiteSpace(runId) || !runId.StartsWith(RunIdPrefix, StringComparison.Ordinal))
            throw new ArgumentException("The Solitaire free-run id is invalid.", nameof(runId));
        var documentId = runId[RunIdPrefix.Length..];
        if (documentId.Length != 64 || documentId.Any(character => !Uri.IsHexDigit(character)))
            throw new ArgumentException("The Solitaire free-run id is invalid.", nameof(runId));
        return database.Collection(CollectionName).Document(documentId);
    }

    private static StoredFreeRun ReadRun(DocumentSnapshot snapshot, string runId, string userId)
    {
        if (!snapshot.Exists ||
            !snapshot.TryGetValue<string>("runId", out var storedRunId) || storedRunId != runId ||
            !snapshot.TryGetValue<string>("userId", out var storedUserId) || storedUserId != userId ||
            !snapshot.TryGetValue<long>("seed", out var seedValue) || seedValue is <= 0 or > uint.MaxValue ||
            !snapshot.TryGetValue<long>("drawCount", out var drawCountValue) || drawCountValue is not (1 or 3) ||
            !snapshot.TryGetValue<string>("status", out var status) || status is not ("started" or "completed") ||
            !snapshot.TryGetValue<Timestamp>("startedAt", out var startedAt) ||
            !snapshot.TryGetValue<long>("schemaVersion", out var schemaVersion) || schemaVersion != 1)
            throw new InvalidOperationException("The Solitaire free run is missing, belongs to another user, or is corrupt.");

        var baseRun = new StoredFreeRun(
            runId, (uint)seedValue, (int)drawCountValue, status, new DateTimeOffset(startedAt.ToDateTime()),
            string.Empty, string.Empty, 0, 0, 0, "", null);
        if (status == "started") return baseRun;

        if (!snapshot.TryGetValue<string>("replayCanonical", out var canonical) || string.IsNullOrWhiteSpace(canonical) ||
            !snapshot.TryGetValue<string>("replayDigest", out var digest) || !DigestPattern.IsMatch(digest) ||
            !snapshot.TryGetValue<long>("score", out var score) || score < 0 || score > int.MaxValue ||
            !snapshot.TryGetValue<long>("moves", out var moves) || moves < 0 || moves > int.MaxValue ||
            !snapshot.TryGetValue<long>("elapsedMilliseconds", out var elapsed) || elapsed < 0 ||
            !snapshot.TryGetValue<string>("terminal", out var terminal) || terminal is not ("won" or "submitted") ||
            !snapshot.TryGetValue<Timestamp>("completedAt", out var completedAt))
            throw new InvalidOperationException("The completed Solitaire free run is corrupt.");

        return baseRun with
        {
            ReplayCanonical = canonical,
            ReplayDigest = digest,
            Score = (int)score,
            Moves = (int)moves,
            ElapsedMilliseconds = elapsed,
            Terminal = terminal,
            CompletedAtUtc = new DateTimeOffset(completedAt.ToDateTime()),
        };
    }

    private static void ValidateUser(string userId)
    {
        if (string.IsNullOrWhiteSpace(userId))
            throw new ArgumentException("An authenticated user is required.", nameof(userId));
    }

    private static string DocumentId(string userId, string idempotencyKey) =>
        Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes($"v1\n{userId}\n{idempotencyKey}")));

    private static uint CreateCryptographicNonZeroSeed()
    {
        Span<byte> bytes = stackalloc byte[sizeof(uint)];
        uint seed;
        do
        {
            RandomNumberGenerator.Fill(bytes);
            seed = BitConverter.ToUInt32(bytes);
        } while (seed == 0);
        return seed;
    }

    private sealed record StoredFreeRun(
        string RunId,
        uint Seed,
        int DrawCount,
        string Status,
        DateTimeOffset StartedAtUtc,
        string ReplayCanonical,
        string ReplayDigest,
        int Score,
        int Moves,
        long ElapsedMilliseconds,
        string Terminal,
        DateTimeOffset? CompletedAtUtc)
    {
        internal SolitaireFreeRunCompletionResult ToCompletionResult(bool replayed) => new(
            RunId, Seed, DrawCount, Score, Moves, ElapsedMilliseconds, Terminal,
            CompletedAtUtc ?? throw new InvalidOperationException("The completed Solitaire free run has no completion time."),
            replayed);
    }
}

public sealed record SolitaireFreeReplayCommand(
    string Type,
    SolitairePileReference? From,
    int? StartIndex,
    SolitairePileReference? To,
    int? Column);

internal sealed record SolitaireFreeRunStartResult(
    string RunId,
    uint Seed,
    int DrawCount,
    DateTimeOffset StartedAtUtc,
    bool WasAlreadyStarted);

internal sealed record SolitaireFreeRunCompletionResult(
    string RunId,
    uint Seed,
    int DrawCount,
    int Score,
    int Moves,
    long ElapsedMilliseconds,
    string Terminal,
    DateTimeOffset CompletedAtUtc,
    bool WasAlreadyCompleted);
