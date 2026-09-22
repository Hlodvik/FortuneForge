using System.Security.Cryptography;
using System.Text;
using Google.Cloud.Firestore;

namespace FortuneForge.Server.Arcade.Competition;

internal sealed record ArcadeCompetitionIdentity
{
    public ArcadeCompetitionIdentity(
        string gameId,
        ArcadeCompetitionWindowKind windowKind,
        DateTimeOffset startsAtUtc,
        DateTimeOffset endsAtUtc)
    {
        if (string.IsNullOrWhiteSpace(gameId)) throw new ArgumentException("A competition game id is required.", nameof(gameId));
        if (startsAtUtc.Offset != TimeSpan.Zero) throw new ArgumentException("Competition starts must be UTC.", nameof(startsAtUtc));
        if (endsAtUtc.Offset != TimeSpan.Zero) throw new ArgumentException("Competition ends must be UTC.", nameof(endsAtUtc));
        if (endsAtUtc <= startsAtUtc) throw new ArgumentException("Competition end must be after its start.", nameof(endsAtUtc));

        GameId = gameId;
        WindowKind = windowKind;
        StartsAtUtc = startsAtUtc;
        EndsAtUtc = endsAtUtc;
    }

    public string GameId { get; }
    public ArcadeCompetitionWindowKind WindowKind { get; }
    public DateTimeOffset StartsAtUtc { get; }
    public DateTimeOffset EndsAtUtc { get; }
    public string DocumentId => ArcadeCompetitionFirestoreDocuments.CompetitionDocumentId(this);
}

internal sealed record ArcadeCompetitionAttemptStart
{
    public ArcadeCompetitionAttemptStart(
        string attemptId,
        ArcadeCompetitionIdentity competition,
        string playerId,
        long entryFeeCents,
        DateTimeOffset enteredAtUtc)
    {
        if (string.IsNullOrWhiteSpace(attemptId)) throw new ArgumentException("An attempt id is required.", nameof(attemptId));
        ArgumentNullException.ThrowIfNull(competition);
        if (string.IsNullOrWhiteSpace(playerId)) throw new ArgumentException("A player id is required.", nameof(playerId));
        if (entryFeeCents <= 0) throw new ArgumentOutOfRangeException(nameof(entryFeeCents));
        ValidateWindowTime(enteredAtUtc, competition, nameof(enteredAtUtc), "Attempt entry time");

        AttemptId = attemptId;
        Competition = competition;
        PlayerId = playerId;
        EntryFeeCents = entryFeeCents;
        EnteredAtUtc = enteredAtUtc;
    }

    public string AttemptId { get; }
    public ArcadeCompetitionIdentity Competition { get; }
    public string PlayerId { get; }
    public long EntryFeeCents { get; }
    public DateTimeOffset EnteredAtUtc { get; }

    private static void ValidateWindowTime(DateTimeOffset value, ArcadeCompetitionIdentity competition, string parameterName, string label)
    {
        if (value.Offset != TimeSpan.Zero) throw new ArgumentException($"{label} must be UTC.", parameterName);
        if (value < competition.StartsAtUtc || value >= competition.EndsAtUtc)
            throw new ArgumentOutOfRangeException(parameterName, $"{label} must fall within its competition window.");
    }
}

internal sealed record ArcadeCompetitionAttemptCompletion
{
    public ArcadeCompetitionAttemptCompletion(
        string attemptId,
        ArcadeCompetitionIdentity competition,
        string playerId,
        long score,
        DateTimeOffset completedAtUtc)
    {
        if (string.IsNullOrWhiteSpace(attemptId)) throw new ArgumentException("An attempt id is required.", nameof(attemptId));
        ArgumentNullException.ThrowIfNull(competition);
        if (string.IsNullOrWhiteSpace(playerId)) throw new ArgumentException("A player id is required.", nameof(playerId));
        if (score < 0) throw new ArgumentOutOfRangeException(nameof(score));
        if (completedAtUtc.Offset != TimeSpan.Zero) throw new ArgumentException("Attempt completion time must be UTC.", nameof(completedAtUtc));
        if (completedAtUtc < competition.StartsAtUtc || completedAtUtc >= competition.EndsAtUtc)
            throw new ArgumentOutOfRangeException(nameof(completedAtUtc), "Attempt completion time must fall before the competition cutoff.");

        AttemptId = attemptId;
        Competition = competition;
        PlayerId = playerId;
        Score = score;
        CompletedAtUtc = completedAtUtc;
    }

    public string AttemptId { get; }
    public ArcadeCompetitionIdentity Competition { get; }
    public string PlayerId { get; }
    public long Score { get; }
    public DateTimeOffset CompletedAtUtc { get; }
}

internal sealed record ArcadeCompetitionAttemptRecord
{
    private ArcadeCompetitionAttemptRecord(
        string attemptId,
        ArcadeCompetitionIdentity competition,
        string playerId,
        long entryFeeCents,
        DateTimeOffset enteredAtUtc,
        long? score,
        DateTimeOffset? completedAtUtc)
    {
        AttemptId = attemptId;
        Competition = competition;
        PlayerId = playerId;
        EntryFeeCents = entryFeeCents;
        EnteredAtUtc = enteredAtUtc;
        Score = score;
        CompletedAtUtc = completedAtUtc;
    }

    public string AttemptId { get; }
    public ArcadeCompetitionIdentity Competition { get; }
    public string PlayerId { get; }
    public long EntryFeeCents { get; }
    public DateTimeOffset EnteredAtUtc { get; }
    public long? Score { get; }
    public DateTimeOffset? CompletedAtUtc { get; }
    public bool IsCompleted => CompletedAtUtc is not null;
    public string DocumentId => ArcadeCompetitionFirestoreDocuments.AttemptDocumentId(this);

    public static ArcadeCompetitionAttemptRecord Start(ArcadeCompetitionAttemptStart start)
    {
        ArgumentNullException.ThrowIfNull(start);
        return new ArcadeCompetitionAttemptRecord(
            start.AttemptId, start.Competition, start.PlayerId, start.EntryFeeCents, start.EnteredAtUtc, null, null);
    }

    public ArcadeCompetitionAttemptRecord Complete(ArcadeCompetitionAttemptCompletion completion)
    {
        ArgumentNullException.ThrowIfNull(completion);
        if (IsCompleted) throw new InvalidOperationException("This arcade competition attempt is already completed.");
        if (completion.AttemptId != AttemptId || completion.Competition.DocumentId != Competition.DocumentId || completion.PlayerId != PlayerId)
            throw new InvalidOperationException("Attempt completion must match the existing competition, attempt, and player.");
        if (completion.CompletedAtUtc < EnteredAtUtc)
            throw new ArgumentOutOfRangeException(nameof(completion), "Attempt completion cannot precede entry.");
        return new ArcadeCompetitionAttemptRecord(
            AttemptId, Competition, PlayerId, EntryFeeCents, EnteredAtUtc, completion.Score, completion.CompletedAtUtc);
    }

    internal static ArcadeCompetitionAttemptRecord ReadCompletedLegacy(
        string attemptId, ArcadeCompetitionIdentity competition, string playerId, long score, long entryFeeCents, DateTimeOffset submittedAtUtc) =>
        new ArcadeCompetitionAttemptRecord(attemptId, competition, playerId, entryFeeCents, submittedAtUtc, score, submittedAtUtc);
}

internal sealed record ArcadeCompetitionStartAttemptResult(ArcadeCompetitionAttemptRecord Attempt, bool WasAlreadyRecorded);
internal sealed record ArcadeCompetitionCompleteAttemptResult(ArcadeCompetitionAttemptRecord Attempt, bool WasAlreadyCompleted);

internal sealed record ArcadeCompetitionSettlementState
{
    public ArcadeCompetitionSettlementState(ArcadeCompetitionIdentity competition, DateTimeOffset? completedAtUtc)
        : this(competition, null, null, null, completedAtUtc)
    {
    }

    public ArcadeCompetitionSettlementState(
        ArcadeCompetitionIdentity competition,
        ArcadeCompetitionPayoutPlan? payoutPlan,
        DateTimeOffset? createdAtUtc,
        DateTimeOffset? updatedAtUtc,
        DateTimeOffset? completedAtUtc)
    {
        ArgumentNullException.ThrowIfNull(competition);
        if (payoutPlan is not null && payoutPlan.Competition != competition)
            throw new ArgumentException("A settlement plan must match its competition.", nameof(payoutPlan));
        if (payoutPlan is null && (createdAtUtc is not null || updatedAtUtc is not null))
            throw new ArgumentException("An unplanned settlement cannot have plan timestamps.", nameof(payoutPlan));
        if (payoutPlan is not null && (createdAtUtc is null || updatedAtUtc is null))
            throw new ArgumentException("A stored settlement plan requires created and updated timestamps.", nameof(createdAtUtc));
        if (createdAtUtc is not null && createdAtUtc.Value.Offset != TimeSpan.Zero)
            throw new ArgumentException("Settlement creation times must be UTC.", nameof(createdAtUtc));
        if (updatedAtUtc is not null && updatedAtUtc.Value.Offset != TimeSpan.Zero)
            throw new ArgumentException("Settlement update times must be UTC.", nameof(updatedAtUtc));
        if (completedAtUtc is not null && completedAtUtc.Value.Offset != TimeSpan.Zero)
            throw new ArgumentException("Settlement completion times must be UTC.", nameof(completedAtUtc));
        if (createdAtUtc is not null && updatedAtUtc < createdAtUtc)
            throw new ArgumentException("Settlement update time cannot precede creation.", nameof(updatedAtUtc));
        if (updatedAtUtc is not null && completedAtUtc is not null && completedAtUtc < updatedAtUtc)
            throw new ArgumentException("Settlement completion time cannot precede its update time.", nameof(completedAtUtc));

        Competition = competition;
        PayoutPlan = payoutPlan;
        CreatedAtUtc = createdAtUtc;
        UpdatedAtUtc = updatedAtUtc;
        CompletedAtUtc = completedAtUtc;
    }

    public ArcadeCompetitionIdentity Competition { get; }
    public ArcadeCompetitionPayoutPlan? PayoutPlan { get; }
    public DateTimeOffset? CreatedAtUtc { get; }
    public DateTimeOffset? UpdatedAtUtc { get; }
    public DateTimeOffset? CompletedAtUtc { get; }
    public bool HasStoredPlan => PayoutPlan is not null;
    public bool IsCompleted => CompletedAtUtc is not null;
}

/// <summary>Persistence boundary only. Implementations must not debit, credit, refund, or pay wallets.</summary>
internal interface IArcadeCompetitionStore
{
    Task<ArcadeCompetitionStartAttemptResult> StartAttemptAsync(ArcadeCompetitionAttemptStart attempt, CancellationToken cancellationToken);
    Task<ArcadeCompetitionCompleteAttemptResult> CompleteAttemptAsync(ArcadeCompetitionAttemptCompletion completion, CancellationToken cancellationToken);
    Task<IReadOnlyList<ArcadeCompetitionAttemptRecord>> LoadAttemptsAsync(ArcadeCompetitionIdentity competition, CancellationToken cancellationToken);
    Task<IReadOnlyList<ArcadeCompetitionAttemptRecord>> LoadAttemptsForGameAsync(string gameId, CancellationToken cancellationToken);
    Task<IReadOnlyList<ArcadeCompetitionIdentity>> LoadDueSettlementCompetitionsAsync(DateTimeOffset cutoffUtc, CancellationToken cancellationToken);
    Task<ArcadeCompetitionSettlementState> ReadSettlementStateAsync(ArcadeCompetitionIdentity competition, CancellationToken cancellationToken);
    Task<ArcadeCompetitionSettlementState> StoreSettlementPlanAsync(ArcadeCompetitionPayoutPlan plan, DateTimeOffset createdAtUtc, CancellationToken cancellationToken);
    Task<ArcadeCompetitionSettlementState> MarkSettlementCompletedAsync(ArcadeCompetitionIdentity competition, DateTimeOffset completedAtUtc, CancellationToken cancellationToken);
}

internal static class ArcadeCompetitionFirestoreDocuments
{
    public const string CompetitionsCollection = "arcadeCompetitions";
    public const string AttemptsCollection = "arcadeCompetitionAttempts";
    public const string SettlementsCollection = "arcadeCompetitionSettlements";

    public static string CompetitionDocumentId(ArcadeCompetitionIdentity identity)
    {
        ArgumentNullException.ThrowIfNull(identity);
        return HashId($"v1\n{identity.GameId}\n{identity.WindowKind}\n{identity.StartsAtUtc.UtcTicks}\n{identity.EndsAtUtc.UtcTicks}");
    }

    public static string AttemptDocumentId(ArcadeCompetitionAttemptRecord attempt)
    {
        ArgumentNullException.ThrowIfNull(attempt);
        return AttemptDocumentId(attempt.Competition, attempt.AttemptId);
    }

    public static string AttemptDocumentId(ArcadeCompetitionIdentity competition, string attemptId)
    {
        ArgumentNullException.ThrowIfNull(competition);
        if (string.IsNullOrWhiteSpace(attemptId)) throw new ArgumentException("An attempt id is required.", nameof(attemptId));
        return HashId($"v1\n{competition.DocumentId}\n{attemptId}");
    }

    public static string SettlementDocumentId(ArcadeCompetitionIdentity identity) => CompetitionDocumentId(identity);

    public static Dictionary<string, object> CompetitionData(ArcadeCompetitionIdentity identity) => new()
    {
        ["gameId"] = identity.GameId,
        ["windowKind"] = identity.WindowKind.ToString().ToLowerInvariant(),
        ["startsAt"] = Timestamp.FromDateTime(identity.StartsAtUtc.UtcDateTime),
        ["endsAt"] = Timestamp.FromDateTime(identity.EndsAtUtc.UtcDateTime),
        ["settlementStatus"] = "pending",
        ["schemaVersion"] = 2L,
    };

    public static Dictionary<string, object> AttemptData(ArcadeCompetitionAttemptRecord attempt) => new()
    {
        ["attemptId"] = attempt.AttemptId,
        ["competitionId"] = attempt.Competition.DocumentId,
        ["gameId"] = attempt.Competition.GameId,
        ["windowKind"] = attempt.Competition.WindowKind.ToString().ToLowerInvariant(),
        ["startsAt"] = Timestamp.FromDateTime(attempt.Competition.StartsAtUtc.UtcDateTime),
        ["endsAt"] = Timestamp.FromDateTime(attempt.Competition.EndsAtUtc.UtcDateTime),
        ["playerId"] = attempt.PlayerId,
        ["entryFeeCents"] = attempt.EntryFeeCents,
        ["status"] = attempt.IsCompleted ? "completed" : "started",
        ["enteredAt"] = Timestamp.FromDateTime(attempt.EnteredAtUtc.UtcDateTime),
        ["score"] = attempt.Score is null ? null! : attempt.Score.Value,
        ["completedAt"] = attempt.CompletedAtUtc is null ? null! : Timestamp.FromDateTime(attempt.CompletedAtUtc.Value.UtcDateTime),
        ["schemaVersion"] = 2L,
    };

    public static Dictionary<string, object> SettlementData(ArcadeCompetitionSettlementState state)
    {
        ArgumentNullException.ThrowIfNull(state);
        if (state.PayoutPlan is null)
        {
            return new Dictionary<string, object>
            {
                ["competitionId"] = state.Competition.DocumentId,
                ["status"] = state.IsCompleted ? "completed" : "pending",
                ["completedAt"] = state.CompletedAtUtc is null ? null! : Timestamp.FromDateTime(state.CompletedAtUtc.Value.UtcDateTime),
                ["schemaVersion"] = 1L,
            };
        }

        return new Dictionary<string, object>
        {
            ["competitionId"] = state.Competition.DocumentId,
            ["gameId"] = state.Competition.GameId,
            ["windowKind"] = state.Competition.WindowKind.ToString().ToLowerInvariant(),
            ["startsAt"] = Timestamp.FromDateTime(state.Competition.StartsAtUtc.UtcDateTime),
            ["endsAt"] = Timestamp.FromDateTime(state.Competition.EndsAtUtc.UtcDateTime),
            ["visibleJackpotCents"] = state.PayoutPlan.VisibleJackpotCents,
            ["houseCutCents"] = state.PayoutPlan.HouseCutCents,
            ["instructions"] = state.PayoutPlan.Instructions
                .Select(static instruction => (object)new Dictionary<string, object>
                {
                    ["kind"] = instruction.Kind.ToString().ToLowerInvariant(),
                    ["playerId"] = instruction.PlayerId,
                    ["placement"] = instruction.Placement is null ? null! : (long)instruction.Placement.Value,
                    ["amountCents"] = instruction.AmountCents,
                    ["idempotencyKey"] = instruction.IdempotencyKey,
                })
                .ToArray(),
            ["status"] = state.IsCompleted ? "completed" : "pending",
            ["createdAt"] = Timestamp.FromDateTime(state.CreatedAtUtc!.Value.UtcDateTime),
            ["updatedAt"] = Timestamp.FromDateTime(state.UpdatedAtUtc!.Value.UtcDateTime),
            ["completedAt"] = state.CompletedAtUtc is null ? null! : Timestamp.FromDateTime(state.CompletedAtUtc.Value.UtcDateTime),
            ["schemaVersion"] = 2L,
        };
    }

    private static string HashId(string value) => Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(value)));
}
