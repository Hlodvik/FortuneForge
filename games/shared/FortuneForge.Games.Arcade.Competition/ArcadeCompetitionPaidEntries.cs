using System.Text.Json;
using FortuneForge.Games.Abstractions.Auditing;
using FortuneForge.Games.Abstractions.Wallet;
using FortuneForge.Server.Arcade.Competition;
using Google.Cloud.Firestore;

namespace FortuneForge.Games.Arcade.Competition;

/// <summary>Coordinates an arcade entry's prepared run, signed wallet debit, and accepted attempt.</summary>
public sealed class ArcadeCompetitionPaidEntries<TRun>(FirestoreDb database, string gameId,
    ArcadeCompetitionRulesOptions rules, IArcadeCompetitionEntryWallet wallet, IArcadeCompetitionRunAdapter<TRun> adapter)
    where TRun : class
{
    public const string PreparedCollection = ArcadeCompetitionEntryFence.PreparedCollection;
    private const string Pending = "pending";
    private const string Accepted = "accepted";
    private const string Declined = "declined";
    private static readonly JsonSerializerOptions Json = RoundAuditCapture.Json;
    private static readonly string EmptyBodySha256 = GameSecurityEventDocuments.Hash(string.Empty);
    private static readonly string EmptyObjectSha256 = GameSecurityEventDocuments.Hash("{}");

    private sealed record Intent(ArcadeCompetitionPaidEntryRequest Request, TRun Run, GameRequestObservation? Observation);
    private sealed record Prepared(Intent Intent, ArcadeCompetitionAttemptRecord Attempt,
        string OutcomeSha256, GameWalletSettlementReceipt? Receipt = null);
    private sealed record Admission(Prepared? Prepared, ArcadeCompetitionPaidEntry<TRun>? Legacy = null);

    public Task<ArcadeCompetitionPaidEntry<TRun>> StartAsync(ArcadeCompetitionPaidEntryRequest request,
        CancellationToken token) => StartAsync(request, allowNewEntries: true, token);

    public async Task<ArcadeCompetitionPaidEntry<TRun>> StartAsync(ArcadeCompetitionPaidEntryRequest request,
        bool allowNewEntries, CancellationToken token)
    {
        ArgumentNullException.ThrowIfNull(request);
        ArgumentNullException.ThrowIfNull(request.Competition);
        ArgumentException.ThrowIfNullOrWhiteSpace(request.AttemptId);
        ArgumentException.ThrowIfNullOrWhiteSpace(request.AuthenticatedPlayerId);
        RequireScope(request.Competition, request.AuthenticatedPlayerId);
        var bodySha256 = string.IsNullOrEmpty(request.RequestBodySha256) ? null : request.RequestBodySha256.ToLowerInvariant();
        if (bodySha256 is not null && !GameSecurityEventDocuments.IsEventId(bodySha256))
            throw new ArgumentException("The arcade entry is missing a valid verified body digest.", nameof(request));
        request = request with { RequestBodySha256 = bodySha256 };
        var admission = await PrepareAsync(request, allowNewEntries, token);
        if (admission.Legacy is { } legacy)
        {
            if (!await wallet.VerifyLegacyChargeAsync(ArcadeCompetitionEntryDebit.From(legacy.Attempt), token))
                throw new GameWalletUnavailableException();
            return legacy;
        }
        return await ConfirmAsync(admission.Prepared ?? throw new GameWalletUnavailableException(), token);
    }

    public Task<int> RecoverPendingAsync(CancellationToken token) => RecoverPendingAsync(database.Collection(PreparedCollection)
        .WhereEqualTo("gameId", gameId).WhereEqualTo("status", Pending), null, token);

    public Task<int> RecoverPendingAsync(ArcadeCompetitionIdentity competition, CancellationToken token)
    {
        RequireScope(competition);
        return RecoverPendingAsync(database.Collection(PreparedCollection)
            .WhereEqualTo("competitionId", competition.DocumentId).WhereEqualTo("status", Pending), competition, token);
    }

    public async Task<ArcadeCompetitionAttemptRecord> ReadAttemptAsync(string attemptDocumentId, string playerId,
        CancellationToken token)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(playerId);
        if (!GameSecurityEventDocuments.IsEventId(attemptDocumentId))
            throw new ArgumentException("The arcade competition attempt ID is invalid.", nameof(attemptDocumentId));
        var snapshot = await AttemptDocument(attemptDocumentId).GetSnapshotAsync(token);
        if (!snapshot.Exists || !snapshot.TryGetValue<string>("playerId", out var actor) || actor != playerId ||
            !snapshot.TryGetValue<string>("gameId", out var game) || game != gameId)
            throw new ArcadeCompetitionEntryConflictException("The accepted arcade competition attempt does not exist.");
        var attempt = FirestoreArcadeCompetitionStore.ReadAttempt(snapshot);
        RequireScope(attempt.Competition, playerId);
        if (attempt.DocumentId != attemptDocumentId) throw new GameWalletUnavailableException();
        return attempt;
    }

    private async Task<int> RecoverPendingAsync(Query query, ArcadeCompetitionIdentity? competition, CancellationToken token)
    {
        var entries = await query.GetSnapshotAsync(token);
        var recovered = 0;
        foreach (var entry in entries.Documents)
        {
            var prepared = ReadPrepared(entry);
            if (competition is not null && prepared.Attempt.Competition != competition)
                throw new GameWalletUnavailableException();
            try { _ = await ConfirmAsync(prepared, token); }
            catch (ArcadeCompetitionInsufficientCreditsException) { /* The declined receipt cleared the entry fence. */ }
            recovered++;
        }
        return recovered;
    }

    public Task<TResult> CompleteAsync<TResult>(ArcadeCompetitionIdentity competition, string attemptDocumentId,
        string playerId, DateTimeOffset completedAtUtc,
        Func<ArcadeCompetitionAttemptRecord, DocumentSnapshot, ArcadeCompetitionRunCompletion<TResult>> verify,
        CancellationToken token)
    {
        RequireScope(competition, playerId);
        if (!GameSecurityEventDocuments.IsEventId(attemptDocumentId))
            throw new ArgumentException("The arcade competition attempt ID is invalid.", nameof(attemptDocumentId));
        ArgumentNullException.ThrowIfNull(verify);
        return database.RunTransactionAsync(async transaction =>
        {
            var attemptDocument = AttemptDocument(attemptDocumentId);
            var snapshot = await transaction.GetSnapshotAsync(attemptDocument, token);
            var attempt = OpenAttempt(snapshot, competition, playerId);
            if (!attempt.IsCompleted)
            {
                var settlement = await transaction.GetSnapshotAsync(ArcadeCompetitionEntryFence.Settlement(database, competition), token);
                ArcadeCompetitionEntryFence.EnsureNotSettled(settlement, competition);
            }
            var runDocument = adapter.RunDocument(database, attempt);
            var run = await transaction.GetSnapshotAsync(runDocument, token);
            var verification = verify(attempt, run);
            if (attempt.IsCompleted)
            {
                if (verification.Score != attempt.Score || verification.WriteRun is not null)
                    throw new ArcadeCompetitionEntryConflictException("The stored attempt has a different completed replay.");
                return verification.Result(attempt);
            }

            var completed = attempt.Complete(new ArcadeCompetitionAttemptCompletion(attempt.AttemptId, competition,
                playerId, verification.Score, completedAtUtc));
            var write = verification.WriteRun ?? throw new InvalidOperationException("The verified replay is missing its run update.");
            write(transaction, runDocument, completed);
            transaction.Set(attemptDocument, ArcadeCompetitionFirestoreDocuments.AttemptData(completed));
            return verification.Result(completed);
        }, cancellationToken: token);
    }

    private Task<Admission> PrepareAsync(ArcadeCompetitionPaidEntryRequest request, bool allowNewEntries, CancellationToken token)
    {
        var operation = OperationDocument(request.AuthenticatedPlayerId, request.AttemptId);
        var observed = GameSecurityContext.Current;
        return database.RunTransactionAsync(async transaction =>
        {
            var index = await transaction.GetSnapshotAsync(operation, token);
            if (index.Exists)
            {
                var saved = ReadPrepared(await transaction.GetSnapshotAsync(PreparedDocument(index.GetValue<string>("attemptDocumentId")), token));
                if (!MatchesRequest(saved.Attempt, request, saved.Intent.Request.RequestBodySha256) ||
                    saved.OutcomeSha256 != index.GetValue<string>("outcomeSha256"))
                    throw new ArcadeCompetitionEntryConflictException("This arcade entry operation was already used for a different attempt.");
                if (saved.Receipt is { InsufficientFunds: false })
                {
                    var persistedAttempt = await transaction.GetSnapshotAsync(AttemptDocument(saved.Attempt.DocumentId), token);
                    var accepted = OpenAttempt(persistedAttempt, saved.Attempt.Competition, saved.Attempt.PlayerId);
                    _ = await ReadLegacyRunAsync(transaction, accepted, token);
                }
                return new Admission(saved);
            }

            var attempt = NewAttempt(request);
            var attemptDocument = AttemptDocument(attempt.DocumentId);
            var active = await transaction.GetSnapshotAsync(attemptDocument, token);
            if (active.Exists)
            {
                var legacy = OpenAttempt(active, request.Competition, request.AuthenticatedPlayerId);
                if (!MatchesRequest(legacy, request))
                    throw new ArcadeCompetitionEntryConflictException("The historical arcade entry started with the legacy request body.");
                return new Admission(null, await ReadLegacyRunAsync(transaction, legacy, token));
            }

            var legacyAttempt = await FindLegacyAttemptAsync(transaction, request, token);
            if (legacyAttempt is not null)
                return new Admission(null, await ReadLegacyRunAsync(transaction, legacyAttempt, token));

            if (!allowNewEntries) throw new ArcadeCompetitionEntryPausedException();
            var settlement = await transaction.GetSnapshotAsync(ArcadeCompetitionEntryFence.Settlement(database, request.Competition), token);
            ArcadeCompetitionEntryFence.EnsureNotSettled(settlement, request.Competition);
            var competition = await transaction.GetSnapshotAsync(ArcadeCompetitionEntryFence.Competition(database, request.Competition), token);
            if (competition.Exists && FirestoreArcadeCompetitionStore.ReadCompetitionIdentity(competition) != request.Competition)
                throw new InvalidOperationException("The stored arcade competition has a different identity.");
            var terms = ArcadeCompetitionWindowTerms.Read(competition, rules);
            if ((!competition.Exists || !competition.ContainsField(ArcadeCompetitionWindowTerms.Field)) &&
                request.EntryFeeCents != terms.EntryFeeCents)
                throw new InvalidOperationException("The requested arcade competition entry fee does not match the configured fee.");
            var acceptedRequest = request with { EntryFeeCents = terms.EntryFeeCents, EnteredAtUtc = attempt.EnteredAtUtc };
            attempt = NewAttempt(acceptedRequest);
            var preparedDocument = PreparedDocument(attempt.DocumentId);
            var collision = await transaction.GetSnapshotAsync(preparedDocument, token);
            if (collision.Exists)
                throw new ArcadeCompetitionEntryConflictException("The arcade competition attempt ID is already reserved.");
            var runDocument = adapter.RunDocument(database, attempt);
            var existingRun = await transaction.GetSnapshotAsync(runDocument, token);
            if (existingRun.Exists) throw new InvalidOperationException("A game run already exists without its competition attempt.");

            var run = adapter.PrepareRun(attempt) ?? throw new InvalidOperationException("The prepared arcade run is missing.");
            var intent = new Intent(acceptedRequest, run, observed);
            var json = JsonSerializer.Serialize(intent, Json);
            var digest = GameSecurityEventDocuments.Hash(json);
            ArcadeCompetitionEntryFence.Add(transaction, competition, request.Competition, terms);
            transaction.Create(preparedDocument, new Dictionary<string, object>
            {
                ["gameId"] = gameId, ["playerId"] = attempt.PlayerId, ["attemptId"] = attempt.AttemptId,
                ["attemptDocumentId"] = attempt.DocumentId, ["competitionId"] = attempt.Competition.DocumentId,
                ["status"] = Pending, ["intentJson"] = json, ["outcomeSha256"] = digest,
                ["enteredAt"] = Timestamp.FromDateTime(attempt.EnteredAtUtc.UtcDateTime), ["schemaVersion"] = 1L,
            });
            transaction.Create(operation, new Dictionary<string, object>
            {
                ["attemptDocumentId"] = attempt.DocumentId, ["outcomeSha256"] = digest,
            });
            return new Admission(new Prepared(intent, attempt, digest));
        }, cancellationToken: token);
    }

    private async Task<ArcadeCompetitionPaidEntry<TRun>> ConfirmAsync(Prepared prepared, CancellationToken token)
    {
        if (prepared.Receipt is { } existing)
            return Result(prepared, existing, alreadyAccepted: true);
        var debit = ArcadeCompetitionEntryDebit.From(prepared.Attempt);
        GameWalletSettlementReceipt receipt;
        try { receipt = await wallet.ChargeAsync(new ArcadeCompetitionEntryCharge(debit, prepared.OutcomeSha256), token); }
        catch (GameWalletConflictException exception) { throw new GameWalletUnavailableException(exception); }
        VerifyReceipt(prepared.Attempt, receipt);
        receipt = receipt with { WasAlreadySettled = false, SettledAtUtc = ArcadeCompetitionFirestoreTime.FloorToMicrosecond(receipt.SettledAtUtc) };
        var alreadyAccepted = await FinalizeAsync(prepared, receipt, token);
        return Result(prepared, receipt, alreadyAccepted);
    }

    private Task<bool> FinalizeAsync(Prepared prepared, GameWalletSettlementReceipt receipt, CancellationToken token) =>
        database.RunTransactionAsync(async transaction =>
        {
            var attempt = prepared.Attempt;
            var record = PreparedDocument(attempt.DocumentId);
            var saved = ReadPrepared(await transaction.GetSnapshotAsync(record, token));
            if (saved.OutcomeSha256 != prepared.OutcomeSha256) throw new GameWalletUnavailableException();
            if (saved.Receipt is { } prior)
                return prior == receipt ? true : throw new GameWalletUnavailableException();

            var competition = await transaction.GetSnapshotAsync(ArcadeCompetitionEntryFence.Competition(database, attempt.Competition), token);
            var settlement = await transaction.GetSnapshotAsync(ArcadeCompetitionEntryFence.Settlement(database, attempt.Competition), token);
            ArcadeCompetitionEntryFence.EnsureNotSettled(settlement, attempt.Competition);
            var attemptDocument = AttemptDocument(attempt.DocumentId);
            var runDocument = adapter.RunDocument(database, attempt);
            var accepted = await transaction.GetAllSnapshotsAsync([attemptDocument, runDocument], token);
            if (accepted.Any(document => document.Exists)) throw new GameWalletUnavailableException();
            if (!receipt.InsufficientFunds)
            {
                using var observation = prepared.Intent.Observation is { } original ? GameSecurityContext.Push(original) : null;
                transaction.Create(attemptDocument, ArcadeCompetitionFirestoreDocuments.AttemptData(attempt));
                adapter.WriteRun(transaction, database, runDocument, attempt, prepared.Intent.Run, receipt.WagerTransactionId!);
            }
            ArcadeCompetitionEntryFence.Remove(transaction, competition);
            var receiptJson = JsonSerializer.Serialize(receipt, Json);
            transaction.Update(record, new Dictionary<string, object>
            {
                ["status"] = receipt.InsufficientFunds ? Declined : Accepted,
                ["receiptJson"] = receiptJson, ["receiptSha256"] = GameSecurityEventDocuments.Hash(receiptJson),
                ["settledAt"] = Timestamp.FromDateTime(receipt.SettledAtUtc.UtcDateTime),
            });
            return false;
        }, cancellationToken: token);

    private Prepared ReadPrepared(DocumentSnapshot raw)
    {
        if (!raw.Exists || raw.GetValue<long>("schemaVersion") != 1 || raw.GetValue<string>("gameId") != gameId)
            throw new GameWalletUnavailableException();
        var intentJson = raw.GetValue<string>("intentJson");
        var digest = raw.GetValue<string>("outcomeSha256");
        if (digest != GameSecurityEventDocuments.Hash(intentJson)) throw new GameWalletUnavailableException();
        var intent = JsonSerializer.Deserialize<Intent>(intentJson, Json);
        if (intent is null || intent.Run is null) throw new GameWalletUnavailableException();
        if (intent.Request.RequestBodySha256 is { Length: > 0 } storedBody &&
            !GameSecurityEventDocuments.IsEventId(storedBody.ToLowerInvariant()))
            throw new GameWalletUnavailableException();
        var attempt = NewAttempt(intent.Request);
        if (attempt.Competition.GameId != gameId || attempt.DocumentId != raw.Id ||
            attempt.DocumentId != raw.GetValue<string>("attemptDocumentId") || attempt.AttemptId != raw.GetValue<string>("attemptId") ||
            attempt.PlayerId != raw.GetValue<string>("playerId") || attempt.Competition.DocumentId != raw.GetValue<string>("competitionId"))
            throw new GameWalletUnavailableException();
        var status = raw.GetValue<string>("status");
        if (status == Pending) return new Prepared(intent, attempt, digest);
        if (status is not (Accepted or Declined)) throw new GameWalletUnavailableException();
        var receiptJson = raw.GetValue<string>("receiptJson");
        if (GameSecurityEventDocuments.Hash(receiptJson) != raw.GetValue<string>("receiptSha256"))
            throw new GameWalletUnavailableException();
        var receipt = JsonSerializer.Deserialize<GameWalletSettlementReceipt>(receiptJson, Json)
            ?? throw new GameWalletUnavailableException();
        VerifyReceipt(attempt, receipt);
        if (receipt.InsufficientFunds != (status == Declined)) throw new GameWalletUnavailableException();
        return new Prepared(intent, attempt, digest, receipt);
    }

    private async Task<ArcadeCompetitionAttemptRecord?> FindLegacyAttemptAsync(Transaction transaction,
        ArcadeCompetitionPaidEntryRequest request, CancellationToken token)
    {
        var records = await transaction.GetSnapshotAsync(database.Collection(ArcadeCompetitionFirestoreDocuments.AttemptsCollection)
            .WhereEqualTo("attemptId", request.AttemptId), token);
        var matching = records.Documents.Where(document =>
            document.TryGetValue<string>("gameId", out var game) && game == gameId &&
            document.TryGetValue<string>("playerId", out var actor) && actor == request.AuthenticatedPlayerId).ToArray();
        if (matching.Length == 0) return null;
        if (matching.Length != 1) throw new ArcadeCompetitionEntryConflictException("Earlier entries share this arcade attempt ID.");
        var previous = FirestoreArcadeCompetitionStore.ReadAttempt(matching[0]);
        if (!MatchesRequest(previous, request))
            throw new ArcadeCompetitionEntryConflictException("This arcade attempt ID was already used with different start options.");
        return previous;
    }

    private async Task<ArcadeCompetitionPaidEntry<TRun>> ReadLegacyRunAsync(Transaction transaction, ArcadeCompetitionAttemptRecord attempt,
        CancellationToken token)
    {
        var run = await transaction.GetSnapshotAsync(adapter.RunDocument(database, attempt), token);
        return new ArcadeCompetitionPaidEntry<TRun>(attempt, adapter.ReadRun(run, attempt), WasAlreadyRecorded: true);
    }

    private static void VerifyReceipt(ArcadeCompetitionAttemptRecord attempt, GameWalletSettlementReceipt receipt)
    {
        if (!ArcadeCompetitionWalletProtocol.MatchesEntryReceipt(ArcadeCompetitionEntryDebit.From(attempt), receipt))
            throw new GameWalletUnavailableException();
    }

    private void RequireScope(ArcadeCompetitionIdentity competition, string? player = null)
    {
        ArgumentNullException.ThrowIfNull(competition);
        if (competition.GameId != gameId || player is not null && GameSecurityContext.Current is { } observation &&
            (observation.GameId != gameId || observation.UserId != player))
            throw new ArgumentException("The arcade competition request has a different game or account scope.");
    }

    private static ArcadeCompetitionPaidEntry<TRun> Result(Prepared prepared, GameWalletSettlementReceipt receipt, bool alreadyAccepted) =>
        receipt.InsufficientFunds ? throw new ArcadeCompetitionInsufficientCreditsException(receipt.BalanceBeforeCents, prepared.Attempt.EntryFeeCents)
            : new ArcadeCompetitionPaidEntry<TRun>(prepared.Attempt, prepared.Intent.Run, alreadyAccepted);

    private static ArcadeCompetitionAttemptRecord OpenAttempt(DocumentSnapshot snapshot, ArcadeCompetitionIdentity competition, string player)
    {
        if (!snapshot.Exists) throw new ArcadeCompetitionEntryConflictException("The accepted arcade competition attempt does not exist.");
        var attempt = FirestoreArcadeCompetitionStore.ReadAttempt(snapshot, competition);
        if (attempt.PlayerId != player || attempt.DocumentId != snapshot.Id)
            throw new ArcadeCompetitionEntryConflictException("The arcade competition attempt belongs to a different account.");
        return attempt;
    }

    private static ArcadeCompetitionAttemptRecord NewAttempt(ArcadeCompetitionPaidEntryRequest request) =>
        ArcadeCompetitionAttemptRecord.Start(new ArcadeCompetitionAttemptStart(request.AttemptId,
            request.Competition, request.AuthenticatedPlayerId, request.EntryFeeCents, request.EnteredAtUtc));

    private static bool MatchesRequest(ArcadeCompetitionAttemptRecord attempt, ArcadeCompetitionPaidEntryRequest request,
        string? storedBodySha256 = null) =>
        attempt.Competition.GameId == request.Competition.GameId && attempt.Competition.WindowKind == request.Competition.WindowKind &&
        attempt.AttemptId == request.AttemptId && attempt.PlayerId == request.AuthenticatedPlayerId &&
        (string.IsNullOrEmpty(storedBodySha256)
            ? request.RequestBodySha256 is null || request.RequestBodySha256 == EmptyBodySha256 || request.RequestBodySha256 == EmptyObjectSha256
            : string.Equals(storedBodySha256, request.RequestBodySha256, StringComparison.OrdinalIgnoreCase));

    private DocumentReference OperationDocument(string account, string attemptId) =>
        ArcadeCompetitionEntryFence.Operation(database, gameId, account, attemptId);

    private DocumentReference AttemptDocument(string id) => database.Collection(ArcadeCompetitionFirestoreDocuments.AttemptsCollection).Document(id);
    private DocumentReference PreparedDocument(string id) => ArcadeCompetitionEntryFence.Prepared(database, id);
}
