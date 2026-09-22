using System.Collections.Immutable;
using Google.Cloud.Firestore;

namespace FortuneForge.Server.Arcade.Competition;

/// <summary>Unregistered Firestore implementation for competition records only; it performs no money operations.</summary>
internal sealed class FirestoreArcadeCompetitionStore : IArcadeCompetitionStore
{
    private readonly FirestoreDb database;

    public FirestoreArcadeCompetitionStore(FirestoreDb database)
    {
        this.database = database;
    }

    public Task<ArcadeCompetitionStartAttemptResult> StartAttemptAsync(
        ArcadeCompetitionAttemptStart attempt,
        CancellationToken cancellationToken) =>
        database.RunTransactionAsync(async transaction =>
        {
            var requested = ArcadeCompetitionAttemptRecord.Start(attempt);
            var competitionReference = CompetitionDocument(attempt.Competition);
            var attemptReference = AttemptDocument(requested);
            var snapshots = await Task.WhenAll(
                transaction.GetSnapshotAsync(competitionReference, cancellationToken),
                transaction.GetSnapshotAsync(attemptReference, cancellationToken));
            var competitionSnapshot = snapshots[0];
            var attemptSnapshot = snapshots[1];

            if (competitionSnapshot.Exists) VerifyCompetition(competitionSnapshot, attempt.Competition);
            else transaction.Create(competitionReference, ArcadeCompetitionFirestoreDocuments.CompetitionData(attempt.Competition));

            if (!attemptSnapshot.Exists)
            {
                transaction.Create(attemptReference, ArcadeCompetitionFirestoreDocuments.AttemptData(requested));
                return new ArcadeCompetitionStartAttemptResult(requested, WasAlreadyRecorded: false);
            }

            var existing = ReadAttempt(attemptSnapshot, attempt.Competition);
            if (!SameStart(existing, requested))
                throw new InvalidOperationException("This arcade competition attempt id was already used with different data.");
            return new ArcadeCompetitionStartAttemptResult(existing, WasAlreadyRecorded: true);
        }, cancellationToken: cancellationToken);

    public Task<ArcadeCompetitionCompleteAttemptResult> CompleteAttemptAsync(
        ArcadeCompetitionAttemptCompletion completion,
        CancellationToken cancellationToken) =>
        database.RunTransactionAsync(async transaction =>
        {
            var attemptReference = AttemptDocumentId(completion.Competition, completion.AttemptId);
            var snapshot = await transaction.GetSnapshotAsync(attemptReference, cancellationToken);
            if (!snapshot.Exists)
                throw new InvalidOperationException("An arcade competition attempt must be started before it can be completed.");

            var existing = ReadAttempt(snapshot, completion.Competition);
            if (!existing.IsCompleted)
            {
                var completed = existing.Complete(completion);
                transaction.Set(attemptReference, ArcadeCompetitionFirestoreDocuments.AttemptData(completed));
                return new ArcadeCompetitionCompleteAttemptResult(completed, WasAlreadyCompleted: false);
            }

            if (existing.Score != completion.Score || existing.CompletedAtUtc != completion.CompletedAtUtc || existing.PlayerId != completion.PlayerId)
                throw new InvalidOperationException("This arcade competition attempt was already completed with different data.");
            return new ArcadeCompetitionCompleteAttemptResult(existing, WasAlreadyCompleted: true);
        }, cancellationToken: cancellationToken);

    public async Task<IReadOnlyList<ArcadeCompetitionAttemptRecord>> LoadAttemptsAsync(
        ArcadeCompetitionIdentity competition,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(competition);
        var snapshot = await database.Collection(ArcadeCompetitionFirestoreDocuments.AttemptsCollection)
            .WhereEqualTo("competitionId", competition.DocumentId)
            .GetSnapshotAsync(cancellationToken);

        return snapshot.Documents
            .Select(document => ReadAttempt(document, competition))
            .OrderBy(attempt => attempt.EnteredAtUtc)
            .ThenBy(attempt => attempt.AttemptId, StringComparer.Ordinal)
            .ToArray();
    }

    public async Task<IReadOnlyList<ArcadeCompetitionAttemptRecord>> LoadAttemptsForGameAsync(
        string gameId,
        CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(gameId)) throw new ArgumentException("A competition game id is required.", nameof(gameId));
        var snapshot = await database.Collection(ArcadeCompetitionFirestoreDocuments.AttemptsCollection)
            .WhereEqualTo("gameId", gameId)
            .GetSnapshotAsync(cancellationToken);

        return snapshot.Documents
            .Select(ReadAttempt)
            .OrderBy(attempt => attempt.Competition.StartsAtUtc)
            .ThenBy(attempt => attempt.EnteredAtUtc)
            .ThenBy(attempt => attempt.AttemptId, StringComparer.Ordinal)
            .ThenBy(attempt => attempt.PlayerId, StringComparer.Ordinal)
            .ToArray();
    }

    public async Task<IReadOnlyList<ArcadeCompetitionIdentity>> LoadDueSettlementCompetitionsAsync(
        DateTimeOffset cutoffUtc,
        CancellationToken cancellationToken)
    {
        if (cutoffUtc.Offset != TimeSpan.Zero)
            throw new ArgumentException("The arcade competition settlement cutoff must be UTC.", nameof(cutoffUtc));

        var snapshot = await database.Collection(ArcadeCompetitionFirestoreDocuments.CompetitionsCollection)
            .WhereEqualTo("settlementStatus", "pending")
            .WhereLessThanOrEqualTo("endsAt", Timestamp.FromDateTime(cutoffUtc.UtcDateTime))
            .GetSnapshotAsync(cancellationToken);
        var candidates = snapshot.Documents
            .Select(ReadCompetitionIdentity)
            .OrderBy(identity => identity.EndsAtUtc)
            .ThenBy(identity => identity.StartsAtUtc)
            .ThenBy(identity => identity.GameId, StringComparer.Ordinal)
            .ThenBy(identity => identity.WindowKind)
            .ToArray();
        var due = new List<ArcadeCompetitionIdentity>(candidates.Length);
        foreach (var competition in candidates)
        {
            var attemptQuery = database.Collection(ArcadeCompetitionFirestoreDocuments.AttemptsCollection)
                .WhereEqualTo("competitionId", competition.DocumentId)
                .Limit(1);
            var attemptsTask = attemptQuery.GetSnapshotAsync(cancellationToken);
            var settlementTask = SettlementDocument(competition).GetSnapshotAsync(cancellationToken);
            await Task.WhenAll(attemptsTask, settlementTask);
            var attempts = await attemptsTask;
            var settlement = await settlementTask;
            if (attempts.Count == 0) continue;
            _ = ReadAttempt(attempts.Documents[0], competition);
            if (settlement.Exists && ReadSettlementState(settlement, competition).IsCompleted) continue;
            due.Add(competition);
        }

        return due;
    }

    public async Task<ArcadeCompetitionSettlementState> ReadSettlementStateAsync(
        ArcadeCompetitionIdentity competition,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(competition);
        var snapshot = await SettlementDocument(competition).GetSnapshotAsync(cancellationToken);
        return snapshot.Exists ? ReadSettlementState(snapshot, competition) : new ArcadeCompetitionSettlementState(competition, null);
    }

    public Task<ArcadeCompetitionSettlementState> StoreSettlementPlanAsync(
        ArcadeCompetitionPayoutPlan plan,
        DateTimeOffset createdAtUtc,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(plan);
        if (createdAtUtc.Offset != TimeSpan.Zero)
            throw new ArgumentException("Settlement plan creation times must be UTC.", nameof(createdAtUtc));

        return database.RunTransactionAsync(async transaction =>
        {
            var reference = SettlementDocument(plan.Competition);
            var snapshot = await transaction.GetSnapshotAsync(reference, cancellationToken);
            if (snapshot.Exists)
            {
                var existing = ReadSettlementState(snapshot, plan.Competition);
                if (existing.PayoutPlan is not null)
                {
                    if (existing.PayoutPlan.HasSameValueAs(plan)) return existing;
                    throw new InvalidOperationException("This arcade competition already has a different immutable payout plan.");
                }
                if (existing.IsCompleted)
                    throw new InvalidOperationException("A completed legacy settlement cannot be assigned a payout plan.");
            }

            var requested = new ArcadeCompetitionSettlementState(
                plan.Competition,
                plan,
                createdAtUtc,
                createdAtUtc,
                completedAtUtc: null);
            transaction.Set(reference, ArcadeCompetitionFirestoreDocuments.SettlementData(requested));
            return requested;
        }, cancellationToken: cancellationToken);
    }

    public Task<ArcadeCompetitionSettlementState> MarkSettlementCompletedAsync(
        ArcadeCompetitionIdentity competition,
        DateTimeOffset completedAtUtc,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(competition);
        if (completedAtUtc.Offset != TimeSpan.Zero)
            throw new ArgumentException("Settlement completion times must be UTC.", nameof(completedAtUtc));
        return database.RunTransactionAsync(async transaction =>
        {
            var settlementReference = SettlementDocument(competition);
            var competitionReference = CompetitionDocument(competition);
            var snapshots = await Task.WhenAll(
                transaction.GetSnapshotAsync(settlementReference, cancellationToken),
                transaction.GetSnapshotAsync(competitionReference, cancellationToken));
            var settlementSnapshot = snapshots[0];
            var competitionSnapshot = snapshots[1];
            if (!settlementSnapshot.Exists)
                throw new InvalidOperationException("An arcade competition payout plan must be stored before settlement can complete.");

            var existing = ReadSettlementState(settlementSnapshot, competition);
            if (existing.IsCompleted) return existing;
            if (existing.PayoutPlan is null)
                throw new InvalidOperationException("An arcade competition payout plan must be stored before settlement can complete.");
            VerifyPendingCompetitionForCompletion(competitionSnapshot, competition);

            var completed = new ArcadeCompetitionSettlementState(
                competition,
                existing.PayoutPlan,
                existing.CreatedAtUtc,
                completedAtUtc,
                completedAtUtc);
            transaction.Update(competitionReference, "settlementStatus", "completed");
            transaction.Set(settlementReference, ArcadeCompetitionFirestoreDocuments.SettlementData(completed));
            return completed;
        }, cancellationToken: cancellationToken);
    }

    private DocumentReference CompetitionDocument(ArcadeCompetitionIdentity identity) =>
        database.Collection(ArcadeCompetitionFirestoreDocuments.CompetitionsCollection).Document(identity.DocumentId);

    private DocumentReference AttemptDocument(ArcadeCompetitionAttemptRecord attempt) =>
        database.Collection(ArcadeCompetitionFirestoreDocuments.AttemptsCollection).Document(attempt.DocumentId);

    private DocumentReference AttemptDocumentId(ArcadeCompetitionIdentity competition, string attemptId) =>
        database.Collection(ArcadeCompetitionFirestoreDocuments.AttemptsCollection)
            .Document(ArcadeCompetitionFirestoreDocuments.AttemptDocumentId(competition, attemptId));

    private DocumentReference SettlementDocument(ArcadeCompetitionIdentity identity) =>
        database.Collection(ArcadeCompetitionFirestoreDocuments.SettlementsCollection)
            .Document(ArcadeCompetitionFirestoreDocuments.SettlementDocumentId(identity));

    private static ArcadeCompetitionAttemptRecord ReadAttempt(DocumentSnapshot snapshot, ArcadeCompetitionIdentity competition)
    {
        if (!snapshot.Exists ||
            ReadString(snapshot, "competitionId") != competition.DocumentId ||
            ReadString(snapshot, "gameId") != competition.GameId ||
            ReadString(snapshot, "windowKind") != competition.WindowKind.ToString().ToLowerInvariant() ||
            ReadTimestamp(snapshot, "startsAt") != competition.StartsAtUtc ||
            ReadTimestamp(snapshot, "endsAt") != competition.EndsAtUtc)
        {
            throw new InvalidOperationException("A stored arcade competition attempt does not match its competition identity.");
        }

        var schemaVersion = ReadLong(snapshot, "schemaVersion");
        var attemptId = ReadRequiredString(snapshot, "attemptId");
        var playerId = ReadRequiredString(snapshot, "playerId");
        var entryFeeCents = ReadLong(snapshot, "entryFeeCents");
        if (schemaVersion == 1)
            return ArcadeCompetitionAttemptRecord.ReadCompletedLegacy(
                attemptId, competition, playerId, ReadLong(snapshot, "score"), entryFeeCents, ReadTimestamp(snapshot, "submittedAt"));

        if (schemaVersion != 2) throw new InvalidOperationException("A stored arcade competition attempt has an unsupported schema version.");
        var started = ArcadeCompetitionAttemptRecord.Start(new ArcadeCompetitionAttemptStart(
            attemptId, competition, playerId, entryFeeCents, ReadTimestamp(snapshot, "enteredAt")));
        return ReadString(snapshot, "status") switch
        {
            "started" => started,
            "completed" => started.Complete(new ArcadeCompetitionAttemptCompletion(
                attemptId, competition, playerId, ReadLong(snapshot, "score"), ReadTimestamp(snapshot, "completedAt"))),
            _ => throw new InvalidOperationException("A stored arcade competition attempt has an invalid status."),
        };
    }

    private static ArcadeCompetitionAttemptRecord ReadAttempt(DocumentSnapshot snapshot)
    {
        var gameId = ReadRequiredString(snapshot, "gameId");
        var identity = new ArcadeCompetitionIdentity(
            gameId,
            ParseWindowKind(ReadRequiredString(snapshot, "windowKind")),
            ReadTimestamp(snapshot, "startsAt"),
            ReadTimestamp(snapshot, "endsAt"));
        return ReadAttempt(snapshot, identity);
    }

    private static ArcadeCompetitionIdentity ReadCompetitionIdentity(DocumentSnapshot snapshot)
    {
        if (!snapshot.Exists || ReadLong(snapshot, "schemaVersion") != 2 ||
            ReadString(snapshot, "settlementStatus") != "pending")
            throw new InvalidOperationException("A stored arcade competition has an invalid schema version.");
        var identity = new ArcadeCompetitionIdentity(
            ReadRequiredString(snapshot, "gameId"),
            ParseWindowKind(ReadRequiredString(snapshot, "windowKind")),
            ReadTimestamp(snapshot, "startsAt"),
            ReadTimestamp(snapshot, "endsAt"));
        if (snapshot.Id != identity.DocumentId)
            throw new InvalidOperationException("A stored arcade competition document id does not match its identity.");
        return identity;
    }

    private static void VerifyPendingCompetitionForCompletion(
        DocumentSnapshot snapshot,
        ArcadeCompetitionIdentity identity)
    {
        if (!snapshot.Exists || ReadLong(snapshot, "schemaVersion") != 2 ||
            ReadString(snapshot, "settlementStatus") != "pending")
        {
            throw new InvalidOperationException("A stored arcade competition is not pending settlement.");
        }
        VerifyCompetition(snapshot, identity);
    }

    internal static ArcadeCompetitionSettlementState ReadSettlementState(DocumentSnapshot snapshot, ArcadeCompetitionIdentity competition)
    {
        if (ReadString(snapshot, "competitionId") != competition.DocumentId)
            throw new InvalidOperationException("A stored arcade competition settlement does not match its competition identity.");

        var status = ReadString(snapshot, "status");
        var schemaVersion = ReadLong(snapshot, "schemaVersion");
        if (schemaVersion == 1)
        {
            return status switch
            {
                "pending" when !HasNonNullField(snapshot, "completedAt") => new ArcadeCompetitionSettlementState(competition, null),
                "completed" => new ArcadeCompetitionSettlementState(competition, ReadTimestamp(snapshot, "completedAt")),
                _ => throw new InvalidOperationException("A stored arcade competition settlement has an invalid status."),
            };
        }

        if (schemaVersion != 2)
            throw new InvalidOperationException("A stored arcade competition settlement has an unsupported schema version.");
        VerifySettlementCompetition(snapshot, competition);
        var plan = ReadPayoutPlan(snapshot, competition);
        var createdAtUtc = ReadTimestamp(snapshot, "createdAt");
        var updatedAtUtc = ReadTimestamp(snapshot, "updatedAt");
        var completedAtUtc = status switch
        {
            "pending" when !HasNonNullField(snapshot, "completedAt") => (DateTimeOffset?)null,
            "completed" => ReadTimestamp(snapshot, "completedAt"),
            _ => throw new InvalidOperationException("A stored arcade competition settlement has an invalid status."),
        };
        return new ArcadeCompetitionSettlementState(
            competition,
            plan,
            createdAtUtc,
            updatedAtUtc,
            completedAtUtc);
    }

    private static bool HasNonNullField(DocumentSnapshot snapshot, string field) =>
        snapshot.ToDictionary().TryGetValue(field, out var value) && value is not null;

    private static ArcadeCompetitionPayoutPlan ReadPayoutPlan(
        DocumentSnapshot snapshot,
        ArcadeCompetitionIdentity competition)
    {
        var values = snapshot.ToDictionary();
        if (!values.TryGetValue("instructions", out var rawInstructions) || rawInstructions is not IEnumerable<object> sequence)
            throw new InvalidOperationException("A stored arcade competition settlement requires payout instructions.");

        var instructions = sequence.Select(ReadCreditInstruction).ToImmutableArray();
        try
        {
            return ArcadeCompetitionPayoutPlan.Restore(
                competition,
                instructions,
                ReadLong(snapshot, "visibleJackpotCents"),
                ReadLong(snapshot, "houseCutCents"));
        }
        catch (ArgumentException exception)
        {
            throw new InvalidOperationException("A stored arcade competition payout plan is invalid.", exception);
        }
    }

    private static ArcadeCompetitionCreditInstruction ReadCreditInstruction(object value)
    {
        if (value is not IDictionary<string, object> fields)
            throw new InvalidOperationException("A stored arcade competition payout instruction has an invalid shape.");

        var kind = RequiredMapString(fields, "kind") switch
        {
            "prize" => ArcadeCompetitionPayoutKind.Prize,
            "refund" => ArcadeCompetitionPayoutKind.Refund,
            _ => throw new InvalidOperationException("A stored arcade competition payout instruction has an invalid kind."),
        };
        var placement = fields.TryGetValue("placement", out var rawPlacement) && rawPlacement is not null
            ? checked((int)RequiredMapLong(fields, "placement"))
            : (int?)null;
        try
        {
            return new ArcadeCompetitionCreditInstruction(
                kind,
                RequiredMapString(fields, "playerId"),
                placement,
                RequiredMapLong(fields, "amountCents"),
                RequiredMapString(fields, "idempotencyKey"));
        }
        catch (ArgumentException exception)
        {
            throw new InvalidOperationException("A stored arcade competition payout instruction is invalid.", exception);
        }
    }

    private static void VerifySettlementCompetition(DocumentSnapshot snapshot, ArcadeCompetitionIdentity identity)
    {
        if (ReadString(snapshot, "gameId") != identity.GameId ||
            ReadString(snapshot, "windowKind") != identity.WindowKind.ToString().ToLowerInvariant() ||
            ReadTimestamp(snapshot, "startsAt") != identity.StartsAtUtc ||
            ReadTimestamp(snapshot, "endsAt") != identity.EndsAtUtc)
        {
            throw new InvalidOperationException("A stored arcade competition settlement has an invalid identity shape.");
        }
    }

    private static void VerifyCompetition(DocumentSnapshot snapshot, ArcadeCompetitionIdentity identity)
    {
        if (ReadString(snapshot, "gameId") != identity.GameId ||
            ReadString(snapshot, "windowKind") != identity.WindowKind.ToString().ToLowerInvariant() ||
            ReadTimestamp(snapshot, "startsAt") != identity.StartsAtUtc ||
            ReadTimestamp(snapshot, "endsAt") != identity.EndsAtUtc)
        {
            throw new InvalidOperationException("A stored arcade competition has an invalid identity shape.");
        }
    }

    private static bool SameStart(ArcadeCompetitionAttemptRecord left, ArcadeCompetitionAttemptRecord right) =>
        left.AttemptId == right.AttemptId && left.Competition.DocumentId == right.Competition.DocumentId &&
        left.PlayerId == right.PlayerId && left.EntryFeeCents == right.EntryFeeCents && left.EnteredAtUtc == right.EnteredAtUtc;

    private static string ReadRequiredString(DocumentSnapshot snapshot, string field)
    {
        var value = ReadString(snapshot, field);
        return !string.IsNullOrWhiteSpace(value) ? value : throw new InvalidOperationException($"Stored arcade competition field '{field}' is required.");
    }

    private static string ReadString(DocumentSnapshot snapshot, string field) =>
        snapshot.TryGetValue<string>(field, out var value) ? value : string.Empty;

    private static string RequiredMapString(IDictionary<string, object> fields, string field) =>
        fields.TryGetValue(field, out var value) && value is string text && !string.IsNullOrWhiteSpace(text)
            ? text
            : throw new InvalidOperationException($"Stored arcade competition payout field '{field}' is required.");

    private static long RequiredMapLong(IDictionary<string, object> fields, string field) =>
        fields.TryGetValue(field, out var value) && value is long number
            ? number
            : throw new InvalidOperationException($"Stored arcade competition payout field '{field}' is required.");

    private static long ReadLong(DocumentSnapshot snapshot, string field) =>
        snapshot.TryGetValue<long>(field, out var value) ? value : throw new InvalidOperationException($"Stored arcade competition field '{field}' is required.");

    private static DateTimeOffset ReadTimestamp(DocumentSnapshot snapshot, string field)
    {
        if (!snapshot.TryGetValue<Timestamp>(field, out var value))
            throw new InvalidOperationException($"Stored arcade competition field '{field}' is required.");
        return new DateTimeOffset(value.ToDateTime());
    }

    private static ArcadeCompetitionWindowKind ParseWindowKind(string value) => value switch
    {
        "daily" => ArcadeCompetitionWindowKind.Daily,
        "weekly" => ArcadeCompetitionWindowKind.Weekly,
        _ => throw new InvalidOperationException("A stored arcade competition attempt has an invalid window kind."),
    };
}
