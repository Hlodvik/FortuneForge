using System.Text.Json;
using FortuneForge.Games.Abstractions.Auditing;
using FortuneForge.Games.Abstractions.Wallet;
using Google.Cloud.Firestore;

namespace FortuneForge.Games.Slots.Persistence;

public interface IClassicSlotRoundRequest
{
    string OperationId { get; }
    public static bool IsValidOperationId(string? operationId) => operationId is { Length: >= 16 and <= 128 } &&
        operationId.All(character => char.IsAsciiLetterOrDigit(character) || character is '-' or '_');
}

public interface IClassicSlotPreparedRound<out TRequest>
{
    Guid AdmissionId { get; }
    string AccountId { get; }
    TRequest Request { get; }
    ClassicSlotRoundState StateBefore { get; }
    ClassicSlotRoundState StateAfter { get; }
    Guid SpinId { get; }
    DateTime GeneratedAtUtc { get; }
    string OutcomeSha256 { get; }
    long WagerCents { get; }
    long PayoutCents { get; }
    bool SpendsLegacy { get; }
    bool ImportedLegacyState { get; }
    GameRequestObservation? Observation { get; }
}

public interface IClassicSlotRoundResult
{
    bool Settled { get; }
    GameWalletSettlementReceipt Wallet { get; }
    long ChargedWagerCents { get; }
}

public sealed record ClassicSlotRoundStorageScope(
    string GameId, string AuditGameId, string StorageGameId, string GameName, string RawCollection, string OwnerField);

/// <summary>Persists and settles a classic round without selecting game rules or interpreting its result.</summary>
public abstract class ClassicSlotPaidRoundStore<TRequest, TPrepared, TResult>(
    FirestoreDb database, TimeProvider clock, ClassicSlotRoundStateCodec state, ClassicSlotRoundStorageScope scope)
    where TRequest : class, IClassicSlotRoundRequest
    where TPrepared : class, IClassicSlotPreparedRound<TRequest>
    where TResult : class, IClassicSlotRoundResult
{
    private const string Operations = "slotPreparedOperations";
    private const string Pending = "pending";
    private const string Settled = "settled";
    private const string Declined = "declined";
    private static readonly TimeSpan Cooldown = TimeSpan.FromMilliseconds(500);
    private static readonly JsonSerializerOptions Json = RoundAuditCapture.Json;

    private sealed record StoredRound(TPrepared Round, TResult? Final);

    public Task<(ClassicSlotRoundState Before, TRequest? PendingRequest)> ReadStateAsync(
        string account, long signedLegacyCount, CancellationToken token) =>
        database.RunTransactionAsync(async transaction =>
        {
            var guard = await transaction.GetSnapshotAsync(Guard(account), token);
            var pending = await ReadPendingAsync(transaction, guard, account, token);
            var before = pending?.StateBefore ?? await state.ReadAsync(transaction, database,
                guard, account, signedLegacyCount, token);
            return (before, pending?.Request);
        }, cancellationToken: token);

    public async Task<TResult> SpinAsync(string account, TRequest request, bool allowStart,
        IGameWalletClient wallet, IGameWalletAccountReader accounts, CancellationToken token)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(account, "accountId");
        ArgumentNullException.ThrowIfNull(request);
        if (!IClassicSlotRoundRequest.IsValidOperationId(request.OperationId))
            throw new ArgumentException($"Include a valid {scope.GameName} spin operation ID.", nameof(request));
        var saved = await FindAsync(account, request, inspectLegacy: !allowStart, token);
        if (saved is null)
        {
            if (!allowStart) throw Paused();
            var balance = await accounts.GetStateAsync(account, token);
            saved = await PrepareAsync(account, request, balance.LegacyFreeGamesRemaining, token);
        }
        if (saved.Final is { } final) return final;
        var round = saved.Round;
        GameWalletSettlementReceipt receipt;
        try
        {
            receipt = await wallet.SettleAsync(account, new(round.SpinId.ToString("N"), round.OutcomeSha256,
                round.WagerCents, round.PayoutCents, round.SpendsLegacy, round.StateBefore.LegacyRemaining), token);
        }
        catch (GameWalletConflictException exception)
        {
            throw new GameWalletUnavailableException(exception);
        }
        return await FinalizeAsync(round, receipt, token);
    }

    protected abstract TPrepared PrepareRound(string account, TRequest request, ClassicSlotRoundState before,
        DateTime now, GameRequestObservation? observation, bool importing);
    protected abstract void ValidateRound(TPrepared round);
    protected abstract TPrepared RemoveObservation(TPrepared round);
    protected abstract TResult CreateResult(TPrepared round, GameWalletSettlementReceipt receipt, bool settled);
    protected abstract TResult RestoreResult(TResult result, TPrepared? round);
    protected abstract void Report(Transaction transaction, FirestoreDb database, TPrepared round, TResult result, DateTime completed);
    protected abstract Exception Conflict(string message, TRequest? pending = null);
    protected abstract Exception CoolingDown(TimeSpan remaining);
    protected abstract Exception Paused();

    private Task<StoredRound?> FindAsync(string account, TRequest request, bool inspectLegacy, CancellationToken token) =>
        database.RunTransactionAsync(async transaction =>
        {
            var index = await transaction.GetSnapshotAsync(Operation(account, request.OperationId), token);
            if (!index.Exists && !inspectLegacy) return null;
            var guard = await transaction.GetSnapshotAsync(Guard(account), token);
            if (!index.Exists)
            {
                RejectLegacyAdmission(guard);
                return null;
            }
            return await ReadIndexedAsync(transaction, index, guard, account, request, token);
        }, cancellationToken: token);

    private void RejectLegacyAdmission(DocumentSnapshot guard)
    {
        if (Text(guard, scope.OwnerField) is null && Text(guard, SlotSpinGuard.ActiveAdmissionField) is not null)
            throw new FortuneForge.Games.Abstractions.LegacySlotAdmissionPendingException();
    }

    private Task<StoredRound> PrepareAsync(string account, TRequest request, long legacyCount, CancellationToken token)
    {
        var operation = Operation(account, request.OperationId);
        var guard = Guard(account);
        var observed = GameSecurityContext.Current;
        if (observed is not null && (observed.UserId != account || observed.GameId != scope.AuditGameId))
            throw new ArgumentException($"The {scope.GameName} request belongs to a different account or game.");

        return database.RunTransactionAsync(async transaction =>
        {
            var snapshots = await transaction.GetAllSnapshotsAsync([operation, guard], token);
            var current = snapshots[1];
            if (snapshots[0].Exists)
                return await ReadIndexedAsync(transaction, snapshots[0], current, account, request, token);
            if (await ReadPendingAsync(transaction, current, account, token) is { } pending)
                throw Conflict($"Finish the prepared {scope.GameName} spin first.", pending.Request);
            RejectLegacyAdmission(current);
            if (Text(current, SlotSpinGuard.ActiveAdmissionField) is not null || Text(current, SlotSpinGuard.PreparedAdmissionField) is not null)
                throw Conflict($"Finish the existing {scope.GameName} spin first.");
            var now = clock.GetUtcNow().UtcDateTime;
            if (current.Exists && current.TryGetValue<Timestamp>(SlotSpinGuard.LastSpinStartedField, out var started) &&
                started.ToDateTime().Add(Cooldown) > now)
                throw CoolingDown(started.ToDateTime().Add(Cooldown) - now);

            var before = await state.ReadAsync(transaction, database, current, account, legacyCount, token);
            var round = PrepareRound(account, request, before, now, observed, Text(current, scope.OwnerField) is null);
            var raw = Identity(round);
            raw["status"] = Pending;
            raw["startedAtUtc"] = Timestamp.FromDateTime(now);
            WriteFrozen(raw, round);
            var index = Identity(round);
            index["requestSha256"] = Hash(JsonSerializer.Serialize(request, Json));
            var fields = state.Write(before);
            fields["userId"] = account;
            fields[SlotSpinGuard.ActiveAdmissionField] = round.AdmissionId.ToString("N");
            fields[SlotSpinGuard.PreparedAdmissionField] = round.AdmissionId.ToString("N");
            fields[SlotSpinGuard.LastSpinStartedField] = Timestamp.FromDateTime(now);
            fields["updatedAt"] = Timestamp.FromDateTime(now);
            transaction.Create(Record(round.AdmissionId), raw);
            transaction.Create(operation, index);
            transaction.Set(guard, fields, SetOptions.MergeAll);
            return new StoredRound(round, null);
        }, cancellationToken: token);
    }

    private Task<TResult> FinalizeAsync(TPrepared round, GameWalletSettlementReceipt receipt, CancellationToken token)
    {
        ValidateWallet(round, receipt);
        receipt = receipt with { WasAlreadySettled = false };
        var guard = Guard(round.AccountId);
        var record = Record(round.AdmissionId);
        return database.RunTransactionAsync(async transaction =>
        {
            var snapshots = await transaction.GetAllSnapshotsAsync([record, guard], token);
            var saved = ReadRecord(snapshots[0], round.AdmissionId, round.AccountId, round.Request.OperationId);
            if (!EqualityComparer<TRequest>.Default.Equals(saved.Round.Request, round.Request) ||
                saved.Round.SpinId != round.SpinId ||
                saved.Round.OutcomeSha256 != round.OutcomeSha256 || saved.Round.WagerCents != round.WagerCents ||
                saved.Round.PayoutCents != round.PayoutCents || saved.Round.SpendsLegacy != round.SpendsLegacy ||
                saved.Round.StateBefore.LegacyRemaining != round.StateBefore.LegacyRemaining ||
                saved.Round.ImportedLegacyState != round.ImportedLegacyState)
                throw new GameWalletUnavailableException();
            if (saved.Final is { } existing)
                return existing.Wallet == receipt ? existing : throw new GameWalletUnavailableException();

            if (Text(snapshots[0], "preparedSha256") != Hash(JsonSerializer.Serialize(round, Json)))
                throw new GameWalletUnavailableException();
            ValidateGuard(snapshots[1], saved.Round);
            var walletTime = receipt.SettledAtUtc.UtcDateTime;
            var generated = round.GeneratedAtUtc;
            var completed = walletTime < generated ? generated : walletTime;
            var settled = !IsDecline(receipt);
            var guardFields = settled ? state.Write(round.StateAfter) : new Dictionary<string, object>();
            if (receipt.LegacyFreeGamesCountMismatch && round.ImportedLegacyState)
                state.UndoImport(guardFields);
            guardFields[SlotSpinGuard.ActiveAdmissionField] = string.Empty;
            guardFields[SlotSpinGuard.PreparedAdmissionField] = FieldValue.Delete;
            guardFields["updatedAt"] = Timestamp.FromDateTime(completed);
            var reply = CreateResult(round, receipt, settled);
            if (settled)
                Report(transaction, database, round, reply, completed);

            var resultJson = JsonSerializer.Serialize(reply, Json);
            var data = new Dictionary<string, object>
            {
                ["status"] = settled ? Settled : Declined, ["completedAt"] = Timestamp.FromDateTime(completed),
                ["resultJson"] = resultJson, ["resultSha256"] = Hash(resultJson)
            };
            // The protected report retains request evidence. Exact retry does not need it in the round document.
            if (round.Observation is not null)
                WriteFrozen(data, RemoveObservation(round));
            transaction.Set(record, data, SetOptions.MergeAll);
            transaction.Set(guard, guardFields, SetOptions.MergeAll);
            return reply;
        }, cancellationToken: token);
    }

    private async Task<StoredRound> ReadIndexedAsync(Transaction transaction, DocumentSnapshot index,
        DocumentSnapshot guard, string account, TRequest request, CancellationToken token)
    {
        var id = CheckIdentity(index, account, request.OperationId);
        if (Text(index, "requestSha256") != Hash(JsonSerializer.Serialize(request, Json)))
            throw Conflict("This operation ID was already used for different spin options.");
        var raw = await transaction.GetSnapshotAsync(Record(id), token);
        var saved = ReadRecord(raw, id, account, request.OperationId);
        if (!EqualityComparer<TRequest>.Default.Equals(saved.Round.Request, request)) throw new GameWalletUnavailableException();
        if (saved.Final is null)
        {
            ValidateGuard(guard, saved.Round);
            ValidateRound(saved.Round);
        }
        return saved;
    }

    private async Task<TPrepared?> ReadPendingAsync(Transaction transaction, DocumentSnapshot guard, string account, CancellationToken token)
    {
        if (Text(guard, scope.OwnerField) != scope.GameId ||
            !Guid.TryParseExact(Text(guard, SlotSpinGuard.PreparedAdmissionField), "N", out var pending))
            return null;
        var raw = await transaction.GetSnapshotAsync(Record(pending), token);
        var saved = ReadRecord(raw, pending, account, Text(raw, "operationId"));
        ValidateGuard(guard, saved.Round);
        if (saved.Final is not null) throw new GameWalletUnavailableException();
        return saved.Round;
    }

    private void ValidateGuard(DocumentSnapshot guard, TPrepared round)
    {
        var admission = round.AdmissionId.ToString("N");
        if (Text(guard, scope.OwnerField) != scope.GameId ||
            Text(guard, SlotSpinGuard.ActiveAdmissionField) != admission ||
            Text(guard, SlotSpinGuard.PreparedAdmissionField) != admission ||
            !state.Read(guard, round.StateBefore.LegacyRemaining).Matches(round.StateBefore))
            throw new GameWalletUnavailableException();
    }

    private StoredRound ReadRecord(DocumentSnapshot snapshot, Guid admission, string account, string? operation)
    {
        if (operation is null || CheckIdentity(snapshot, account, operation) != admission)
            throw new GameWalletUnavailableException();
        var round = ReadChecked<TPrepared>(snapshot, "prepared");
        if (round.AdmissionId != admission || round.AccountId != account || round.Request.OperationId != operation ||
            round.SpinId.ToString("N") != Text(snapshot, "spinId"))
            throw new GameWalletUnavailableException();
        var status = Text(snapshot, "status");
        if (status == Pending) return new(round, null);
        if (status is not (Settled or Declined)) throw new GameWalletUnavailableException();
        var result = ReadChecked<TResult>(snapshot, "result");
        if ((status == Settled) != result.Settled ||
            (status == Declined) != IsDecline(result.Wallet) || result.ChargedWagerCents != round.WagerCents)
            throw new GameWalletUnavailableException();
        ValidateWallet(round, result.Wallet);
        return new(round, RestoreResult(result, status == Settled ? round : null));
    }

    private void ValidateWallet(TPrepared round, GameWalletSettlementReceipt wallet)
    {
        var spin = round.SpinId.ToString("N");
        var declined = IsDecline(wallet);
        var beforeLegacy = round.StateBefore.LegacyRemaining;
        var countOkay = wallet.LegacyFreeGamesCountMismatch
            ? wallet.LegacyFreeGamesRemaining >= 0 && wallet.LegacyFreeGamesRemaining != beforeLegacy
            : wallet.LegacyFreeGamesRemaining == beforeLegacy - (!declined && round.SpendsLegacy ? 1 : 0);
        if (wallet.SettlementId != GameWalletSettlementIdentity.CreateId(scope.GameId, round.AccountId, spin) ||
            !countOkay || wallet.BalanceBeforeCents < 0 || wallet.BalanceAfterWagerCents < 0 || wallet.BalanceAfterCents < 0 ||
            (wallet.InsufficientFunds ? 1 : 0) + (wallet.InsufficientLegacyFreeGames ? 1 : 0) +
                (wallet.LegacyFreeGamesCountMismatch ? 1 : 0) > 1 ||
            wallet.WagerTransactionId != (!declined && round.WagerCents > 0 ? $"{spin}-wager" : null) ||
            wallet.PayoutTransactionId != (!declined && round.PayoutCents > 0 ? $"{spin}-payout" : null) ||
            wallet.LegacyFreeGameTransactionId != (!declined && round.SpendsLegacy ? $"{spin}-free-game-use" : null) ||
            wallet.BalanceAfterWagerCents != checked(wallet.BalanceBeforeCents - (declined ? 0 : round.WagerCents)) ||
            wallet.BalanceAfterCents != checked(wallet.BalanceAfterWagerCents + (declined ? 0 : round.PayoutCents)))
            throw new GameWalletUnavailableException();
    }

    private static bool IsDecline(GameWalletSettlementReceipt wallet) =>
        wallet.InsufficientFunds || wallet.InsufficientLegacyFreeGames || wallet.LegacyFreeGamesCountMismatch;

    private static TValue ReadChecked<TValue>(DocumentSnapshot snapshot, string prefix)
    {
        if (Text(snapshot, prefix + "Json") is not { } value || Text(snapshot, prefix + "Sha256") != Hash(value))
            throw new GameWalletUnavailableException();
        return JsonSerializer.Deserialize<TValue>(value, Json) ?? throw new GameWalletUnavailableException();
    }

    private static void WriteFrozen(IDictionary<string, object> fields, TPrepared round)
    {
        var json = JsonSerializer.Serialize(round, Json);
        fields["preparedJson"] = json;
        fields["preparedSha256"] = Hash(json);
    }

    private Dictionary<string, object> Identity(TPrepared round) => new()
    {
        ["owner"] = scope.GameId, ["userId"] = round.AccountId, ["gameId"] = scope.GameId,
        ["admissionId"] = round.AdmissionId.ToString("N"), ["operationId"] = round.Request.OperationId,
        ["spinId"] = round.SpinId.ToString("N")
    };

    private Guid CheckIdentity(DocumentSnapshot document, string account, string operation)
    {
        if (Text(document, "owner") != scope.GameId || Text(document, "userId") != account ||
            Text(document, "gameId") != scope.GameId || Text(document, "operationId") != operation ||
            !Guid.TryParseExact(Text(document, "admissionId"), "N", out var id) || id == Guid.Empty)
            throw new GameWalletUnavailableException();
        return id;
    }

    private static string Hash(string value) => GameSecurityEventDocuments.Hash(value);

    private static string? Text(DocumentSnapshot snapshot, string field) =>
        snapshot.Exists && snapshot.TryGetValue<string>(field, out var value) && !string.IsNullOrWhiteSpace(value) ? value : null;

    private DocumentReference Guard(string account) =>
        database.Collection(SlotSpinGuard.Collection).Document(SlotSpinGuard.DocumentId(account, scope.StorageGameId));
    private DocumentReference Record(Guid admission) => database.Collection(scope.RawCollection).Document(admission.ToString("N"));
    private DocumentReference Operation(string account, string operation) =>
        database.Collection(Operations).Document(Hash($"{scope.GameId}\n{account}\n{operation}"));
}
