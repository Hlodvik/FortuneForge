using Google.Cloud.Firestore;
using FortuneForge.Server.Arcade.Asteroids;
using System.Text.RegularExpressions;
using System.Security.Cryptography;
using System.Text;

namespace FortuneForge.Server.Arcade.Competition;

/// <summary>
/// Unregistered internal admission boundary. It atomically debits an entry and starts an attempt;
/// settlement, refunds, and payouts deliberately belong to separate future work.
/// </summary>
internal sealed class FirestoreArcadeCompetitionPaidEntryCoordinator : IArcadeCompetitionPaidEntryCoordinator
{
    private const string SlotsCreditsCurrencyId = "slotsCredits";
    private const string AvailableFractionalCentsField = "availableFractionalCents";
    private static readonly Regex SeedHexPattern = new("^[0-9a-f]{16}$", RegexOptions.CultureInvariant);
    private static readonly Regex DigestHexPattern = new("^[0-9a-f]{64}$", RegexOptions.CultureInvariant);
    private static readonly Regex CanonicalReplayPattern = new("^v1\\|[1-9][0-9]*\\|(?:[0-9]+:[0-9]+(?:,[0-9]+:[0-9]+)*)?$", RegexOptions.CultureInvariant);
    private readonly FirestoreDb database;
    private readonly long entryFeeCents;

    internal FirestoreArcadeCompetitionPaidEntryCoordinator(
        FirestoreDb database,
        ArcadeCompetitionRulesOptions? rulesOptions = null)
    {
        this.database = database ?? throw new ArgumentNullException(nameof(database));
        rulesOptions ??= new ArcadeCompetitionRulesOptions();
        if (rulesOptions.EntryFeeCents <= 0) throw new ArgumentOutOfRangeException(nameof(rulesOptions));
        entryFeeCents = rulesOptions.EntryFeeCents;
    }

    public async Task<ArcadeCompetitionPaidEntryResult> StartPaidAttemptAsync(
        ArcadeCompetitionPaidEntryRequest request,
        CancellationToken cancellationToken) =>
        (await StartAsync(request, null, cancellationToken)).Entry;

    public async Task<ArcadeCompetitionAsteroidsPaidEntryResult> StartAsteroidsPaidAttemptAsync(
        ArcadeCompetitionPaidEntryRequest request,
        AsteroidsRunIdentity proposedRun,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(proposedRun);
        var outcome = await StartAsync(request, proposedRun, cancellationToken);
        return new ArcadeCompetitionAsteroidsPaidEntryResult(
            outcome.Entry.Attempt,
            outcome.Entry.WasAlreadyRecorded,
            outcome.Run ?? throw new InvalidOperationException("An Asteroids paid entry did not produce a run."));
    }

    public Task<AsteroidsReplayCompletionResult> CompleteAsteroidsReplayAsync(
        AsteroidsReplayCompletionRequest request,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);
        var attemptDocumentId = AttemptDocumentIdFromRunId(request.RunId);
        return database.RunTransactionAsync(async transaction =>
        {
            var competitionReference = CompetitionDocument(request.Competition);
            var settlementReference = SettlementDocument(request.Competition);
            var runReference = database.Collection("asteroidsRuns").Document(attemptDocumentId);
            var first = await Task.WhenAll(
                transaction.GetSnapshotAsync(competitionReference, cancellationToken),
                transaction.GetSnapshotAsync(settlementReference, cancellationToken),
                transaction.GetSnapshotAsync(runReference, cancellationToken));
            if (!first[0].Exists) throw new InvalidOperationException("The paid competition is missing.");
            VerifyCompetition(first[0], request.Competition);
            VerifySettlementIsAcceptingEntries(first[1], request.Competition);
            var run = ReadRunForCompletion(first[2], request);
            var attemptReference = database.Collection(ArcadeCompetitionFirestoreDocuments.AttemptsCollection).Document(attemptDocumentId);
            var attemptSnapshot = await transaction.GetSnapshotAsync(attemptReference, cancellationToken);
            var canonical = CanonicalReplay(request.Replay);
            var digest = Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(canonical)));
            if (run.Status == "completed")
            {
                if (run.ReplayCanonical != canonical || run.ReplayDigest != digest)
                    throw new InvalidOperationException("This Asteroids run was already completed with a different replay.");
                var completedAttempt = ReadCompletedAttempt(attemptSnapshot, request.Competition);
                if (completedAttempt.AttemptId != run.Attempt.AttemptId ||
                    completedAttempt.PlayerId != run.Attempt.PlayerId ||
                    completedAttempt.Score != run.Score ||
                    completedAttempt.CompletedAtUtc != run.CompletedAtUtc)
                {
                    throw new InvalidOperationException("The completed Asteroids run does not match its competition attempt.");
                }
                return new AsteroidsReplayCompletionResult(completedAttempt, run.Score, run.Terminal, true);
            }
            if (run.Status != "started" || !attemptSnapshot.Exists)
                throw new InvalidOperationException("The Asteroids run is not available for completion.");
            var attempt = ReadAttempt(attemptSnapshot, request.Competition);
            if (attempt.PlayerId != request.AuthenticatedPlayerId || attempt.AttemptId != run.Attempt.AttemptId)
                throw new InvalidOperationException("The Asteroids run does not match its competition attempt.");
            var state = AsteroidsReplayEvaluator.Evaluate(run.Run, request.Replay);
            if (!state.IsTerminal) throw new ArgumentException("Asteroids replay must reach a terminal state.", nameof(request));
            var completed = attempt.Complete(new ArcadeCompetitionAttemptCompletion(
                attempt.AttemptId, request.Competition, request.AuthenticatedPlayerId, state.Score, request.CompletedAtUtc));
            transaction.Set(runReference, new Dictionary<string, object>
            {
                ["status"] = "completed", ["replayCanonical"] = canonical, ["replayDigest"] = digest,
                ["score"] = state.Score, ["terminal"] = state.Terminal.ToString().ToLowerInvariant(),
                ["completedAt"] = Timestamp.FromDateTime(request.CompletedAtUtc.UtcDateTime),
            }, SetOptions.MergeAll);
            transaction.Set(attemptReference, ArcadeCompetitionFirestoreDocuments.AttemptData(completed));
            return new AsteroidsReplayCompletionResult(completed, state.Score, state.Terminal, false);
        }, cancellationToken: cancellationToken);
    }

    private Task<PaidEntryOutcome> StartAsync(
        ArcadeCompetitionPaidEntryRequest request,
        AsteroidsRunIdentity? proposedRun,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);
        if (request.EntryFeeCents != entryFeeCents)
            throw new InvalidOperationException("The requested arcade competition entry fee does not match the configured fee.");
        var started = new ArcadeCompetitionAttemptStart(
            request.AttemptId,
            request.Competition,
            request.AuthenticatedPlayerId,
            request.EntryFeeCents,
            request.EnteredAtUtc);
        var requested = ArcadeCompetitionAttemptRecord.Start(started);
        var competitionReference = CompetitionDocument(request.Competition);
        var settlementReference = SettlementDocument(request.Competition);
        var attemptReference = AttemptDocument(requested);
        var balanceReference = BalanceDocument(request.AuthenticatedPlayerId);
        var entryReference = BalanceTransactionDocument($"arcade-competition-{requested.DocumentId}-entry");
        var runReference = proposedRun is null ? null : AsteroidsRunDocument(requested);
        if (proposedRun is not null && proposedRun.RunId != AsteroidsRunId(requested))
            throw new ArgumentException("The Asteroids run id must be deterministic for its competition attempt.", nameof(proposedRun));

        return database.RunTransactionAsync(async transaction =>
        {
            var snapshots = new List<Task<DocumentSnapshot>>
            {
                transaction.GetSnapshotAsync(competitionReference, cancellationToken),
                transaction.GetSnapshotAsync(settlementReference, cancellationToken),
                transaction.GetSnapshotAsync(attemptReference, cancellationToken),
                transaction.GetSnapshotAsync(balanceReference, cancellationToken),
                transaction.GetSnapshotAsync(entryReference, cancellationToken),
            };
            if (runReference is not null) snapshots.Add(transaction.GetSnapshotAsync(runReference, cancellationToken));
            var reads = await Task.WhenAll(snapshots);
            var competitionSnapshot = reads[0];
            var settlementSnapshot = reads[1];
            var attemptSnapshot = reads[2];
            var balanceSnapshot = reads[3];
            var entrySnapshot = reads[4];
            var runSnapshot = runReference is null ? null : reads[5];

            if (competitionSnapshot.Exists) VerifyCompetition(competitionSnapshot, request.Competition);
            VerifySettlementIsAcceptingEntries(settlementSnapshot, request.Competition);
            if (attemptSnapshot.Exists)
            {
                var existing = ReadAttempt(attemptSnapshot, request.Competition);
                if (!SameStart(existing, requested))
                    throw new InvalidOperationException("This arcade competition attempt id was already used with different data.");
                if (!entrySnapshot.Exists)
                    throw new InvalidOperationException("A recorded arcade competition entry is missing its debit ledger record.");
                VerifyEntryLedger(entrySnapshot, request.AuthenticatedPlayerId, entryFeeCents, request.AttemptId);
                var existingRun = proposedRun is null ? null : ReadAsteroidsRun(
                    runSnapshot ?? throw new InvalidOperationException("An Asteroids run snapshot is missing."), existing);
                return new PaidEntryOutcome(new ArcadeCompetitionPaidEntryResult(existing, WasAlreadyRecorded: true), existingRun);
            }

            if (entrySnapshot.Exists)
                throw new InvalidOperationException("An arcade competition entry ledger record already exists without its attempt.");
            if (runSnapshot is { Exists: true })
                throw new InvalidOperationException("An Asteroids run already exists without its competition attempt.");
            var availableCents = ReadBalanceCents(balanceSnapshot, request.AuthenticatedPlayerId);
            if (availableCents < entryFeeCents)
                throw new ArcadeCompetitionInsufficientCreditsException(availableCents, entryFeeCents);

            var remainingCents = checked(availableCents - entryFeeCents);
            if (!competitionSnapshot.Exists)
                transaction.Create(competitionReference, ArcadeCompetitionFirestoreDocuments.CompetitionData(request.Competition));
            transaction.Update(balanceReference, BalanceUpdate(remainingCents, request.EnteredAtUtc));
            transaction.Create(entryReference, BalanceTransactionData(
                entryReference.Id,
                request.AuthenticatedPlayerId,
                -entryFeeCents,
                remainingCents,
                request.AttemptId,
                request.EnteredAtUtc));
            transaction.Create(attemptReference, ArcadeCompetitionFirestoreDocuments.AttemptData(requested));
            AsteroidsRunRecord? createdRun = null;
            if (proposedRun is not null && runReference is not null)
            {
                createdRun = new AsteroidsRunRecord(proposedRun, requested);
                transaction.Create(runReference, AsteroidsRunData(createdRun));
            }
            return new PaidEntryOutcome(new ArcadeCompetitionPaidEntryResult(requested, WasAlreadyRecorded: false), createdRun);
        }, cancellationToken: cancellationToken);
    }

    private DocumentReference CompetitionDocument(ArcadeCompetitionIdentity identity) =>
        database.Collection(ArcadeCompetitionFirestoreDocuments.CompetitionsCollection).Document(identity.DocumentId);

    private DocumentReference AttemptDocument(ArcadeCompetitionAttemptRecord attempt) =>
        database.Collection(ArcadeCompetitionFirestoreDocuments.AttemptsCollection).Document(attempt.DocumentId);

    private DocumentReference SettlementDocument(ArcadeCompetitionIdentity identity) =>
        database.Collection(ArcadeCompetitionFirestoreDocuments.SettlementsCollection).Document(
            ArcadeCompetitionFirestoreDocuments.SettlementDocumentId(identity));

    private DocumentReference BalanceDocument(string userId) =>
        database.Collection("userBalances").Document($"{userId}_{SlotsCreditsCurrencyId}");

    private DocumentReference BalanceTransactionDocument(string transactionId) =>
        database.Collection("balanceTransactions").Document(transactionId);

    private DocumentReference AsteroidsRunDocument(ArcadeCompetitionAttemptRecord attempt) =>
        database.Collection("asteroidsRuns").Document(attempt.DocumentId);

    private static Dictionary<string, object> BalanceUpdate(long cents, DateTimeOffset updatedAtUtc) => new()
    {
        ["available"] = cents / 100,
        [AvailableFractionalCentsField] = cents % 100,
        ["version"] = FieldValue.Increment(1),
        ["updatedAt"] = Timestamp.FromDateTime(updatedAtUtc.UtcDateTime),
    };

    private static Dictionary<string, object> BalanceTransactionData(
        string transactionId,
        string userId,
        long amountCents,
        long balanceAfterCents,
        string idempotencyKey,
        DateTimeOffset createdAtUtc) => new()
    {
        ["transactionId"] = transactionId,
        ["userId"] = userId,
        ["currencyId"] = SlotsCreditsCurrencyId,
        ["amount"] = (double)(amountCents / 100m),
        ["balanceAfter"] = (double)(balanceAfterCents / 100m),
        ["type"] = "arcade-competition-entry",
        ["idempotencyKey"] = idempotencyKey,
        ["createdAt"] = Timestamp.FromDateTime(createdAtUtc.UtcDateTime),
    };

    private static Dictionary<string, object> AsteroidsRunData(AsteroidsRunRecord run) => new()
    {
        ["runId"] = run.Run.RunId,
        ["seedHex"] = run.Run.Seed.ToString("x16"),
        ["status"] = "started",
        ["attemptId"] = run.Attempt.AttemptId,
        ["attemptDocumentId"] = run.Attempt.DocumentId,
        ["competitionId"] = run.Attempt.Competition.DocumentId,
        ["gameId"] = run.Attempt.Competition.GameId,
        ["playerId"] = run.Attempt.PlayerId,
        ["entryFeeCents"] = run.Attempt.EntryFeeCents,
        ["enteredAt"] = Timestamp.FromDateTime(run.Attempt.EnteredAtUtc.UtcDateTime),
        ["startsAt"] = Timestamp.FromDateTime(run.Attempt.Competition.StartsAtUtc.UtcDateTime),
        ["endsAt"] = Timestamp.FromDateTime(run.Attempt.Competition.EndsAtUtc.UtcDateTime),
        ["schemaVersion"] = 1L,
    };

    private static long ReadBalanceCents(DocumentSnapshot snapshot, string userId)
    {
        if (!snapshot.Exists ||
            !snapshot.TryGetValue<string>("userId", out var storedUserId) || storedUserId != userId ||
            !snapshot.TryGetValue<string>("currencyId", out var currencyId) || currencyId != SlotsCreditsCurrencyId ||
            !snapshot.TryGetValue<long>("available", out var available) ||
            !snapshot.TryGetValue<long>("reserved", out var reserved) || reserved < 0 ||
            !snapshot.TryGetValue<long>("version", out var version) || version < 1)
            throw new InvalidOperationException("The authenticated player's slots credit balance is unavailable.");
        var fractional = snapshot.TryGetValue<long>(AvailableFractionalCentsField, out var storedFractional)
            ? storedFractional
            : 0L;
        if (available < 0 || fractional is < 0 or >= 100)
            throw new InvalidOperationException("The authenticated player's slots credit balance is invalid.");
        return checked(available * 100 + fractional);
    }

    private static void VerifyCompetition(DocumentSnapshot snapshot, ArcadeCompetitionIdentity identity)
    {
        if (!snapshot.TryGetValue<string>("gameId", out var gameId) || gameId != identity.GameId ||
            !snapshot.TryGetValue<string>("windowKind", out var windowKind) || windowKind != identity.WindowKind.ToString().ToLowerInvariant() ||
            !snapshot.TryGetValue<Timestamp>("startsAt", out var startsAt) || new DateTimeOffset(startsAt.ToDateTime()) != identity.StartsAtUtc ||
            !snapshot.TryGetValue<Timestamp>("endsAt", out var endsAt) || new DateTimeOffset(endsAt.ToDateTime()) != identity.EndsAtUtc)
        {
            throw new InvalidOperationException("A stored arcade competition has an invalid identity shape.");
        }
    }

    private static ArcadeCompetitionAttemptRecord ReadAttempt(DocumentSnapshot snapshot, ArcadeCompetitionIdentity competition)
    {
        VerifyCompetitionAttemptIdentity(snapshot, competition);
        if (!snapshot.TryGetValue<string>("attemptId", out var attemptId) || string.IsNullOrWhiteSpace(attemptId) ||
            !snapshot.TryGetValue<string>("playerId", out var playerId) || string.IsNullOrWhiteSpace(playerId) ||
            !snapshot.TryGetValue<long>("entryFeeCents", out var storedEntryFee) ||
            !snapshot.TryGetValue<Timestamp>("enteredAt", out var enteredAt) ||
            !snapshot.TryGetValue<string>("status", out var status) || status != "started" ||
            !snapshot.TryGetValue<long>("schemaVersion", out var schemaVersion) || schemaVersion != 2)
        {
            throw new InvalidOperationException("A stored arcade competition attempt is invalid.");
        }
        return ArcadeCompetitionAttemptRecord.Start(new ArcadeCompetitionAttemptStart(
            attemptId, competition, playerId, storedEntryFee, new DateTimeOffset(enteredAt.ToDateTime())));
    }

    private static ArcadeCompetitionAttemptRecord ReadCompletedAttempt(DocumentSnapshot snapshot, ArcadeCompetitionIdentity competition)
    {
        VerifyCompetitionAttemptIdentity(snapshot, competition);
        if (!snapshot.TryGetValue<string>("attemptId", out var attemptId) || string.IsNullOrWhiteSpace(attemptId) ||
            !snapshot.TryGetValue<string>("playerId", out var playerId) || string.IsNullOrWhiteSpace(playerId) ||
            !snapshot.TryGetValue<long>("entryFeeCents", out var entryFee) || entryFee <= 0 ||
            !snapshot.TryGetValue<Timestamp>("enteredAt", out var enteredAt) ||
            !snapshot.TryGetValue<string>("status", out var status) || status != "completed" ||
            !snapshot.TryGetValue<long>("score", out var score) || score < 0 ||
            !snapshot.TryGetValue<Timestamp>("completedAt", out var completedAt) ||
            !snapshot.TryGetValue<long>("schemaVersion", out var schemaVersion) || schemaVersion != 2)
            throw new InvalidOperationException("A completed arcade competition attempt is invalid.");
        var started = ArcadeCompetitionAttemptRecord.Start(new ArcadeCompetitionAttemptStart(
            attemptId, competition, playerId, entryFee, new DateTimeOffset(enteredAt.ToDateTime())));
        return started.Complete(new ArcadeCompetitionAttemptCompletion(
            attemptId, competition, playerId, score, new DateTimeOffset(completedAt.ToDateTime())));
    }

    private static void VerifyCompetitionAttemptIdentity(DocumentSnapshot snapshot, ArcadeCompetitionIdentity competition)
    {
        if (!snapshot.TryGetValue<string>("competitionId", out var competitionId) || competitionId != competition.DocumentId ||
            !snapshot.TryGetValue<string>("gameId", out var gameId) || gameId != competition.GameId ||
            !snapshot.TryGetValue<string>("windowKind", out var windowKind) || windowKind != competition.WindowKind.ToString().ToLowerInvariant() ||
            !snapshot.TryGetValue<Timestamp>("startsAt", out var startsAt) || new DateTimeOffset(startsAt.ToDateTime()) != competition.StartsAtUtc ||
            !snapshot.TryGetValue<Timestamp>("endsAt", out var endsAt) || new DateTimeOffset(endsAt.ToDateTime()) != competition.EndsAtUtc)
        {
            throw new InvalidOperationException("A stored arcade competition attempt does not match its competition identity.");
        }
    }

    private static void VerifySettlementIsAcceptingEntries(DocumentSnapshot snapshot, ArcadeCompetitionIdentity competition)
    {
        if (!snapshot.Exists) return;
        var settlement = FirestoreArcadeCompetitionStore.ReadSettlementState(snapshot, competition);
        if (settlement.PayoutPlan is not null || settlement.IsCompleted)
            throw new ArcadeCompetitionSettlementCompletedException();
    }

    private static bool SameStart(ArcadeCompetitionAttemptRecord left, ArcadeCompetitionAttemptRecord right) =>
        left.AttemptId == right.AttemptId && left.Competition.DocumentId == right.Competition.DocumentId &&
        left.PlayerId == right.PlayerId && left.EntryFeeCents == right.EntryFeeCents && left.EnteredAtUtc == right.EnteredAtUtc;

    private static void VerifyEntryLedger(DocumentSnapshot snapshot, string userId, long chargedCents, string attemptId)
    {
        if (!snapshot.TryGetValue<string>("userId", out var storedUserId) || storedUserId != userId ||
            !snapshot.TryGetValue<string>("currencyId", out var currencyId) || currencyId != SlotsCreditsCurrencyId ||
            !snapshot.TryGetValue<double>("amount", out var amount) || amount != (double)(-chargedCents / 100m) ||
            !snapshot.TryGetValue<string>("type", out var type) || type != "arcade-competition-entry" ||
            !snapshot.TryGetValue<string>("idempotencyKey", out var key) || key != attemptId)
        {
            throw new InvalidOperationException("A recorded arcade competition entry has an invalid debit ledger record.");
        }
    }

    private static AsteroidsRunRecord ReadAsteroidsRun(DocumentSnapshot snapshot, ArcadeCompetitionAttemptRecord attempt)
    {
        if (!snapshot.Exists ||
            !snapshot.TryGetValue<string>("runId", out var runId) || runId != AsteroidsRunId(attempt) ||
            !snapshot.TryGetValue<string>("seedHex", out var seedHex) || !SeedHexPattern.IsMatch(seedHex) ||
            !snapshot.TryGetValue<string>("attemptId", out var attemptId) || attemptId != attempt.AttemptId ||
            !snapshot.TryGetValue<string>("attemptDocumentId", out var documentId) || documentId != attempt.DocumentId ||
            !snapshot.TryGetValue<string>("competitionId", out var competitionId) || competitionId != attempt.Competition.DocumentId ||
            !snapshot.TryGetValue<string>("gameId", out var gameId) || gameId != attempt.Competition.GameId ||
            !snapshot.TryGetValue<string>("playerId", out var playerId) || playerId != attempt.PlayerId ||
            !snapshot.TryGetValue<Timestamp>("startsAt", out var startsAt) || new DateTimeOffset(startsAt.ToDateTime()) != attempt.Competition.StartsAtUtc ||
            !snapshot.TryGetValue<Timestamp>("endsAt", out var endsAt) || new DateTimeOffset(endsAt.ToDateTime()) != attempt.Competition.EndsAtUtc ||
            !snapshot.TryGetValue<long>("schemaVersion", out var schemaVersion) || schemaVersion != 1)
        {
            throw new InvalidOperationException("A recorded Asteroids run does not match its paid competition attempt.");
        }
        try
        {
            return new AsteroidsRunRecord(new AsteroidsRunIdentity(runId, Convert.ToUInt64(seedHex, 16)), attempt);
        }
        catch (Exception exception) when (exception is ArgumentException or FormatException or OverflowException)
        {
            throw new InvalidOperationException("A recorded Asteroids run has an invalid seed.");
        }
    }

    private static StoredRun ReadRunForCompletion(DocumentSnapshot snapshot, AsteroidsReplayCompletionRequest request)
    {
        if (!snapshot.Exists || !snapshot.TryGetValue<string>("runId", out var runId) || runId != request.RunId ||
            !snapshot.TryGetValue<string>("seedHex", out var seedHex) || !SeedHexPattern.IsMatch(seedHex) ||
            !snapshot.TryGetValue<string>("attemptId", out var attemptId) ||
            !snapshot.TryGetValue<string>("attemptDocumentId", out var attemptDocumentId) ||
            !snapshot.TryGetValue<string>("competitionId", out var competitionId) || competitionId != request.Competition.DocumentId ||
            !snapshot.TryGetValue<string>("gameId", out var gameId) || gameId != request.Competition.GameId ||
            !snapshot.TryGetValue<string>("playerId", out var playerId) || playerId != request.AuthenticatedPlayerId ||
            !snapshot.TryGetValue<long>("entryFeeCents", out var entryFee) || entryFee <= 0 ||
            !snapshot.TryGetValue<Timestamp>("enteredAt", out var enteredAt) ||
            !snapshot.TryGetValue<Timestamp>("startsAt", out var startsAt) || new DateTimeOffset(startsAt.ToDateTime()) != request.Competition.StartsAtUtc ||
            !snapshot.TryGetValue<Timestamp>("endsAt", out var endsAt) || new DateTimeOffset(endsAt.ToDateTime()) != request.Competition.EndsAtUtc ||
            !snapshot.TryGetValue<string>("status", out var status))
            throw new InvalidOperationException("A stored Asteroids run is invalid.");
        ArcadeCompetitionAttemptRecord attempt;
        try
        {
            var started = new ArcadeCompetitionAttemptStart(attemptId, request.Competition, playerId, entryFee, new DateTimeOffset(enteredAt.ToDateTime()));
            attempt = ArcadeCompetitionAttemptRecord.Start(started);
        }
        catch { throw new InvalidOperationException("A stored Asteroids run is invalid."); }
        if (attempt.DocumentId != attemptDocumentId) throw new InvalidOperationException("A stored Asteroids run is invalid.");
        var identity = new AsteroidsRunIdentity(runId, Convert.ToUInt64(seedHex, 16));
        var canonical = snapshot.TryGetValue<string>("replayCanonical", out var storedCanonical) ? storedCanonical : string.Empty;
        var digest = snapshot.TryGetValue<string>("replayDigest", out var storedDigest) ? storedDigest : string.Empty;
        var score = snapshot.TryGetValue<long>("score", out var storedScore) ? storedScore : 0L;
        var terminal = snapshot.TryGetValue<string>("terminal", out var storedTerminal) && Enum.TryParse<AsteroidsTerminalState>(storedTerminal, true, out var parsed) ? parsed : AsteroidsTerminalState.Active;
        var completedAt = snapshot.TryGetValue<Timestamp>("completedAt", out var storedCompletedAt)
            ? new DateTimeOffset(storedCompletedAt.ToDateTime())
            : (DateTimeOffset?)null;
        if (status == "completed" && (!CanonicalReplayPattern.IsMatch(canonical) || !DigestHexPattern.IsMatch(digest) || score < 0 || terminal == AsteroidsTerminalState.Active || completedAt is null))
            throw new InvalidOperationException("A completed Asteroids run is invalid.");
        if (status != "started" && status != "completed")
            throw new InvalidOperationException("A stored Asteroids run is invalid.");
        return new StoredRun(identity, attempt, status, canonical, digest, score, terminal, completedAt);
    }

    private static string CanonicalReplay(AsteroidsReplay replay)
    {
        return AsteroidsReplayEvaluator.CanonicalizeReplay(replay);
    }

    private static string AttemptDocumentIdFromRunId(string runId)
    {
        const string prefix = "asteroids_";
        if (string.IsNullOrWhiteSpace(runId) || !runId.StartsWith(prefix, StringComparison.Ordinal) || runId.Length != prefix.Length + 64 ||
            !runId[prefix.Length..].All(character => character is >= '0' and <= '9' or >= 'a' and <= 'f'))
            throw new ArgumentException("Asteroids run id is invalid.", nameof(runId));
        return runId[prefix.Length..];
    }

    internal static string AsteroidsRunId(ArcadeCompetitionAttemptRecord attempt) => $"asteroids_{attempt.DocumentId}";

    private sealed record PaidEntryOutcome(ArcadeCompetitionPaidEntryResult Entry, AsteroidsRunRecord? Run);
    private sealed record StoredRun(AsteroidsRunIdentity Run, ArcadeCompetitionAttemptRecord Attempt, string Status, string ReplayCanonical, string ReplayDigest, long Score, AsteroidsTerminalState Terminal, DateTimeOffset? CompletedAtUtc);
}

internal sealed record ArcadeCompetitionPaidEntryRequest(
    string AttemptId,
    ArcadeCompetitionIdentity Competition,
    string AuthenticatedPlayerId,
    long EntryFeeCents,
    DateTimeOffset EnteredAtUtc);

internal sealed record ArcadeCompetitionPaidEntryResult(
    ArcadeCompetitionAttemptRecord Attempt,
    bool WasAlreadyRecorded);

internal sealed record AsteroidsRunRecord(AsteroidsRunIdentity Run, ArcadeCompetitionAttemptRecord Attempt);

internal sealed record ArcadeCompetitionAsteroidsPaidEntryResult(
    ArcadeCompetitionAttemptRecord Attempt,
    bool WasAlreadyRecorded,
    AsteroidsRunRecord Run);

internal sealed record AsteroidsReplayCompletionRequest(string RunId, ArcadeCompetitionIdentity Competition, string AuthenticatedPlayerId, AsteroidsReplay Replay, DateTimeOffset CompletedAtUtc);
internal sealed record AsteroidsReplayCompletionResult(ArcadeCompetitionAttemptRecord Attempt, long Score, AsteroidsTerminalState Terminal, bool WasAlreadyCompleted);

internal interface IArcadeCompetitionPaidEntryCoordinator
{
    Task<ArcadeCompetitionPaidEntryResult> StartPaidAttemptAsync(
        ArcadeCompetitionPaidEntryRequest request,
        CancellationToken cancellationToken);
    Task<ArcadeCompetitionAsteroidsPaidEntryResult> StartAsteroidsPaidAttemptAsync(
        ArcadeCompetitionPaidEntryRequest request,
        AsteroidsRunIdentity proposedRun,
        CancellationToken cancellationToken);
    Task<AsteroidsReplayCompletionResult> CompleteAsteroidsReplayAsync(AsteroidsReplayCompletionRequest request, CancellationToken cancellationToken);
}

internal sealed class ArcadeCompetitionInsufficientCreditsException(long availableCents, long requiredCents) : Exception(
    "The authenticated player does not have enough slots credits for this arcade competition entry.")
{
    public long AvailableCents { get; } = availableCents;
    public long RequiredCents { get; } = requiredCents;
}
