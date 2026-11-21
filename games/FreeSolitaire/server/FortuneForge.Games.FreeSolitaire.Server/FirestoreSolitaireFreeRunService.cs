using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using Google.Cloud.Firestore;
using FortuneForge.Games.Abstractions.Analytics;
using FortuneForge.Games.Solitaire;
using FortuneForge.Games.Firestore;
using FortuneForge.Games.Abstractions.Auditing;

namespace FortuneForge.Games.FreeSolitaire.Server;

/// <summary>
/// Persists authenticated free-play runs without touching competitive or financial data.
/// </summary>
public sealed class FirestoreSolitaireFreeRunService
{
    public const string CollectionName = SolitaireFreeRunIdentity.CollectionName;
    private const string RunIdPrefix = SolitaireFreeRunIdentity.RunIdPrefix;
    public const string LegacyGameId = SolitaireFreeRunIdentity.LegacyGameId;
    public const string StandaloneGameId = SolitaireFreeRunIdentity.StandaloneGameId;
    private static readonly Regex IdempotencyKeyPattern = new("^[A-Za-z0-9_-]{16,128}$", RegexOptions.CultureInvariant);
    private static readonly Regex DigestPattern = new("^[0-9a-f]{64}$", RegexOptions.CultureInvariant);
    private static readonly JsonSerializerOptions CanonicalJson = new(JsonSerializerDefaults.Web);
    private readonly FirestoreDb database;
    private readonly TimeProvider timeProvider;
    private readonly Func<uint> createSeed;

    public FirestoreSolitaireFreeRunService(
        FirestoreDb database,
        TimeProvider timeProvider,
        Func<uint>? createSeed = null)
    {
        this.database = database ?? throw new ArgumentNullException(nameof(database));
        this.timeProvider = timeProvider ?? throw new ArgumentNullException(nameof(timeProvider));
        this.createSeed = createSeed ?? FreeSolitaireSeeds.Create;
    }

    public Task<SolitaireFreeRunStartResult> StartAsync(
        string idempotencyKey,
        string authenticatedUserId,
        int drawCount,
        CancellationToken cancellationToken,
        string? replayRunId = null,
        string gameId = LegacyGameId)
    {
        ValidateUser(authenticatedUserId);
        ValidateGameId(gameId);
        SolitaireRules.ValidateDrawCount(drawCount);
        if (string.IsNullOrWhiteSpace(idempotencyKey) || !IdempotencyKeyPattern.IsMatch(idempotencyKey))
            throw new ArgumentException("The idempotency key is invalid.", nameof(idempotencyKey));
        if (replayRunId is not null && string.IsNullOrWhiteSpace(replayRunId))
            throw new ArgumentException("The replay run id is invalid.", nameof(replayRunId));

        var documentId = DocumentId(authenticatedUserId, idempotencyKey);
        var runId = $"{RunIdPrefix}{documentId}";
        var startedAtUtc = timeProvider.GetUtcNow();
        uint? proposedSeed = null;
        var reference = database.Collection(CollectionName).Document(documentId);
        var replayReference = replayRunId is null ? null : RunDocument(replayRunId);

        return database.RunTransactionAsync(async transaction =>
        {
            var snapshot = await transaction.GetSnapshotAsync(reference, cancellationToken);
            if (snapshot.Exists)
            {
                var existing = ReadRun(snapshot, runId, authenticatedUserId);
                if (!snapshot.TryGetValue<string>("startIdempotencyKey", out var storedKey) || storedKey != idempotencyKey ||
                    existing.DrawCount != drawCount ||
                    existing.GameId != gameId ||
                    (snapshot.TryGetValue<string>("replayRunId", out var storedReplayRunId) ? storedReplayRunId : null) != replayRunId)
                    throw new InvalidOperationException("The Solitaire free-run idempotency record conflicts with this request.");
                return new SolitaireFreeRunStartResult(
                    existing.RunId, existing.Seed, existing.DrawCount, existing.StartedAtUtc, true, existing.GameId);
            }

            if (replayReference is not null)
            {
                var sourceSnapshot = await transaction.GetSnapshotAsync(replayReference, cancellationToken);
                var source = ReadRun(sourceSnapshot, replayRunId!, authenticatedUserId);
                if (source.Status != "completed")
                    throw new InvalidOperationException("Only a completed Solitaire free run can be replayed.");
                if (source.DrawCount != drawCount)
                    throw new InvalidOperationException("The Solitaire replay draw count must match the original run.");
                if (source.GameId != gameId)
                    throw new InvalidOperationException("The Solitaire replay must stay in the same game.");
                proposedSeed = source.Seed;
            }

            proposedSeed ??= createSeed();
            if (proposedSeed == 0)
                throw new InvalidOperationException("The Solitaire seed source returned zero.");
            var game = SolitaireEngine.CreateGame(proposedSeed.Value, drawCount);

            var document = new Dictionary<string, object>
            {
                ["runId"] = runId,
                ["userId"] = authenticatedUserId,
                ["seed"] = (long)proposedSeed.Value,
                ["drawCount"] = (long)drawCount,
                ["gameId"] = gameId,
                ["startIdempotencyKey"] = idempotencyKey,
                ["status"] = "started",
                ["startedAt"] = Timestamp.FromDateTime(startedAtUtc.UtcDateTime),
                ["schemaVersion"] = 1L,
            };
            if (replayRunId is not null) document["replayRunId"] = replayRunId;
            transaction.Create(reference, document);
            FirestoreGameJournal.Write(transaction, database, gameId, runId,
                0, startedAtUtc.UtcDateTime, new { seed = proposedSeed, drawCount, replayRunId, game },
                [authenticatedUserId], "free-run-start");
            return new SolitaireFreeRunStartResult(runId, proposedSeed.Value, drawCount, startedAtUtc, false, gameId);
        }, cancellationToken: cancellationToken);
    }

    public Task<SolitaireFreeRunCompletionResult> CompleteAsync(
        string runId,
        string authenticatedUserId,
        IReadOnlyList<SolitaireFreeReplayCommand> commands,
        CancellationToken cancellationToken)
    {
        ValidateUser(authenticatedUserId);
        ArgumentNullException.ThrowIfNull(commands);
        if (commands.Count > SolitaireBoardCompute.MaximumReplayCommands)
            throw new ArgumentException($"A Solitaire replay may contain at most {SolitaireBoardCompute.MaximumReplayCommands} commands.", nameof(commands));

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

            var elapsedMilliseconds = ActiveElapsedMilliseconds(stored, completedAtUtc);
            var replay = new SolitaireBoardReplayRequest(stored.Seed, stored.DrawCount, normalized
                .Select((command, index) => new SolitaireCommandRequest(
                    command.Type, index + 1, command.From, command.StartIndex, command.To, command.Column)).ToArray(),
                elapsedMilliseconds);
            var outcome = SolitaireBoardCompute.Replay(replay);
            var game = outcome.PrivateState;
            var terminal = outcome.Won ? "won" : "submitted";
            var score = outcome.Score;
            FirestoreGameJournal.Write(transaction, database, stored.GameId, runId,
                game.Moves, completedAtUtc.UtcDateTime, new { stored, game, commands = normalized, replayDigest = digest, score, elapsedMilliseconds, terminal },
                [authenticatedUserId], "free-replay-complete");
            transaction.Set(reference, new Dictionary<string, object>
            {
                ["status"] = "completed",
                ["replayCanonical"] = canonical,
                ["replayDigest"] = digest,
                ["score"] = (long)score,
                ["moves"] = (long)game.Moves,
                ["elapsedMilliseconds"] = elapsedMilliseconds,
                ["terminal"] = terminal,
                ["isPaused"] = false,
                ["pausedMilliseconds"] = TotalPausedMilliseconds(stored, completedAtUtc),
                ["completedAt"] = Timestamp.FromDateTime(completedAtUtc.UtcDateTime),
            }, SetOptions.MergeAll);
            if (game.Moves > 0)
            {
                var analytics = GameAnalyticsOutbox.Create(new GameAnalyticsSettlement(
                    stored.GameId, runId, authenticatedUserId, "free", true, completedAtUtc.UtcDateTime,
                    stored.StartedAtUtc.UtcDateTime, 0, 0, CollectionName, reference.Id))!;
                transaction.Set(database.Collection(GameAnalyticsOutbox.CollectionName).Document(analytics.Id), analytics.Fields);
                FirestoreGameAudit.WriteAcceptedParticipation(transaction, database, stored.GameId,
                    authenticatedUserId, completedAtUtc.UtcDateTime);
            }
            return new SolitaireFreeRunCompletionResult(
                runId, stored.Seed, stored.DrawCount, score, game.Moves,
                elapsedMilliseconds, terminal, completedAtUtc, false, stored.GameId);
        }, cancellationToken: cancellationToken);
    }

    public async Task<SolitaireFreeRunStateResult> GetAsync(
        string runId, string authenticatedUserId, CancellationToken cancellationToken)
    {
        ValidateUser(authenticatedUserId);
        var stored = ReadRun(await RunDocument(runId).GetSnapshotAsync(cancellationToken), runId, authenticatedUserId);
        IReadOnlyList<SolitaireFreeReplayCommand>? commands = null;
        if (stored.Status == "completed")
        {
            if (GameSecurityEventDocuments.Hash(stored.ReplayCanonical) != stored.ReplayDigest)
                throw new InvalidOperationException("The completed Solitaire replay digest is corrupt.");
            try
            {
                commands = JsonSerializer.Deserialize<SolitaireFreeReplayCommand[]>(stored.ReplayCanonical, CanonicalJson)
                    ?? throw new JsonException("The completed replay is missing.");
            }
            catch (JsonException exception)
            {
                throw new InvalidOperationException("The completed Solitaire replay is corrupt.", exception);
            }
        }
        return new(stored.RunId, stored.Seed, stored.DrawCount, stored.GameId, stored.Status, stored.StartedAtUtc,
            stored.IsPaused, stored.Status == "completed" ? stored.ElapsedMilliseconds
                : ActiveElapsedMilliseconds(stored, timeProvider.GetUtcNow()),
            stored.Status == "completed" ? stored.ToCompletionResult(true) : null, commands);
    }

    public Task<SolitaireFreeRunPauseResult> PauseAsync(
        string runId,
        string authenticatedUserId,
        CancellationToken cancellationToken) =>
        SetPausedAsync(runId, authenticatedUserId, true, cancellationToken);

    public Task<SolitaireFreeRunPauseResult> ResumeAsync(
        string runId,
        string authenticatedUserId,
        CancellationToken cancellationToken) =>
        SetPausedAsync(runId, authenticatedUserId, false, cancellationToken);

    private Task<SolitaireFreeRunPauseResult> SetPausedAsync(
        string runId,
        string authenticatedUserId,
        bool pause,
        CancellationToken cancellationToken)
    {
        ValidateUser(authenticatedUserId);
        var changedAtUtc = timeProvider.GetUtcNow();
        var reference = RunDocument(runId);
        return database.RunTransactionAsync(async transaction =>
        {
            var snapshot = await transaction.GetSnapshotAsync(reference, cancellationToken);
            var stored = ReadRun(snapshot, runId, authenticatedUserId);
            if (stored.Status != "started")
                throw new InvalidOperationException("The Solitaire free run is not available for pause or resume.");
            if (stored.IsPaused == pause)
                return new SolitaireFreeRunPauseResult(runId, pause, ActiveElapsedMilliseconds(stored, changedAtUtc));

            var pausedMilliseconds = pause
                ? stored.PausedMilliseconds
                : TotalPausedMilliseconds(stored, changedAtUtc);
            transaction.Set(reference, new Dictionary<string, object>
            {
                ["isPaused"] = pause,
                ["pausedMilliseconds"] = pausedMilliseconds,
                ["pauseStartedAt"] = Timestamp.FromDateTime(changedAtUtc.UtcDateTime),
            }, SetOptions.MergeAll);
            var updated = stored with
            {
                IsPaused = pause,
                PausedMilliseconds = pausedMilliseconds,
                PauseStartedAtUtc = pause ? changedAtUtc : null,
            };
            FirestoreGameJournal.Write(transaction, database, stored.GameId, runId,
                0, changedAtUtc.UtcDateTime, updated, [authenticatedUserId], pause ? "free-run-pause" : "free-run-resume");
            return new SolitaireFreeRunPauseResult(runId, pause, ActiveElapsedMilliseconds(updated, changedAtUtc));
        }, cancellationToken: cancellationToken);
    }

    private static long ActiveElapsedMilliseconds(StoredFreeRun run, DateTimeOffset now) =>
        Math.Max(0, (long)(now - run.StartedAtUtc).TotalMilliseconds - TotalPausedMilliseconds(run, now));

    private static long TotalPausedMilliseconds(StoredFreeRun run, DateTimeOffset now) =>
        run.PausedMilliseconds + (run.IsPaused && run.PauseStartedAtUtc is { } pauseStartedAt
            ? Math.Max(0, (long)(now - pauseStartedAt).TotalMilliseconds)
            : 0);

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

        var gameId = snapshot.TryGetValue<string>("gameId", out var storedGameId)
            ? storedGameId : LegacyGameId;
        if (gameId is not (LegacyGameId or StandaloneGameId))
            throw new InvalidOperationException("The Solitaire free-run game identity is corrupt.");
        var baseRun = new StoredFreeRun(
            runId, (uint)seedValue, (int)drawCountValue, status, new DateTimeOffset(startedAt.ToDateTime()),
            string.Empty, string.Empty, 0, 0, 0, "", null, 0, false, null, gameId);
        // Older free-run documents predate pause tracking and remain fully playable.
        var pausedMilliseconds = snapshot.TryGetValue<long>("pausedMilliseconds", out var storedPausedMilliseconds)
            ? storedPausedMilliseconds : 0;
        var isPaused = snapshot.TryGetValue<bool>("isPaused", out var storedIsPaused) && storedIsPaused;
        DateTimeOffset? pauseStartedAtUtc = snapshot.TryGetValue<Timestamp>("pauseStartedAt", out var pauseStartedAt)
            ? new DateTimeOffset(pauseStartedAt.ToDateTime()) : null;
        if (pausedMilliseconds < 0 || (isPaused && pauseStartedAtUtc is null) || (status == "completed" && isPaused))
            throw new InvalidOperationException("The Solitaire free-run pause state is corrupt.");
        baseRun = baseRun with
        {
            PausedMilliseconds = pausedMilliseconds,
            IsPaused = isPaused,
            PauseStartedAtUtc = isPaused ? pauseStartedAtUtc : null,
        };
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

    private static void ValidateGameId(string gameId)
    {
        if (gameId is not (LegacyGameId or StandaloneGameId))
            throw new ArgumentException("The Solitaire free-run game is invalid.", nameof(gameId));
    }

    private static string DocumentId(string userId, string idempotencyKey) =>
        Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes($"v1\n{userId}\n{idempotencyKey}")));

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
        DateTimeOffset? CompletedAtUtc,
        long PausedMilliseconds,
        bool IsPaused,
        DateTimeOffset? PauseStartedAtUtc,
        string GameId)
    {
        internal SolitaireFreeRunCompletionResult ToCompletionResult(bool replayed) => new(
            RunId, Seed, DrawCount, Score, Moves, ElapsedMilliseconds, Terminal,
            CompletedAtUtc ?? throw new InvalidOperationException("The completed Solitaire free run has no completion time."),
            replayed, GameId);
    }
}
